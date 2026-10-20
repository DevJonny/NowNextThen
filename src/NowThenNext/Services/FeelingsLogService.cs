using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;

namespace NowThenNext.Services;

/// <summary>
/// Stores the child's feeling picks in IndexedDB (device only, not synced or backed up).
/// </summary>
public class FeelingsLogService(IJSRuntime jsRuntime, TimeProvider timeProvider) : IFeelingsLogService
{
    public const string StoreName = "feelings-log";

    /// <summary>Oldest picks are dropped beyond this, so the log can't grow forever.</summary>
    public const int MaxEntries = 1000;

    public async Task LogAsync(string feelingId)
    {
        var entries = await GetHistoryAsync();
        entries.Insert(0, new FeelingLogEntry
        {
            FeelingId = feelingId,
            TimestampUtc = timeProvider.GetUtcNow().UtcDateTime
        });

        if (entries.Count > MaxEntries)
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);

        await SaveAsync(entries);
    }

    public async Task<List<FeelingLogEntry>> GetHistoryAsync()
    {
        try
        {
            var json = await jsRuntime.InvokeAsync<string?>("indexedDb.getItem", StoreName);
            if (string.IsNullOrEmpty(json))
                return [];

            var entries = JsonSerializer.Deserialize<List<FeelingLogEntry>>(json) ?? [];
            return entries.OrderByDescending(e => e.TimestampUtc).ToList();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read feelings log: {ex.Message}");
            return [];
        }
    }

    public async Task ClearAsync()
    {
        await jsRuntime.InvokeVoidAsync("indexedDb.removeItem", StoreName);
    }

    private async Task SaveAsync(List<FeelingLogEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries);
        try
        {
            await jsRuntime.InvokeVoidAsync("indexedDb.setItem", StoreName, json);
        }
        catch (JSException ex) when (ex.Message.Contains("quota", StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageQuotaExceededException("Storage quota exceeded. Please delete some data to free up space.", ex);
        }
    }
}
