using KitBuilder.Core.Domain;

namespace KitBuilder.Core.Kits;

/// <summary>
/// A position in the kit. A candidate fits the slot when its weapon is listed in
/// <see cref="Weapons"/> or its category is listed in <see cref="Categories"/>.
/// <see cref="TypicalWeight"/> is the usual share of the budget for the slot.
/// </summary>
public sealed record SlotDefinition(
    string Id,
    IReadOnlySet<string> Weapons,
    IReadOnlySet<ItemCategory> Categories,
    double TypicalWeight)
{
    public bool Accepts(KitCandidate c) => Weapons.Contains(c.Weapon) || Categories.Contains(c.Category);

    public static SlotDefinition ForWeapons(string id, double typicalWeight, params string[] weapons) =>
        new(id, new HashSet<string>(weapons, StringComparer.Ordinal), new HashSet<ItemCategory>(), typicalWeight);

    public static SlotDefinition ForCategory(string id, double typicalWeight, ItemCategory category) =>
        new(id, new HashSet<string>(StringComparer.Ordinal), new HashSet<ItemCategory> { category }, typicalWeight);
}

/// <summary>Built-in slot sets (section 4.2 of the scope).</summary>
public static class KitPresets
{
    public static readonly SlotDefinition Knife = SlotDefinition.ForCategory("knife", 40, ItemCategory.Knife);
    public static readonly SlotDefinition Gloves = SlotDefinition.ForCategory("gloves", 30, ItemCategory.Gloves);
    public static readonly SlotDefinition Ak47 = SlotDefinition.ForWeapons("ak47", 8, "AK-47");
    public static readonly SlotDefinition M4 = SlotDefinition.ForWeapons("m4", 7, "M4A4", "M4A1-S");
    public static readonly SlotDefinition Awp = SlotDefinition.ForWeapons("awp", 8, "AWP");
    public static readonly SlotDefinition DesertEagle = SlotDefinition.ForWeapons("deagle", 3.5, "Desert Eagle");
    public static readonly SlotDefinition StartPistol = SlotDefinition.ForWeapons("pistol", 3.5, "USP-S", "Glock-18");

    /// <summary>"Main items" preset: knife, gloves, AK-47, M4, AWP, Desert Eagle, USP-S/Glock-18.</summary>
    public static IReadOnlyList<SlotDefinition> MainItems { get; } =
        [Knife, Gloves, Ak47, M4, Awp, DesertEagle, StartPistol];

    /// <summary>Weapons available to each side (catalog weapon names).</summary>
    public static IReadOnlyDictionary<TeamSide, IReadOnlyList<string>> WeaponsBySide { get; } =
        new Dictionary<TeamSide, IReadOnlyList<string>>
        {
            [TeamSide.CounterTerrorist] =
            [
                "USP-S", "P2000", "Dual Berettas", "P250", "Five-SeveN", "CZ75-Auto", "Desert Eagle", "R8 Revolver",
                "MP9", "MP7", "MP5-SD", "UMP-45", "P90", "PP-Bizon",
                "FAMAS", "M4A4", "M4A1-S", "AUG", "SSG 08", "AWP", "SCAR-20",
                "Nova", "XM1014", "MAG-7", "M249", "Negev", "Zeus x27",
            ],
            [TeamSide.Terrorist] =
            [
                "Glock-18", "Dual Berettas", "P250", "Tec-9", "CZ75-Auto", "Desert Eagle", "R8 Revolver",
                "MAC-10", "MP7", "MP5-SD", "UMP-45", "P90", "PP-Bizon",
                "Galil AR", "AK-47", "SG 553", "SSG 08", "AWP", "G3SG1",
                "Nova", "XM1014", "Sawed-Off", "M249", "Negev", "Zeus x27",
            ],
        };

    /// <summary>"Whole inventory" preset: knife, gloves and one slot per weapon of the side.</summary>
    public static IReadOnlyList<SlotDefinition> FullInventory(TeamSide side)
    {
        IEnumerable<string> weapons = side == TeamSide.Both
            ? WeaponsBySide[TeamSide.CounterTerrorist].Concat(WeaponsBySide[TeamSide.Terrorist])
            : WeaponsBySide[side];
        var slots = new List<SlotDefinition> { Knife, Gloves };
        foreach (var w in weapons.Distinct(StringComparer.Ordinal))
        {
            slots.Add(SlotDefinition.ForWeapons("weapon:" + w, TypicalWeaponWeight(w), w));
        }

        return slots;
    }

    private static double TypicalWeaponWeight(string weapon) => weapon switch
    {
        "AK-47" or "AWP" => 8,
        "M4A4" or "M4A1-S" => 7,
        "Desert Eagle" or "USP-S" or "Glock-18" => 3.5,
        _ => 1.5,
    };
}
