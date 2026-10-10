using NowThenNext.Models;
using NowThenNext.Services.Sync;

namespace NowThenNext.Tests.Unit;

public class SyncReplaceTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = Base.AddDays(1);

    private static ImageItem Img(string id, DateTime modified, bool deleted = false, string label = "x") => new()
    {
        Id = id,
        Label = label,
        LastModifiedUtc = modified,
        IsDeleted = deleted
    };

    private static bool SameLabel(ImageItem a, ImageItem b) => a.IsDeleted == b.IsDeleted && a.Label == b.Label;

    [Fact]
    public void OmittedRecord_BecomesTombstoneStampedNow()
    {
        var stored = new List<ImageItem> { Img("1", Base), Img("2", Base) };
        var incoming = new List<ImageItem> { Img("1", Base) };

        var result = SyncReplace.ReplaceAll(stored, incoming, Now, SameLabel);

        var omitted = Assert.Single(result, i => i.Id == "2");
        Assert.True(omitted.IsDeleted);
        Assert.Equal(Now, omitted.LastModifiedUtc);
    }

    [Fact]
    public void UnchangedRecord_KeepsItsTimestamp()
    {
        var stored = new List<ImageItem> { Img("1", Base) };
        var incoming = new List<ImageItem> { Img("1", Base) };

        var result = SyncReplace.ReplaceAll(stored, incoming, Now, SameLabel);

        Assert.Equal(Base, Assert.Single(result).LastModifiedUtc);
    }

    [Fact]
    public void ChangedOrNewRecord_IsStampedNow()
    {
        var stored = new List<ImageItem> { Img("1", Base, label: "old") };
        var incoming = new List<ImageItem> { Img("1", Base, label: "new"), Img("2", Base) };

        var result = SyncReplace.ReplaceAll(stored, incoming, Now, SameLabel);

        Assert.All(result, i => Assert.Equal(Now, i.LastModifiedUtc));
    }

    [Fact]
    public void RestoringATombstonedRecord_RevivesIt()
    {
        var stored = new List<ImageItem> { Img("1", Base, deleted: true) };
        var incoming = new List<ImageItem> { Img("1", Base) };

        var result = SyncReplace.ReplaceAll(stored, incoming, Now, SameLabel);

        var item = Assert.Single(result);
        Assert.False(item.IsDeleted);
        Assert.Equal(Now, item.LastModifiedUtc);
    }

    [Fact]
    public void ExistingTombstone_NotInIncoming_KeepsOriginalTimestamp()
    {
        var stored = new List<ImageItem> { Img("1", Base, deleted: true) };

        var result = SyncReplace.ReplaceAll(stored, new List<ImageItem>(), Now, SameLabel);

        Assert.Equal(Base, Assert.Single(result).LastModifiedUtc);
    }
}
