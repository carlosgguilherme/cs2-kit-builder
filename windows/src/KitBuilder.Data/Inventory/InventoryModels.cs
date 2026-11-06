using KitBuilder.Core.Domain;

namespace KitBuilder.Data.Inventory;

public sealed record AppliedItem(string Name, string? ImageUrl);

public sealed record InventoryItem(
    string AssetId,
    string MarketHashName,
    string Name,
    string Type,
    string? Weapon,
    ItemCategory Category,
    string? RarityName,
    string? RarityColor,
    Exterior? Exterior,
    bool StatTrak,
    bool Souvenir,
    string? NameTag,
    IReadOnlyList<AppliedItem> Stickers,
    IReadOnlyList<AppliedItem> Charms,
    string? InspectLink,
    double? Float,
    int? PaintSeed,
    string? IconUrl,
    bool Tradable,
    bool Marketable);

public sealed record InventoryPage(IReadOnlyList<InventoryItem> Items, bool MoreItems, string? LastAssetId, int TotalCount);

public sealed record InventorySnapshot(
    string SteamId64,
    IReadOnlyList<InventoryItem> Items,
    DateTimeOffset FetchedAt,
    DateTimeOffset NextRefreshAllowedAt,
    bool FromCache);

public sealed class InventoryPrivateException(string steamId)
    : Exception($"Inventory of {steamId} is private or the profile is private.")
{
    public string SteamId { get; } = steamId;
}

public sealed class ProfileNotFoundException(string input) : Exception($"Steam profile not found: {input}");
