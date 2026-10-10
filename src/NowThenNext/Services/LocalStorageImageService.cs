using System.Text;
using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;
using NowThenNext.Services.Sync;

namespace NowThenNext.Services;

/// <summary>
/// Implementation of IImageStorageService using browser IndexedDB via JS interop
/// </summary>
public class LocalStorageImageService : IImageStorageService
{
    private readonly IJSRuntime _jsRuntime;
    private const string StoreName = "images";

    // Fallback quota estimate when navigator.storage.estimate() is unavailable
    private const long FallbackQuotaBytes = 500 * 1024 * 1024; // 500MB

    // Average compressed image size estimate (used for capacity calculations)
    private const long AverageImageSizeBytes = 100 * 1024; // 100KB average

    public LocalStorageImageService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc />
    public event Action? DataChanged;

    private void RaiseDataChanged() => DataChanged?.Invoke();

    public async Task SaveImageAsync(ImageItem image)
    {
        var images = await GetRawImagesAsync();

        image.LastModifiedUtc = DateTime.UtcNow;

        // Check if image with this Id already exists (update) or is new (add)
        var existingIndex = images.FindIndex(i => i.Id == image.Id);
        if (existingIndex >= 0)
        {
            images[existingIndex] = image;
        }
        else
        {
            images.Add(image);
        }

        await SaveAllImagesAsync(images);
        RaiseDataChanged();
    }

    public async Task<List<ImageItem>> GetImagesByCategoryAsync(ImageCategory category)
    {
        var images = await GetAllImagesAsync();
        return images.Where(i => i.Category == category).ToList();
    }

    public async Task<List<ImageItem>> GetFavoriteImagesAsync()
    {
        var images = await GetAllImagesAsync();
        return images.Where(i => i.IsFavorite).ToList();
    }

    public async Task DeleteImageAsync(string id)
    {
        // Soft delete: keep a tombstone so the deletion propagates to other devices.
        var images = await GetRawImagesAsync();
        var image = images.FirstOrDefault(i => i.Id == id);
        if (image == null)
        {
            return;
        }

        image.IsDeleted = true;
        image.LastModifiedUtc = DateTime.UtcNow;
        await SaveAllImagesAsync(images); // strips the tombstone's image payload
        RaiseDataChanged();
    }

    public async Task<bool> ToggleFavoriteAsync(string id)
    {
        var images = await GetRawImagesAsync();
        var image = images.FirstOrDefault(i => i.Id == id && !i.IsDeleted);

        if (image == null)
        {
            return false;
        }

        image.IsFavorite = !image.IsFavorite;
        image.LastModifiedUtc = DateTime.UtcNow;
        await SaveAllImagesAsync(images);
        RaiseDataChanged();
        return image.IsFavorite;
    }

    /// <summary>
    /// Returns live (non-deleted) images. Tombstones are excluded — use
    /// <see cref="GetSyncSnapshotAsync"/> when tombstones are needed.
    /// </summary>
    public async Task<List<ImageItem>> GetAllImagesAsync()
    {
        var all = await GetRawImagesAsync();
        return all.Where(i => !i.IsDeleted).ToList();
    }

