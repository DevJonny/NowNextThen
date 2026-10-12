using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>
/// Orchestrates cross-device sync with Google Drive's appDataFolder. Auth and the
/// Drive HTTP calls live in JS (<c>window.googleAuth</c> / <c>window.driveSync</c>);
/// this service owns the merge and the status state machine. Images live in their own
/// Drive files, referenced by hash from a small manifest (see <see cref="SyncBlobs"/>).
/// Ported from DiceCalc's <c>syncService.ts</c>.
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

    // An unreferenced image file younger than this may belong to another device's sync that
    // hasn't written its manifest yet, so it is left alone.
    private static readonly TimeSpan OrphanBlobMinAge = TimeSpan.FromHours(1);
    private const int TransferConcurrency = 4;

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

    private readonly SyncBlobs _blobs = new();

    // Images downloaded during the current sync, kept across its retries so a conflict or
    // mid-sync edit doesn't fetch them again.
    private readonly Dictionary<string, string> _downloaded = new();

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
            LastSyncedUtc = _time.GetUtcNow().UtcDateTime;
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
            _downloaded.Clear();
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
        // Oldest first. Extra manifests come from devices whose first syncs raced; fold them
        // all into the oldest so every device converges on the same file.
        var files = await _js.InvokeAsync<List<DriveFile>>("driveSync.listFiles");
        var manifests = files.Where(f => f.Name == SyncBlobs.ManifestFileName).ToList();
        var legacy = files.Where(f => f.Name == SyncBlobs.LegacyFileName).ToList();
        var existing = manifests.FirstOrDefault();
        var duplicates = manifests.Skip(1).ToList();

        var blobFiles = new Dictionary<string, DriveFile>();
        var duplicateBlobs = new List<DriveFile>();
        foreach (var file in files)
        {
            if (SyncBlobs.TryGetBlobHash(file.Name, out var hash) && !blobFiles.TryAdd(hash, file))
                duplicateBlobs.Add(file);
        }

        // Image payloads this sync can supply, by hash: from local stores, the legacy file,
        // and downloads from an earlier attempt.
        var payloads = new Dictionary<string, string>(_downloaded);

        SyncEnvelope? remote = null;
        foreach (var file in manifests.Concat(legacy))
        {
            var remoteJson = await _js.InvokeAsync<string?>("driveSync.downloadFile", file.Id);
            if (string.IsNullOrEmpty(remoteJson)) continue;

            var envelope = JsonSerializer.Deserialize<SyncEnvelope>(remoteJson, EnvelopeJson);
            if (envelope == null) continue;

            // Manifests are already stripped; the legacy file carries its images inline.
            envelope.Images ??= [];
            envelope.LearningCards ??= new LearningCardsSnapshot();
            envelope.LearningCards.Categories ??= [];
            envelope.LearningCards.Cards ??= [];
            envelope.PhonicsProgress ??= new Dictionary<string, PhonicsCardProgress>();
            _blobs.Strip(envelope, payloads, isLocalStore: false);
            remote = remote == null ? envelope : MergeEnvelopes(remote, envelope);
        }

        var editCountAtSnapshot = _localEditCount;
        var local = new SyncEnvelope
        {
            Images = await _images.GetSyncSnapshotAsync(),
            LearningCards = await _learning.GetSyncSnapshotAsync(),
            PhonicsProgress = await _phonics.GetSyncSnapshotAsync()
        };
        _blobs.Strip(local, payloads, isLocalStore: true);

        var imageMerge = SyncMergeEngine.MergeById(local.Images, remote?.Images ?? []);
        var categoryMerge = SyncMergeEngine.MergeById(local.LearningCards.Categories, remote?.LearningCards.Categories ?? []);
        var cardMerge = SyncMergeEngine.MergeById(local.LearningCards.Cards, remote?.LearningCards.Cards ?? []);
        var phonicsMerge = SyncMergeEngine.MergePhonics(local.PhonicsProgress, remote?.PhonicsProgress ?? []);

        var localChanged = imageMerge.LocalChanged || categoryMerge.LocalChanged
            || cardMerge.LocalChanged || phonicsMerge.LocalChanged;
        var remoteChanged = imageMerge.RemoteChanged || categoryMerge.RemoteChanged
            || cardMerge.RemoteChanged || phonicsMerge.RemoteChanged;

        var merged = new SyncEnvelope
        {
            Version = 2,
            LastSyncedUtc = _time.GetUtcNow().UtcDateTime,
            Images = imageMerge.Merged,
            LearningCards = new LearningCardsSnapshot { Categories = categoryMerge.Merged, Cards = cardMerge.Merged },
            PhonicsProgress = phonicsMerge.Merged
        };
        var referenced = SyncBlobs.Refs(merged);

        if (localChanged)
        {
            // Fetch only the images this device doesn't have. A missing one is skipped:
            // Hydrate keeps the local copy of that record, and the next sync tries again.
            var toDownload = referenced
                .Where(hash => !payloads.ContainsKey(hash) && blobFiles.ContainsKey(hash))
                .ToList();
            foreach (var batch in toDownload.Chunk(TransferConcurrency))
            {
                var downloaded = await Task.WhenAll(batch.Select(async hash =>
                    (Hash: hash, Payload: await DownloadBlobAsync(blobFiles[hash]))));
                foreach (var (hash, payload) in downloaded)
                {
                    if (payload == null || SyncBlobs.Hash(payload) != hash) continue;
                    payloads[hash] = payload;
                    _downloaded[hash] = payload;
                }
            }

            var mergedLearning = new LearningCardsSnapshot
            {
                Categories = categoryMerge.Merged,
                Cards = _blobs.Hydrate(cardMerge.Merged, local.LearningCards.Cards, payloads)
            };
            var mergedImages = _blobs.Hydrate(imageMerge.Merged, local.Images, payloads);

            // Writing a merge built from snapshots older than a local edit would erase that edit.
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _images.ApplyMergedAsync(mergedImages);
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _learning.ApplyMergedAsync(mergedLearning);
            EnsureNoLocalEditsSince(editCountAtSnapshot);
            await _phonics.ApplyMergedAsync(phonicsMerge.Merged);
        }

        // Upload every referenced image Drive lacks (new local ones, legacy payloads, or one
        // another device failed to upload) before writing a manifest that points at it.
        var toUpload = referenced
            .Where(hash => !blobFiles.ContainsKey(hash) && payloads.ContainsKey(hash))
            .ToList();
        foreach (var batch in toUpload.Chunk(TransferConcurrency))
            await Task.WhenAll(batch.Select(hash => _js.InvokeVoidAsync(
                "driveSync.uploadFile", SyncBlobs.BlobFileName(hash), payloads[hash], "text/plain", null, null).AsTask()));

        if (remoteChanged || existing == null || duplicates.Count > 0 || legacy.Count > 0)
        {
            var manifestJson = JsonSerializer.Serialize(merged, EnvelopeJson);
            await _js.InvokeVoidAsync("driveSync.uploadFile",
                SyncBlobs.ManifestFileName, manifestJson, "application/json", existing?.Id, existing?.Version);
        }

        // Only after the merged data is safely in the oldest manifest.
        var orphanCutoff = _time.GetUtcNow().UtcDateTime - OrphanBlobMinAge;
        var obsolete = duplicates
            .Concat(legacy)
            .Concat(duplicateBlobs)
            .Concat(blobFiles
                .Where(b => !referenced.Contains(b.Key) && b.Value.CreatedTime < orphanCutoff)
                .Select(b => b.Value));
        foreach (var file in obsolete)
            await _js.InvokeVoidAsync("driveSync.deleteFile", file.Id);
    }

    private async Task<string?> DownloadBlobAsync(DriveFile file)
    {
        try
        {
            return await _js.InvokeAsync<string?>("driveSync.downloadFile", file.Id);
        }
        catch (JSException jsex) when (ParseDriveStatus(jsex.Message) == 404)
        {
            return null; // deleted since we listed it
        }
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

    /// <summary>An appDataFolder file as listed by JS (<c>{ id, name, version, createdTime }</c>).</summary>
    private sealed class DriveFile
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Version { get; set; }
        public DateTime CreatedTime { get; set; }
    }
}
