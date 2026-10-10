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

    private const int DebounceMs = 2000;
    private const int MaxRetries = 3;

    // The envelope round-trips through Drive as a string, deserialized here — so use
    // one options instance for both directions. (Default = PascalCase property names.)
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerOptions.Default);

    private bool _inFlight;
    private bool _initialized;
    private CancellationTokenSource? _debounceCts;

    public SyncService(
        IJSRuntime js,
        IImageStorageService images,
        ILearningCardsDataService learning,
        IPhonicsProgressService phonics)
    {
        _js = js;
        _images = images;
        _learning = learning;
        _phonics = phonics;

        // Auto-schedule a sync whenever local data changes (app lifetime; no unsubscribe).
        _images.DataChanged += ScheduleSync;
        _learning.DataChanged += ScheduleSync;
        _phonics.DataChanged += ScheduleSync;
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
    /// Called once after first render. Silently re-authenticates if a previous
    /// session exists, then does an initial sync.
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

            var token = await _js.InvokeAsync<string?>("googleAuth.trySilentSignIn");
            SetStatus(SyncStatus.Idle);
            if (!string.IsNullOrEmpty(token))
            {
                await SyncNowAsync();
            }
        }
        catch
        {
            SetStatus(SyncStatus.NotSignedIn);
        }
    }

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

    /// <summary>Debounced sync after a local change.</summary>
    public void ScheduleSync()
    {
        if (Status == SyncStatus.NotSignedIn) return;

        _debounceCts?.Cancel();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;
        _ = DebouncedSyncAsync(cts.Token);
    }

    private async Task DebouncedSyncAsync(CancellationToken ct)
    {
        try { await Task.Delay(DebounceMs, ct); }
        catch (TaskCanceledException) { return; }
        if (!ct.IsCancellationRequested)
            await SyncNowAsync();
    }

    public async Task SyncNowAsync()
    {
        if (_inFlight) return;
        _debounceCts?.Cancel();

        var signedIn = await _js.InvokeAsync<bool>("googleAuth.isSignedIn");
        if (!signedIn)
        {
            var hasPrevious = await _js.InvokeAsync<bool>("googleAuth.hasPreviousSession");
            if (!hasPrevious)
            {
                SetStatus(SyncStatus.NotSignedIn);
                return;
            }

            var token = await _js.InvokeAsync<string?>("googleAuth.signIn");
            if (string.IsNullOrEmpty(token))
            {
                SetStatus(SyncStatus.NotSignedIn);
                return;
            }
        }

        _inFlight = true;
        Error = null;
        SetStatus(SyncStatus.Syncing);

        try
        {
            await RunWithRetryAsync();
            LastSyncedUtc = DateTime.UtcNow;
            SetStatus(SyncStatus.Synced);
        }
        catch (JSException jsex)
        {
            var status = ParseDriveStatus(jsex.Message);
            if (status == 401)
            {
                SetStatus(SyncStatus.NotSignedIn);
            }
            else if (IsNetworkError(jsex.Message))
            {
                // Transient network failure — keep local state, revert quietly.
                SetStatus(SyncStatus.Idle);
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
        }
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
            catch (JSException jsex)
            {
                var status = ParseDriveStatus(jsex.Message);
                if (status == 412 && attempt < MaxRetries - 1)
                    continue; // etag conflict — re-download and merge again

                if (status == 401 && attempt < MaxRetries - 1)
                {
                    var token = await _js.InvokeAsync<string?>("googleAuth.signIn");
                    if (string.IsNullOrEmpty(token)) throw;
                    continue;
                }

                throw;
            }
        }
    }

    private async Task PerformSyncAsync()
    {
        var existing = await _js.InvokeAsync<DriveFileHandle?>("driveSync.findSyncFile");

        SyncEnvelope? remote = null;
        if (existing != null)
        {
            var remoteJson = await _js.InvokeAsync<string?>("driveSync.downloadSyncFile", existing.Id);
            if (!string.IsNullOrEmpty(remoteJson))
                remote = JsonSerializer.Deserialize<SyncEnvelope>(remoteJson, EnvelopeJson);
        }

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
            await _images.ApplyMergedAsync(imageMerge.Merged);
            await _learning.ApplyMergedAsync(mergedLearning);
            await _phonics.ApplyMergedAsync(phonicsMerge.Merged);
        }

        if (remoteChanged || existing == null)
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
            await _js.InvokeVoidAsync("driveSync.uploadSyncFile", envelopeJson, existing?.Id, existing?.Etag);
        }
    }

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

    /// <summary>Small handle for the Drive sync file returned by JS (<c>{ id, etag }</c>).</summary>
    private sealed class DriveFileHandle
    {
        public string Id { get; set; } = string.Empty;
        public string? Etag { get; set; }
    }
}
