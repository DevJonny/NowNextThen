using Microsoft.Extensions.Time.Testing;
using Microsoft.JSInterop;
using NowThenNext.Models;
using NowThenNext.Services.Sync;
using NowThenNext.Tests.Unit.Fakes;

namespace NowThenNext.Tests.Unit;

/// <summary>
/// The signed-in sync paths that can overwrite user data, run against an in-memory
/// Drive and in-memory stores.
/// </summary>
public class SyncServiceTests
{
    private const string FindFiles = "driveSync.listFiles";
    private const string Upload = "driveSync.uploadFile";

    private static readonly DateTime Base = DateTime.UtcNow.AddHours(-1);

    private readonly FakeImageStore _images = new();
    private readonly FakeLearningStore _learning = new();
    private readonly FakePhonicsStore _phonics = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeDriveJsRuntime _drive;
    private readonly SyncService _sync;

    public SyncServiceTests()
    {
        _drive = new FakeDriveJsRuntime(_time);
        _sync = new SyncService(_drive, _images, _learning, _phonics, _time);
    }

    private static ImageItem Img(string id, string label = "", int minutes = 0) => new()
    {
        Id = id,
        Label = label == "" ? id : label,
        Base64Data = "data:" + id,
        CreatedAt = Base,
        LastModifiedUtc = Base.AddMinutes(minutes)
    };

    private static SyncEnvelope Envelope(params ImageItem[] images) => new() { Images = [.. images] };

    private static IEnumerable<string> Ids(IEnumerable<ImageItem> images) => images.Select(i => i.Id).Order();

    private static IEnumerable<string> RemoteIds(FakeDriveJsRuntime.DriveFile file)
        => Ids(FakeDriveJsRuntime.Read(file).Images);

    /// <summary>Timer-driven syncs finish on their own continuation, so poll briefly.</summary>
    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition not met");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Unauthorized_AsksToReconnect_AndStopsAutoSyncing()
    {
        _drive.FailWith = id => id == FindFiles ? FakeDriveJsRuntime.DriveError(401) : null;

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.NeedsReconnect, _sync.Status);

