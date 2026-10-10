using NowThenNext.Models;

namespace NowThenNext.Services;

public interface ILearningCardsDataService
{
    /// <summary>
    /// Raised after a local mutation so the sync engine can schedule an upload.
    /// Not raised by <see cref="ApplyMergedAsync"/>.
    /// </summary>
    event Action? DataChanged;

    Task<List<LearningCategory>> GetAllCategoriesAsync();
    Task<List<LearningCard>> GetCardsByCategoryAsync(string categoryId);
    Task AddCustomCategoryAsync(LearningCategory category);
    Task AddCustomCardAsync(LearningCard card);
    Task DeleteCustomCardAsync(string cardId);
    Task DeleteCustomCategoryAsync(string categoryId);
    Task<string?> GetRawCustomDataJsonAsync();
    Task SetRawCustomDataJsonAsync(string json);
    Task ClearCustomDataAsync();

    /// <summary>Full custom-data snapshot (including tombstones) for sync.</summary>
    Task<LearningCardsSnapshot> GetSyncSnapshotAsync();

    /// <summary>Overwrite custom data with the merged snapshot. Does not raise <see cref="DataChanged"/>.</summary>
    Task ApplyMergedAsync(LearningCardsSnapshot snapshot);
}
