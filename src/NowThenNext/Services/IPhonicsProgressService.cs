using NowThenNext.Models;

namespace NowThenNext.Services;

public interface IPhonicsProgressService
{
    /// <summary>
    /// Raised after a local mutation so the sync engine can schedule an upload.
    /// Not raised by <see cref="ApplyMergedAsync"/>.
    /// </summary>
    event Action? DataChanged;

    Task MarkCompletedAsync(string graphemeId);
    Task<bool> IsCompletedAsync(string graphemeId);
    Task<string?> GetNextUnlockedGraphemeIdAsync(int phaseId);
    Task<(int Completed, int Total)> GetPhaseProgressAsync(int phaseId);
    Task ResetPhaseAsync(int phaseId);
    Task ResetAllAsync();

    /// <summary>Full progress map (per card, with sync metadata) for sync.</summary>
    Task<Dictionary<string, PhonicsCardProgress>> GetSyncSnapshotAsync();

    /// <summary>Overwrite local progress with the merged map. Does not raise <see cref="DataChanged"/>.</summary>
    Task ApplyMergedAsync(Dictionary<string, PhonicsCardProgress> progress);
}
