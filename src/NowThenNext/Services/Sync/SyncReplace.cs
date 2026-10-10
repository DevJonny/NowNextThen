using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>
/// Applies a "this is now the complete live set" write (e.g. a restore) in a sync-safe way.
/// </summary>
public static class SyncReplace
{
    /// <summary>
    /// Returns <paramref name="incoming"/> plus tombstones for every stored record it omits.
    /// New or changed records are stamped with <paramref name="now"/> so they win the next
    /// merge; unchanged ones keep their timestamp so they don't override newer remote edits.
    /// </summary>
    public static List<T> ReplaceAll<T>(List<T> stored, List<T> incoming, DateTime now)
        where T : ISyncEntity
    {
        var storedById = stored.ToDictionary(e => e.Id);
        var incomingIds = incoming.Select(e => e.Id).ToHashSet();

        foreach (var entity in incoming)
        {
            if (entity.LastModifiedUtc == default
                || !storedById.TryGetValue(entity.Id, out var existing)
                || !SyncContent.Same(existing, entity))
            {
                entity.LastModifiedUtc = now;
            }
        }

        var result = new List<T>(incoming);
        foreach (var existing in stored.Where(e => !incomingIds.Contains(e.Id)))
        {
            if (!existing.IsDeleted)
            {
                existing.IsDeleted = true;
                existing.LastModifiedUtc = now;
            }
            result.Add(existing);
        }
        return result;
    }
}
