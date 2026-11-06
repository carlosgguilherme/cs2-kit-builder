using System.Text;
using KitBuilder.Core.Color;
using KitBuilder.Core.Domain;

namespace KitBuilder.Core.Kits;

/// <summary>
/// Kit assembly engine: a multiple-choice knapsack (one candidate per slot under a
/// budget band) solved with a cost-bucketed beam search plus local refinement.
/// The rules are specified in docs/kit-engine.md and must match the Android port
/// (including iteration order and stable sorting, which make results deterministic).
/// </summary>
public sealed class KitEngine
{
    public const int CandidatesPerSlot = 60;
    public const double MinUnderspend = 0.05;
    private const int BeamBuckets = 48;
    private const int BeamPerBucket = 32;
    private const int PriceSpreadSamples = 12;
    private const int MaxRefinePasses = 8;
    private const double Epsilon = 1e-12;

    public KitResult Build(KitRequest request, IReadOnlyList<KitCandidate> catalog, CancellationToken ct = default)
    {
        var (lo, hi) = Band(request);
        if (request.Slots.Count == 0)
        {
            return Fail(new Infeasibility(InfeasibilityReason.NoSlots, null, null, null, null, [Suggestion.AddSlots]), lo, hi);
        }

        var ctx = new Context(request);
        int n = request.Slots.Count;

        // 1. Candidates per slot (all filters except price).
        var eligible = new List<Scored>[n];
        for (int s = 0; s < n; s++)
        {
            ct.ThrowIfCancellationRequested();
            var slotReq = request.Slots[s];
            if (slotReq.LockedItemId is { } lockedId)
            {
                var locked = catalog.FirstOrDefault(c => c.ItemId == lockedId && slotReq.Slot.Accepts(c)
                    && !(c.Owned && request.OwnedItems == OwnedItemsPolicy.Ignore));
                if (locked is null)
                {
                    return Fail(new Infeasibility(InfeasibilityReason.LockedItemUnavailable, slotReq.Slot.Id, null, null, null,
                        [Suggestion.DisableSlot]), lo, hi);
                }

                eligible[s] = [ctx.Score(locked)];
                continue;
            }

            eligible[s] = Filter(ctx, slotReq, catalog, applyColor: true);
            if (eligible[s].Count == 0)
            {
                return Fail(ExplainEmptySlot(ctx, slotReq, catalog, hi), lo, hi);
            }
        }

        // 2. Budget feasibility.
        var slotMin = eligible.Select(l => l.Min(x => x.Price)).ToArray();
        long minSum = slotMin.Sum();
        long maxSum = eligible.Sum(l => l.Max(x => x.Price));
        if (minSum > hi)
        {
            int worst = -1;
            for (int s = 0; s < n; s++)
            {
                if (slotMin[s] > hi && (worst < 0 || slotMin[s] > slotMin[worst]))
                {
                    worst = s;
                }
            }

            var sugg = new List<Suggestion> { Suggestion.IncreaseBudget, Suggestion.DisableSlot };
            if (ctx.HasTarget)
            {
                sugg.Add(Suggestion.RelaxColor);
            }

            return Fail(new Infeasibility(InfeasibilityReason.BudgetTooLow,
                worst >= 0 ? request.Slots[worst].Slot.Id : null, worst >= 0 ? slotMin[worst] : null,
                null, hi, sugg, minSum,
                request.Slots.Select((r, i) => (r.Slot.Id, slotMin[i])).ToDictionary(x => x.Id, x => x.Item2)), lo, hi);
        }

        Infeasibility? warning = null;
        long loEff = lo;
        if (maxSum < lo)
        {
            // The budget cannot be spent: return the best kit anyway, flagged.
            warning = new Infeasibility(InfeasibilityReason.BudgetTooHigh, null, null, null, maxSum,
                [Suggestion.DecreaseBudget, Suggestion.AddSlots, Suggestion.RelaxColor]);
            loEff = 0;
        }

        // 3. Each slot must leave room for the cheapest option of every other slot.
        var pool = new List<Scored>[n];
        for (int s = 0; s < n; s++)
        {
            long cap = hi - (minSum - slotMin[s]);
            pool[s] = eligible[s].Where(x => x.Price <= cap).ToList();
        }

        // 4. Search.
        var pruned = pool.Select(Prune).ToArray();
        var complete = BeamSearch(ctx, pruned, loEff, hi, ct);
        if (complete.Count == 0)
        {
            return Fail(new Infeasibility(InfeasibilityReason.BudgetTooLow, null, null, null, hi,
                [Suggestion.IncreaseBudget, Suggestion.DisableSlot], minSum), lo, hi);
        }

        var best = Refine(ctx, complete[0], pool, loEff, hi, ct);
        var chosen = new List<Scored[]> { best };
        int minDiff = Math.Max(1, (int)Math.Ceiling(n / 3.0));
        foreach (var kit in complete)
        {
            if (chosen.Count > request.MaxAlternativeKits)
            {
                break;
            }

            if (!chosen.All(k => Differences(k, kit) >= minDiff))
            {
                continue;
            }

            var refined = Refine(ctx, kit, pool, loEff, hi, ct);
            chosen.Add(chosen.All(k => Differences(k, refined) >= minDiff) ? refined : kit);
        }

        var alternatives = new Dictionary<string, SlotAlternatives>(StringComparer.Ordinal);
        for (int s = 0; s < n; s++)
        {
            alternatives[request.Slots[s].Slot.Id] = SlotOptions(ctx, best, s, eligible[s], loEff, hi);
        }

        return new KitResult(warning is null, ctx.ToProposal(best, lo, hi),
            chosen.Skip(1).Select(k => ctx.ToProposal(k, lo, hi)).ToList(), alternatives, warning, lo, hi);
    }

