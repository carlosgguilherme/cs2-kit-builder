using System.Net;
using System.Text.Json;
using KitBuilder.Data.Storage;

namespace KitBuilder.Data.Inventory;

/// <summary>Public CS2 inventory (appid 730, context 2) with pagination and a per-profile refresh limit.</summary>
public sealed class SteamInventoryClient(HttpClient http, LocalDatabase db, DataOptions options, TimeProvider? time = null)
{
    public const int PageSize = 2000;
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public static string Url(string steamId64, string? startAssetId, int count = PageSize) =>
        $"https://steamcommunity.com/inventory/{steamId64}/730/2?l=english&count={count}"
        + (startAssetId is null ? "" : "&start_assetid=" + startAssetId);

    /// <summary>Returns the cached snapshot unless it is older than the refresh limit (or forced and allowed).</summary>
    public async Task<InventorySnapshot> LoadAsync(string steamId64, bool forceRefresh = false, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var cached = ReadCache(steamId64);
        if (cached is not null)
        {
            bool allowed = now >= cached.NextRefreshAllowedAt;
            if (!allowed || !forceRefresh)
            {
                return cached;
            }
        }

        var items = new List<InventoryItem>();
        int total = 0;
        string? start = null;
        for (int guard = 0; guard < 50; guard++)
        {
            using var resp = await http.GetAsync(Url(steamId64, start), ct).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                throw new InventoryPrivateException(steamId64);
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (json.Trim() is "null" or "")
            {
                throw new InventoryPrivateException(steamId64);
            }

            var page = InventoryParser.Parse(json, steamId64);
            items.AddRange(page.Items);
            total = Math.Max(total, page.TotalCount);
            if (!page.MoreItems || page.LastAssetId is null)
            {
                break;
            }

            start = page.LastAssetId;
        }

        // total_inventory_count includes trade-protected items that the public inventory hides
        // (verified 2026-11-12: 28 counted, 22 returned, 6 received in a trade < 7 days earlier).
        int hidden = Math.Max(0, total - items.Count);
        using (var c = db.Open())
        {
            LocalDatabase.Exec(c, """
                INSERT INTO inventories(steam_id, json, fetched_at, hidden_count) VALUES ($s, $j, $t, $h)
                ON CONFLICT(steam_id) DO UPDATE SET json = $j, fetched_at = $t, hidden_count = $h
                """, ("$s", steamId64), ("$j", JsonSerializer.Serialize(items)), ("$t", now.ToUnixTimeSeconds()), ("$h", hidden));
        }

        return new InventorySnapshot(steamId64, items, now, now + options.InventoryMinInterval, false, hidden);
    }

    private InventorySnapshot? ReadCache(string steamId64)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json, fetched_at, hidden_count FROM inventories WHERE steam_id = $s";
        cmd.Parameters.AddWithValue("$s", steamId64);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            return null;
        }

        var at = DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1));
        var items = JsonSerializer.Deserialize<List<InventoryItem>>(r.GetString(0)) ?? [];
        return new InventorySnapshot(steamId64, items, at, at + options.InventoryMinInterval, true, r.GetInt32(2));
    }
}
