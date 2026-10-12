using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>
/// Orchestrates cross-device sync with Google Drive's appDataFolder. Auth and the
/// Drive HTTP calls live in JS (<c>window.googleAuth</c> / <c>window.driveSync</c>);
/// this service owns the merge and the status state machine. Ported from DiceCalc's
/// <c>syncService.ts</c>.
/// </summary>
public partial class SyncService
{
    private readonly IJSRuntime _js;
    private readonly IImageStorageService _images;
    private readonly ILearningCardsDataService _learning;
    private readonly IPhonicsProgressService _phonics;
    private readonly TimeProvider _time;

    private const int DebounceMs = 2000;
    private const int MaxRetries = 3;
    private const int FirstOfflineRetryMs = 15_000;
    private const int MaxOfflineRetryMs = 5 * 60_000;

    // The envelope round-trips through Drive as a string, deserialized here — so use
    // one options instance for both directions. (Default = PascalCase property names.)
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerOptions.Default);

    private bool _inFlight;
    private bool _syncRequestedWhileInFlight;
    private bool _initialized;
    private CancellationTokenSource? _debounceCts;
    private int _offlineRetryMs = FirstOfflineRetryMs;

    // Bumped on every local edit, so a sync can tell an edit landed after it read the snapshots.
    private int _localEditCount;

    public SyncService(
        IJSRuntime js,
        IImageStorageService images,
        ILearningCardsDataService learning,
        IPhonicsProgressService phonics,
        TimeProvider time)
    {
        _js = js;
        _images = images;
        _learning = learning;
        _phonics = phonics;
        _time = time;

        // Auto-schedule a sync whenever local data changes (app lifetime; no unsubscribe).
        _images.DataChanged += OnLocalDataChanged;
        _learning.DataChanged += OnLocalDataChanged;
        _phonics.DataChanged += OnLocalDataChanged;
    }

    public SyncStatus Status { get; private set; } = SyncStatus.NotSignedIn;
    public string? Error { get; private set; }
    public DateTime? LastSyncedUtc { get; private set; }

    /// <summary>Raised on every status change so the UI can re-render.</summary>
    public event Action? StatusChanged;

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Called once after first render. Restores a still-valid token from this browser
    /// session (no popup) and does an initial sync; otherwise asks the user to reconnect.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var hasPrevious = await _js.InvokeAsync<bool>("googleAuth.hasPreviousSession");
            if (!hasPrevious)
            {
                SetStatus(SyncStatus.NotSignedIn);
                return;
            }

            var token = await _js.InvokeAsync<string?>("googleAuth.restoreSession");
            if (string.IsNullOrEmpty(token))
            {
                SetStatus(SyncStatus.NeedsReconnect);
                return;
            }

            SetStatus(SyncStatus.Idle);
            await SyncNowAsync();
        }
        catch
        {
            SetStatus(SyncStatus.NotSignedIn);
        }
    }

    /// <summary>Opens Google's sign-in popup. Must be called from a user gesture (a click).</summary>
    public async Task<bool> SignInAsync()
    {
        try
        {
            var token = await _js.InvokeAsync<string?>("googleAuth.signIn");
            if (string.IsNullOrEmpty(token))
                return false;

            SetStatus(SyncStatus.Idle);
            await SyncNowAsync();
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            SetStatus(SyncStatus.Error);
            return false;
        }
    }

    public async Task SignOutAsync()
    {
        try { await _js.InvokeVoidAsync("googleAuth.signOut"); }
        catch { /* best effort */ }
        SetStatus(SyncStatus.NotSignedIn);
    }

    private void OnLocalDataChanged()
    {
        _localEditCount++;
        ScheduleSync();
    }

    /// <summary>Debounced sync after a local change.</summary>
    public void ScheduleSync() => ScheduleSyncAfter(DebounceMs);

    private void ScheduleSyncAfter(int delayMs)
    {
        if (Status is SyncStatus.NotSignedIn or SyncStatus.NeedsReconnect) return;

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DelayedSyncAsync(delayMs, cts.Token);
    }

    private async Task DelayedSyncAsync(int delayMs, CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromMilliseconds(delayMs), _time, ct); }
        catch (TaskCanceledException) { return; }
        if (!ct.IsCancellationRequested)
            await SyncNowAsync();
    }

    public async Task SyncNowAsync()
    {
        if (_inFlight)
        {
            // A change landed mid-sync and may have missed the snapshot; run again afterwards.
            _syncRequestedWhileInFlight = true;
            return;
        }
        _debounceCts?.Cancel();

        // Never open the sign-in popup here: this also runs from timers, which aren't user gestures.
        var signedIn = await _js.InvokeAsync<bool>("googleAuth.isSignedIn");
        if (!signedIn)
        {
            SetStatus(await SignedOutStatusAsync());
            return;
        }

        _inFlight = true;
        Error = null;
        SetStatus(SyncStatus.Syncing);

        try
        {
            await RunWithRetryAsync();
            LastSyncedUtc = DateTime.UtcNow;
            _offlineRetryMs = FirstOfflineRetryMs;
            SetStatus(SyncStatus.Synced);
        }
        catch (JSException jsex)
        {
            var status = ParseDriveStatus(jsex.Message);
            if (status == 401)
            {
                SetStatus(await SignedOutStatusAsync());
            }
            else if (IsNetworkError(jsex.Message))
            {
                // Transient network failure: keep local state, and retry with backoff so an
                // edit made offline still uploads once the connection is back.
                SetStatus(SyncStatus.Idle);
                ScheduleSyncAfter(_offlineRetryMs);
                _offlineRetryMs = Math.Min(_offlineRetryMs * 2, MaxOfflineRetryMs);
            }
            else
            {
                Error = jsex.Message;
                SetStatus(SyncStatus.Error);
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            SetStatus(SyncStatus.Error);
        }
        finally
        {
            _inFlight = false;
            if (_syncRequestedWhileInFlight)
            {
                _syncRequestedWhileInFlight = false;
                ScheduleSync();
            }
        }
    }

    private async Task<SyncStatus> SignedOutStatusAsync()
    {
        var hasPrevious = await _js.InvokeAsync<bool>("googleAuth.hasPreviousSession");
        return hasPrevious ? SyncStatus.NeedsReconnect : SyncStatus.NotSignedIn;
    }

    private async Task RunWithRetryAsync()
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                await PerformSyncAsync();
                return;
            }
            catch (LocalEditDuringSyncException)
            {
                // Merge result is stale. Out of retries is fine: the edit already queued a sync.
                if (attempt < MaxRetries - 1) continue;
                return;
            }
            catch (JSException jsex)
            {
                var status = ParseDriveStatus(jsex.Message);
                if (status == 412 && attempt < MaxRetries - 1)
                    continue; // another device wrote first — re-download and merge again

                throw; // 401 can't be retried without a user gesture; SyncNowAsync asks to reconnect.
            }
        }
    }

    private async Task PerformSyncAsync()
    {
        // Oldest first. Extra files come from devices whose first syncs raced; fold them
        // all into the oldest so every device converges on the same file.
        var files = await _js.InvokeAsync<List<DriveFileHandle>>("driveSync.findSyncFiles");
        var existing = files.FirstOrDefault();
        var duplicates = files.Skip(1).ToList();

        SyncEnvelope? remote = null;
        foreach (var file in files)
        {
            var remoteJson = await _js.InvokeAsync<string?>("driveSync.downloadSyncFile", file.Id);
            if (string.IsNullOrEmpty(remoteJson)) continue;

            var envelope = JsonSerializer.Deserialize<SyncEnvelope>(remoteJson, EnvelopeJson);
            if (envelope != null)
                remote = remote == null ? envelope : MergeEnvelopes(remote, envelope);
        }

        var editCountAtSnapshot = _localEditCount;
        var localImages = await _images.GetSyncSnapshotAsync();
        var localLearning = await _learning.GetSyncSnapshotAsync();
        var localPhonics = await _phonics.GetSyncSnapshotAsync();

        var remoteImages = remote?.Images ?? new List<ImageItem>();
        var remoteCategories = remote?.LearningCards?.Categories ?? new List<LearningCategory>();
        var remoteCards = remote?.LearningCards?.Cards ?? new List<LearningCard>();
        var remotePhonics = remote?.PhonicsProgress ?? new Dictionary<string, PhonicsCardProgress>();

        var imageMerge = SyncMergeEngine.MergeById(localImages, remoteImages);
        var categoryMerge = SyncMergeEngine.MergeById(localLearning.Categories, remoteCategories);
        var cardMerge = SyncMergeEngine.MergeById(localLearning.Cards, remoteCards);
        var phonicsMerge = SyncMergeEngine.MergePhonics(localPhonics, remotePhonics);

        var localChanged = imageMerge.LocalChanged || categoryMerge.LocalChanged
            || cardMerge.LocalChanged || phonicsMerge.LocalChanged;
        var remoteChanged = imageMerge.RemoteChanged || categoryMerge.RemoteChanged
            || cardMerge.RemoteChanged || phonicsMerge.RemoteChanged;

        var mergedLearning = new LearningCardsSnapshot
        {
            Categories = categoryMerge.Merged,
            Cards = cardMerge.Merged
        };

        if (localChanged)
        {
            // Writing a merge built from snapshots older than a local edit would erase that edit.
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _images.ApplyMergedAsync(imageMerge.Merged);
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _learning.ApplyMergedAsync(mergedLearning);
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _phonics.ApplyMergedAsync(phonicsMerge.Merged);
        }

        if (remoteChanged || existing == null || duplicates.Count > 0)
        {
            var envelope = new SyncEnvelope
            {
                Version = 1,
                LastSyncedUtc = DateTime.UtcNow,
                Images = imageMerge.Merged,
                LearningCards = mergedLearning,
                PhonicsProgress = phonicsMerge.Merged
            };

            var envelopeJson = JsonSerializer.Serialize(envelope, EnvelopeJson);
            await _js.InvokeVoidAsync("driveSync.uploadSyncFile", envelopeJson, existing?.Id, existing?.Version);
        }

        // Only after the merged data is safely in the oldest file.
        foreach (var duplicate in duplicates)
            await _js.InvokeVoidAsync("driveSync.deleteSyncFile", duplicate.Id);
    }

    private void EnsureNoLocalEditsSince(int editCount)
    {
        if (_localEditCount != editCount) throw new LocalEditDuringSyncException();
    }

    private sealed class LocalEditDuringSyncException : Exception;

    private static SyncEnvelope MergeEnvelopes(SyncEnvelope a, SyncEnvelope b) => new()
    {
        Version = Math.Max(a.Version, b.Version),
        LastSyncedUtc = a.LastSyncedUtc > b.LastSyncedUtc ? a.LastSyncedUtc : b.LastSyncedUtc,
        Images = SyncMergeEngine.MergeById(a.Images ?? new(), b.Images ?? new()).Merged,
        LearningCards = new LearningCardsSnapshot
        {
            Categories = SyncMergeEngine.MergeById(
                a.LearningCards?.Categories ?? new(), b.LearningCards?.Categories ?? new()).Merged,
            Cards = SyncMergeEngine.MergeById(
                a.LearningCards?.Cards ?? new(), b.LearningCards?.Cards ?? new()).Merged
        },
        PhonicsProgress = SyncMergeEngine.MergePhonics(
            a.PhonicsProgress ?? new(), b.PhonicsProgress ?? new()).Merged
    };

    /// <summary>Parses the numeric status out of a <c>DRIVE_ERROR:{status}:...</c> message, else -1.</summary>
    private static int ParseDriveStatus(string message)
    {
        var match = DriveErrorRegex().Match(message);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            ? code
            : -1;
    }

    private static bool IsNetworkError(string message)
        => message.Contains("Failed to fetch", StringComparison.OrdinalIgnoreCase)
        || message.Contains("NetworkError", StringComparison.OrdinalIgnoreCase)
        || message.Contains("TypeError", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"DRIVE_ERROR:(\d+):")]
    private static partial Regex DriveErrorRegex();

    /// <summary>Small handle for a Drive sync file returned by JS (<c>{ id, version }</c>).</summary>
    private sealed class DriveFileHandle
    {
        public string Id { get; set; } = string.Empty;
        public string? Version { get; set; }
    }
}
