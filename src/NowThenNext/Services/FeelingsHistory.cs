using System.Globalization;
using NowThenNext.Models;

namespace NowThenNext.Services;

public record FeelingHistoryItem(Feeling Feeling, DateTime LocalTime);

public record FeelingHistoryDay(string Label, IReadOnlyList<FeelingHistoryItem> Items);

/// <summary>Shapes the raw feelings log for the history page.</summary>
public static class FeelingsHistory
{
    /// <summary>
    /// Groups entries by local calendar day, newest first. Entries whose feeling no
    /// longer exists are skipped.
    /// </summary>
    public static List<FeelingHistoryDay> GroupByDay(
        IEnumerable<FeelingLogEntry> entries, DateTime nowUtc, TimeZoneInfo timeZone)
    {
        var today = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone).Date;

        return ToItems(entries, timeZone)
            .OrderByDescending(i => i.LocalTime)
            .GroupBy(i => i.LocalTime.Date)
            .Select(g => new FeelingHistoryDay(DayLabel(g.Key, today), g.ToList()))
            .ToList();
    }

    /// <summary>How many picks fell in each zone over the last <paramref name="days"/> local days, including today.</summary>
    public static Dictionary<Zone, int> CountByZone(
        IEnumerable<FeelingLogEntry> entries, DateTime nowUtc, TimeZoneInfo timeZone, int days = 7)
    {
        var firstDay = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timeZone).Date.AddDays(1 - days);
        var counts = Enum.GetValues<Zone>().ToDictionary(z => z, _ => 0);

        foreach (var item in ToItems(entries, timeZone).Where(i => i.LocalTime.Date >= firstDay))
            counts[item.Feeling.Zone]++;

        return counts;
    }

    private static IEnumerable<FeelingHistoryItem> ToItems(IEnumerable<FeelingLogEntry> entries, TimeZoneInfo timeZone)
    {
        foreach (var entry in entries)
        {
            var feeling = ZonesData.GetFeeling(entry.FeelingId);
            if (feeling == null) continue;

            var utc = DateTime.SpecifyKind(entry.TimestampUtc, DateTimeKind.Utc);
            yield return new FeelingHistoryItem(feeling, TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone));
        }
    }

    private static string DayLabel(DateTime day, DateTime today)
    {
        if (day == today) return "Today";
        if (day == today.AddDays(-1)) return "Yesterday";
        return day.ToString("dddd d MMMM", CultureInfo.InvariantCulture);
    }
}
