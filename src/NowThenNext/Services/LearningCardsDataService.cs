using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;
using NowThenNext.Services.Sync;

namespace NowThenNext.Services;

public class LearningCardsDataService : ILearningCardsDataService
{
    private readonly IJSRuntime _jsRuntime;
    private const string StoreName = "learning-cards";

    public LearningCardsDataService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public event Action? DataChanged;

    private void RaiseDataChanged() => DataChanged?.Invoke();

    public async Task<List<LearningCategory>> GetAllCategoriesAsync()
    {
        var builtInCategories = GetBuiltInCategories();
        var customData = await GetCustomDataAsync();
        var customCategories = customData.Categories
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        var result = new List<LearningCategory>(builtInCategories.Count + customCategories.Count);
        result.AddRange(builtInCategories);
        result.AddRange(customCategories);
        return result;
    }

    public async Task<List<LearningCard>> GetCardsByCategoryAsync(string categoryId)
    {
        // Check built-in categories first
        var builtInCards = GetBuiltInCards(categoryId);
        if (builtInCards != null)
            return builtInCards;

        // Check custom categories
        var customData = await GetCustomDataAsync();
        return customData.Cards
            .Where(c => c.CategoryId == categoryId && !c.IsDeleted)
            .ToList();
    }

    public async Task AddCustomCategoryAsync(LearningCategory category)
    {
        if (category.IsBuiltIn)
            return;

        category.LastModifiedUtc = DateTime.UtcNow;
        var customData = await GetCustomDataAsync();
        customData.Categories.Add(category);
        await SaveCustomDataAsync(customData);
        RaiseDataChanged();
    }

    public async Task AddCustomCardAsync(LearningCard card)
    {
        if (card.IsBuiltIn)
            return;

        card.LastModifiedUtc = DateTime.UtcNow;
        var customData = await GetCustomDataAsync();
        customData.Cards.Add(card);
        await SaveCustomDataAsync(customData);
        RaiseDataChanged();
    }

    public async Task DeleteCustomCardAsync(string cardId)
    {
        var customData = await GetCustomDataAsync();
        var card = customData.Cards.FirstOrDefault(c => c.Id == cardId);
        if (card == null)
            return;

        // Soft delete so the deletion propagates on sync.
        card.IsDeleted = true;
        card.LastModifiedUtc = DateTime.UtcNow;
        await SaveCustomDataAsync(customData);
        RaiseDataChanged();
    }

    public async Task DeleteCustomCategoryAsync(string categoryId)
    {
        // Check that it's not a built-in category
        var builtInIds = GetBuiltInCategories().Select(c => c.Id).ToHashSet();
        if (builtInIds.Contains(categoryId))
            return;

        var customData = await GetCustomDataAsync();
        var now = DateTime.UtcNow;

        var category = customData.Categories.FirstOrDefault(c => c.Id == categoryId);
        if (category != null)
        {
            category.IsDeleted = true;
            category.LastModifiedUtc = now;
        }

        // Cascade: tombstone every card belonging to the category.
        foreach (var card in customData.Cards.Where(c => c.CategoryId == categoryId && !c.IsDeleted))
        {
            card.IsDeleted = true;
            card.LastModifiedUtc = now;
        }

        await SaveCustomDataAsync(customData);
        RaiseDataChanged();
    }

    private static List<LearningCategory> GetBuiltInCategories() =>
    [
        LearningCardsBuiltInData.GetShapesCategory(),
        LearningCardsBuiltInData.GetColoursCategory(),
        LearningCardsBuiltInData.GetAnimalsCategory(),
        LearningCardsBuiltInData.GetDinosaursCategory()
    ];

    private static List<LearningCard>? GetBuiltInCards(string categoryId) => categoryId switch
    {
        "shapes" => LearningCardsBuiltInData.GetShapesCards(),
        "colours" => LearningCardsBuiltInData.GetColoursCards(),
        "animals" => LearningCardsBuiltInData.GetAnimalsCards(),
        "dinosaurs" => LearningCardsBuiltInData.GetDinosaursCards(),
        _ => null
    };

