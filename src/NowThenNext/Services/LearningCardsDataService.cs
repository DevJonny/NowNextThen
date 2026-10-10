using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;

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

            // Normalize missing sync metadata for records saved before sync existed.
            foreach (var category in data.Categories)
            {
                if (category.LastModifiedUtc == default)
                    category.LastModifiedUtc = category.CreatedAt != default ? category.CreatedAt : DateTime.UtcNow;
            }
            foreach (var card in data.Cards)
            {
                if (card.LastModifiedUtc == default)
                    card.LastModifiedUtc = DateTime.UtcNow;
            }

            return data;
        }
        catch
        {
            return new LearningCardsSnapshot();
        }
    }

    private async Task SaveCustomDataAsync(LearningCardsSnapshot data)
    {
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

    public async Task<string?> GetRawCustomDataJsonAsync()
    {
        return await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);
    }

    public async Task SetRawCustomDataJsonAsync(string json)
    {
        await _jsRuntime.InvokeVoidAsync("indexedDb.setItem", StoreName, json);
        RaiseDataChanged();
    }

    public async Task ClearCustomDataAsync()
    {
        await _jsRuntime.InvokeVoidAsync("indexedDb.removeItem", StoreName);
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
