using System.Net;

namespace KitBuilder.Data.Http;

/// <summary>Thrown when Steam keeps answering 429 after the allowed retries.</summary>
public sealed class RateLimitedException(DateTimeOffset retryAfter)
    : Exception($"Steam rate limit reached; retry after {retryAfter:O}.")
{
    public DateTimeOffset RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Serializes requests to Steam with a minimum interval and backs off exponentially
/// on HTTP 429 (honoring Retry-After). The request schedule is shared by every handler
/// (Steam limits per IP); the backoff values belong to each handler instance.
/// </summary>
public sealed class SteamThrottlingHandler(
    TimeSpan minInterval,
    int maxRetries = 3,
    TimeProvider? time = null,
    TimeSpan? initialBackoff = null,
    TimeSpan? maxBackoff = null) : DelegatingHandler
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset nextAllowed = DateTimeOffset.MinValue;
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public TimeSpan InitialBackoff { get; } = initialBackoff ?? TimeSpan.FromSeconds(30);

    public TimeSpan MaxBackoff { get; } = maxBackoff ?? TimeSpan.FromMinutes(10);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var backoff = InitialBackoff;
        for (int attempt = 0; ; attempt++)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            HttpResponseMessage response;
            try
            {
                var wait = nextAllowed - clock.GetUtcNow();
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, clock, ct).ConfigureAwait(false);
                }

                response = await base.SendAsync(request, ct).ConfigureAwait(false);
                nextAllowed = clock.GetUtcNow() + minInterval;
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retry = response.Headers.RetryAfter?.Delta ?? backoff;
                    nextAllowed = clock.GetUtcNow() + retry;
                    if (attempt >= maxRetries)
                    {
                        response.Dispose();
                        throw new RateLimitedException(nextAllowed);
                    }

                    response.Dispose();
                    backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
                    continue;
                }
            }
            finally
            {
                Gate.Release();
            }

            return response;
        }
    }

    /// <summary>Test hook: forget the shared schedule.</summary>
    public static void Reset() => nextAllowed = DateTimeOffset.MinValue;
}