    /// <summary>
    /// Reads the full stored list (including tombstones) and normalizes missing
    /// sync metadata (older records saved before sync existed).
    /// </summary>
    private async Task<List<ImageItem>> GetRawImagesAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);

            if (string.IsNullOrEmpty(json))
            {
                return new List<ImageItem>();
            }

            var list = JsonSerializer.Deserialize<List<ImageItem>>(json) ?? new List<ImageItem>();
            var migrated = false;
            foreach (var item in list.Where(i => i.LastModifiedUtc == default))
            {
                item.LastModifiedUtc = item.CreatedAt != default ? item.CreatedAt : DateTime.UtcNow;
                migrated = true;
            }

            // Persist the one-time migration so the timestamp stays stable across reads.
            // Not a user edit, so no DataChanged.
            if (migrated)
            {
                try { await SaveAllImagesAsync(list); }
                catch (Exception ex) { Console.Error.WriteLine($"Failed to persist image migration: {ex.Message}"); }
            }
            return list;
        }
        catch
        {
            return new List<ImageItem>();
        }
    }

    private async Task SaveAllImagesAsync(List<ImageItem> images)
    {
        // A tombstone only needs Id, timestamp and IsDeleted to merge correctly.
        foreach (var image in images.Where(i => i.IsDeleted))
        {
            image.Base64Data = string.Empty;
        }

        var json = JsonSerializer.Serialize(images);
        try
        {
            await _jsRuntime.InvokeVoidAsync("indexedDb.setItem", StoreName, json);
        }
        catch (JSException ex) when (ex.Message.Contains("QuotaExceededError") ||
                                      ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageQuotaExceededException("Storage quota exceeded. Please delete some images to free up space.", ex);
        }
    }

    public async Task<double> GetStorageUsagePercentageAsync()
    {
        var (usage, quota) = await GetStorageEstimateAsync();
        if (quota <= 0) return 0;
        return Math.Min(100.0, (usage / (double)quota) * 100.0);
    }

    public async Task<int> GetEstimatedRemainingCapacityAsync()
    {
        var (usage, quota) = await GetStorageEstimateAsync();
        var remainingBytes = Math.Max(0, quota - usage);
        return (int)(remainingBytes / AverageImageSizeBytes);
    }

    public async Task<StorageInfo> GetStorageInfoAsync()
    {
        var (usage, quota) = await GetStorageEstimateAsync();
        var usagePercentage = quota > 0 ? Math.Min(100.0, (usage / (double)quota) * 100.0) : 0;
        var remainingBytes = Math.Max(0, quota - usage);
        var estimatedRemaining = (int)(remainingBytes / AverageImageSizeBytes);

        return new StorageInfo(
            UsagePercentage: usagePercentage,
            EstimatedRemainingImages: estimatedRemaining,
            CurrentUsageBytes: usage,
            EstimatedQuotaBytes: quota
        );
    }

    private async Task<(long Usage, long Quota)> GetStorageEstimateAsync()
    {
        try
        {
            var estimate = await _jsRuntime.InvokeAsync<StorageEstimate>("indexedDb.getStorageEstimate");
            var quota = estimate.Quota > 0 ? estimate.Quota : FallbackQuotaBytes;
            return (estimate.Usage, quota);
        }
        catch
        {
            return (0, FallbackQuotaBytes);
        }
    }

    public async Task ClearAllImagesAsync()
    {
        // Tombstone rather than remove, so the clear propagates on sync.
        await SaveImagesAsync(new List<ImageItem>());
    }

    public async Task SaveImagesAsync(List<ImageItem> images)
    {
        var stored = await GetRawImagesAsync();
        var result = SyncReplace.ReplaceAll(stored, images, DateTime.UtcNow);

        await SaveAllImagesAsync(result);
        RaiseDataChanged();
    }

    public async Task<List<ImageItem>> GetSyncSnapshotAsync()
    {
        return await GetRawImagesAsync();
    }

    public async Task ApplyMergedAsync(List<ImageItem> images)
    {
        // Deliberately does NOT raise DataChanged: this is the result of a sync
        // pull, not a local edit, so it must not trigger another sync cycle.
        await SaveAllImagesAsync(images);
    }

    public async Task<List<StorageItemInfo>> GetStorageBreakdownAsync()
    {
        var items = new List<StorageItemInfo>();

        // Calculate sizes for each image
        var images = await GetAllImagesAsync();
        foreach (var image in images)
        {
            var json = JsonSerializer.Serialize(image);
            var sizeBytes = (long)Encoding.UTF8.GetByteCount(json);
            items.Add(new StorageItemInfo(
                Id: image.Id,
                Label: string.IsNullOrWhiteSpace(image.Label) ? "Untitled" : image.Label,
                Category: image.Category.ToString(),
                SizeBytes: sizeBytes,
                ThumbnailData: image.Base64Data
            ));
        }

        // Calculate size for phonics progress
        try
        {
            var phonicsJson = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", "phonics-progress");
            if (!string.IsNullOrEmpty(phonicsJson))
            {
                var sizeBytes = (long)Encoding.UTF8.GetByteCount(phonicsJson);
                items.Add(new StorageItemInfo(
                    Id: "phonics-progress",
                    Label: "Phonics Progress",
                    Category: "System",
                    SizeBytes: sizeBytes,
                    ThumbnailData: null
                ));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read phonics progress for storage breakdown: {ex.Message}");
        }

        // Calculate size for learning cards
        try
        {
            var learningJson = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", "learning-cards");
            if (!string.IsNullOrEmpty(learningJson))
            {
                var sizeBytes = (long)Encoding.UTF8.GetByteCount(learningJson);
                items.Add(new StorageItemInfo(
                    Id: "learning-cards",
                    Label: "Learning Cards Data",
                    Category: "System",
                    SizeBytes: sizeBytes,
                    ThumbnailData: null
                ));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read learning cards for storage breakdown: {ex.Message}");
        }

        // Sort by size descending
        items.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));

        return items;
    }

    private record StorageEstimate(long Usage, long Quota);
}
