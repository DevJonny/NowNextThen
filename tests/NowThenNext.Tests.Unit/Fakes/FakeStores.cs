using System.Text.Json;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Tests.Unit.Fakes;

// In-memory stores exposing only what SyncService uses. Snapshots and applies deep-copy,
// as IndexedDB serialization would, so a stale merge can't alias live data.

internal static class Copy
{
    public static T Deep<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}

public sealed class FakeImageStore : IImageStorageService
{
    public List<ImageItem> Items { get; set; } = [];
    public event Action? DataChanged;

    /// <summary>Runs after each snapshot is taken (simulates a user edit mid-sync).</summary>
    public Action? AfterSnapshot { get; set; }
    public Exception? ApplyFailure { get; set; }

    /// <summary>A user edit: upsert and raise <see cref="DataChanged"/>.</summary>
    public void Edit(ImageItem item)
    {
        Items.RemoveAll(i => i.Id == item.Id);
        Items.Add(item);
        DataChanged?.Invoke();
    }

    public Task<List<ImageItem>> GetSyncSnapshotAsync()
    {
        var snapshot = Copy.Deep(Items);
        AfterSnapshot?.Invoke();
        return Task.FromResult(snapshot);
    }

    public Task ApplyMergedAsync(List<ImageItem> images)
    {
        if (ApplyFailure != null) throw ApplyFailure;
        Items = Copy.Deep(images);
        return Task.CompletedTask;
    }

    public Task SaveImageAsync(ImageItem image) => throw new NotImplementedException();
    public Task<List<ImageItem>> GetImagesByCategoryAsync(ImageCategory category) => throw new NotImplementedException();
    public Task<List<ImageItem>> GetFavoriteImagesAsync() => throw new NotImplementedException();
    public Task DeleteImageAsync(string id) => throw new NotImplementedException();
    public Task<bool> ToggleFavoriteAsync(string id) => throw new NotImplementedException();
    public Task<List<ImageItem>> GetAllImagesAsync() => throw new NotImplementedException();
    public Task<double> GetStorageUsagePercentageAsync() => throw new NotImplementedException();
    public Task<int> GetEstimatedRemainingCapacityAsync() => throw new NotImplementedException();
    public Task<StorageInfo> GetStorageInfoAsync() => throw new NotImplementedException();
    public Task ClearAllImagesAsync() => throw new NotImplementedException();
    public Task SaveImagesAsync(List<ImageItem> images) => throw new NotImplementedException();
    public Task<List<StorageItemInfo>> GetStorageBreakdownAsync() => throw new NotImplementedException();
}

public sealed class FakeLearningStore : ILearningCardsDataService
{
    public LearningCardsSnapshot Data { get; set; } = new();
    public event Action? DataChanged { add { } remove { } }
    public Exception? ApplyFailure { get; set; }

    public Task<LearningCardsSnapshot> GetSyncSnapshotAsync() => Task.FromResult(Copy.Deep(Data));

    public Task ApplyMergedAsync(LearningCardsSnapshot snapshot)
    {
        if (ApplyFailure != null) throw ApplyFailure;
        Data = Copy.Deep(snapshot);
        return Task.CompletedTask;
    }

    public Task<List<LearningCategory>> GetAllCategoriesAsync() => throw new NotImplementedException();
    public Task<List<LearningCard>> GetCardsByCategoryAsync(string categoryId) => throw new NotImplementedException();
    public Task AddCustomCategoryAsync(LearningCategory category) => throw new NotImplementedException();
    public Task AddCustomCardAsync(LearningCard card) => throw new NotImplementedException();
    public Task DeleteCustomCardAsync(string cardId) => throw new NotImplementedException();
    public Task DeleteCustomCategoryAsync(string categoryId) => throw new NotImplementedException();
    public Task<string?> GetRawCustomDataJsonAsync() => throw new NotImplementedException();
    public Task SetRawCustomDataJsonAsync(string json) => throw new NotImplementedException();
    public Task ClearCustomDataAsync() => throw new NotImplementedException();
}

public sealed class FakePhonicsStore : IPhonicsProgressService
{
    public Dictionary<string, PhonicsCardProgress> Progress { get; set; } = new();
    public event Action? DataChanged { add { } remove { } }

    public Task<Dictionary<string, PhonicsCardProgress>> GetSyncSnapshotAsync() => Task.FromResult(Copy.Deep(Progress));

    public Task ApplyMergedAsync(Dictionary<string, PhonicsCardProgress> progress)
    {
        Progress = Copy.Deep(progress);
        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(string graphemeId) => throw new NotImplementedException();
    public Task<bool> IsCompletedAsync(string graphemeId) => throw new NotImplementedException();
    public Task<string?> GetNextUnlockedGraphemeIdAsync(int phaseId) => throw new NotImplementedException();
    public Task<(int Completed, int Total)> GetPhaseProgressAsync(int phaseId) => throw new NotImplementedException();
    public Task ResetPhaseAsync(int phaseId) => throw new NotImplementedException();
    public Task ResetAllAsync() => throw new NotImplementedException();
}
