namespace NowThenNext.Models;

/// <summary>
/// The merged snapshot of every synced store. Version 2 is the Drive manifest
/// (<c>nownextthen-manifest.json</c>), with image payloads replaced by refs to their own
/// files (see <c>SyncBlobs</c>); version 1 was the legacy <c>nownextthen-sync.json</c>,
/// which carried every image inline. Records carry their own sync metadata (see <see cref="ISyncEntity"/>).
/// </summary>
public class SyncEnvelope
{
    public int Version { get; set; } = 1;
    public DateTime LastSyncedUtc { get; set; }
    public List<ImageItem> Images { get; set; } = [];
    public LearningCardsSnapshot LearningCards { get; set; } = new();
    public Dictionary<string, PhonicsCardProgress> PhonicsProgress { get; set; } = new();
}
