using System.Net;
using KitBuilder.Data.Storage;

namespace KitBuilder.Data.Http;

/// <summary>
/// Downloads a URL to a cache file, revalidating with ETag/Last-Modified at most
/// once per <c>checkInterval</c>. Works offline with the last cached copy.
/// </summary>
public sealed class CachedDownloader(HttpClient http, LocalDatabase db, string cacheDir, TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public sealed record Result(string FilePath, DateTimeOffset CheckedAt, bool FromNetwork, bool Stale);

    public async Task<Result> GetAsync(string url, TimeSpan checkInterval, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheDir);
        var file = Path.Combine(cacheDir, FileName(url));
        var now = clock.GetUtcNow();
        string? etag = null, lastModified = null;
        DateTimeOffset? checkedAt = null;
        using (var c = db.Open())
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT etag, last_modified, checked_at FROM http_cache WHERE url = $u";
            cmd.Parameters.AddWithValue("$u", url);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                etag = r.IsDBNull(0) ? null : r.GetString(0);
                lastModified = r.IsDBNull(1) ? null : r.GetString(1);
                checkedAt = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(2));
            }
        }

        bool haveFile = File.Exists(file);
        if (haveFile && checkedAt is { } last && now - last < checkInterval)
        {
            return new Result(file, last, false, false);
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            if (haveFile && etag is not null)
            {
                req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            if (haveFile && lastModified is not null)
            {
                req.Headers.TryAddWithoutValidation("If-Modified-Since", lastModified);
            }

            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.NotModified && haveFile)
            {
                Save(url, etag, lastModified, file, now);
                return new Result(file, now, true, false);
            }

            resp.EnsureSuccessStatusCode();
            var tmp = file + ".tmp";
            await using (var fs = File.Create(tmp))
            {
                await resp.Content.CopyToAsync(fs, ct).ConfigureAwait(false);
            }

            File.Move(tmp, file, overwrite: true);
            Save(url, resp.Headers.ETag?.ToString(), resp.Content.Headers.LastModified?.ToString("R"), file, now);
            return new Result(file, now, true, false);
        }
        catch (Exception) when (haveFile && !ct.IsCancellationRequested)
        {
            // Offline or server error: keep using the cached copy.
            return new Result(file, checkedAt ?? now, false, true);
        }
    }

    private void Save(string url, string? etag, string? lastModified, string file, DateTimeOffset now)
    {
        using var c = db.Open();
        LocalDatabase.Exec(c, """
            INSERT INTO http_cache(url, etag, last_modified, file, checked_at) VALUES ($u, $e, $l, $f, $t)
            ON CONFLICT(url) DO UPDATE SET etag = $e, last_modified = $l, file = $f, checked_at = $t
            """, ("$u", url), ("$e", etag), ("$l", lastModified), ("$f", file), ("$t", now.ToUnixTimeSeconds()));
    }

    private static string FileName(string url)
    {
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(url)))[..16];
        return hash + "-" + Path.GetFileName(new Uri(url).AbsolutePath);
    }
}
