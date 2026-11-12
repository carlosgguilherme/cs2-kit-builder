using System.Text.Json.Nodes;
using KitBuilder.Core.Color;
using KitBuilder.Core.Domain;
using KitBuilder.Core.Kits;

namespace KitBuilder.Presentation.Tests;

internal sealed class FixtureCatalog : IKitCatalog
{
    public IReadOnlyList<KitCandidate> Candidates { get; } = Load();

    public Task<IReadOnlyList<KitCandidate>> GetCandidatesAsync(CancellationToken ct = default) => Task.FromResult(Candidates);

    public ItemVisual GetVisual(KitCandidate c) => new(null, "#eb4b4b", "https://steamcommunity.com/market/listings/730/" + c.Name);

    private static List<KitCandidate> Load()
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "kit-engine", "catalog.json")))!;
        return doc["candidates"]!.AsArray().Select(c => new KitCandidate(
            (string)c!["itemId"]!, (string)c["skinId"]!, (string)c["name"]!, (string)c["weapon"]!,
            DomainNames.ParseCategory((string)c["category"]!), DomainNames.ParseRarity((string)c["rarity"]!),
            DomainNames.ParseExterior((string?)c["exterior"]), (bool)c["statTrak"]!, (bool)c["souvenir"]!, (long)c["priceCents"]!,
            new SkinColorProfile(c["colors"]!.AsArray().Select(x => new PaletteColor(OkLab.FromHex((string)x!["hex"]!),
                (double)x["weight"]!, (string)x["hex"]!)).ToList(), (bool)c["isMultiColor"]!),
            (int?)c["salesVolume"], Owned: (bool)c["owned"]!)).ToList();
    }
}

public class ViewModelTests
{
    private static KitBuilderViewModel NewVm(params string[] enabledSlots)
    {
        var vm = new KitBuilderViewModel(new FixtureCatalog());
        foreach (var s in vm.Slots)
        {
            s.IsEnabled = enabledSlots.Contains(s.Slot.Id);
        }

        return vm;
    }

    [Fact]
    public async Task GeneratesTheAcceptanceKitWithTotalsInBrl()
    {
        var vm = NewVm("ak47", "m4", "awp", "deagle", "pistol");
        vm.TargetColorHex = "#d32f2f";
        vm.Budget = 200;
        await vm.GenerateAsync(0);

        Assert.Null(vm.ProblemMessage);
        Assert.Equal(5, vm.Picks.Count);
        Assert.Equal("AK-47", vm.Picks[0].SlotLabel);
        Assert.Equal("R$ 192,00", vm.TotalText);
        Assert.StartsWith("−R$ 8,00 (−4,0%)", vm.DeltaText);
        Assert.Equal(BudgetState.Within, vm.BudgetState);
        Assert.Equal("Dentro do orçamento", vm.BudgetStateText);
        Assert.False(vm.IsBusy);
        Assert.All(vm.Picks, p => Assert.StartsWith("R$ ", p.PriceText));
    }

    [Fact]
    public async Task SwapKeepsTheOtherPiecesAndUpdatesTotals()
    {
        var vm = NewVm("ak47", "m4", "awp", "deagle", "pistol");
        await vm.GenerateAsync(0);
        long before = vm.TotalCents;
        var awp = vm.Picks.Single(p => p.SlotId == "awp");
        var alt = awp.Alternatives[0];
        var others = vm.Picks.Where(p => p.SlotId != "awp").Select(p => p.Candidate.ItemId).ToList();

        vm.Swap(alt);

        Assert.Equal(alt.Candidate.ItemId, vm.Picks.Single(p => p.SlotId == "awp").Candidate.ItemId);
        Assert.Equal(others, vm.Picks.Where(p => p.SlotId != "awp").Select(p => p.Candidate.ItemId).ToList());
        Assert.Equal(before + alt.Candidate.PriceCents - awp.Candidate.PriceCents, vm.TotalCents);
        // The swapped slot still offers alternatives, now relative to the new piece.
        var after = vm.Picks.Single(p => p.SlotId == "awp");
        Assert.DoesNotContain(after.Alternatives, a => a.Candidate.ItemId == alt.Candidate.ItemId);
        // ...and the original piece is offered back.
        var back = after.Alternatives.Single(a => a.Candidate.ItemId == awp.Candidate.ItemId);
        vm.Swap(back);
        Assert.Equal(before, vm.TotalCents);
    }

