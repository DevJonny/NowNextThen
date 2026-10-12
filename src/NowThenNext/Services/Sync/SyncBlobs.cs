using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NowThenNext.Models;

namespace NowThenNext.Services.Sync;

/// <summary>
/// Moves image payloads out of synced records and into content-addressed Drive files, so
/// the manifest stays small and a sync only transfers images that changed. A stripped
/// record holds <c>nnt-blob:&lt;sha256&gt;</c> in place of its payload; because the ref is
/// derived from the content, the merge engine's tie-breaks still agree across devices.
/// </summary>
public sealed class SyncBlobs
{
    public const string ManifestFileName = "nownextthen-manifest.json";
    public const string LegacyFileName = "nownextthen-sync.json";
    public const string BlobFilePrefix = "nownextthen-blob-";
    private const string RefPrefix = "nnt-blob:";

    /// <summary>Shorter payloads (built-in SVGs, asset paths) stay inline in the manifest.</summary>
    public const int MinBlobLength = 1024;

    // Hashing every image on every sync is costly in WASM, so remember each record's hash
    // until its timestamp or payload length changes.
    private readonly Dictionary<string, (DateTime Modified, int Length, string Hash)> _hashes = new();

    public static string BlobFileName(string hash) => BlobFilePrefix + hash;

    public static string Ref(string hash) => RefPrefix + hash;

    public static bool TryGetBlobHash(string fileName, out string hash)
    {
        hash = fileName.StartsWith(BlobFilePrefix, StringComparison.Ordinal) ? fileName[BlobFilePrefix.Length..] : "";
        return hash.Length > 0;
    }

    public static bool TryGetRef(string? value, out string hash)
    {
        hash = value != null && value.StartsWith(RefPrefix, StringComparison.Ordinal) ? value[RefPrefix.Length..] : "";
        return hash.Length > 0;
    }

    public static string Hash(string payload)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    /// <summary>Every blob hash a record in <paramref name="envelope"/> refers to.</summary>
    public static HashSet<string> Refs(SyncEnvelope envelope)
    {
        var refs = new HashSet<string>();
        foreach (var value in Payloads(envelope))
            if (TryGetRef(value, out var hash)) refs.Add(hash);
        return refs;
    }

    /// <summary>
    /// Replaces large payloads in <paramref name="envelope"/> with refs, in place, adding each
    /// payload to <paramref name="payloads"/> by hash.
    /// </summary>
    public void Strip(SyncEnvelope envelope, Dictionary<string, string> payloads)
    {
        foreach (var image in envelope.Images)
            image.Base64Data = StripOne("image:" + image.Id, image.LastModifiedUtc, image.Base64Data, payloads);
        foreach (var card in envelope.LearningCards.Cards)
            card.ImageData = StripOne("card:" + card.Id, card.LastModifiedUtc, card.ImageData, payloads);
    }

    /// <summary>
    /// Copies of <paramref name="merged"/> with payloads restored from <paramref name="payloads"/>.
    /// A record whose payload is unavailable falls back to <paramref name="local"/>'s copy, or
    /// is left out if this device has none, so the store never holds a dangling ref.
    /// </summary>
    public List<ImageItem> Hydrate(List<ImageItem> merged, List<ImageItem> local, Dictionary<string, string> payloads)
        => Hydrate(merged, local, payloads, "image:", i => i.Base64Data, (i, v) => i.Base64Data = v);

    /// <inheritdoc cref="Hydrate(List{ImageItem}, List{ImageItem}, Dictionary{string, string})"/>
    public List<LearningCard> Hydrate(List<LearningCard> merged, List<LearningCard> local, Dictionary<string, string> payloads)
        => Hydrate(merged, local, payloads, "card:", c => c.ImageData, (c, v) => c.ImageData = v);

    private List<T> Hydrate<T>(
        List<T> merged,
        List<T> local,
        Dictionary<string, string> payloads,
        string keyPrefix,
        Func<T, string> get,
        Action<T, string> set) where T : class, ISyncEntity
    {
        var localById = local.ToDictionary(e => e.Id);
        var result = new List<T>(merged.Count);

        foreach (var entity in merged)
        {
            var candidate = entity;
            if (TryGetRef(get(entity), out var hash) && !payloads.ContainsKey(hash))
            {
                if (!localById.TryGetValue(entity.Id, out var fallback)) continue;
                candidate = fallback;
                TryGetRef(get(candidate), out hash);
            }

            var copy = Clone(candidate);
            if (TryGetRef(get(copy), out hash))
            {
                var payload = payloads[hash];
                set(copy, payload);
                _hashes[keyPrefix + copy.Id] = (copy.LastModifiedUtc, payload.Length, hash);
            }
            result.Add(copy);
        }

        return result;
    }

    private string StripOne(string key, DateTime modified, string? payload, Dictionary<string, string> payloads)
    {
        if (payload == null || payload.Length < MinBlobLength || TryGetRef(payload, out _))
            return payload ?? string.Empty;

        if (!_hashes.TryGetValue(key, out var cached) || cached.Modified != modified || cached.Length != payload.Length)
        {
            cached = (modified, payload.Length, Hash(payload));
            _hashes[key] = cached;
        }

        payloads.TryAdd(cached.Hash, payload);
        return Ref(cached.Hash);
    }

    private static IEnumerable<string> Payloads(SyncEnvelope envelope)
        => envelope.Images.Select(i => i.Base64Data).Concat(envelope.LearningCards.Cards.Select(c => c.ImageData));

    // Records are small once stripped, so a JSON round trip is a cheap deep copy.
    private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