    private async Task<LearningCardsSnapshot> GetCustomDataAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);
            if (string.IsNullOrEmpty(json))
                return new LearningCardsSnapshot();

            var data = JsonSerializer.Deserialize<LearningCardsSnapshot>(json) ?? new LearningCardsSnapshot();

            // Persist the one-time migration (no DataChanged) so legacy timestamps stay stable.
            if (NormalizeTimestamps(data, DateTime.UtcNow))
            {
                try { await SaveCustomDataAsync(data); }
                catch (Exception ex) { Console.Error.WriteLine($"Failed to persist learning cards migration: {ex.Message}"); }
            }

            return data;
        }
        catch
        {
            return new LearningCardsSnapshot();
        }
    }

    /// <summary>
    /// Fills missing sync timestamps on records saved before sync existed. Categories use
    /// <c>CreatedAt</c>; cards have none, so they use <paramref name="fallback"/>.
    /// </summary>
    private static bool NormalizeTimestamps(LearningCardsSnapshot data, DateTime fallback)
    {
        var changed = false;
        foreach (var category in data.Categories.Where(c => c.LastModifiedUtc == default))
        {
            category.LastModifiedUtc = category.CreatedAt != default ? category.CreatedAt : fallback;
            changed = true;
        }
        foreach (var card in data.Cards.Where(c => c.LastModifiedUtc == default))
        {
            card.LastModifiedUtc = fallback;
            changed = true;
        }
        return changed;
    }

    private async Task SaveCustomDataAsync(LearningCardsSnapshot data)
    {
        // A tombstone only needs Id, timestamp and IsDeleted to merge correctly.
        foreach (var card in data.Cards.Where(c => c.IsDeleted))
            card.ImageData = string.Empty;

        var json = JsonSerializer.Serialize(data);
        try
        {
            await _jsRuntime.InvokeVoidAsync("indexedDb.setItem", StoreName, json);
        }
        catch (JSException ex) when (ex.Message.Contains("QuotaExceededError") ||
                                      ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageQuotaExceededException("Storage quota exceeded. Please delete some data to free up space.", ex);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save learning cards data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Live custom data as JSON for backup and merge restore. Tombstones are left out, so a
    /// backup record can revive one locally deleted (restore stamps it as a fresh edit).
    /// </summary>
    public async Task<string?> GetRawCustomDataJsonAsync()
    {
        var json = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);
        if (string.IsNullOrEmpty(json))
            return json;

        var data = await GetCustomDataAsync();
        return JsonSerializer.Serialize(new LearningCardsSnapshot
        {
            Categories = data.Categories.Where(c => !c.IsDeleted).ToList(),
            Cards = data.Cards.Where(c => !c.IsDeleted).ToList()
        });
    }

    public async Task SetRawCustomDataJsonAsync(string json)
    {
        var incoming = JsonSerializer.Deserialize<LearningCardsSnapshot>(json) ?? new LearningCardsSnapshot();
        await ReplaceCustomDataAsync(incoming);
    }

    public async Task ClearCustomDataAsync()
    {
        // Tombstone rather than remove, so the clear propagates on sync.
        await ReplaceCustomDataAsync(new LearningCardsSnapshot());
    }

    /// <summary>
    /// Makes <paramref name="incoming"/> the complete set of live custom data. Changed records
    /// are stamped now; stored records it omits become tombstones, so a restore isn't undone
    /// by the next sync.
    /// </summary>
    private async Task ReplaceCustomDataAsync(LearningCardsSnapshot incoming)
    {
        var stored = await GetCustomDataAsync();
        var now = DateTime.UtcNow;

        var categories = SyncReplace.ReplaceAll(stored.Categories, incoming.Categories, now);
        var cards = SyncReplace.ReplaceAll(stored.Cards, incoming.Cards, now);

        await SaveCustomDataAsync(new LearningCardsSnapshot { Categories = categories, Cards = cards });
        RaiseDataChanged();
    }

    public async Task<LearningCardsSnapshot> GetSyncSnapshotAsync()
    {
        return await GetCustomDataAsync();
    }

    public async Task ApplyMergedAsync(LearningCardsSnapshot snapshot)
    {
        // Result of a sync pull — must not raise DataChanged.
        await SaveCustomDataAsync(snapshot);
    }

}
