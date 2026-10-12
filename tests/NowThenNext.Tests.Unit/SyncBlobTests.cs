using Microsoft.Extensions.Time.Testing;
using NowThenNext.Models;
using NowThenNext.Services.Sync;
using NowThenNext.Tests.Unit.Fakes;

namespace NowThenNext.Tests.Unit;

/// <summary>
/// Images sync as their own content-addressed Drive files referenced from a small manifest.
/// </summary>
public class SyncBlobTests
{
    private const string Download = "driveSync.downloadFile";

    private static readonly DateTime Base = DateTime.UtcNow.AddHours(-1);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly FakeDriveJsRuntime _drive;
    private readonly Device _device;

    public SyncBlobTests()
    {
        _drive = new FakeDriveJsRuntime(_time);
        _device = new Device(_drive, _time);
    }

    /// <summary>One device's stores and sync service, sharing a Drive with others.</summary>
    private sealed class Device
    {
        public FakeImageStore Images { get; } = new();
        public FakeLearningStore Learning { get; } = new();
        public SyncService Sync { get; }

        public Device(FakeDriveJsRuntime drive, TimeProvider time)
            => Sync = new SyncService(drive, Images, Learning, new FakePhonicsStore(), time);

        public ImageItem Image(string id) => Images.Items.Single(i => i.Id == id);
    }

    private static string Payload(string seed) => "data:image/jpeg;base64," + new string('A', 2000) + seed;

    private static ImageItem Img(string id, string? payload = null, string label = "", int minutes = 0) => new()
    {
        Id = id,
        Label = label,
        Base64Data = payload ?? Payload(id),
        CreatedAt = Base,
        LastModifiedUtc = Base.AddMinutes(minutes)
    };

    private static ImageItem Stripped(ImageItem image)
    {
        image.Base64Data = SyncBlobs.Ref(SyncBlobs.Hash(image.Base64Data));
        return image;
    }

    private SyncEnvelope Manifest() => FakeDriveJsRuntime.Read(Assert.Single(_drive.Manifests));

    private int BlobDownloads() => _drive.Calls.Count(c => c == Download) - _drive.Manifests.Count();

    [Fact]
    public async Task FirstSync_UploadsEachImageAsItsOwnFile_AndTheManifestHoldsOnlyRefs()
    {
        _device.Images.Items = [Img("a"), Img("b"), Img("tiny", payload: "data:small")];

        await _device.Sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Synced, _device.Sync.Status);
        Assert.Equal(
            new[] { Payload("a"), Payload("b") }.Select(p => SyncBlobs.BlobFileName(SyncBlobs.Hash(p))).Order(),
            _drive.Blobs.Select(b => b.Name).Order());
        Assert.Equal(Payload("a"), _drive.Blobs.Single(b => b.Name.EndsWith(SyncBlobs.Hash(Payload("a")))).Content);

        var manifest = Assert.Single(_drive.Manifests);
        Assert.DoesNotContain(Payload("a"), manifest.Content);
        Assert.Equal(2, FakeDriveJsRuntime.Read(manifest).Version);
        Assert.Equal("data:small", FakeDriveJsRuntime.Read(manifest).Images.Single(i => i.Id == "tiny").Base64Data);

