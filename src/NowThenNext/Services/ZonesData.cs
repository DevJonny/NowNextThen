using NowThenNext.Models;

namespace NowThenNext.Services;

/// <summary>
/// The built-in Zones of Regulation feelings. Colours are softened versions of the
/// standard zone colours so they stay recognisable without being harsh.
/// </summary>
public static class ZonesData
{
    public static IReadOnlyList<ZoneInfo> Zones { get; } =
    [
        new(Zone.Green, "green", "happy", "Green", "Good to go!", "#8CC084", "#487642", "#E7F3E4",
        [
            new("happy", "Happy", Zone.Green, FaceExpression.Happy),
            new("calm", "Calm", Zone.Green, FaceExpression.Calm),
            new("proud", "Proud", Zone.Green, FaceExpression.Proud),
            new("focused", "Focused", Zone.Green, FaceExpression.Focused),
        ]),
        new(Zone.Yellow, "yellow", "worried", "Yellow", "Slow down!", "#EBC866", "#7F6216", "#FBF2D6",
        [
            new("confused", "Confused", Zone.Yellow, FaceExpression.Confused),
            new("frustrated", "Frustrated", Zone.Yellow, FaceExpression.Frustrated),
            new("worried", "Worried", Zone.Yellow, FaceExpression.Worried),
            new("excited", "Excited", Zone.Yellow, FaceExpression.Excited),
        ]),
        new(Zone.Red, "red", "angry", "Red", "Stop!", "#E0918B", "#9A4842", "#F9E3E1",
        [
            new("scared", "Scared", Zone.Red, FaceExpression.Scared),
            new("furious", "Furious", Zone.Red, FaceExpression.Furious),
            new("angry", "Angry", Zone.Red, FaceExpression.Angry),
            new("panicked", "Panicked", Zone.Red, FaceExpression.Panicked),
        ]),
        new(Zone.Blue, "blue", "sad", "Blue", "Rest!", "#86AFD6", "#3F6A94", "#E3EDF7",
        [
            new("sick", "Sick", Zone.Blue, FaceExpression.Sick),
            new("bored", "Bored", Zone.Blue, FaceExpression.Bored),
            new("tired", "Tired", Zone.Blue, FaceExpression.Tired),
            new("sad", "Sad", Zone.Blue, FaceExpression.Sad),
        ]),
    ];

    public static ZoneInfo Get(Zone zone) => Zones.First(z => z.Zone == zone);

    public static ZoneInfo? GetBySlug(string? slug) =>
        Zones.FirstOrDefault(z => string.Equals(z.Slug, slug, StringComparison.OrdinalIgnoreCase));

    public static Feeling? GetFeeling(string? id) =>
        Zones.SelectMany(z => z.Feelings)
             .FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));
}