    /// <summary>Replaces one pick without touching the others ("swap" without rebalancing).</summary>
    public KitProposal ReplacePick(KitRequest request, KitProposal current, string slotId, KitCandidate replacement)
    {
        var ctx = new Context(request);
        var (lo, hi) = Band(request);
        var picks = current.Picks.Select(p => ctx.Score(p.SlotId == slotId ? replacement : p.Candidate)).ToArray();
        return ctx.ToProposal(picks, lo, hi);
    }

    /// <summary>Locks <paramref name="itemId"/> in the slot and rebuilds the rest of the kit.</summary>
    public KitResult Rebalance(KitRequest request, IReadOnlyList<KitCandidate> catalog, string slotId, string itemId,
        CancellationToken ct = default)
    {
        var slots = request.Slots.Select(s => s.Slot.Id == slotId ? s with { LockedItemId = itemId } : s).ToList();
        return Build(request with { Slots = slots }, catalog, ct);
    }

    /// <summary>Budget band: [B * (1 - max(t, 5%)), B * (1 + t)], t clamped to [0, 30%].</summary>
    public static (long Min, long Max) Band(KitRequest r)
    {
        double t = Math.Clamp(r.BudgetTolerance, 0, 0.30);
        long hi = (long)Math.Floor(r.BudgetCents * (1 + t));
        long lo = (long)Math.Ceiling(r.BudgetCents * (1 - Math.Max(t, MinUnderspend)));
        return (Math.Max(0, lo), hi);
    }

    private static KitResult Fail(Infeasibility problem, long lo, long hi) =>
        new(false, null, [], new Dictionary<string, SlotAlternatives>(), problem, lo, hi);

