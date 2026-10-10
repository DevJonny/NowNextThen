namespace NowThenNext.Models;

/// <summary>
/// The single JSON document synced to Google Drive's appDataFolder
/// (file name <c>nownextthen-sync.json</c>). Holds a merged snapshot of every
/// synced store. Records carry their own sync metadata (see <see cref="ISyncEntity"/>).
/// </summary>
public class SyncEnvelope
{
    public int Version { get; set; } = 1;
    public DateTime LastSyncedUtc { get; set; }
    public List<ImageItem> Images { get; set; } = [];
    public LearningCardsSnapshot LearningCards { get; set; } = new();
    public Dictionary<string, PhonicsCardProgress> PhonicsProgress { get; set; } = new();
}
