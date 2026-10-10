using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>
/// Total ordering over an entity's content (every field except <c>Id</c> and
/// <c>LastModifiedUtc</c>). Zero means identical content. Used to break ties between
/// records with equal timestamps the same way on every device, and to detect real
/// edits during a restore.
/// </summary>
public static class SyncContent
{
    /// <summary>
    /// Compares content. A tombstone sorts above a live record so that, on a tie,
    /// the deletion wins.
    /// </summary>
    public static int Compare<T>(T a, T b) where T : ISyncEntity => (a, b) switch
    {
        (ImageItem x, ImageItem y) => Compare(x, y),
        (LearningCategory x, LearningCategory y) => Compare(x, y),
        (LearningCard x, LearningCard y) => Compare(x, y),
        _ => throw new NotSupportedException($"No content comparison for {typeof(T).Name}")
    };

    public static bool Same<T>(T a, T b) where T : ISyncEntity => Compare(a, b) == 0;

    private static int Compare(ImageItem a, ImageItem b) => First(
        a.IsDeleted.CompareTo(b.IsDeleted),
        string.CompareOrdinal(a.Base64Data, b.Base64Data),
        string.CompareOrdinal(a.Label, b.Label),
        a.Category.CompareTo(b.Category),
        a.IsFavorite.CompareTo(b.IsFavorite),
        a.CreatedAt.CompareTo(b.CreatedAt));

    private static int Compare(LearningCategory a, LearningCategory b) => First(
        a.IsDeleted.CompareTo(b.IsDeleted),
        string.CompareOrdinal(a.Name, b.Name),
        string.CompareOrdinal(a.Emoji, b.Emoji),
        a.IsBuiltIn.CompareTo(b.IsBuiltIn),
        a.CreatedAt.CompareTo(b.CreatedAt));

    private static int Compare(LearningCard a, LearningCard b) => First(
        a.IsDeleted.CompareTo(b.IsDeleted),
        string.CompareOrdinal(a.CategoryId, b.CategoryId),
        string.CompareOrdinal(a.Word, b.Word),
        string.CompareOrdinal(a.ImageData, b.ImageData),
        a.IsBuiltIn.CompareTo(b.IsBuiltIn));

    private static int First(params int[] comparisons) => comparisons.FirstOrDefault(c => c != 0);
}