    private static List<Scored> Filter(Context ctx, SlotRequest slot, IReadOnlyList<KitCandidate> catalog,
        bool applyColor, bool applyVariantRules = true)
    {
        var r = ctx.Request;
        var list = new List<Scored>();
        foreach (var c in catalog)
        {
            if (!slot.Slot.Accepts(c) || (c.Owned && r.OwnedItems == OwnedItemsPolicy.Ignore))
            {
                continue;
            }

            if (applyVariantRules)
            {
                if ((c.StatTrak && !r.AllowStatTrak) || (c.Souvenir && !r.AllowSouvenir))
                {
                    continue;
                }

                if (r.WorstExterior is { } worst && c.Exterior is { } ext && ext > worst)
                {
                    continue;
                }

                if ((r.MinFloat is { } fmin && c.FloatMax is { } cmax && cmax < fmin)
                    || (r.MaxFloat is { } fmax && c.FloatMin is { } cmin && cmin > fmax))
                {
                    continue;
                }
            }

            var scored = ctx.Score(c);
            if (scored.Price <= 0 && !(c.Owned && r.OwnedItems == OwnedItemsPolicy.ZeroCost))
            {
                continue; // no known price
            }

            if (applyColor && ctx.HasTarget && scored.Color < ColorScorer.Threshold(r.ColorTolerance))
            {
                continue;
            }

            list.Add(scored);
        }

        return list;
    }

    private static Infeasibility ExplainEmptySlot(Context ctx, SlotRequest slot, IReadOnlyList<KitCandidate> catalog, long hi)
    {
        var sugg = new List<Suggestion>();
        long? cheapestNoColor = null;
        if (ctx.HasTarget)
        {
            var noColor = Filter(ctx, slot, catalog, applyColor: false);
            if (noColor.Count > 0)
            {
                cheapestNoColor = noColor.Min(x => x.Price);
                sugg.Add(Suggestion.RelaxColor);
            }
        }

        if (cheapestNoColor is null && Filter(ctx, slot, catalog, applyColor: false, applyVariantRules: false).Count > 0)
        {
            sugg.Add(Suggestion.AllowStatTrakOrSouvenir);
            sugg.Add(Suggestion.RelaxExteriorOrFloat);
        }

        sugg.Add(Suggestion.DisableSlot);
        return new Infeasibility(InfeasibilityReason.NoCandidatesForSlot, slot.Slot.Id, null, cheapestNoColor, hi, sugg);
    }

    /// <summary>
    /// Keeps the price x score Pareto frontier, a spread across the price range (so
    /// the lower budget bound stays reachable) and then the best remaining scores.
    /// </summary>
    private static List<Scored> Prune(List<Scored> all)
    {
        if (all.Count <= CandidatesPerSlot)
        {
            return all.OrderBy(x => x.Price).ThenBy(x => x.C.ItemId, StringComparer.Ordinal).ToList();
        }

        var byPrice = all.OrderBy(x => x.Price).ThenByDescending(x => x.SlotScore)
            .ThenBy(x => x.C.ItemId, StringComparer.Ordinal).ToList();
        var keep = new List<Scored>(CandidatesPerSlot);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Add(Scored x)
        {
            if (keep.Count < CandidatesPerSlot && seen.Add(x.C.ItemId))
            {
                keep.Add(x);
            }
        }

        double bestScore = double.NegativeInfinity;
        foreach (var x in byPrice)
        {
            if (x.SlotScore > bestScore + Epsilon)
            {
                bestScore = x.SlotScore;
                if (keep.Count < CandidatesPerSlot / 2)
                {
                    Add(x);
                }
            }
        }

        for (int i = 0; i < PriceSpreadSamples; i++)
        {
            Add(byPrice[(int)((long)i * (byPrice.Count - 1) / (PriceSpreadSamples - 1))]);
        }

        foreach (var x in all.OrderByDescending(x => x.SlotScore).ThenBy(x => x.Price)
                     .ThenBy(x => x.C.ItemId, StringComparer.Ordinal))
        {
            if (keep.Count >= CandidatesPerSlot)
            {
                break;
            }

            Add(x);
        }

        return keep.OrderBy(x => x.Price).ThenBy(x => x.C.ItemId, StringComparer.Ordinal).ToList();
    }

    /// <summary>Beam node: the chosen candidate of one slot plus a link to the previous choices.</summary>
    private sealed class BeamNode(BeamNode? parent, int candidate, long cost, double slotSum, double harmonySum, int pairs)
    {
        public BeamNode? Parent { get; } = parent;

        public int Candidate { get; } = candidate;

        public long Cost { get; } = cost;

