using System.Text.Json;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using NowThenNext.Models;
using NowThenNext.Services.Sync;

namespace NowThenNext.Tests.Unit.Fakes;

/// <summary>
/// Stands in for <c>window.googleAuth</c> and <c>window.driveSync</c>, backed by an
/// in-memory appDataFolder. Mirrors googleDrive.js: files are listed oldest first, and
/// an upload whose expected version is stale fails with <c>DRIVE_ERROR:412</c>.
/// </summary>
public sealed class FakeDriveJsRuntime(TimeProvider time) : IJSRuntime
{
    public sealed class DriveFile
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public int Version { get; set; } = 1;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedTime { get; init; }
    }

    // SyncService writes the envelope with default (PascalCase) options.
    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerOptions.Default);

    private int _nextId;

    public List<DriveFile> Files { get; } = [];
    public List<string> Calls { get; } = [];
    public bool SignedIn { get; set; } = true;
    public bool HasPreviousSession { get; set; } = true;

    /// <summary>Return an exception to make that JS call fail, or null to let it run.</summary>
    public Func<string, Exception?>? FailWith { get; set; }

    /// <summary>Runs at the start of each manifest upload, before the version check (simulates another device writing).</summary>
    public Action? BeforeUpload { get; set; }

    public int CallCount(string identifier) => Calls.Count(c => c == identifier);

    public IEnumerable<DriveFile> Manifests => Files.Where(f => f.Name == SyncBlobs.ManifestFileName);
    public IEnumerable<DriveFile> Blobs => Files.Where(f => f.Name.StartsWith(SyncBlobs.BlobFilePrefix));

    public DriveFile AddManifest(SyncEnvelope envelope) => AddFile(SyncBlobs.ManifestFileName, Serialize(envelope));
    public DriveFile AddLegacy(SyncEnvelope envelope) => AddFile(SyncBlobs.LegacyFileName, Serialize(envelope));
    public DriveFile AddBlob(string payload) => AddFile(SyncBlobs.BlobFileName(SyncBlobs.Hash(payload)), payload);

    public DriveFile AddFile(string name, string content)
    {
        var file = new DriveFile
        {
            Id = $"file-{++_nextId}",
            Name = name,
            Content = content,
            CreatedTime = time.GetUtcNow().UtcDateTime
        };
        Files.Add(file);
        return file;
    }

    public static string Serialize(SyncEnvelope envelope) => JsonSerializer.Serialize(envelope, EnvelopeJson);

    public static SyncEnvelope Read(DriveFile file) => JsonSerializer.Deserialize<SyncEnvelope>(file.Content, EnvelopeJson)!;

    public static Exception DriveError(int status) => new JSException($"DRIVE_ERROR:{status}:{status} Error");

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
    {
        Calls.Add(identifier);
        try
        {
            if (FailWith?.Invoke(identifier) is { } failure) throw failure;

            var result = Dispatch(identifier, args ?? []);
            if (typeof(TValue) == typeof(IJSVoidResult)) return ValueTask.FromResult(default(TValue)!);

            // Round-trip through JSON like real interop, so result shapes are checked too.
            var json = JsonSerializer.Serialize(result, JsonSerializerOptions.Web);
            return ValueTask.FromResult(JsonSerializer.Deserialize<TValue>(json, JsonSerializerOptions.Web)!);
        }
        catch (Exception ex)
        {
            return ValueTask.FromException<TValue>(ex);
        }
    }

    private object? Dispatch(string identifier, object?[] args) => identifier switch
    {
        "googleAuth.isSignedIn" => SignedIn,
        "googleAuth.hasPreviousSession" => HasPreviousSession,
        "driveSync.listFiles" => Files
            .Select(f => new { id = f.Id, name = f.Name, version = f.Version.ToString(), createdTime = f.CreatedTime })
            .ToList(),
        "driveSync.downloadFile" => Find((string)args[0]!).Content,
        "driveSync.uploadFile" => Upload((string)args[0]!, (string)args[1]!, (string?)args[3], (string?)args[4]),
        "driveSync.deleteFile" => Files.RemoveAll(f => f.Id == (string)args[0]!),
        _ => throw new NotSupportedException($"Unexpected JS call: {identifier}")
    };

    private object Upload(string name, string content, string? existingId, string? expectedVersion)
    {
        if (name == SyncBlobs.ManifestFileName) BeforeUpload?.Invoke();

        if (existingId == null)
        {
            var created = AddFile(name, content);
            return new { id = created.Id, version = created.Version.ToString() };
        }

        var file = Find(existingId);
        if (expectedVersion != null && file.Version.ToString() != expectedVersion)
            throw DriveError(412);

        file.Content = content;
        file.Version++;
        return new { id = file.Id, version = file.Version.ToString() };
    }

    private DriveFile Find(string id) => Files.SingleOrDefault(f => f.Id == id) ?? throw DriveError(404);
}
