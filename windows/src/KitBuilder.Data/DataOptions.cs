namespace KitBuilder.Data;

public sealed class DataOptions
{
    /// <summary>Folder for caches (catalog, images, SQLite).</summary>
    public string DataDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KitBuilder");

    public string CatalogBaseUrl { get; set; } = "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/";

    /// <summary>Catalog locale. ByMykel has no pt-BR folder today, so the parser falls back to "en".</summary>
    public IReadOnlyList<string> CatalogLocales { get; set; } = ["pt-BR", "en"];

    public TimeSpan CatalogCheckInterval { get; set; } = TimeSpan.FromDays(1);

    public TimeSpan PriceTtl { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Minimum interval between two full inventory loads of the same profile.</summary>
    public TimeSpan InventoryMinInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Minimum delay between two requests to steamcommunity.com.</summary>
    public TimeSpan SteamRequestInterval { get; set; } = TimeSpan.FromSeconds(3.5);

    /// <summary>Steam currency code; 7 = BRL.</summary>
    public int SteamCurrency { get; set; } = 7;

    public string UserAgent { get; set; } = "KitBuilder/0.1 (+noncommercial)";

    public string DatabasePath => Path.Combine(DataDirectory, "kitbuilder.db");
}
