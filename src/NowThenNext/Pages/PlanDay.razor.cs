using Microsoft.AspNetCore.Components;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Pages;

public partial class PlanDay
{
    [Inject]
    private IImageStorageService ImageStorage { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    private List<ImageItem> PlacesImages = new();
    private List<ImageItem> SelectedImages = new();
    private bool IsLoading = true;

    protected override async Task OnInitializedAsync()
    {
        await LoadPlacesAsync();
    }

    private async Task LoadPlacesAsync()
    {
        IsLoading = true;
        StateHasChanged();

        try
        {
            PlacesImages = await ImageStorage.GetImagesByCategoryAsync(ImageCategory.Places);
        }
        finally
        {
            IsLoading = false;
            StateHasChanged();
        }
    }

    private void ToggleSelection(ImageItem image)
    {
        var existingIndex = SelectedImages.FindIndex(i => i.Id == image.Id);

        if (existingIndex >= 0)
        {
            // Deselect
            SelectedImages.RemoveAt(existingIndex);
        }
        else if (SelectedImages.Count < 3)
        {
            // Select (only if we have room)
            SelectedImages.Add(image);
        }

        StateHasChanged();
    }

    private int GetSelectionIndex(string imageId)
    {
        return SelectedImages.FindIndex(i => i.Id == imageId);
    }

    private string GetSlotLabel(int index)
    {
        return index switch
        {
            0 => "Now",
            1 => "Next",
            2 => "Then",
            _ => ""
        };
    }

    private void ClearSelection()
    {
        SelectedImages.Clear();
        StateHasChanged();
    }

    private void ShowSchedule()
    {
        // Pass selected image IDs as comma-separated query string
        var imageIds = string.Join(",", SelectedImages.Select(i => i.Id));
        Navigation.NavigateTo($"schedule?ids={imageIds}");
    }

    private string GetImageSrc(ImageItem image)
    {
        if (image.Base64Data.StartsWith("data:"))
        {
            return image.Base64Data;
        }
        return $"data:image/jpeg;base64,{image.Base64Data}";
    }

    private void GoHome()
    {
        Navigation.NavigateTo("");
    }
}