        public double SlotSum { get; } = slotSum;

        public double HarmonySum { get; } = harmonySum;

        public int Pairs { get; } = pairs;

        public double Key => KeyOf(SlotSum, HarmonySum, Pairs);

        public static double KeyOf(double slotSum, double harmonySum, int pairs) =>
            slotSum + 0.2 * (pairs == 0 ? 0.5 : harmonySum / pairs);
    }

    private static List<Scored[]> BeamSearch(Context ctx, List<Scored>[] pruned, long lo, long hi, CancellationToken ct)
    {
        int n = pruned.Length;
        var slots = ctx.Request.Slots;
        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => slots[i].Slot.TypicalWeight * slots[i].Priority)
            .ThenBy(i => i).ToArray();
        var minRem = new long[n + 1];
        var maxRem = new long[n + 1];
        for (int k = n - 1; k >= 0; k--)
        {
            minRem[k] = minRem[k + 1] + pruned[order[k]].Min(x => x.Price);
            maxRem[k] = maxRem[k + 1] + pruned[order[k]].Max(x => x.Price);
        }

        // Harmony between every pair of pruned candidates of two slots, computed once.
        // harmony[j][k] (j < k, positions in "order") is a |pruned_j| x |pruned_k| matrix.
        var harmony = new double[n][][];
        for (int j = 0; j < n; j++)
        {
            harmony[j] = new double[n][];
            var a = pruned[order[j]];
            for (int k = j + 1; k < n; k++)
            {
                var b = pruned[order[k]];
                var m = new double[a.Count * b.Count];
                for (int x = 0; x < a.Count; x++)
                {
                    for (int y = 0; y < b.Count; y++)
                    {
                        m[x * b.Count + y] = Context.Harmony(b[y], a[x]);
                    }
                }

                harmony[j][k] = m;
            }
        }

        var beam = new List<BeamNode?> { null };
        var path = new int[n];
        for (int k = 0; k < n; k++)
        {
            ct.ThrowIfCancellationRequested();
            var cands = pruned[order[k]];
            double priority = slots[order[k]].Priority;
            // Best BeamPerBucket states of each cost bucket, equivalent to a stable sort by key
            // (descending) followed by Take: on equal keys the earlier state wins.
            var buckets = new BeamNode?[BeamBuckets][];
            var counts = new int[BeamBuckets];
            foreach (var state in beam)
            {
                // Choices of the previous positions, in position order.
                var node = state;
                for (int j = k - 1; j >= 0; j--)
                {
                    path[j] = node!.Candidate;
                    node = node.Parent;
                }

                long baseCost = state?.Cost ?? 0;
                double baseSlot = state?.SlotSum ?? 0, baseHarmony = state?.HarmonySum ?? 0;
                int pairs = (state?.Pairs ?? 0) + k;
                for (int c = 0; c < cands.Count; c++)
                {
                    long cost = baseCost + cands[c].Price;
                    if (cost + minRem[k + 1] > hi || cost + maxRem[k + 1] < lo)
                    {
                        continue;
                    }

                    double h = baseHarmony;
                    for (int j = 0; j < k; j++)
                    {
                        h += harmony[j][k][path[j] * cands.Count + c];
                    }

                    double slotSum = baseSlot + priority * cands[c].SlotScore;
                    double key = BeamNode.KeyOf(slotSum, h, pairs);
                    int b = (int)(cost * BeamBuckets / (hi + 1));
                    var arr = buckets[b] ??= new BeamNode?[BeamPerBucket];
                    int count = counts[b];
                    if (count == BeamPerBucket && key <= arr[count - 1]!.Key)
                    {
                        continue;
                    }

                    int pos = count < BeamPerBucket ? count : BeamPerBucket - 1;
                    while (pos > 0 && arr[pos - 1]!.Key < key)
                    {
                        if (pos < BeamPerBucket)
                        {
                            arr[pos] = arr[pos - 1];
                        }

                        pos--;
                    }

                    arr[pos] = new BeamNode(state, c, cost, slotSum, h, pairs);
                    counts[b] = Math.Min(BeamPerBucket, count + 1);
                }
            }

            beam = [];
            for (int b = 0; b < BeamBuckets; b++)
            {
                for (int i = 0; i < counts[b]; i++)
                {
                    beam.Add(buckets[b]![i]);
                }
            }

            if (beam.Count == 0)
            {
                return [];
            }
        }

