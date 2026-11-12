using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KitBuilder.Core.Color;
using KitBuilder.Core.Kits;

namespace KitBuilder.Presentation;

/// <summary>Supplies engine candidates and what the UI needs to draw an item.</summary>
public interface IKitCatalog
{
    Task<IReadOnlyList<KitCandidate>> GetCandidatesAsync(CancellationToken ct = default);

    ItemVisual GetVisual(KitCandidate candidate);
}

public sealed record ItemVisual(string? ImageUrl, string RarityColorHex, string MarketUrl);

public enum BudgetState
{
    Within,
    NearEdge,
    Out,
}

public sealed class SlotToggle(SlotDefinition slot, string label) : ObservableObject
{
    private bool isEnabled = true;
    private double priority = 1.0;

    public SlotDefinition Slot { get; } = slot;

    public string Label { get; } = label;

    public bool IsEnabled { get => isEnabled; set => SetProperty(ref isEnabled, value); }

    /// <summary>1 = normal; above 1 = "spend more here".</summary>
    public double Priority { get => priority; set => SetProperty(ref priority, value); }
}

public sealed record AlternativeViewModel(string SlotId, KitCandidate Candidate, string Name, string PriceText,
    string DeltaText, bool IsCheaper, bool KeepsKitWithinBand, string Hint, ItemVisual Visual);

public sealed record PickViewModel(string SlotId, string SlotLabel, KitCandidate Candidate, string Name, string PriceText,
    string RarityName, bool IsOwned, int ColorMatchPercent, ItemVisual Visual, IReadOnlyList<AlternativeViewModel> Alternatives);

/// <summary>"Montar kit" screen (scope 4.2): inputs, generation off the UI thread, swaps and totals.</summary>
public sealed class KitBuilderViewModel : ObservableObject
{
    private readonly IKitCatalog catalog;
    private readonly KitEngine engine = new();
    private readonly Texts texts;
    private CancellationTokenSource? running;
    private KitRequest? lastRequest;
    private KitResult? lastResult;
    private KitProposal? current;

    private bool useColor = true;
    private string targetColorHex = "#D32F2F";
    private double colorTolerance = 0.5;
    private double budget = 200;
    private double budgetTolerancePercent = 10;
    private bool allowStatTrak;
    private bool allowSouvenir;
    private bool isBusy;
    private string? problemMessage;
    private string totalText = "";
    private string deltaText = "";
    private string budgetStateText = "";
    private BudgetState budgetState;
    private int harmonyPercent;
    private int seed;

    public KitBuilderViewModel(IKitCatalog catalog, Texts? texts = null)
    {
        this.catalog = catalog;
        this.texts = texts ?? new Texts();
        Slots = new ObservableCollection<SlotToggle>(KitPresets.MainItems.Select(s => new SlotToggle(s, this.texts.Slot(s.Id))));
        GenerateCommand = new AsyncRelayCommand(() => GenerateAsync(0));
        GenerateAnotherCommand = new AsyncRelayCommand(() => GenerateAsync(seed + 1));
        SwapCommand = new RelayCommand<AlternativeViewModel>(a => Swap(a!), a => a is not null);
        SwapAndRebalanceCommand = new AsyncRelayCommand<AlternativeViewModel>(a => SwapAndRebalanceAsync(a!), a => a is not null);
    }

    public ObservableCollection<SlotToggle> Slots { get; }

    public ObservableCollection<PickViewModel> Picks { get; } = [];

    public ObservableCollection<string> AlternativeKitSummaries { get; } = [];

    public IAsyncRelayCommand GenerateCommand { get; }

    public IAsyncRelayCommand GenerateAnotherCommand { get; }

    public IRelayCommand<AlternativeViewModel> SwapCommand { get; }

    public IAsyncRelayCommand<AlternativeViewModel> SwapAndRebalanceCommand { get; }

