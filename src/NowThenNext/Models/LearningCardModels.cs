namespace NowThenNext.Models;

/// <summary>
/// Represents a category of learning cards (e.g. Shapes, Colours, Animals, Dinosaurs, or user-created)
/// </summary>
public class LearningCategory : ISyncEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Sync metadata (see ISyncEntity)
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Represents a single learning flashcard with an image and a word
/// </summary>
public class LearningCard : ISyncEntity
{
    public string Id { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string ImageData { get; set; } = string.Empty;
    public string Word { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }

    // Sync metadata (see ISyncEntity)
    public DateTime LastModifiedUtc { get; set; }
    public bool IsDeleted { get; set; }
}

/// <summary>
/// Serializable container for a user's custom learning-cards data.
/// Persisted (JSON) in the "learning-cards" IndexedDB store and used as the
/// learning-cards slice of the sync envelope. Property names must stay
/// "Categories"/"Cards" for backward compatibility with existing stored data.
/// </summary>
public class LearningCardsSnapshot
{
    public List<LearningCategory> Categories { get; set; } = [];
    public List<LearningCard> Cards { get; set; } = [];
}
