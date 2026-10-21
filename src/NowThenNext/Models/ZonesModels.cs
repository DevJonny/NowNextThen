namespace NowThenNext.Models;

/// <summary>The four Zones of Regulation.</summary>
public enum Zone
{
    Green,
    Yellow,
    Red,
    Blue
}

/// <summary>Which face <c>FeelingFace</c> draws. One per feeling.</summary>
public enum FaceExpression
{
    Happy,
    Calm,
    Proud,
    Focused,
    Confused,
    Frustrated,
    Worried,
    Excited,
    Scared,
    Furious,
    Angry,
    Panicked,
    Sick,
    Bored,
    Tired,
    Sad
}

/// <param name="Slug">Lower-case route segment, e.g. <c>green</c>.</param>
/// <param name="IconFeelingId">The feeling whose face represents the whole zone.</param>
/// <param name="Color">Face fill.</param>
/// <param name="ColorDark">Headings, borders and face outline; readable on <paramref name="ColorLight"/>.</param>
/// <param name="ColorLight">Tile background.</param>
public record ZoneInfo(
    Zone Zone,
    string Slug,
    string IconFeelingId,
    string Name,
    string Tagline,
    string Color,
    string ColorDark,
    string ColorLight,
    IReadOnlyList<Feeling> Feelings)
{
    public Feeling Icon => Feelings.First(f => f.Id == IconFeelingId);
}

/// <param name="Id">Unique across all zones; also the route segment, e.g. <c>happy</c>.</param>
public record Feeling(string Id, string Name, Zone Zone, FaceExpression Expression);

/// <summary>One pick the child made, stored in the <c>feelings-log</c> IndexedDB store.</summary>
public class FeelingLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FeelingId { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; }
}