        // A later edit must not schedule a sync: only a user gesture can get a new token.
        _images.Edit(Img("l1"));
        _time.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(1, _drive.CallCount(FindFiles));
    }

    [Fact]
    public async Task Unauthorized_WithoutPreviousSession_IsNotSignedIn()
    {
        _drive.HasPreviousSession = false;
        _drive.FailWith = id => id == FindFiles ? FakeDriveJsRuntime.DriveError(401) : null;

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.NotSignedIn, _sync.Status);
    }

    [Fact]
    public async Task VersionConflict_RedownloadsAndRemergesTheOtherDevicesWrite()
    {
        var file = _drive.AddManifest(Envelope(Img("r1")));
        _images.Items = [Img("l1")];

        // Another device writes between our download and our upload, once.
        var otherDeviceWrote = false;
        _drive.BeforeUpload = () =>
        {
            if (otherDeviceWrote) return;
            otherDeviceWrote = true;
            file.Content = FakeDriveJsRuntime.Serialize(Envelope(Img("r1"), Img("o1")));
            file.Version++;
        };

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Synced, _sync.Status);
        Assert.Equal(2, _drive.CallCount(Upload));
        Assert.Equal(["l1", "o1", "r1"], RemoteIds(Assert.Single(_drive.Manifests)));
        Assert.Equal(["l1", "o1", "r1"], Ids(_images.Items));
    }

    [Fact]
    public async Task VersionConflictEveryAttempt_GivesUpWithError()
    {
        var file = _drive.AddManifest(Envelope());
        _images.Items = [Img("l1")];
        _drive.BeforeUpload = () => file.Version++;

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Error, _sync.Status);
        Assert.Contains("412", _sync.Error);
        Assert.Empty(FakeDriveJsRuntime.Read(file).Images);
    }

    [Fact]
    public async Task DuplicateSyncFiles_AreMergedIntoTheOldestAndTheRestDeleted()
    {
        var oldest = _drive.AddManifest(Envelope(Img("r1"), Img("shared", "old", minutes: 0)));
        _drive.AddManifest(Envelope(Img("r2"), Img("shared", "new", minutes: 5)));
        _images.Items = [Img("l1")];

        await _sync.SyncNowAsync();

        var remaining = Assert.Single(_drive.Manifests);
        Assert.Equal(oldest.Id, remaining.Id);
        Assert.Equal(["l1", "r1", "r2", "shared"], RemoteIds(remaining));
        Assert.Equal("new", FakeDriveJsRuntime.Read(remaining).Images.Single(i => i.Id == "shared").Label);
        Assert.Equal(["l1", "r1", "r2", "shared"], Ids(_images.Items));
    }

    [Fact]
    public async Task DuplicateSyncFiles_WithNothingNew_StillCollapseToOne()
    {
        var oldest = _drive.AddManifest(Envelope(Img("r1")));
        _drive.AddManifest(Envelope(Img("r1")));
        _images.Items = [Img("r1")];

        await _sync.SyncNowAsync();

        Assert.Equal(oldest.Id, Assert.Single(_drive.Manifests).Id);
    }

    [Fact]
    public async Task LocalEditMidSync_AbortsTheStaleApplyAndRetries()
    {
        _drive.AddManifest(Envelope(Img("r1")));
        _images.Items = [Img("l1", "before")];

        // The user edits l1 after the sync has read its snapshot, once.
        var edited = false;
        _images.AfterSnapshot = () =>
        {
            if (edited) return;
            edited = true;
            _images.Edit(Img("l1", "after", minutes: 1));
        };

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Synced, _sync.Status);
        Assert.Equal("after", _images.Items.Single(i => i.Id == "l1").Label);
        Assert.Equal(["l1", "r1"], Ids(_images.Items));
        var remote = FakeDriveJsRuntime.Read(Assert.Single(_drive.Manifests));
        Assert.Equal("after", remote.Images.Single(i => i.Id == "l1").Label);
    }

    [Fact]
    public async Task LocalEditOnEveryAttempt_NeverOverwritesTheEdit_AndQueuesAnotherSync()
    {
        _drive.AddManifest(Envelope(Img("r1")));
        _images.Items = [Img("l1")];

        var edits = 0;
        _images.AfterSnapshot = () => _images.Edit(Img("l1", $"edit-{++edits}", minutes: edits));

        await _sync.SyncNowAsync();

        Assert.Equal(3, edits);
        Assert.Equal("edit-3", _images.Items.Single(i => i.Id == "l1").Label);
        Assert.DoesNotContain(_images.Items, i => i.Id == "r1");

        // The edits queued a debounced sync, which picks up the remote record.
        _images.AfterSnapshot = null;
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitUntil(() => _images.Items.Any(i => i.Id == "r1"));
        Assert.Equal("edit-3", _images.Items.Single(i => i.Id == "l1").Label);
    }

    [Fact]
    public async Task NetworkFailure_RetriesWithDoublingBackoff()
    {
        _images.Items = [Img("l1")];
        var failuresLeft = 2;
        _drive.FailWith = id => id == FindFiles && failuresLeft-- > 0
            ? new JSException("TypeError: Failed to fetch")
            : null;

        await _sync.SyncNowAsync();
        Assert.Equal(SyncStatus.Idle, _sync.Status);
        Assert.Null(_sync.Error);

        _time.Advance(TimeSpan.FromSeconds(15));
        await WaitUntil(() => _drive.CallCount(FindFiles) == 2);
        await WaitUntil(() => _sync.Status == SyncStatus.Idle);

        // Second retry waits 30s, not 15s.
        _time.Advance(TimeSpan.FromSeconds(15));
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.Equal(2, _drive.CallCount(FindFiles));

        _time.Advance(TimeSpan.FromSeconds(15));
        await WaitUntil(() => _sync.Status == SyncStatus.Synced);
        Assert.Equal(["l1"], RemoteIds(Assert.Single(_drive.Manifests)));
    }

    [Fact]
    public async Task StoreApplyFailingPartway_LeavesRemoteUntouched_AndNextSyncRecovers()
    {
        var category = new LearningCategory { Id = "c1", Name = "Remote", LastModifiedUtc = Base };
        var file = _drive.AddManifest(new SyncEnvelope
        {
            Images = [Img("r1")],
            LearningCards = new LearningCardsSnapshot { Categories = [category] }
        });
        _images.Items = [Img("l1")];
        _learning.ApplyFailure = new InvalidOperationException("Quota exceeded");

        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Error, _sync.Status);
        Assert.Equal("Quota exceeded", _sync.Error);
        Assert.Equal(1, file.Version);
        Assert.Equal(["r1"], RemoteIds(file));
        Assert.Equal(["l1", "r1"], Ids(_images.Items)); // the images apply had already landed

        _learning.ApplyFailure = null;
        await _sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Synced, _sync.Status);
        Assert.Equal("c1", Assert.Single(_learning.Data.Categories).Id);
        Assert.Equal(["l1", "r1"], RemoteIds(file));
    }
}
