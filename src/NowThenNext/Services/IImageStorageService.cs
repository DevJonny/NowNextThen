using NowThenNext.Models;

namespace NowThenNext.Services;

/// <summary>
/// Service for storing and retrieving images from browser localStorage
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// Raised after a local mutation (save/delete/favorite/restore) so the sync
    /// engine can schedule an upload. Not raised by <see cref="ApplyMergedAsync"/>.
    /// </summary>
    event Action? DataChanged;

    /// <summary>
    /// Save an image to localStorage
    /// </summary>
    Task SaveImageAsync(ImageItem image);

    /// <summary>
    /// Retrieve all images by category
    /// </summary>
    Task<List<ImageItem>> GetImagesByCategoryAsync(ImageCategory category);

    /// <summary>
    /// Retrieve all favorited images across all categories
    /// </summary>
    Task<List<ImageItem>> GetFavoriteImagesAsync();

    /// <summary>
    /// Delete an image by its Id
    /// </summary>
    Task DeleteImageAsync(string id);

    /// <summary>
    /// Toggle the favorite status of an image
    /// </summary>
    Task<bool> ToggleFavoriteAsync(string id);

    /// <summary>
    /// Get all images (for internal use)
    /// </summary>
    Task<List<ImageItem>> GetAllImagesAsync();

    /// <summary>
    /// Get current storage usage as a percentage of estimated quota (0-100)
    /// </summary>
    Task<double> GetStorageUsagePercentageAsync();

    /// <summary>
    /// Get estimated remaining image capacity based on average image size
    /// </summary>
    Task<int> GetEstimatedRemainingCapacityAsync();

    /// <summary>
    /// Storage information including usage percentage and remaining capacity
    /// </summary>
    Task<StorageInfo> GetStorageInfoAsync();

    /// <summary>
    /// Clear all images from storage (as tombstones, so the clear propagates on sync)
    /// </summary>
    Task ClearAllImagesAsync();

    /// <summary>
    /// Make these the complete set of live images (used for restore operations).
    /// Stored images not in the list become tombstones.
    /// </summary>
    Task SaveImagesAsync(List<ImageItem> images);

    /// <summary>
    /// Get a breakdown of storage usage per item, sorted by size descending
    /// </summary>
    Task<List<StorageItemInfo>> GetStorageBreakdownAsync();

    /// <summary>
    /// Full snapshot for sync, including soft-deleted tombstones and normalized
    /// sync metadata. Not for UI use.
    /// </summary>
    Task<List<ImageItem>> GetSyncSnapshotAsync();

    /// <summary>
    /// Overwrite local storage with a merged image list (including tombstones).
    /// Does not raise <see cref="DataChanged"/>.
    /// </summary>
    Task ApplyMergedAsync(List<ImageItem> images);
}

/// <summary>
/// Contains storage usage information
/// </summary>
public record StorageInfo(
    double UsagePercentage,
    int EstimatedRemainingImages,
    long CurrentUsageBytes,
    long EstimatedQuotaBytes
);

/// <summary>
/// Contains information about an individual stored item's size
/// </summary>
public record StorageItemInfo(
    string Id,
    string Label,
    string Category,
    long SizeBytes,
    string? ThumbnailData
);
