using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;

namespace NowThenNext.Services;

public class PhonicsProgressService : IPhonicsProgressService
{
    private readonly IJSRuntime _jsRuntime;
    private readonly IPhonicsDataService _phonicsData;
    private const string StoreName = "phonics-progress";

    public PhonicsProgressService(IJSRuntime jsRuntime, IPhonicsDataService phonicsData)
    {
        _jsRuntime = jsRuntime;
        _phonicsData = phonicsData;
    }

    /// <inheritdoc />
    public event Action? DataChanged;

    private void RaiseDataChanged() => DataChanged?.Invoke();

    public async Task MarkCompletedAsync(string graphemeId)
    {
        var data = await GetDataAsync();
        data[graphemeId] = new PhonicsCardProgress { Completed = true, LastModifiedUtc = DateTime.UtcNow };
        await SaveDataAsync(data);
        RaiseDataChanged();
    }

    public async Task<bool> IsCompletedAsync(string graphemeId)
    {
        var data = await GetDataAsync();
        return IsDone(data, graphemeId);
    }

    public async Task<string?> GetNextUnlockedGraphemeIdAsync(int phaseId)
    {
        var phase = _phonicsData.GetPhase(phaseId);
        if (phase == null) return null;

        var data = await GetDataAsync();
        var nextCard = phase.Weeks
            .SelectMany(w => w.Cards)
            .OrderBy(c => c.OrderIndex)
            .FirstOrDefault(card => !IsDone(data, card.Id));

        return nextCard?.Id;
    }

    public async Task<(int Completed, int Total)> GetPhaseProgressAsync(int phaseId)
    {
        var phase = _phonicsData.GetPhase(phaseId);
        if (phase == null) return (0, 0);

        var data = await GetDataAsync();
        var allCards = phase.Weeks.SelectMany(w => w.Cards).ToList();
        var completedCount = allCards.Count(c => IsDone(data, c.Id));

        return (completedCount, allCards.Count);
    }

    public async Task ResetPhaseAsync(int phaseId)
    {
        var phase = _phonicsData.GetPhase(phaseId);
        if (phase == null) return;

        var phaseCardIds = phase.Weeks
            .SelectMany(w => w.Cards)
            .Select(c => c.Id);

        var data = await GetDataAsync();
        var now = DateTime.UtcNow;
        foreach (var id in phaseCardIds)
        {
            // Reset by tombstoning (Completed=false) so the reset propagates on sync.
            data[id] = new PhonicsCardProgress { Completed = false, LastModifiedUtc = now };
        }
        await SaveDataAsync(data);
        RaiseDataChanged();
    }

    public async Task ResetAllAsync()
    {
        // Cover every known card, not just keys seen here, so progress completed only on
        // another device is reset too when this tombstone map merges.
        var allIds = _phonicsData.GetAllPhases()
            .SelectMany(p => p.Weeks)
            .SelectMany(w => w.Cards)
            .Select(c => c.Id);

        var data = await GetDataAsync();
        var now = DateTime.UtcNow;
        foreach (var key in data.Keys.Concat(allIds).Distinct().ToList())
        {
            data[key] = new PhonicsCardProgress { Completed = false, LastModifiedUtc = now };
        }
        await SaveDataAsync(data);
        RaiseDataChanged();
    }

    public async Task<Dictionary<string, PhonicsCardProgress>> GetSyncSnapshotAsync()
    {
        return await GetDataAsync();
    }

    public async Task ApplyMergedAsync(Dictionary<string, PhonicsCardProgress> progress)
    {
        // Result of a sync pull — must not raise DataChanged.
        await SaveDataAsync(progress);
    }

    private static bool IsDone(Dictionary<string, PhonicsCardProgress> data, string id)
        => data.TryGetValue(id, out var p) && p.Completed;

    /// <summary>
    /// Loads progress. Transparently migrates the legacy shape (a JSON array =
    /// <c>HashSet&lt;string&gt;</c> of completed ids) to the keyed map.
    /// </summary>
    private async Task<Dictionary<string, PhonicsCardProgress>> GetDataAsync()
    {
        try
        {
            var json = await _jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, PhonicsCardProgress>();

            var trimmed = json.TrimStart();
            if (trimmed.StartsWith('['))
            {
                // Legacy: HashSet<string> of completed card ids.
                var oldSet = JsonSerializer.Deserialize<HashSet<string>>(json) ?? new HashSet<string>();
                var now = DateTime.UtcNow;
                var migrated = oldSet.ToDictionary(
                    id => id,
                    _ => new PhonicsCardProgress { Completed = true, LastModifiedUtc = now });

                // Persist once (no DataChanged) so the timestamp doesn't move on every read.
                try { await SaveDataAsync(migrated); }
                catch (Exception ex) { Console.Error.WriteLine($"Failed to persist phonics migration: {ex.Message}"); }
                return migrated;
            }

            return JsonSerializer.Deserialize<Dictionary<string, PhonicsCardProgress>>(json)
                   ?? new Dictionary<string, PhonicsCardProgress>();
        }
        catch
        {
            return new Dictionary<string, PhonicsCardProgress>();
        }
    }

    private async Task SaveDataAsync(Dictionary<string, PhonicsCardProgress> data)
    {
        var json = JsonSerializer.Serialize(data);
        try
        {
            await _jsRuntime.InvokeVoidAsync("indexedDb.setItem", StoreName, json);
        }
        catch (JSException ex) when (ex.Message.Contains("QuotaExceededError") ||
                                      ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageQuotaExceededException("Storage quota exceeded. Please delete some data to free up space.", ex);
        }
        catch (Exception ex)
        {
            // Log but don't throw - prevent unhandled exceptions from breaking the UI
            Console.Error.WriteLine($"Failed to save phonics progress: {ex.Message}");
        }
    }
}
