# Kit engine specification

Reference implementation: `windows/src/KitBuilder.Core` (`ColorScorer`, `KitEngine`, `KitScoring`).
The Android port must implement **exactly** these rules and pass `shared/test-fixtures/kit-engine/cases.json`
(golden picks) and `shared/test-fixtures/oklab.json`. Golden output is regenerated with
`KITBUILDER_UPDATE_GOLDEN=1 dotnet test windows/tests/KitBuilder.Core.Tests` and must be reviewed.

Determinism rules: iterate catalog and slots in input order; every sort is **stable**; explicit
tie-breaks use ordinal comparison of `itemId`; prices are integer cents.

## Color

- OKLab from sRGB (Ottosson). Distance used for matching:
  `d = sqrt((0.5·ΔL)² + Δa² + Δb²)` (lightness half-weighted: images contain shading).
- `sigma(t) = 0.08 + 0.17·t`, `threshold(t) = 0.45 − 0.35·t`, tolerance `t ∈ [0,1]` (0 strict, 1 free).
- `colorScore = max(Σ wᵢ·simᵢ, maxᵢ simᵢ·min(1, sqrt(wᵢ/0.20)))`, `simᵢ = exp(−(dᵢ/sigma)²)`;
  ×0.9 when `isMultiColor`. No profile → 0.
- Signature color of a skin: the heaviest palette color with chroma ≥ 0.05, else the heaviest color.
- Harmony of two skins: missing signature → 0.5; either signature with chroma < 0.05 → 0.7;
  else `exp(−(d/0.15)²)`.

## Candidate scores

- Effective price: 0 for owned items under `ZeroCost`, else market price. *Value* (for the budget
  distribution) is always the market price.
- `quality = 0.45·rarity + 0.35·exterior + 0.20·popularity`; rarity = (rank−1)/7 (Consumer 1 … Contraband 8);
  exterior FN 1, MW 0.8, FT 0.6, WW 0.4, BS 0.25, none 0.7; popularity = min(1, log10(1+volume)/4), unknown 0.5.
- `slotScore = 0.65·color + 0.35·quality` with a target color, else `quality`.
- Seed ≠ 0: `slotScore ×= 1 + 0.16·(u − 0.5)`, `u` = FNV-1a 64 over UTF-8 `"<seed>:<itemId>"`, `(h >> 11) / 2^53`.

## Kit score

`slotPart = Σ priorityᵢ·slotScoreᵢ / Σ priorityᵢ`; `harmony` = mean over all pairs (1 slot → 0.5);
`allocation = 1 − 0.5·Σ |valueᵢ/Σvalue − targetShareᵢ|`, `targetShareᵢ ∝ typicalWeightᵢ·priorityᵢ`
(knife 40, gloves 30, AK-47 8, AWP 8, M4 7, Desert Eagle 3.5, USP-S/Glock 3.5, other weapons 1.5);
`closeness = clamp(1 − |total − B| / (B·max(t, 0.05)), 0, 1)`.

- With target color: `0.55·slotPart + 0.20·harmony + 0.15·allocation + 0.10·closeness`
- Without: `0.55·slotPart + 0.10·harmony + 0.20·allocation + 0.15·closeness`

## Algorithm

1. **Band**: `t = clamp(tolerance, 0, 0.30)`, `max = floor(B·(1+t))`, `min = ceil(B·(1 − max(t, 0.05)))`
   (a 5 % underspend margin keeps `t = 0` satisfiable without ever exceeding B).
2. **Filter per slot** (accepted weapon/category, owned policy, StatTrak/Souvenir, worst exterior,
   float overlap, known price, color threshold). Locked slot → only the locked item (no filters).
   Empty slot → `NoCandidatesForSlot` (+ cheapest without color filter, suggestions).
3. `Σ cheapest > max` → `BudgetTooLow` with the slot whose cheapest option alone exceeds the budget
   (most expensive one), the minimum kit total and the cheapest option of every slot.
   `Σ most expensive < min` → best effort kit flagged `BudgetTooHigh` (lower bound dropped).
4. **Cap**: each slot keeps candidates with `price ≤ max − (Σ cheapest − cheapest of this slot)`.
5. **Prune** to 60 per slot: Pareto frontier price × slotScore (up to 30), 12 evenly spaced price
   samples, then best slotScore (ties: lower price, then itemId).
6. **Beam search**: slots ordered by `typicalWeight·priority` desc; prune states that cannot stay in
   the band; keep the best 32 states (key = Σ priority·slotScore + 0.2·mean pair harmony) in each of
   48 cost buckets. Complete kits ranked by kit score.
7. **Refine** the best kit: up to 8 passes; for each unlocked slot pick the replacement (from the
   capped pool) that most improves the kit score (> 1e−12) inside the band.
8. **Alternative kits** (up to 3): next ranked kits differing from every chosen kit in ≥ ⌈n/3⌉ slots
   (by skin); refined unless refinement breaks that diversity.
9. **Per-slot alternatives** (5 cheaper, 5 pricier), also for locked slots (computed from the slot's
   normal filtered candidates, issue #2): same slot, similar color (|Δscore| ≤ 0.20, or
   harmony ≥ 0.5 without target), one entry per skin/variant (exterior closest in price), with price
   delta and whether the kit stays in the band.
10. **Swap**: `ReplacePick` (others unchanged) or `Rebalance` (lock the new item, rebuild the rest).

## Measured performance

Synthetic 21,000-item catalog, 7 slots: ~53 ms Release / ~126 ms Debug (4 vCPU Linux); GitHub's
Windows runner is about 3x slower. Real catalog with real colors (16k priced variants): 9 ms for
"R$ 200, main items without knife/gloves, red".

Implementation notes (no effect on results): beam states are linked to their parent instead of
copying the pick array; harmony between pruned candidates is precomputed per slot pair; each cost
bucket keeps its best 32 states with an insertion-ordered bounded selection (same result as a stable
sort followed by Take).
