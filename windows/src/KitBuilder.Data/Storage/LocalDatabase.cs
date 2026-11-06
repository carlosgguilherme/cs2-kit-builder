using Microsoft.Data.Sqlite;

namespace KitBuilder.Data.Storage;

/// <summary>SQLite cache (prices, crawl progress, HTTP validators, inventory snapshots).</summary>
public sealed class LocalDatabase
{
    private readonly string connectionString;

    public LocalDatabase(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path, Pooling = true }.ToString();
        using var c = Open();
        Exec(c, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS prices (
              market_hash_name TEXT PRIMARY KEY,
              price_cents INTEGER NOT NULL,
              currency INTEGER NOT NULL,
              listings INTEGER,
              source TEXT NOT NULL,
              fetched_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS kv (key TEXT PRIMARY KEY, value TEXT NOT NULL, updated_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS http_cache (
              url TEXT PRIMARY KEY, etag TEXT, last_modified TEXT, file TEXT NOT NULL, checked_at INTEGER NOT NULL);
            CREATE TABLE IF NOT EXISTS inventories (
              steam_id TEXT PRIMARY KEY, json TEXT NOT NULL, fetched_at INTEGER NOT NULL);
            """);
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        return c;
    }

    public static void Exec(SqliteConnection c, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args)
        {
            cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        }

        cmd.ExecuteNonQuery();
    }

    public string? GetValue(string key)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM kv WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetValue(string key, string value, DateTimeOffset now)
    {
        using var c = Open();
        Exec(c, "INSERT INTO kv(key, value, updated_at) VALUES ($k, $v, $t) ON CONFLICT(key) DO UPDATE SET value = $v, updated_at = $t",
            ("$k", key), ("$v", value), ("$t", now.ToUnixTimeSeconds()));
    }
}
