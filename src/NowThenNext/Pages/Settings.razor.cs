using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using NowThenNext.Models;
using NowThenNext.Services;
using NowThenNext.Services.Sync;

namespace NowThenNext.Pages;

public partial class Settings : IDisposable
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IImageStorageService ImageStorage { get; set; } = default!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = default!;

    [Inject]
    private ILearningCardsDataService LearningCards { get; set; } = default!;

    [Inject]
    private SyncService Sync { get; set; } = default!;

    private bool IsBackingUp { get; set; }
    private bool IsRestoring { get; set; }
    private bool ShowRestoreChoiceModal { get; set; }
    private List<ImageItem>? PendingRestoreImages { get; set; }
    private string? PendingLearningCardsJson { get; set; }
    private string? SuccessMessage { get; set; }
    private string? ErrorMessage { get; set; }

    // Storage usage state
    private bool IsLoadingStorage { get; set; }
    private string? StorageError { get; set; }
    private StorageInfo? StorageUsageInfo { get; set; }
    private List<StorageItemInfo>? StorageBreakdownItems { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Sync.StatusChanged += OnSyncStatusChanged;
        await LoadStorageInfoAsync();
    }

    // --- Cloud Sync ---

    private bool IsSyncBusy => Sync.Status == SyncStatus.Syncing;

    private string SyncStatusSlug => Sync.Status switch
    {
        SyncStatus.Synced => "synced",
        SyncStatus.Syncing => "syncing",
        SyncStatus.Error => "error",
        SyncStatus.Idle => "idle",
        _ => "signed-out"
    };

    private string SyncStatusLabel => Sync.Status switch
    {
        SyncStatus.Synced => "Synced",
        SyncStatus.Syncing => "Syncing…",
        SyncStatus.Error => "Sync error",
        SyncStatus.Idle => "Connected",
        _ => "Not signed in"
    };

    private void OnSyncStatusChanged() => InvokeAsync(StateHasChanged);

    private async Task SignIn() => await Sync.SignInAsync();

    private async Task SyncNow() => await Sync.SyncNowAsync();

    private async Task SignOut() => await Sync.SignOutAsync();

    public void Dispose()
    {
        Sync.StatusChanged -= OnSyncStatusChanged;
    }

    private async Task LoadStorageInfoAsync()
    {
        IsLoadingStorage = true;
        StorageError = null;

        try
        {
            StorageUsageInfo = await ImageStorage.GetStorageInfoAsync();
            StorageBreakdownItems = await ImageStorage.GetStorageBreakdownAsync();
        }
        catch
        {
            StorageError = "Unable to calculate storage usage";
        }
        finally
        {
            IsLoadingStorage = false;
        }
    }

    private static string GetCategoryColor(string category) => category switch
    {
        "Places" => "#5B9A9A",
        "Food" => "#7BA893",
        "Activities" => "#D4A06A",
        _ => "#606F7B" // System and unknown
    };

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
            return "0 KB";
        if (bytes < 1024)
            return "< 1 KB";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    private void GoBack()
    {
        Navigation.NavigateTo("");
    }

    private async Task BackupData()
    {
        try
        {
            // Clear any previous messages
            SuccessMessage = null;
            ErrorMessage = null;
            IsBackingUp = true;
            StateHasChanged();

            // Get all images from storage
            var images = await ImageStorage.GetAllImagesAsync();

            // Get learning cards custom data
            JsonElement? learningCardsData = null;
            try
            {
                var learningCardsJson = await LearningCards.GetRawCustomDataJsonAsync();
                if (!string.IsNullOrEmpty(learningCardsJson))
                {
                    learningCardsData = JsonSerializer.Deserialize<JsonElement>(learningCardsJson);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to read learning cards data for backup: {ex.Message}");
            }

            // Create backup data structure
            var backup = new BackupData
            {
                Version = "1.0",
                CreatedAt = DateTime.UtcNow,
                Images = images,
                LearningCardsData = learningCardsData
            };

            // Serialize to JSON
            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // Generate filename with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd");
            var filename = $"nowthenext-backup-{timestamp}.json";

            // Trigger download via JavaScript
            await JSRuntime.InvokeVoidAsync("downloadJsonFile", filename, json);

            // Show success message
            SuccessMessage = $"Backup downloaded! ({images.Count} images saved)";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBackingUp = false;
            StateHasChanged();
        }
    }

    private async Task TriggerRestoreFilePicker()
    {
        // Clear any previous messages
        SuccessMessage = null;
        ErrorMessage = null;
        StateHasChanged();

        // Trigger the hidden file input
        await JSRuntime.InvokeVoidAsync("triggerFileInputClick", "restore-file-input");
    }

    private async Task OnRestoreFileSelected(InputFileChangeEventArgs e)
    {
        try
        {
            SuccessMessage = null;
            ErrorMessage = null;

            var file = e.File;
            if (file == null)
            {
                return;
            }

            // Read the file content
            using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024); // 50MB max
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();

            // Try to parse and validate the backup file
            BackupData? backup;
            try
            {
                backup = JsonSerializer.Deserialize<BackupData>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                ErrorMessage = "Invalid backup file: The file is not valid JSON.";
                StateHasChanged();
                return;
            }

            // Validate backup structure
            if (backup == null)
            {
                ErrorMessage = "Invalid backup file: Could not parse backup data.";
                StateHasChanged();
                return;
            }

            if (backup.Images == null)
            {
                ErrorMessage = "Invalid backup file: No images found in backup.";
                StateHasChanged();
                return;
            }

            // Validate each image has required fields
            foreach (var image in backup.Images)
            {
                if (string.IsNullOrEmpty(image.Id) || string.IsNullOrEmpty(image.Base64Data))
                {
                    ErrorMessage = "Invalid backup file: Some images are missing required data.";
                    StateHasChanged();
                    return;
                }
            }

            // Store pending data and show choice modal
            PendingRestoreImages = backup.Images;
            PendingLearningCardsJson = backup.LearningCardsData.HasValue
                ? backup.LearningCardsData.Value.GetRawText()
                : null;
            ShowRestoreChoiceModal = true;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to read backup file: {ex.Message}";
            StateHasChanged();
        }
    }

    private void CancelRestore()
    {
        ShowRestoreChoiceModal = false;
        PendingRestoreImages = null;
        PendingLearningCardsJson = null;
        StateHasChanged();
    }

    private async Task PerformReplace()
    {
        if (PendingRestoreImages == null)
        {
            return;
        }

        try
        {
            ShowRestoreChoiceModal = false;
            IsRestoring = true;
            StateHasChanged();

            // Clear all existing images
            await ImageStorage.ClearAllImagesAsync();

            // Save all images from backup
            await ImageStorage.SaveImagesAsync(PendingRestoreImages);

            // Restore learning cards data (replace)
            await RestoreLearningCardsAsync(replace: true);

            SuccessMessage = $"Restore complete! {PendingRestoreImages.Count} images imported (replaced all data).";
        }
        catch (StorageQuotaExceededException)
        {
            ErrorMessage = "Restore failed: Storage quota exceeded. The backup is too large for available storage.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
            PendingRestoreImages = null;
            PendingLearningCardsJson = null;
            StateHasChanged();
        }
    }

    private async Task PerformMerge()
    {
        if (PendingRestoreImages == null)
        {
            return;
        }

        try
        {
            ShowRestoreChoiceModal = false;
            IsRestoring = true;
            StateHasChanged();

            // Get existing images
            var existingImages = await ImageStorage.GetAllImagesAsync();
            var existingIds = new HashSet<string>(existingImages.Select(i => i.Id));

            // Find new images (not already in storage)
            var newImages = PendingRestoreImages.Where(i => !existingIds.Contains(i.Id)).ToList();

            // Merge: add new images to existing
            var mergedImages = existingImages.Concat(newImages).ToList();

            // Save merged list
            await ImageStorage.SaveImagesAsync(mergedImages);

            // Restore learning cards data (merge)
            await RestoreLearningCardsAsync(replace: false);

            var skippedCount = PendingRestoreImages.Count - newImages.Count;
            if (skippedCount > 0)
            {
                SuccessMessage = $"Restore complete! {newImages.Count} new images added, {skippedCount} duplicates skipped.";
            }
            else
            {
                SuccessMessage = $"Restore complete! {newImages.Count} images added.";
            }
        }
        catch (StorageQuotaExceededException)
        {
            ErrorMessage = "Restore failed: Storage quota exceeded. Not enough space for the merged data.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Restore failed: {ex.Message}";
        }
        finally
        {
            IsRestoring = false;
            PendingRestoreImages = null;
            PendingLearningCardsJson = null;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Restores learning cards custom data from the backup.
    /// When replace is true, overwrites existing learning cards data.
    /// When replace is false, merges backup data with existing data (adds new categories/cards, skips duplicates by ID).
    /// </summary>
    private async Task RestoreLearningCardsAsync(bool replace)
    {
        if (string.IsNullOrEmpty(PendingLearningCardsJson))
        {
            if (replace)
            {
                // In replace mode, clear existing learning cards data even if backup has none
                try
                {
                    await LearningCards.ClearCustomDataAsync();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to clear learning cards data: {ex.Message}");
                }
            }
            return;
        }

        try
        {
            if (replace)
            {
                // Replace: overwrite with backup data
                await LearningCards.SetRawCustomDataJsonAsync(PendingLearningCardsJson);
            }
            else
            {
                // Merge: combine existing and backup learning cards data
                var existingJson = await LearningCards.GetRawCustomDataJsonAsync();

                if (string.IsNullOrEmpty(existingJson))
                {
                    // No existing data - just set the backup data
                    await LearningCards.SetRawCustomDataJsonAsync(PendingLearningCardsJson);
                }
                else
                {
                    // Merge categories and cards by ID (skip duplicates)
                    var mergedJson = MergeLearningCardsJson(existingJson, PendingLearningCardsJson);
                    await LearningCards.SetRawCustomDataJsonAsync(mergedJson);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to restore learning cards data: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Merges two learning cards JSON strings by combining categories and cards, skipping duplicates by ID.
    /// </summary>
    private static string MergeLearningCardsJson(string existingJson, string backupJson)
    {
        using var existingDoc = JsonDocument.Parse(existingJson);
        using var backupDoc = JsonDocument.Parse(backupJson);

        var existingRoot = existingDoc.RootElement;
        var backupRoot = backupDoc.RootElement;

        // Get existing category and card IDs for deduplication
        var existingCategoryIds = new HashSet<string>();
        var existingCardIds = new HashSet<string>();

        if (existingRoot.TryGetProperty("Categories", out var existingCategories))
        {
            foreach (var cat in existingCategories.EnumerateArray()
                .Where(c => c.TryGetProperty("Id", out _)))
            {
                cat.TryGetProperty("Id", out var idProp);
                existingCategoryIds.Add(idProp.GetString() ?? "");
            }
        }

        if (existingRoot.TryGetProperty("Cards", out var existingCards))
        {
            foreach (var card in existingCards.EnumerateArray()
                .Where(c => c.TryGetProperty("Id", out _)))
            {
                card.TryGetProperty("Id", out var idProp);
                existingCardIds.Add(idProp.GetString() ?? "");
            }
        }

        // Build merged JSON
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);

        writer.WriteStartObject();

        // Merge categories
        writer.WriteStartArray("Categories");
        if (existingRoot.TryGetProperty("Categories", out var existCats))
        {
            foreach (var cat in existCats.EnumerateArray())
                cat.WriteTo(writer);
        }
        if (backupRoot.TryGetProperty("Categories", out var backupCats))
        {
            foreach (var cat in backupCats.EnumerateArray()
                .Where(c => c.TryGetProperty("Id", out var id) &&
                    !existingCategoryIds.Contains(id.GetString() ?? "")))
            {
                cat.WriteTo(writer);
            }
        }
        writer.WriteEndArray();

        // Merge cards
        writer.WriteStartArray("Cards");
        if (existingRoot.TryGetProperty("Cards", out var existCards))
        {
            foreach (var card in existCards.EnumerateArray())
                card.WriteTo(writer);
        }
        if (backupRoot.TryGetProperty("Cards", out var backupCards))
        {
            foreach (var card in backupCards.EnumerateArray()
                .Where(c => c.TryGetProperty("Id", out var id) &&
                    !existingCardIds.Contains(id.GetString() ?? "")))
            {
                card.WriteTo(writer);
            }
        }
        writer.WriteEndArray();

        writer.WriteEndObject();
        writer.Flush();

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
}
