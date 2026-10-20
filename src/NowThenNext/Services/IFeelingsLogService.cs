using NowThenNext.Models;

namespace NowThenNext.Services;

public interface IFeelingsLogService
{
    /// <summary>Records that the child picked <paramref name="feelingId"/> now.</summary>
    Task LogAsync(string feelingId);

    /// <summary>All recorded picks, newest first.</summary>
    Task<List<FeelingLogEntry>> GetHistoryAsync();

    Task ClearAsync();
}
