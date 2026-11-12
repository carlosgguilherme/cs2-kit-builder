using System.Globalization;
using KitBuilder.Core.Kits;

namespace KitBuilder.Presentation;

/// <summary>UI texts. pt-BR is the product language; "en" is ready for later.</summary>
public sealed class Texts(string culture = "pt-BR")
{
    private static readonly Dictionary<string, Dictionary<string, string>> Table = new()
    {
        ["pt-BR"] = new()
        {
            ["slot.knife"] = "Faca", ["slot.gloves"] = "Luvas", ["slot.ak47"] = "AK-47", ["slot.m4"] = "M4A4 / M4A1-S",
            ["slot.awp"] = "AWP", ["slot.deagle"] = "Desert Eagle", ["slot.pistol"] = "USP-S / Glock-18",
            ["kit.withinBudget"] = "Dentro do orçamento", ["kit.nearEdge"] = "No limite da margem", ["kit.outOfBudget"] = "Fora da margem",
            ["problem.NoSlots"] = "Escolha pelo menos um item para o kit.",
            ["problem.LockedItemUnavailable"] = "A skin travada em {0} não está disponível.",
            ["problem.NoCandidatesForSlot"] = "Não encontrei nenhuma skin para {0} com esses filtros.",
            ["problem.NoCandidatesForSlot.color"] = " Sem o filtro de cor, a mais barata custa {0}.",
            ["problem.BudgetTooLow.slot"] = "O orçamento não cobre {0}: a opção mais barata no tom escolhido custa {1}.",
            ["problem.BudgetTooLow"] = "O orçamento é baixo para este kit.",
            ["problem.BudgetTooLow.total"] = " O kit mais barato possível custa {0}.",
            ["problem.BudgetTooHigh"] = "Não dá para gastar todo o orçamento: o kit mais caro possível custa {0}. Este é o melhor kit encontrado.",
            ["suggestion.IncreaseBudget"] = "aumentar o orçamento", ["suggestion.DecreaseBudget"] = "diminuir o orçamento",
            ["suggestion.DisableSlot"] = "desligar esse item", ["suggestion.RelaxColor"] = "afrouxar a cor",
            ["suggestion.AllowStatTrakOrSouvenir"] = "aceitar StatTrak ou Souvenir",
            ["suggestion.RelaxExteriorOrFloat"] = "afrouxar exterior ou float", ["suggestion.AddSlots"] = "adicionar itens ao kit",
            ["suggestion.prefix"] = " Tente: ", ["alt.keeps"] = "mantém o kit na margem", ["alt.breaks"] = "tira o kit da margem",
        },
        ["en"] = new()
        {
            ["slot.knife"] = "Knife", ["slot.gloves"] = "Gloves", ["slot.ak47"] = "AK-47", ["slot.m4"] = "M4A4 / M4A1-S",
            ["slot.awp"] = "AWP", ["slot.deagle"] = "Desert Eagle", ["slot.pistol"] = "USP-S / Glock-18",
            ["kit.withinBudget"] = "Within budget", ["kit.nearEdge"] = "At the edge of the margin", ["kit.outOfBudget"] = "Outside the margin",
            ["problem.NoSlots"] = "Pick at least one item for the kit.",
            ["problem.LockedItemUnavailable"] = "The skin locked in {0} is not available.",
            ["problem.NoCandidatesForSlot"] = "No skin found for {0} with these filters.",
            ["problem.NoCandidatesForSlot.color"] = " Without the color filter, the cheapest costs {0}.",
            ["problem.BudgetTooLow.slot"] = "The budget does not cover {0}: the cheapest option in the chosen tone costs {1}.",
            ["problem.BudgetTooLow"] = "The budget is too low for this kit.",
            ["problem.BudgetTooLow.total"] = " The cheapest possible kit costs {0}.",
            ["problem.BudgetTooHigh"] = "The whole budget cannot be spent: the most expensive kit costs {0}. This is the best kit found.",
            ["suggestion.IncreaseBudget"] = "increase the budget", ["suggestion.DecreaseBudget"] = "decrease the budget",
            ["suggestion.DisableSlot"] = "turn that item off", ["suggestion.RelaxColor"] = "relax the color",
            ["suggestion.AllowStatTrakOrSouvenir"] = "allow StatTrak or Souvenir",
            ["suggestion.RelaxExteriorOrFloat"] = "relax exterior or float", ["suggestion.AddSlots"] = "add items to the kit",
            ["suggestion.prefix"] = " Try: ", ["alt.keeps"] = "keeps the kit in the margin", ["alt.breaks"] = "takes the kit out of the margin",
        },
    };

    private readonly Dictionary<string, string> map = Table.TryGetValue(culture, out var m) ? m : Table["pt-BR"];
    private readonly CultureInfo money = CultureInfo.GetCultureInfo("pt-BR");

    public string this[string key] => map.TryGetValue(key, out var v) ? v : key;

    public string Format(string key, params object[] args) => string.Format(CultureInfo.InvariantCulture, this[key], args);

    public string Slot(string slotId) => map.TryGetValue("slot." + slotId, out var v)
        ? v
        : slotId.StartsWith("weapon:", StringComparison.Ordinal) ? slotId["weapon:".Length..] : slotId;

    /// <summary>"R$ 1.234,56" (BRL).</summary>
    public string Money(long cents) => (cents / 100m).ToString("C2", money).Replace(' ', ' ');

    /// <summary>"+R$ 23,40" / "−R$ 5,00".</summary>
    public string Delta(long cents) => (cents < 0 ? "−" : "+") + Money(Math.Abs(cents));

    /// <summary>Human message for an engine problem (scope 6.3: explain and suggest).</summary>
    public string Problem(Infeasibility p)
    {
        string slot = p.SlotId is null ? "" : Slot(p.SlotId);
        string text = p.Reason switch
        {
            InfeasibilityReason.NoSlots => this["problem.NoSlots"],
            InfeasibilityReason.LockedItemUnavailable => Format("problem.LockedItemUnavailable", slot),
            InfeasibilityReason.NoCandidatesForSlot => Format("problem.NoCandidatesForSlot", slot)
                + (p.CheapestIgnoringColorCents is { } c ? Format("problem.NoCandidatesForSlot.color", Money(c)) : ""),
            InfeasibilityReason.BudgetTooLow => (p.SlotId is not null && p.CheapestCents is { } cheapest
                    ? Format("problem.BudgetTooLow.slot", slot, Money(cheapest))
                    : this["problem.BudgetTooLow"])
                + (p.MinimumTotalCents is { } min ? Format("problem.BudgetTooLow.total", Money(min)) : ""),
            InfeasibilityReason.BudgetTooHigh => Format("problem.BudgetTooHigh", Money(p.BoundCents ?? 0)),
            _ => p.Reason.ToString(),
        };
        if (p.Suggestions.Count > 0 && p.Reason != InfeasibilityReason.BudgetTooHigh)
        {
            text += this["suggestion.prefix"] + string.Join(", ", p.Suggestions.Select(s => this["suggestion." + s])) + ".";
        }

        return text;
    }
}
