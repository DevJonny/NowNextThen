using Microsoft.AspNetCore.Components;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Pages;

public partial class ScheduleDisplay : IDisposable
{
    [Inject]
    private IImageStorageService ImageStorage { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private List<ImageItem> ScheduleItems = new();
    private bool IsLoading = true;
    private bool IsAutoAdvance = false;
    private int CurrentAutoAdvanceIndex = 0;
    private Timer? _autoAdvanceTimer;
    private const int AutoAdvanceIntervalMs = 7000; // 7 seconds - in the 5-10 second range

    protected override async Task OnInitializedAsync()
    {
        await LoadScheduleItemsAsync();
    }

    private async Task LoadScheduleItemsAsync()
    {
        IsLoading = true;
        StateHasChanged();

        try
        {
            // Parse image IDs from query string
            var uri = new Uri(Navigation.Uri);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var idsParam = query["ids"];

            if (!string.IsNullOrEmpty(idsParam))
            {
                var imageIds = idsParam.Split(',', StringSplitOptions.RemoveEmptyEntries);

                // Load all images from storage and filter by IDs
                var allImages = await ImageStorage.GetAllImagesAsync();

                // Maintain the order from the query string
                foreach (var id in imageIds)
                {
                    var image = allImages.FirstOrDefault(img => img.Id == id);
                    if (image != null)
                    {
                        ScheduleItems.Add(image);
                    }
                }
            }
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private void ToggleAutoAdvance()
    {
        IsAutoAdvance = !IsAutoAdvance;

        if (IsAutoAdvance)
        {
            StartAutoAdvance();
        }
        else
        {
            StopAutoAdvance();
        }
    }

    private void StartAutoAdvance()
    {
        // Start from the beginning
        CurrentAutoAdvanceIndex = 0;

        // Create a timer that advances every 7 seconds
        _autoAdvanceTimer = new Timer(OnAutoAdvanceTick, null, AutoAdvanceIntervalMs, AutoAdvanceIntervalMs);
    }

    private void StopAutoAdvance()
    {
        _autoAdvanceTimer?.Dispose();
        _autoAdvanceTimer = null;
        CurrentAutoAdvanceIndex = 0;
    }

    private void OnAutoAdvanceTick(object? state)
    {
        // Move to next item, wrapping back to start
        CurrentAutoAdvanceIndex = (CurrentAutoAdvanceIndex + 1) % ScheduleItems.Count;
        InvokeAsync(StateHasChanged);
    }

    private void JumpToItem(int index)
    {
        CurrentAutoAdvanceIndex = index;

        // Reset the timer to give full time on this item
        if (_autoAdvanceTimer != null)
        {
            _autoAdvanceTimer.Dispose();
            _autoAdvanceTimer = new Timer(OnAutoAdvanceTick, null, AutoAdvanceIntervalMs, AutoAdvanceIntervalMs);
        }
    }

    private string GetSlotLabel(int index)
    {
        return index switch
        {
            0 => "Now",
            1 => "Then",
            2 => "Next",
            _ => ""
        };
    }

    private string GetLayoutClass()
    {
        return ScheduleItems.Count switch
        {
            1 => "single-item",
            2 => "two-items",
            3 => "three-items",
            _ => ""
        };
    }

    private string GetImageSrc(ImageItem image)
    {
        if (image.Base64Data.StartsWith("data:"))
        {
            return image.Base64Data;
        }
        return $"data:image/jpeg;base64,{image.Base64Data}";
    }

    private void GoBack()
    {
        Navigation.NavigateTo("plan");
    }

    public void Dispose()
    {
        _autoAdvanceTimer?.Dispose();
    }
}
