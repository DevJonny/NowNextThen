using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

/// <summary>
/// E2E tests for library views (Places, Food, Activities) and image management functionality.
/// Tests displaying images, navigation, delete, and favorite operations.
/// </summary>
[Collection("BlazorApp")]
public class LibraryTests
{
    private readonly BlazorAppFixture _fixture;

    public LibraryTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task PlacesLibrary_DisplaysUploadedPlaceImages()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // First, upload a place image (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"TestPlace_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Act - Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - The uploaded image should be displayed with its label
            var imageLabel = page.Locator($"text={uniqueLabel}");
            await Assertions.Expect(imageLabel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FoodLibrary_DisplaysUploadedFoodImages()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // First, upload a food image (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"TestFood_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Food", clearStorageFirst: true);

            // Act - Navigate to Food library
            await page.GotoAsync($"{_fixture.BaseUrl}/food");
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - The uploaded image should be displayed with its label
            var imageLabel = page.Locator($"text={uniqueLabel}");
            await Assertions.Expect(imageLabel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task LibraryNavigation_PlacesAndFoodPagesLoad()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Verify we're on the Places page
            var placesHeading = page.Locator("h1");
            await Assertions.Expect(placesHeading).ToContainTextAsync("Places");
            Assert.EndsWith("/places", page.Url);

            // Navigate to Food library
            await page.GotoAsync($"{_fixture.BaseUrl}/food");
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Verify we're on the Food page
            var foodHeading = page.Locator("h1");
            await Assertions.Expect(foodHeading).ToContainTextAsync("Food");
            Assert.EndsWith("/food", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task EmptyState_ShownWhenNoImages()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Clear localStorage to ensure no images
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Reload to see empty state
            await page.ReloadAsync();
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - Empty state should be visible
            var emptyState = page.Locator(".empty-state");
            await Assertions.Expect(emptyState).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Assert - Empty state message should reference "No Places Yet"
            var emptyMessage = page.Locator("text=No Places Yet");
            await Assertions.Expect(emptyMessage).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task DeleteButton_ShowsConfirmationDialog()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload an image first (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"DeleteTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Find and hover over the image tile to reveal delete button
            var imageTile = page.Locator(".image-tile").First;
            await imageTile.HoverAsync();

            // Act - Click the delete button
            var deleteButton = page.Locator(".delete-button").First;
            await deleteButton.ClickAsync();

            // Assert - Confirmation modal should appear
            var modal = page.Locator(".modal-overlay");
            await Assertions.Expect(modal).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });

            // Assert - Modal should contain "Delete Image?" title
            var modalTitle = page.Locator(".modal-title");
            await Assertions.Expect(modalTitle).ToContainTextAsync("Delete Image?");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ConfirmingDelete_RemovesImageFromView()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload an image first (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"ToDelete_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Verify the image is there
            var imageLabel = page.Locator($"text={uniqueLabel}");
            await Assertions.Expect(imageLabel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Hover to reveal delete button and click it
            var imageTile = page.Locator(".image-tile").First;
            await imageTile.HoverAsync();
            var deleteButton = page.Locator(".delete-button").First;
            await deleteButton.ClickAsync();

            // Wait for modal
            await page.WaitForSelectorAsync(".modal-overlay", new PageWaitForSelectorOptions { Timeout = 5000 });

            // Act - Click the confirm delete button
            var confirmButton = page.Locator(".modal-delete-button");
            await confirmButton.ClickAsync();

            // Assert - The image should no longer be visible
            await Assertions.Expect(imageLabel).Not.ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FavoriteToggle_ChangesIconState()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload an image first (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"FavTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Find the favorite button (should not have 'favorited' class initially)
            var favoriteButton = page.Locator(".favorite-button").First;

            // Assert - Button should not have favorited class initially
            await Assertions.Expect(favoriteButton).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("favorited"));

            // Act - Click the favorite button
            await favoriteButton.ClickAsync();

            // Assert - Button should now have favorited class
            await Assertions.Expect(favoriteButton).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("favorited"));

            // Act - Click again to unfavorite
            await favoriteButton.ClickAsync();

            // Assert - Button should no longer have favorited class
            await Assertions.Expect(favoriteButton).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("favorited"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FavoritedImage_AppearsInFavoritesView()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload an image first (which will navigate to page and allow localStorage access)
            var uniqueLabel = $"FavoritesViewTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Places library and favorite the image
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Click the favorite button
            var favoriteButton = page.Locator(".favorite-button").First;
            await favoriteButton.ClickAsync();

            // Wait for favorite state to update
            await Assertions.Expect(favoriteButton).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("favorited"));

            // Act - Navigate to Favorites page
            await page.GotoAsync($"{_fixture.BaseUrl}/favorites");
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - The favorited image should appear in Favorites view
            var imageLabel = page.Locator($"text={uniqueLabel}");
            await Assertions.Expect(imageLabel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Assert - The Places section header should be visible
            var placesSection = page.Locator(".section-header:has-text('Places')");
            await Assertions.Expect(placesSection).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Helper method to upload a test image with the specified label and category.
    /// </summary>
    /// <param name="page">The Playwright page instance.</param>
    /// <param name="label">The label for the image.</param>
    /// <param name="category">The category (Places or Food).</param>
    /// <param name="clearStorageFirst">Whether to clear localStorage before uploading (requires navigation first).</param>
    private async Task UploadTestImageAsync(IPage page, string label, string category, bool clearStorageFirst = false)
    {
        await page.GotoAsync($"{_fixture.BaseUrl}/upload");
        await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });

        if (clearStorageFirst)
        {
            await page.ClearLocalStorageAsync();
        }

        // Upload a test image
        var testImagePath = await CreateTestImageAsync();
        await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

        // Wait for preview
        await page.WaitForSelectorAsync(".preview-image", new PageWaitForSelectorOptions { Timeout = 10000 });

        // Fill label
        var labelInput = page.Locator("#image-label");
        await labelInput.FillAsync(label);

        // Select category
        var categoryButton = page.Locator($"button:has-text('{category}')");
        await categoryButton.ClickAsync();

        // Confirm upload
        var confirmButton = page.Locator(".confirm-button");
        await confirmButton.ClickAsync();

        // Wait for success message
        await page.WaitForSelectorAsync(".success-message", new PageWaitForSelectorOptions { Timeout = 15000 });
    }

    /// <summary>
    /// Creates a simple test image file for upload testing.
    /// Returns the path to the created image.
    /// </summary>
    private static async Task<string> CreateTestImageAsync()
    {
        // Create a simple valid PNG image (a small colored square)
        byte[] pngBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
            0x00, 0x00, 0x00, 0x0D, // IHDR chunk length
            0x49, 0x48, 0x44, 0x52, // IHDR
            0x00, 0x00, 0x00, 0x01, // width: 1
            0x00, 0x00, 0x00, 0x01, // height: 1
            0x08, 0x02, // bit depth: 8, color type: 2 (RGB)
            0x00, 0x00, 0x00, // compression, filter, interlace
            0x90, 0x77, 0x53, 0xDE, // CRC
            0x00, 0x00, 0x00, 0x0C, // IDAT chunk length
            0x49, 0x44, 0x41, 0x54, // IDAT
            0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00, // compressed data
            0x01, 0xA8, 0x00, 0x85, // pixel data + checksum
            0xA5, 0xEF, 0x73, 0x4F, // CRC
            0x00, 0x00, 0x00, 0x00, // IEND chunk length
            0x49, 0x45, 0x4E, 0x44, // IEND
            0xAE, 0x42, 0x60, 0x82  // CRC
        ];

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_image_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(tempPath, pngBytes);
        return tempPath;
    }
}
