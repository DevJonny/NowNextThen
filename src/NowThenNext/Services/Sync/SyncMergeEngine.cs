using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>Result of merging a local and remote collection.</summary>
public record MergeResult<T>(T Merged, bool LocalChanged, bool RemoteChanged);

/// <summary>
/// Pure, deterministic merge logic for cross-device sync. Per-entity
/// last-write-wins by <see cref="ISyncEntity.LastModifiedUtc"/>, with tombstone
/// (<see cref="ISyncEntity.IsDeleted"/>) propagation and a 90-day tombstone GC.
/// Ported from DiceCalc's <c>mergeEngine.ts</c>.
/// </summary>
public static class SyncMergeEngine
{
    public static readonly TimeSpan TombstoneMaxAge = TimeSpan.FromDays(90);

    /// <summary>
    /// Merge two lists of sync entities by <see cref="ISyncEntity.Id"/>.
    /// <c>LocalChanged</c> means the local store should adopt the merged result;
    /// <c>RemoteChanged</c> means the merged result should be uploaded.
    /// </summary>
    public static MergeResult<List<T>> MergeById<T>(List<T> local, List<T> remote) where T : class, ISyncEntity
    {
        var localById = ToLatestById(local);
        var remoteById = ToLatestById(remote);

        var ids = new HashSet<string>(localById.Keys);
        ids.UnionWith(remoteById.Keys);

        var merged = new List<T>();
        var localChanged = false;
        var remoteChanged = false;

        foreach (var id in ids)
        {
            localById.TryGetValue(id, out var l);
            remoteById.TryGetValue(id, out var r);
            var (winner, lc, rc) = Pick(l, r);
            merged.Add(winner);
            localChanged |= lc;
            remoteChanged |= rc;
        }

        // GC tombstones older than the retention window.
        var cutoff = DateTime.UtcNow - TombstoneMaxAge;
        var filtered = merged.Where(x => !(x.IsDeleted && x.LastModifiedUtc < cutoff)).ToList();
        if (filtered.Count != merged.Count)
        {
            localChanged = true;
            remoteChanged = true;
        }

        return new MergeResult<List<T>>(filtered, localChanged, remoteChanged);
    }

    /// <summary>
    /// Merge two phonics progress maps by card id, newest timestamp wins per card.
    /// (Phonics entries are small and self-tombstoning via <c>Completed=false</c>,
    /// so they are not GC'd.)
    /// </summary>
    public static MergeResult<Dictionary<string, PhonicsCardProgress>> MergePhonics(
        Dictionary<string, PhonicsCardProgress> local,
        Dictionary<string, PhonicsCardProgress> remote)
    {
        var keys = new HashSet<string>(local.Keys);
        keys.UnionWith(remote.Keys);

        var merged = new Dictionary<string, PhonicsCardProgress>();
        var localChanged = false;
        var remoteChanged = false;

        foreach (var key in keys)
        {
            local.TryGetValue(key, out var l);
            remote.TryGetValue(key, out var r);

            if (l != null && r != null)
            {
                if (l.LastModifiedUtc > r.LastModifiedUtc) { merged[key] = l; remoteChanged = true; }
                else if (r.LastModifiedUtc > l.LastModifiedUtc) { merged[key] = r; localChanged = true; }
                // Tie: completion wins, so every device makes the same choice.
                else if (l.Completed == r.Completed) merged[key] = l;
                else if (l.Completed) { merged[key] = l; remoteChanged = true; }
                else { merged[key] = r; localChanged = true; }
            }
            else if (l != null) { merged[key] = l; remoteChanged = true; }
            else { merged[key] = r!; localChanged = true; }
        }

        return new MergeResult<Dictionary<string, PhonicsCardProgress>>(merged, localChanged, remoteChanged);
    }

    private static (T Winner, bool LocalChanged, bool RemoteChanged) Pick<T>(T? local, T? remote)
        where T : class, ISyncEntity
    {
        if (local != null && remote != null)
        {
            if (local.LastModifiedUtc > remote.LastModifiedUtc) return (local, false, true);
            if (remote.LastModifiedUtc > local.LastModifiedUtc) return (remote, true, false);

            // Equal timestamps: break the tie on content so every device picks the same record.
            var tie = SyncContent.Compare(local, remote);
            if (tie > 0) return (local, false, true);
            if (tie < 0) return (remote, true, false);
            return (local, false, false);
        }

        if (local != null) return (local, false, true);
        return (remote!, true, false);
    }

    private static Dictionary<string, T> ToLatestById<T>(List<T> items) where T : class, ISyncEntity
    {
        // Guard against accidental duplicate ids in a store by keeping the newest.
        var map = new Dictionary<string, T>();
        foreach (var item in items)
        {
            if (!map.TryGetValue(item.Id, out var existing) || item.LastModifiedUtc >= existing.LastModifiedUtc)
                map[item.Id] = item;
        }
        return map;
    }
}
