namespace NowThenNext.Models;

/// <summary>
/// Per-card phonics progress with sync metadata. Completing a card sets
/// <see cref="Completed"/> = true; a reset sets it to false with a fresh
/// timestamp (acting as the tombstone) so resets propagate across devices.
/// </summary>
public class PhonicsCardProgress
{
    public bool Completed { get; set; }
    public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
}
