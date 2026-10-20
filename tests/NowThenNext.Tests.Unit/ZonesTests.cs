using System.Text.Json;
using Microsoft.JSInterop;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Tests.Unit;

public class ZonesDataTests
{
    [Fact]
    public void FourZones_EachWithFourFeelingsInThatZone()
    {
        Assert.Equal(Enum.GetValues<Zone>(), ZonesData.Zones.Select(z => z.Zone));
        Assert.All(ZonesData.Zones, zone =>
        {
            Assert.Equal(4, zone.Feelings.Count);
            Assert.All(zone.Feelings, f => Assert.Equal(zone.Zone, f.Zone));
            Assert.Equal(zone.Zone, zone.Icon.Zone);
        });
    }

    [Fact]
    public void FeelingIdsAreUnique_AndEveryExpressionIsDrawnOnce()
    {
        var feelings = ZonesData.Zones.SelectMany(z => z.Feelings).ToList();

        Assert.Equal(feelings.Count, feelings.Select(f => f.Id).Distinct().Count());
        Assert.Equal(
            Enum.GetValues<FaceExpression>().OrderBy(e => e),
            feelings.Select(f => f.Expression).OrderBy(e => e));
    }

    [Fact]
    public void Lookups_IgnoreCase_AndReturnNullWhenUnknown()
    {
        Assert.Equal(Zone.Red, ZonesData.GetBySlug("RED")?.Zone);
        Assert.Equal("happy", ZonesData.GetFeeling("Happy")?.Id);
        Assert.Null(ZonesData.GetBySlug("purple"));
        Assert.Null(ZonesData.GetFeeling("hungry"));
    }
}

public class FeelingsLogServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc);

    private readonly FakeIndexedDb _db = new();
    private readonly FixedTimeProvider _time = new(Now);
    private FeelingsLogService CreateService() => new(_db, _time);

    [Fact]
    public async Task Log_AddsNewestFirst()
    {
        var service = CreateService();

        await service.LogAsync("happy");
        _time.UtcNow = Now.AddMinutes(5);
        await service.LogAsync("sad");

        var history = await service.GetHistoryAsync();
        Assert.Equal(["sad", "happy"], history.Select(e => e.FeelingId));
        Assert.Equal(Now.AddMinutes(5), history[0].TimestampUtc);
    }

    [Fact]
    public async Task Log_DropsOldestBeyondCap()
    {
        var full = Enumerable.Range(0, FeelingsLogService.MaxEntries)
            .Select(i => new FeelingLogEntry { FeelingId = "calm", TimestampUtc = Now.AddMinutes(-i - 1) })
            .ToList();
        _db.Stores[FeelingsLogService.StoreName] = JsonSerializer.Serialize(full);
        var service = CreateService();

        await service.LogAsync("angry");

        var history = await service.GetHistoryAsync();
        Assert.Equal(FeelingsLogService.MaxEntries, history.Count);
        Assert.Equal("angry", history[0].FeelingId);
        Assert.Equal(Now.AddMinutes(-FeelingsLogService.MaxEntries + 1), history[^1].TimestampUtc);
    }

    [Fact]
    public async Task CorruptStore_ReadsAsEmpty()
    {
        _db.Stores[FeelingsLogService.StoreName] = "{not json";

        Assert.Empty(await CreateService().GetHistoryAsync());
    }

    [Fact]
    public async Task Clear_RemovesEverything()
    {
        var service = CreateService();
        await service.LogAsync("happy");

        await service.ClearAsync();

        Assert.Empty(await service.GetHistoryAsync());
    }
}

public class FeelingsHistoryTests
{
    // Fixed UTC+1 zone so results don't depend on the machine's time zone database.
    private static readonly TimeZoneInfo PlusOne =
        TimeZoneInfo.CreateCustomTimeZone("Test+1", TimeSpan.FromHours(1), "Test+1", "Test+1");

    private static readonly DateTime NowUtc = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static FeelingLogEntry Entry(string feelingId, DateTime utc) =>
        new() { FeelingId = feelingId, TimestampUtc = utc };

    [Fact]
    public void GroupByDay_UsesLocalDays_AndFriendlyLabels()
    {
        var entries = new[]
        {
            Entry("happy", NowUtc.AddHours(-1)),
            // 23:30 UTC on the 19th is 00:30 on the 20th in UTC+1, so it's "Today".
            Entry("calm", new DateTime(2026, 10, 19, 23, 30, 0, DateTimeKind.Utc)),
            Entry("sad", new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc)),
            Entry("angry", new DateTime(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc)),
        };

        var days = FeelingsHistory.GroupByDay(entries, NowUtc, PlusOne);

        Assert.Equal(["Today", "Yesterday", "Thursday 15 October"], days.Select(d => d.Label));
        Assert.Equal(["happy", "calm"], days[0].Items.Select(i => i.Feeling.Id));
        Assert.Equal(new DateTime(2026, 10, 20, 0, 30, 0), days[0].Items[1].LocalTime);
    }

    [Fact]
    public void GroupByDay_SkipsUnknownFeelings()
    {
        var days = FeelingsHistory.GroupByDay([Entry("hungry", NowUtc)], NowUtc, PlusOne);

        Assert.Empty(days);
    }

    [Fact]
    public void CountByZone_CoversTheLastSevenLocalDays()
    {
        var entries = new[]
        {
            Entry("happy", NowUtc),
            Entry("calm", NowUtc.AddDays(-6)),
            Entry("sad", NowUtc.AddDays(-6)),
            Entry("angry", NowUtc.AddDays(-7)), // eighth day back: excluded
        };

        var counts = FeelingsHistory.CountByZone(entries, NowUtc, PlusOne);

        Assert.Equal(2, counts[Zone.Green]);
        Assert.Equal(1, counts[Zone.Blue]);
        Assert.Equal(0, counts[Zone.Red]);
        Assert.Equal(0, counts[Zone.Yellow]);
    }
}

/// <summary>In-memory stand-in for <c>window.indexedDb</c>.</summary>
internal sealed class FakeIndexedDb : IJSRuntime
{
    public Dictionary<string, string> Stores { get; } = new();

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
        InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        var store = (string)args![0]!;
        switch (identifier)
        {
            case "indexedDb.getItem":
                return ValueTask.FromResult((TValue)(object?)Stores.GetValueOrDefault(store)!);
            case "indexedDb.setItem":
                Stores[store] = (string)args[1]!;
                return ValueTask.FromResult<TValue>(default!);
            case "indexedDb.removeItem":
                Stores.Remove(store);
                return ValueTask.FromResult<TValue>(default!);
            default:
                throw new NotSupportedException(identifier);
        }
    }
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public DateTime UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => new(UtcNow, TimeSpan.Zero);
}
