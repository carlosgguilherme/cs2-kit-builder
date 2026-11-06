using KitBuilder.Core.Domain;

namespace KitBuilder.Data.Catalog;

/// <summary>One market item (skin + exterior + StatTrak/Souvenir) from ByMykel skins_not_grouped.json.</summary>
public sealed record CatalogItem(
    string ItemId,
    string SkinId,
    string MarketHashName,
    string Name,
    string Weapon,
    ItemCategory Category,
    Rarity Rarity,
    string RarityColor,
    Exterior? Exterior,
    bool StatTrak,
    bool Souvenir,
    string? Phase,
    double? MinFloat,
    double? MaxFloat,
    TeamSide Team,
    string? ImageUrl,
    string? PatternName);