        // The local store keeps its full images.
        Assert.Equal(Payload("a"), _device.Image("a").Base64Data);
    }

    [Fact]
    public async Task LabelEdit_RewritesTheManifestWithoutReuploadingTheImage()
    {
        _device.Images.Items = [Img("a")];
        await _device.Sync.SyncNowAsync();
        var blob = Assert.Single(_drive.Blobs);
        var manifestVersion = Assert.Single(_drive.Manifests).Version;

        _device.Images.Items = [Img("a", label: "Park", minutes: 5)];
        await _device.Sync.SyncNowAsync();

        Assert.Same(blob, Assert.Single(_drive.Blobs));
        Assert.Equal(manifestVersion + 1, Assert.Single(_drive.Manifests).Version);
        Assert.Equal("Park", Manifest().Images.Single().Label);
    }

    [Fact]
    public async Task RemoteChange_DownloadsOnlyTheImagesThisDeviceLacks()
    {
        _drive.AddManifest(new SyncEnvelope { Version = 2, Images = [Stripped(Img("known")), Stripped(Img("new"))] });
        _drive.AddBlob(Payload("known"));
        _drive.AddBlob(Payload("new"));
        _device.Images.Items = [Img("known")];

        await _device.Sync.SyncNowAsync();

        Assert.Equal(1, BlobDownloads());
        Assert.Equal(Payload("new"), _device.Image("new").Base64Data);
        Assert.Equal(Payload("known"), _device.Image("known").Base64Data);
    }

    [Fact]
    public async Task LegacySyncFile_IsMigratedIntoManifestAndBlobs_ThenDeleted()
    {
        _drive.AddLegacy(new SyncEnvelope { Images = [Img("remote")] });
        _device.Images.Items = [Img("local")];

        await _device.Sync.SyncNowAsync();

        Assert.DoesNotContain(_drive.Files, f => f.Name == SyncBlobs.LegacyFileName);
        Assert.Equal(["local", "remote"], Manifest().Images.Select(i => i.Id).Order());
        Assert.Equal(2, _drive.Blobs.Count());
        Assert.Equal(Payload("remote"), _device.Image("remote").Base64Data);
    }

    [Fact]
    public async Task FailedImageUpload_WritesNoManifestPointingAtIt()
    {
        _device.Images.Items = [Img("a")];
        _drive.FailWith = id => id == "driveSync.uploadFile" ? FakeDriveJsRuntime.DriveError(500) : null;

        await _device.Sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Error, _device.Sync.Status);
        Assert.Empty(_drive.Manifests);
    }

    [Fact]
    public async Task FailedManifestUpload_KeepsTheLegacyFile()
    {
        _drive.AddLegacy(new SyncEnvelope { Images = [Img("remote")] });
        _drive.FailWith = id => id == "driveSync.uploadFile" && _drive.Blobs.Count() == 1
            ? FakeDriveJsRuntime.DriveError(500)
            : null;

        await _device.Sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Error, _device.Sync.Status);
        Assert.Contains(_drive.Files, f => f.Name == SyncBlobs.LegacyFileName);
    }

    [Fact]
    public async Task MissingBlob_KeepsTheLocalCopy_AndPicksUpTheChangeOnceTheImageArrives()
    {
        var newer = Img("a", payload: Payload("a-v2"), label: "Newer", minutes: 5);
        _drive.AddManifest(new SyncEnvelope { Version = 2, Images = [Stripped(newer), Stripped(Img("b"))] });
        _drive.AddBlob(Payload("a")); // the old image is there; a-v2 and b were never uploaded
        _device.Images.Items = [Img("a")];

        await _device.Sync.SyncNowAsync();

        Assert.Equal(SyncStatus.Synced, _device.Sync.Status);
        Assert.Equal(Payload("a"), _device.Image("a").Base64Data);
        Assert.Equal("", _device.Image("a").Label);
        Assert.DoesNotContain(_device.Images.Items, i => i.Id == "b");

        _drive.AddBlob(Payload("a-v2"));
        _drive.AddBlob(Payload("b"));
        await _device.Sync.SyncNowAsync();

        Assert.Equal(Payload("a-v2"), _device.Image("a").Base64Data);
        Assert.Equal("Newer", _device.Image("a").Label);
        Assert.Equal(Payload("b"), _device.Image("b").Base64Data);
    }

    [Fact]
    public async Task UnreferencedBlobs_AreDeletedOnlyOnceOldEnough()
    {
        _device.Images.Items = [Img("kept")];
        var orphan = _drive.AddBlob(Payload("orphan"));
        await _device.Sync.SyncNowAsync();
        Assert.Contains(orphan, _drive.Blobs);

        _time.Advance(TimeSpan.FromHours(2));
        await _device.Sync.SyncNowAsync();

        Assert.DoesNotContain(orphan, _drive.Blobs);
        Assert.Single(_drive.Blobs); // the referenced image survives, however old
    }

    [Fact]
    public async Task DuplicateBlobFiles_AreCollapsedToOne()
    {
        _device.Images.Items = [Img("a")];
        var first = _drive.AddBlob(Payload("a"));
        _drive.AddBlob(Payload("a"));

        await _device.Sync.SyncNowAsync();

        Assert.Same(first, Assert.Single(_drive.Blobs));
    }

    [Fact]
    public async Task LearningCards_UploadedImagesBecomeBlobs_BuiltInArtStaysInline()
    {
        _device.Learning.Data = new LearningCardsSnapshot
        {
            Cards =
            [
                new LearningCard { Id = "photo", ImageData = Payload("photo"), LastModifiedUtc = Base },
                new LearningCard { Id = "builtin", ImageData = "images/animals/cat.png", LastModifiedUtc = Base }
            ]
        };

        await _device.Sync.SyncNowAsync();

        var cards = Manifest().LearningCards.Cards;
        Assert.True(SyncBlobs.TryGetRef(cards.Single(c => c.Id == "photo").ImageData, out _));
        Assert.Equal("images/animals/cat.png", cards.Single(c => c.Id == "builtin").ImageData);
        Assert.Equal(Payload("photo"), Assert.Single(_drive.Blobs).Content);
    }

    [Fact]
    public async Task TwoDevices_ShareAnImage_ThenADeleteRemovesItsFileEverywhere()
    {
        var tablet = new Device(_drive, _time);
        _device.Images.Items = [Img("a")];

        await _device.Sync.SyncNowAsync();
        await tablet.Sync.SyncNowAsync();
        Assert.Equal(Payload("a"), tablet.Image("a").Base64Data);

        tablet.Images.Items = [new ImageItem { Id = "a", IsDeleted = true, LastModifiedUtc = Base.AddMinutes(5) }];
        await tablet.Sync.SyncNowAsync();
        await _device.Sync.SyncNowAsync();
        Assert.True(_device.Image("a").IsDeleted);

        _time.Advance(TimeSpan.FromHours(2));
        await _device.Sync.SyncNowAsync();
        Assert.Empty(_drive.Blobs);
    }
}
