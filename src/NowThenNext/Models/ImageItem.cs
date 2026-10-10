namespace NowThenNext.Models;

/// <summary>
/// Category for stored images
/// </summary>
public enum ImageCategory
{
    Places,
    Food,
    Activities
}

/// <summary>
/// Represents a stored image for the schedule or food choices
/// </summary>
public class ImageItem : ISyncEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Base64Data { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ImageCategory Category { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Sync metadata (see ISyncEntity)
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}