        var complete = new List<Scored[]>(beam.Count);
        foreach (var state in beam)
        {
            var picks = new Scored[n];
            var node = state;
            for (int k = n - 1; k >= 0; k--)
            {
                picks[order[k]] = pruned[order[k]][node!.Candidate];
                node = node.Parent;
            }

            complete.Add(picks);
        }

        return complete
            .Select(p => (Picks: p, Score: ctx.FullScore(p)))
            .OrderByDescending(x => x.Score)
            .Select(x => x.Picks)
            .ToList();
    }

    /// <summary>
    /// Local search: for each slot (in request order) take the replacement that most
    /// improves the kit score while staying in the band; repeat until no improvement.
    /// Scores are evaluated incrementally (O(slots) per candidate).
    /// </summary>
    private static Scored[] Refine(Context ctx, Scored[] start, List<Scored>[] pool, long lo, long hi, CancellationToken ct)
    {
        var current = (Scored[])start.Clone();
        int n = current.Length;
        for (int pass = 0; pass < MaxRefinePasses; pass++)
        {
            bool improved = false;
            for (int s = 0; s < n; s++)
            {
                ct.ThrowIfCancellationRequested();
                if (ctx.Request.Slots[s].LockedItemId is not null)
                {
                    continue;
                }

                var eval = new SwapEvaluator(ctx, current, s);
                var original = current[s];
                double bestScore = eval.Score(original);
                Scored? bestSwap = null;
                foreach (var cand in pool[s])
                {
                    if (ReferenceEquals(cand, original))
                    {
                        continue;
                    }

                    long newTotal = eval.OthersCost + cand.Price;
                    if (newTotal < lo || newTotal > hi)
                    {
                        continue;
                    }

                    double score = eval.Score(cand);
                    if (score > bestScore + Epsilon)
                    {
                        bestScore = score;
                        bestSwap = cand;
                    }
                }

                if (bestSwap is not null)
                {
                    current[s] = bestSwap;
                    improved = true;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return current;
    }

    /// <summary>Kit score with slot <c>s</c> replaced, computed from precomputed parts of the other slots.</summary>
    private readonly struct SwapEvaluator
    {
        private readonly Context ctx;
        private readonly Scored[] picks;
        private readonly int slot;
        private readonly double othersSlotSum;
        private readonly double othersHarmony;
        private readonly long othersValue;

        public SwapEvaluator(Context ctx, Scored[] picks, int slot)
        {
            this.ctx = ctx;
            this.picks = picks;
            this.slot = slot;
            int n = picks.Length;
            double slotSum = 0, harmony = 0;
            long cost = 0, value = 0;
            for (int i = 0; i < n; i++)
            {
                if (i == slot)
                {
                    continue;
                }

                slotSum += ctx.Request.Slots[i].Priority * picks[i].SlotScore;
                cost += picks[i].Price;
                value += picks[i].Value;
                for (int j = i + 1; j < n; j++)
                {
                    if (j != slot)
                    {
                        harmony += Context.Harmony(picks[i], picks[j]);
                    }
                }
            }

            othersSlotSum = slotSum;
            othersHarmony = harmony;
            OthersCost = cost;
            othersValue = value;
        }

        public long OthersCost { get; }

        public double Score(Scored cand)
        {
            int n = picks.Length;
            double harmony = othersHarmony;
            for (int i = 0; i < n; i++)
            {
                if (i != slot)
                {
                    harmony += Context.Harmony(cand, picks[i]);
                }
            }

            long value = othersValue + cand.Value;
            double deviation = 0;
            for (int i = 0; i < n; i++)
            {
                long v = i == slot ? cand.Value : picks[i].Value;
                deviation += Math.Abs(ctx.Share(v, value, n) - ctx.TargetShare[i]);
            }

            int pairs = n * (n - 1) / 2;
            return ctx.Compose(
                (othersSlotSum + ctx.Request.Slots[slot].Priority * cand.SlotScore) / ctx.PrioritySum,
                pairs == 0 ? 0.5 : harmony / pairs,
                1 - 0.5 * deviation,
                ctx.Closeness(OthersCost + cand.Price));
        }
    }

    private static int Differences(Scored[] a, Scored[] b)
    {
        int d = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i].C.SkinId != b[i].C.SkinId)
            {
                d++;
            }
        }

        return d;
    }

    private static SlotAlternatives SlotOptions(Context ctx, Scored[] kit, int s, List<Scored> eligible, long lo, long hi)
    {
        var chosen = kit[s];
        long total = kit.Sum(x => x.Price);
        bool Similar(Scored x) => ctx.HasTarget
            ? Math.Abs(x.Color - chosen.Color) <= 0.20
            : Context.Harmony(x, chosen) >= 0.5;

        var similar = eligible
            .Where(x => x.C.ItemId != chosen.C.ItemId && x.Price <= hi && Similar(x))
            // One entry per skin/variant and side: the exterior whose price is closest to the current pick.
            .GroupBy(x => (x.C.SkinId, x.C.StatTrak, x.C.Souvenir, Cheaper: x.Price < chosen.Price))
            .Select(g => g.OrderBy(x => Math.Abs(x.Price - chosen.Price)).ThenBy(x => x.C.ItemId, StringComparer.Ordinal).First())
            .ToList();
        int k = ctx.Request.AlternativesPerSide;
        AlternativeOption Opt(Scored x)
        {
            long newTotal = total - chosen.Price + x.Price;
            return new AlternativeOption(x.C, x.Price, x.Price - chosen.Price, x.Color, newTotal >= lo && newTotal <= hi);
        }

        var cheaper = similar.Where(x => x.Price < chosen.Price)
            .OrderByDescending(x => x.Price).ThenBy(x => x.C.ItemId, StringComparer.Ordinal).Take(k).Select(Opt).ToList();
        var pricier = similar.Where(x => x.Price >= chosen.Price)
            .OrderBy(x => x.Price).ThenBy(x => x.C.ItemId, StringComparer.Ordinal).Take(k).Select(Opt).ToList();
        return new SlotAlternatives(ctx.Request.Slots[s].Slot.Id, cheaper, pricier);
    }

    /// <summary>
    /// A candidate with its precomputed scores. <see cref="Price"/> is what the user pays
    /// (0 for owned items under ZeroCost); <see cref="Value"/> is the market price, used
    /// for the budget distribution so owned items still count as a valuable piece.
    /// </summary>
    internal sealed record Scored(KitCandidate C, long Price, long Value, double Color, double Quality, double SlotScore,
        PaletteColor? Signature);

    private sealed class Context
    {
        public Context(KitRequest request)
        {
            Request = request;
            int n = request.Slots.Count;
            double weightSum = 0;
            foreach (var s in request.Slots)
            {
                PrioritySum += s.Priority;
                weightSum += s.Slot.TypicalWeight * s.Priority;
            }

            TargetShare = request.Slots
                .Select(s => weightSum > 0 ? s.Slot.TypicalWeight * s.Priority / weightSum : 1.0 / n).ToArray();
        }

        public KitRequest Request { get; }

        public bool HasTarget => Request.TargetColor is not null;

        public double PrioritySum { get; }

        public double[] TargetShare { get; }

        public static double Harmony(Scored a, Scored b) => ColorScorer.Harmony(a.Signature, b.Signature);

        public Scored Score(KitCandidate c)
        {
            long price = c.Owned && Request.OwnedItems == OwnedItemsPolicy.ZeroCost ? 0 : c.PriceCents;
            double color = HasTarget ? ColorScorer.Score(Request.TargetColor!.Value, c.Colors, Request.ColorTolerance) : 0;
            double quality = KitScoring.Quality(c);
            double slotScore = HasTarget ? 0.65 * color + 0.35 * quality : quality;
            if (Request.Seed != 0)
            {
                slotScore *= 1 + 0.16 * (KitScoring.UnitHash(Request.Seed, c.ItemId) - 0.5);
            }

            return new Scored(c, price, Math.Max(0, c.PriceCents), color, quality, slotScore, c.Colors?.Signature);
        }

        public double Share(long value, long totalValue, int n) => totalValue > 0 ? (double)value / totalValue : 1.0 / n;

        public double Closeness(long total)
        {
            double margin = Request.BudgetCents * Math.Max(Math.Clamp(Request.BudgetTolerance, 0, 0.30), MinUnderspend);
            return margin > 0 ? Math.Clamp(1 - Math.Abs(total - Request.BudgetCents) / margin, 0, 1) : 1;
        }

        public double Compose(double slotPart, double harmony, double allocation, double closeness) => HasTarget
            ? 0.55 * slotPart + 0.20 * harmony + 0.15 * allocation + 0.10 * closeness
            : 0.55 * slotPart + 0.10 * harmony + 0.20 * allocation + 0.15 * closeness;

        public double FullScore(Scored[] picks)
        {
            int n = picks.Length;
            double slotSum = 0, harmony = 0;
            long total = 0, value = 0;
            for (int i = 0; i < n; i++)
            {
                slotSum += Request.Slots[i].Priority * picks[i].SlotScore;
                total += picks[i].Price;
                value += picks[i].Value;
                for (int j = i + 1; j < n; j++)
                {
                    harmony += Harmony(picks[i], picks[j]);
                }
            }

            double deviation = 0;
            for (int i = 0; i < n; i++)
            {
                deviation += Math.Abs(Share(picks[i].Value, value, n) - TargetShare[i]);
            }

            int pairs = n * (n - 1) / 2;
            return Compose(slotSum / PrioritySum, pairs == 0 ? 0.5 : harmony / pairs, 1 - 0.5 * deviation, Closeness(total));
        }

        public KitProposal ToProposal(Scored[] picks, long lo, long hi)
        {
            long total = picks.Sum(x => x.Price);
            int n = picks.Length;
            double harmony = 0;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    harmony += Harmony(picks[i], picks[j]);
                }
            }

            var list = picks.Select((p, i) => new KitPick(Request.Slots[i].Slot.Id, p.C, p.Price, p.Color, p.Quality, p.SlotScore))
                .ToList();
            int pairs = n * (n - 1) / 2;
            return new KitProposal(list, total, Request.BudgetCents, total >= lo && total <= hi, FullScore(picks),
                pairs == 0 ? 1.0 : harmony / pairs);
        }
    }
}

/// <summary>Scoring helpers shared with the Android port (see docs/kit-engine.md).</summary>
public static class KitScoring
{
    public static double Quality(KitCandidate c)
    {
        double rarity = c.Rarity == Rarity.Unknown ? 0 : Math.Min(1.0, ((int)c.Rarity - 1) / 7.0);
        double exterior = c.Exterior switch
        {
            Exterior.FactoryNew => 1.0,
            Exterior.MinimalWear => 0.8,
            Exterior.FieldTested => 0.6,
            Exterior.WellWorn => 0.4,
            Exterior.BattleScarred => 0.25,
            _ => 0.7,
        };
        double popularity = c.SalesVolume is { } v ? Math.Min(1.0, Math.Log10(1 + Math.Max(0, v)) / 4.0) : 0.5;
        return 0.45 * rarity + 0.35 * exterior + 0.20 * popularity;
    }

    /// <summary>Deterministic value in [0,1) from (seed, itemId): FNV-1a 64-bit over UTF-8 "seed:itemId".</summary>
    public static double UnitHash(int seed, string itemId)
    {
        ulong h = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(seed.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + itemId))
        {
            h ^= b;
            h *= 1099511628211UL;
        }

        return (h >> 11) / (double)(1UL << 53);
    }
}
