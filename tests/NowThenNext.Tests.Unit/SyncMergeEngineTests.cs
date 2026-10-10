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
    public void EqualTimestamps_NoChangeEitherSide()
    {
        var local = new List<ImageItem> { Img("1", Base, label: "local") };
        var remote = new List<ImageItem> { Img("1", Base, label: "remote") };

        var result = SyncMergeEngine.MergeById(local, remote);

        Assert.False(result.LocalChanged);
        Assert.False(result.RemoteChanged);
        Assert.Equal("local", result.Merged[0].Label); // local kept on tie
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
}