    public bool UseColor { get => useColor; set => SetProperty(ref useColor, value); }

    public string TargetColorHex { get => targetColorHex; set => SetProperty(ref targetColorHex, value); }

    /// <summary>0 = strict, 1 = free.</summary>
    public double ColorTolerance { get => colorTolerance; set => SetProperty(ref colorTolerance, Math.Clamp(value, 0, 1)); }

    /// <summary>Budget in BRL.</summary>
    public double Budget { get => budget; set => SetProperty(ref budget, Math.Max(0, value)); }

    /// <summary>0 to 30.</summary>
    public double BudgetTolerancePercent
    {
        get => budgetTolerancePercent;
        set => SetProperty(ref budgetTolerancePercent, Math.Clamp(value, 0, 30));
    }

    public bool AllowStatTrak { get => allowStatTrak; set => SetProperty(ref allowStatTrak, value); }

    public bool AllowSouvenir { get => allowSouvenir; set => SetProperty(ref allowSouvenir, value); }

    public bool IsBusy { get => isBusy; private set => SetProperty(ref isBusy, value); }

    public string? ProblemMessage { get => problemMessage; private set => SetProperty(ref problemMessage, value); }

    public string TotalText { get => totalText; private set => SetProperty(ref totalText, value); }

    public string DeltaText { get => deltaText; private set => SetProperty(ref deltaText, value); }

    public BudgetState BudgetState { get => budgetState; private set => SetProperty(ref budgetState, value); }

    public string BudgetStateText { get => budgetStateText; private set => SetProperty(ref budgetStateText, value); }

    public int HarmonyPercent { get => harmonyPercent; private set => SetProperty(ref harmonyPercent, value); }

    public long TotalCents => current?.TotalCents ?? 0;

    public KitProposal? CurrentKit => current;

    public KitRequest BuildRequest(int requestSeed)
    {
        OkLab? target = null;
        if (UseColor)
        {
            try
            {
                target = OkLab.FromHex(TargetColorHex);
            }
            catch (FormatException)
            {
                target = null;
            }
        }

        return new KitRequest
        {
            TargetColor = target,
            ColorTolerance = ColorTolerance,
            BudgetCents = (long)Math.Round(Budget * 100),
            BudgetTolerance = BudgetTolerancePercent / 100,
            Slots = Slots.Where(s => s.IsEnabled).Select(s => new SlotRequest(s.Slot, s.Priority)).ToList(),
            AllowStatTrak = AllowStatTrak,
            AllowSouvenir = AllowSouvenir,
            Seed = requestSeed,
        };
    }

    public Task GenerateAsync(int requestSeed) => RunAsync(BuildRequest(requestSeed), requestSeed);

