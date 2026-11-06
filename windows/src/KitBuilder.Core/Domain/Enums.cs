namespace KitBuilder.Core.Domain;

/// <summary>Item rarity. Numeric value is the rank used for scoring.</summary>
public enum Rarity
{
    Unknown = 0,
    ConsumerGrade = 1,
    IndustrialGrade = 2,
    MilSpec = 3,
    Restricted = 4,
    Classified = 5,
    Covert = 6,
    Extraordinary = 7,
    Contraband = 8,
}

/// <summary>Exterior (wear). Lower value = better condition.</summary>
public enum Exterior
{
    FactoryNew = 1,
    MinimalWear = 2,
    FieldTested = 3,
    WellWorn = 4,
    BattleScarred = 5,
}

public enum ItemCategory
{
    Other = 0,
    Rifle,
    Pistol,
    Smg,
    Heavy,
    Knife,
    Gloves,
    Equipment,
    Agent,
}

public enum TeamSide
{
    Both = 0,
    CounterTerrorist,
    Terrorist,
}

public static class DomainNames
{
    public static Rarity ParseRarity(string? name) => name switch
    {
        "Consumer Grade" => Rarity.ConsumerGrade,
        "Industrial Grade" => Rarity.IndustrialGrade,
        "Mil-Spec Grade" or "Mil-Spec" => Rarity.MilSpec,
        "Restricted" => Rarity.Restricted,
        "Classified" => Rarity.Classified,
        "Covert" => Rarity.Covert,
        "Extraordinary" => Rarity.Extraordinary,
        "Contraband" => Rarity.Contraband,
        _ => Rarity.Unknown,
    };

    public static Exterior? ParseExterior(string? name) => name switch
    {
        "Factory New" => Exterior.FactoryNew,
        "Minimal Wear" => Exterior.MinimalWear,
        "Field-Tested" => Exterior.FieldTested,
        "Well-Worn" => Exterior.WellWorn,
        "Battle-Scarred" => Exterior.BattleScarred,
        _ => null,
    };

    /// <summary>Maps ByMykel category names (and Steam type words) to <see cref="ItemCategory"/>.</summary>
    public static ItemCategory ParseCategory(string? name) => name switch
    {
        "Rifles" => ItemCategory.Rifle,
        "Pistols" => ItemCategory.Pistol,
        "SMGs" => ItemCategory.Smg,
        "Heavy" => ItemCategory.Heavy,
        "Knives" => ItemCategory.Knife,
        "Gloves" => ItemCategory.Gloves,
        "Equipment" => ItemCategory.Equipment,
        "Agents" => ItemCategory.Agent,
        _ => ItemCategory.Other,
    };

    public static TeamSide ParseTeam(string? id) => id switch
    {
        "counter-terrorists" => TeamSide.CounterTerrorist,
        "terrorists" => TeamSide.Terrorist,
        _ => TeamSide.Both,
    };

    /// <summary>Standard float range of each exterior.</summary>
    public static (double Min, double Max) FloatBand(Exterior exterior) => exterior switch
    {
        Exterior.FactoryNew => (0.00, 0.07),
        Exterior.MinimalWear => (0.07, 0.15),
        Exterior.FieldTested => (0.15, 0.38),
        Exterior.WellWorn => (0.38, 0.45),
        _ => (0.45, 1.00),
    };
}