    [Fact]
    public async Task SwapAndRebalanceLocksThePieceAndStaysInTheBand()
    {
        var vm = NewVm("ak47", "m4", "awp", "deagle", "pistol");
        await vm.GenerateAsync(0);
        var alt = vm.Picks.Single(p => p.SlotId == "awp").Alternatives.First();
        await vm.SwapAndRebalanceAsync(alt);
        Assert.Equal(alt.Candidate.ItemId, vm.Picks.Single(p => p.SlotId == "awp").Candidate.ItemId);
        Assert.NotEqual(BudgetState.Out, vm.BudgetState);
    }

    [Fact]
    public async Task ExplainsAnImpossibleBudgetInPortuguese()
    {
        var vm = NewVm("knife", "gloves", "ak47", "m4", "awp", "deagle", "pistol");
        vm.UseColor = false;
        vm.Budget = 50;
        await vm.GenerateAsync(0);
        Assert.Empty(vm.Picks);
        Assert.Equal("O orçamento não cobre Faca: a opção mais barata no tom escolhido custa R$ 350,00. "
            + "O kit mais barato possível custa R$ 667,50. Tente: aumentar o orçamento, desligar esse item.", vm.ProblemMessage);
    }

    [Fact]
    public async Task GenerateAnotherUsesTheNextSeedAndNoSlotsIsExplained()
    {
        var vm = NewVm("ak47", "m4", "awp", "deagle", "pistol");
        await vm.GenerateCommand.ExecuteAsync(null);
        await vm.GenerateAnotherCommand.ExecuteAsync(null);
        Assert.Equal(5, vm.Picks.Count);

        var empty = NewVm();
        await empty.GenerateAsync(0);
        Assert.Equal("Escolha pelo menos um item para o kit. Tente: adicionar itens ao kit.", empty.ProblemMessage);
    }

    [Fact]
    public void InvalidColorFallsBackToNoColorAndTextsFormatMoney()
    {
        var vm = NewVm("ak47");
        vm.TargetColorHex = "not-a-color";
        Assert.Null(vm.BuildRequest(0).TargetColor);
        var t = new Texts();
        Assert.Equal("R$ 1.234,56", t.Money(123456));
        Assert.Equal("+R$ 23,40", t.Delta(2340));
        Assert.Equal("−R$ 5,00", t.Delta(-500));
        Assert.Equal("Knife", new Texts("en").Slot("knife"));
        Assert.Equal("P250", t.Slot("weapon:P250"));
    }

    [Fact]
    public async Task SavedKitsRoundTrip()
    {
        var vm = NewVm("ak47", "m4", "awp", "deagle", "pistol");
        await vm.GenerateAsync(0);
        var path = Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N"), "kits.json");
        var store = new SavedKitsStore(path);
        var saved = store.Save("Vermelho 200", vm.CurrentKit!, vm.TargetColorHex);
        var copy = store.Duplicate(saved.Id)!;
        store.Rename(copy.Id, "Cópia");
        Assert.Equal(["Vermelho 200", "Cópia"], new SavedKitsStore(path).List().Select(k => k.Name));
        Assert.Equal(19200, saved.TotalCentsAtSave);

        var prices = saved.Items.ToDictionary(i => i.ItemId, i => i.PriceCents + 100);
        Assert.Equal(19200 + 500, SavedKitsStore.CurrentTotal(saved, prices));
        Assert.Null(SavedKitsStore.CurrentTotal(saved, new Dictionary<string, long>()));

        store.Delete(saved.Id);
        Assert.Single(store.List());
    }
}
