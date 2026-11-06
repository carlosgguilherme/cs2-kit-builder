using KitBuilder.Core.Color;
using KitBuilder.Core.Domain;

namespace KitBuilder.Core.Kits;

/// <summary>
/// A purchasable (or owned) item considered by the engine. Prices are in cents of
/// the display currency (BRL by default) to keep arithmetic exact.
/// </summary>
public sealed record KitCandidate(
    string ItemId,
    string SkinId,
    string Name,
    string Weapon,
    ItemCategory Category,
    Rarity Rarity,
    Exterior? Exterior,
    bool StatTrak,
    bool Souvenir,
    long PriceCents,
    SkinColorProfile? Colors,
    int? SalesVolume = null,
    double? FloatMin = null,
    double? FloatMax = null,
    bool Owned = false);

public enum OwnedItemsPolicy
{
    /// <summary>Owned items are not used.</summary>
    Ignore = 0,
    /// <summary>Owned items cost nothing.</summary>
    ZeroCost,
    /// <summary>Owned items are used at market price (as an option the user may keep).</summary>
    MarketPrice,
}

public sealed record SlotRequest(SlotDefinition Slot, double Priority = 1.0, string? LockedItemId = null);

public sealed record KitRequest
{
    /// <summary>Target color, or null for "no color preference".</summary>
    public OkLab? TargetColor { get; init; }

    /// <summary>0 = strict, 1 = free.</summary>
    public double ColorTolerance { get; init; } = 0.5;

    public required long BudgetCents { get; init; }

    /// <summary>Budget margin, 0 to 0.30 (default 0.10).</summary>
    public double BudgetTolerance { get; init; } = 0.10;

    public required IReadOnlyList<SlotRequest> Slots { get; init; }

    public bool AllowStatTrak { get; init; }

    public bool AllowSouvenir { get; init; }

    /// <summary>Worst acceptable exterior (e.g. FieldTested excludes WW and BS).</summary>
    public Exterior? WorstExterior { get; init; }

    public double? MinFloat { get; init; }

    public double? MaxFloat { get; init; }

    public OwnedItemsPolicy OwnedItems { get; init; } = OwnedItemsPolicy.ZeroCost;

    /// <summary>0 = canonical result; any other value gives a different, reproducible combination.</summary>
    public int Seed { get; init; }

    public int MaxAlternativeKits { get; init; } = 3;

    public int AlternativesPerSide { get; init; } = 5;
}

public sealed record KitPick(
    string SlotId,
    KitCandidate Candidate,
    long EffectivePriceCents,
    double ColorScore,
    double QualityScore,
    double SlotScore);

public sealed record KitProposal(
    IReadOnlyList<KitPick> Picks,
    long TotalCents,
    long BudgetCents,
    bool WithinBudgetBand,
    double Score,
    double ColorHarmony)
{
    public long DeltaCents => TotalCents - BudgetCents;

    public double DeltaRatio => BudgetCents == 0 ? 0 : (double)DeltaCents / BudgetCents;
}

public sealed record AlternativeOption(KitCandidate Candidate, long EffectivePriceCents, long DeltaCents,
    double ColorScore, bool KeepsKitWithinBand);

public sealed record SlotAlternatives(string SlotId, IReadOnlyList<AlternativeOption> Cheaper,
    IReadOnlyList<AlternativeOption> Pricier);

public enum InfeasibilityReason
{
    NoCandidatesForSlot,
    LockedItemUnavailable,
    BudgetTooLow,
    BudgetTooHigh,
    NoSlots,
}

public enum Suggestion
{
    IncreaseBudget,
    DecreaseBudget,
    DisableSlot,
    RelaxColor,
    AllowStatTrakOrSouvenir,
    RelaxExteriorOrFloat,
    AddSlots,
}

/// <summary>
/// Why no kit fits. Text is produced by the UI from these codes (i18n).
/// <see cref="SlotId"/>/<see cref="CheapestCents"/>: the slot at fault and its cheapest option
/// (for BudgetTooLow: the slot whose cheapest option alone exceeds the budget, if any).
/// <see cref="CheapestIgnoringColorCents"/>: cheapest option of the slot without the color filter.
/// <see cref="BoundCents"/>: budget upper bound (BudgetTooLow) or maximum reachable total (BudgetTooHigh).
/// <see cref="MinimumTotalCents"/>: cheapest possible kit (BudgetTooLow).
/// <see cref="CheapestBySlot"/>: cheapest option of every slot (BudgetTooLow), so the UI can list them.
/// </summary>
public sealed record Infeasibility(
    InfeasibilityReason Reason,
    string? SlotId,
    long? CheapestCents,
    long? CheapestIgnoringColorCents,
    long? BoundCents,
    IReadOnlyList<Suggestion> Suggestions,
    long? MinimumTotalCents = null,
    IReadOnlyDictionary<string, long>? CheapestBySlot = null);

public sealed record KitResult(
    bool Feasible,
    KitProposal? Best,
    IReadOnlyList<KitProposal> AlternativeKits,
    IReadOnlyDictionary<string, SlotAlternatives> AlternativesBySlot,
    Infeasibility? Problem,
    long BandMinCents,
    long BandMaxCents);