    private async Task RunAsync(KitRequest request, int requestSeed)
    {
        running?.Cancel();
        var cts = running = new CancellationTokenSource();
        IsBusy = true;
        try
        {
            var candidates = await catalog.GetCandidatesAsync(cts.Token).ConfigureAwait(true);
            var result = await Task.Run(() => engine.Build(request, candidates, cts.Token), cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            seed = requestSeed;
            lastRequest = request;
            lastResult = result;
            ProblemMessage = result.Problem is { } p ? texts.Problem(p) : null;
            AlternativeKitSummaries.Clear();
            foreach (var alt in result.AlternativeKits)
            {
                AlternativeKitSummaries.Add($"{texts.Money(alt.TotalCents)} · {string.Join(", ", alt.Picks.Select(x => x.Candidate.Name))}");
            }

            Show(result.Best, result);
        }
        catch (OperationCanceledException)
        {
            // A newer request replaced this one.
        }
        finally
        {
            if (ReferenceEquals(running, cts))
            {
                IsBusy = false;
            }
        }
    }

    /// <summary>Swap one piece and keep the others.</summary>
    public void Swap(AlternativeViewModel alternative)
    {
        if (lastRequest is null || current is null || lastResult is null)
        {
            return;
        }

        Show(engine.ReplacePick(lastRequest, current, alternative.SlotId, alternative.Candidate), lastResult);
    }

    /// <summary>Swap one piece and rebuild the rest of the kit around it.</summary>
    public Task SwapAndRebalanceAsync(AlternativeViewModel alternative)
    {
        if (lastRequest is null)
        {
            return Task.CompletedTask;
        }

        var slots = lastRequest.Slots
            .Select(s => s.Slot.Id == alternative.SlotId ? s with { LockedItemId = alternative.Candidate.ItemId } : s).ToList();
        return RunAsync(lastRequest with { Slots = slots }, seed);
    }

    private void Show(KitProposal? kit, KitResult result)
    {
        current = kit;
        Picks.Clear();
        if (kit is null)
        {
            TotalText = DeltaText = BudgetStateText = "";
            HarmonyPercent = 0;
            OnPropertyChanged(nameof(TotalCents));
            return;
        }

        foreach (var pick in kit.Picks)
        {
            var alts = new List<AlternativeViewModel>();
            if (result.AlternativesBySlot.TryGetValue(pick.SlotId, out var slotAlts))
            {
                // Deltas are relative to the piece currently shown. After a swap the engine's original
                // pick is offered too, so the user can always go back.
                var options = slotAlts.Cheaper.Concat(slotAlts.Pricier).Select(a => (a.Candidate, a.EffectivePriceCents)).ToList();
                if (result.Best?.Picks.FirstOrDefault(x => x.SlotId == pick.SlotId) is { } original)
                {
                    options.Add((original.Candidate, original.EffectivePriceCents));
                }

                foreach (var a in options.Where(a => a.Candidate.ItemId != pick.Candidate.ItemId)
                             .OrderBy(a => a.EffectivePriceCents).ThenBy(a => a.Candidate.ItemId, StringComparer.Ordinal))
                {
                    long delta = a.EffectivePriceCents - pick.EffectivePriceCents;
                    long newTotal = kit.TotalCents + delta;
                    bool keeps = newTotal >= result.BandMinCents && newTotal <= result.BandMaxCents;
                    alts.Add(new AlternativeViewModel(pick.SlotId, a.Candidate, a.Candidate.Name, texts.Money(a.EffectivePriceCents),
                        texts.Delta(delta), delta < 0, keeps, texts[keeps ? "alt.keeps" : "alt.breaks"], catalog.GetVisual(a.Candidate)));
                }
            }

            Picks.Add(new PickViewModel(pick.SlotId, texts.Slot(pick.SlotId), pick.Candidate, pick.Candidate.Name,
                texts.Money(pick.EffectivePriceCents), pick.Candidate.Rarity.ToString(), pick.Candidate.Owned,
                (int)Math.Round(pick.ColorScore * 100), catalog.GetVisual(pick.Candidate), alts));
        }

        TotalText = texts.Money(kit.TotalCents);
        DeltaText = $"{texts.Delta(kit.DeltaCents)} ({kit.DeltaRatio * 100:+0.0;−0.0}%)".Replace('.', ',');
        HarmonyPercent = (int)Math.Round(kit.ColorHarmony * 100);
        long margin = Math.Max(1, result.BandMaxCents - kit.BudgetCents);
        BudgetState = !kit.WithinBudgetBand
            ? BudgetState.Out
            : kit.TotalCents > kit.BudgetCents + margin * 0.8 || kit.TotalCents < result.BandMinCents + (kit.BudgetCents - result.BandMinCents) * 0.2
                ? BudgetState.NearEdge
                : BudgetState.Within;
        BudgetStateText = texts[BudgetState switch
        {
            BudgetState.Within => "kit.withinBudget",
            BudgetState.NearEdge => "kit.nearEdge",
            _ => "kit.outOfBudget",
        }];
        OnPropertyChanged(nameof(TotalCents));
    }
}
