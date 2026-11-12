using System.Text.Json;
using KitBuilder.Core.Kits;

namespace KitBuilder.Presentation;

public sealed record SavedKitItem(string SlotId, string ItemId, string Name, long PriceCents);

public sealed record SavedKit(string Id, string Name, DateTimeOffset SavedAt, long BudgetCents, string? TargetColorHex,
    long TotalCentsAtSave, IReadOnlyList<SavedKitItem> Items);

/// <summary>"Kits salvos" (scope 4.3): a JSON file in the app data folder.</summary>
public sealed class SavedKitsStore(string filePath, TimeProvider? time = null)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public IReadOnlyList<SavedKit> List()
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<SavedKit>>(File.ReadAllText(filePath)) ?? [];
    }

    public SavedKit Save(string name, KitProposal kit, string? targetColorHex)
    {
        var saved = new SavedKit(Guid.NewGuid().ToString("N"), name, clock.GetUtcNow(), kit.BudgetCents, targetColorHex, kit.TotalCents,
            kit.Picks.Select(p => new SavedKitItem(p.SlotId, p.Candidate.ItemId, p.Candidate.Name, p.EffectivePriceCents)).ToList());
        Write([.. List(), saved]);
        return saved;
    }

    public void Rename(string id, string name) => Write(List().Select(k => k.Id == id ? k with { Name = name } : k));

    public SavedKit? Duplicate(string id)
    {
        var source = List().FirstOrDefault(k => k.Id == id);
        if (source is null)
        {
            return null;
        }

        var copy = source with { Id = Guid.NewGuid().ToString("N"), Name = source.Name + " (2)", SavedAt = clock.GetUtcNow() };
        Write([.. List(), copy]);
        return copy;
    }

    public void Delete(string id) => Write(List().Where(k => k.Id != id));

    /// <summary>Total with current prices (null when an item has no price now).</summary>
    public static long? CurrentTotal(SavedKit kit, IReadOnlyDictionary<string, long> currentPriceByItemId)
    {
        long total = 0;
        foreach (var item in kit.Items)
        {
            if (!currentPriceByItemId.TryGetValue(item.ItemId, out var price))
            {
                return null;
            }

            total += price;
        }

        return total;
    }

    private void Write(IEnumerable<SavedKit> kits)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        var tmp = filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(kits.ToList(), Json));
        File.Move(tmp, filePath, overwrite: true);
    }
}
