using System.Net;
using KitBuilder.Data.Storage;

namespace KitBuilder.Data.Tests;

internal static class Fx
{
    public static string Path(string relative) => System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", relative);

    public static string Text(string relative) => File.ReadAllText(Path(relative));

    public static LocalDatabase TempDb() =>
        new(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N"), "t.db"));
}

/// <summary>Programmable HTTP handler: no network in tests.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(respond(request));
    }

    public static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
}

/// <summary>Manually advanced clock.</summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;

    public override DateTimeOffset GetUtcNow() => Now;
}
