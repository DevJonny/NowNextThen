using NowThenNext.Models;
using NowThenNext.Services.Sync;

namespace NowThenNext.Tests.Unit;

public class SyncMergeEngineTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ImageItem Img(string id, DateTime modified, bool deleted = false, string label = "x") => new()
    {
        Id = id,
        Label = label,
        CreatedAt = Base,
        Base64Data = "data",
        LastModifiedUtc = modified,
        IsDeleted = deleted
    };

    [Fact]
    public void NewerLocalWins_AndFlagsRemoteChanged()
    {
        var local = new List<ImageItem> { Img("1", Base.AddMinutes(10), label: "local") };
        var remote = new List<ImageItem> { Img("1", Base, label: "remote") };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Single(result.Merged);
        Assert.Equal("local", result.Merged[0].Label);
        Assert.False(result.LocalChanged);
        Assert.True(result.RemoteChanged);
    }

    [Fact]
    public void NewerRemoteWins_AndFlagsLocalChanged()
    {
        var local = new List<ImageItem> { Img("1", Base, label: "local") };
        var remote = new List<ImageItem> { Img("1", Base.AddMinutes(10), label: "remote") };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Equal("remote", result.Merged[0].Label);
        Assert.True(result.LocalChanged);
        Assert.False(result.RemoteChanged);
    }

    [Fact]
    public void LocalOnlyItem_MustUploadToRemote()
    {
        var local = new List<ImageItem> { Img("1", Base) };
        var remote = new List<ImageItem>();

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Single(result.Merged);
        Assert.True(result.RemoteChanged);
        Assert.False(result.LocalChanged);
    }

    [Fact]
    public void RemoteOnlyItem_MustPullToLocal()
    {
        var local = new List<ImageItem>();
        var remote = new List<ImageItem> { Img("1", Base) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Single(result.Merged);
        Assert.True(result.LocalChanged);
        Assert.False(result.RemoteChanged);
    }

    [Fact]
    public void DeletionPropagates_TombstoneWinsWhenNewer()
    {
        // Remote deleted the item more recently than local's last edit.
        // Use recent timestamps so the tombstone isn't GC'd by the 90-day rule.
        var recent = DateTime.UtcNow - TimeSpan.FromDays(1);
        var local = new List<ImageItem> { Img("1", recent - TimeSpan.FromMinutes(5)) };
        var remote = new List<ImageItem> { Img("1", recent, deleted: true) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Single(result.Merged);
        Assert.True(result.Merged[0].IsDeleted);
        Assert.True(result.LocalChanged); // local must adopt the tombstone
    }

    [Fact]
    public void OldTombstone_IsGarbageCollected()
    {
        var oldDeleted = DateTime.UtcNow - SyncMergeEngine.TombstoneMaxAge - TimeSpan.FromDays(1);
        var local = new List<ImageItem> { Img("1", oldDeleted, deleted: true) };
        var remote = new List<ImageItem> { Img("1", oldDeleted, deleted: true) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Empty(result.Merged);
        Assert.True(result.LocalChanged);
        Assert.True(result.RemoteChanged);
    }

    [Fact]
    public void RecentTombstone_IsKept()
    {
        var recentlyDeleted = DateTime.UtcNow - TimeSpan.FromDays(1);
        var local = new List<ImageItem> { Img("1", recentlyDeleted, deleted: true) };
        var remote = new List<ImageItem> { Img("1", recentlyDeleted, deleted: true) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Single(result.Merged);
        Assert.True(result.Merged[0].IsDeleted);
    }

    [Fact]
    public void DisjointItems_AreUnioned()
    {
        var local = new List<ImageItem> { Img("1", Base) };
        var remote = new List<ImageItem> { Img("2", Base) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.Equal(2, result.Merged.Count);
        Assert.True(result.LocalChanged);  // must pull remote's "2"
        Assert.True(result.RemoteChanged); // must push local's "1"
    }

    [Fact]
    public void Phonics_NewerCompletionWins()
    {
        var local = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = true, LastModifiedUtc = Base.AddMinutes(10) }
        };
        var remote = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = false, LastModifiedUtc = Base }
        };

        var result = SyncMergeEngine.MergePhonics(local, remote);

        Assert.True(result.Merged["p2-w1-s"].Completed);
        Assert.True(result.RemoteChanged);
        Assert.False(result.LocalChanged);
    }

    [Fact]
    public void Phonics_RemoteResetPropagatesToLocal()
    {
        // Remote reset (Completed=false) more recently than local completion.
        var local = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = true, LastModifiedUtc = Base }
        };
        var remote = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = false, LastModifiedUtc = Base.AddMinutes(10) }
        };

        var result = SyncMergeEngine.MergePhonics(local, remote);

        Assert.False(result.Merged["p2-w1-s"].Completed);
        Assert.True(result.LocalChanged);
    }

    [Fact]
    public void Phonics_UnionsDisjointKeys()
    {
        var local = new Dictionary<string, PhonicsCardProgress>
        {
            ["a"] = new() { Completed = true, LastModifiedUtc = Base }
        };
        var remote = new Dictionary<string, PhonicsCardProgress>
        {
            ["b"] = new() { Completed = true, LastModifiedUtc = Base }
        };

        var result = SyncMergeEngine.MergePhonics(local, remote);

        Assert.Equal(2, result.Merged.Count);
        Assert.True(result.LocalChanged);
        Assert.True(result.RemoteChanged);
    }

    [Fact]
    public void EqualTimestamps_DifferentContent_BothDevicesPickTheSameRecord()
    {
        var a = new List<ImageItem> { Img("1", Base, label: "apple") };
        var b = new List<ImageItem> { Img("1", Base, label: "banana") };

        var onA = SyncMergeEngine.MergeById(a, b);
        var onB = SyncMergeEngine.MergeById(b, a);

        Assert.Equal(onA.Merged[0].Label, onB.Merged[0].Label);
        // Exactly one side adopts the other's record on each device.
        Assert.True(onA.LocalChanged ^ onA.RemoteChanged);
        Assert.True(onB.LocalChanged ^ onB.RemoteChanged);
    }

    [Fact]
    public void EqualTimestamps_TombstoneWins()
    {
        // Recent, so the tombstone isn't garbage-collected.
        var recent = DateTime.UtcNow.AddMinutes(-1);
        var local = new List<ImageItem> { Img("1", recent) };
        var remote = new List<ImageItem> { Img("1", recent, deleted: true) };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.True(result.Merged[0].IsDeleted);
        Assert.True(result.LocalChanged);
    }

    [Fact]
    public void EqualTimestamps_SameContent_NoChange()
    {
        var result = SyncMergeEngine.MergeById(
            new List<ImageItem> { Img("1", Base) },
            new List<ImageItem> { Img("1", Base) });

        Assert.False(result.LocalChanged);
        Assert.False(result.RemoteChanged);
    }

    [Fact]
    public void EqualTimestamps_CategoryCreatedAtDiffers_IsAChange()
    {
        var local = new List<LearningCategory> { new() { Id = "c", Name = "n", CreatedAt = Base, LastModifiedUtc = Base } };
        var remote = new List<LearningCategory> { new() { Id = "c", Name = "n", CreatedAt = Base.AddDays(1), LastModifiedUtc = Base } };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.True(result.LocalChanged || result.RemoteChanged);
    }

    [Fact]
    public void Phonics_EqualTimestamps_CompletionWins()
    {
        var local = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = false, LastModifiedUtc = Base }
        };
        var remote = new Dictionary<string, PhonicsCardProgress>
        {
            ["p2-w1-s"] = new() { Completed = true, LastModifiedUtc = Base }
        };

        var result = SyncMergeEngine.MergePhonics(local, remote);

        Assert.True(result.Merged["p2-w1-s"].Completed);
        Assert.True(result.LocalChanged);
    }
}
