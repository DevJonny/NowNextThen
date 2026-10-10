namespace NowThenNext.Models;

/// <summary>
/// Sync metadata contract for records that participate in cross-device sync.
/// Enables per-entity last-write-wins merging with soft-delete tombstones.
/// </summary>
public interface ISyncEntity
{
    /// <summary>Stable unique identifier used to match records across devices.</summary>
    string Id { get; }

    /// <summary>UTC timestamp of the last change; the newest wins during a merge.</summary>
    DateTime LastModifiedUtc { get; set; }

    /// <summary>Soft-delete tombstone. Deleted records are kept (not removed) so the
    /// deletion propagates to other devices; tombstones are GC'd after 90 days.</summary>
    bool IsDeleted { get; set; }
}
