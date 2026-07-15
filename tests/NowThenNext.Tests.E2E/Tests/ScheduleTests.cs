using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

/// <summary>
/// E2E tests for the schedule flow (Plan the Day).
/// Tests selection of images, Now/Next/Then badges, and schedule display.
/// </summary>
[Collection("BlazorApp")]
public class ScheduleTests
{
    private readonly BlazorAppFixture _fixture;

    public ScheduleTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CanSelectImagesInScheduleSelectionView()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload a place image first
            var uniqueLabel = $"PlanTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Act - Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Find and click the selectable tile
            var selectableTile = page.Locator(".selectable-tile").First;
            await selectableTile.ClickAsync();

            // Assert - The tile should have the 'selected' class
            await Assertions.Expect(selectableTile).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("selected"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SelectionShowsNowNextThenBadgesInOrder()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload 3 place images
            await UploadTestImageAsync(page, $"First_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: true);
            await UploadTestImageAsync(page, $"Second_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: false);
            await UploadTestImageAsync(page, $"Third_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: false);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Act - Select 3 images in order
            var tiles = page.Locator(".selectable-tile");
            await tiles.Nth(0).ClickAsync();
            await tiles.Nth(1).ClickAsync();
            await tiles.Nth(2).ClickAsync();

            // Assert - Check that badges show Now, Next, Then labels in order
            var badges = page.Locator(".selection-badge");
            var badgeLabels = page.Locator(".badge-label");

            // First badge should say "Now"
            await Assertions.Expect(badgeLabels.Nth(0)).ToContainTextAsync("Now");
            // Second badge should say "Next"
            await Assertions.Expect(badgeLabels.Nth(1)).ToContainTextAsync("Next");
            // Third badge should say "Then"
            await Assertions.Expect(badgeLabels.Nth(2)).ToContainTextAsync("Then");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task CannotSelectMoreThan3Images()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload 4 place images
            await UploadTestImageAsync(page, $"Img1_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: true);
            await UploadTestImageAsync(page, $"Img2_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: false);
            await UploadTestImageAsync(page, $"Img3_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: false);
            await UploadTestImageAsync(page, $"Img4_{Guid.NewGuid().ToString()[..8]}", "Places", clearStorageFirst: false);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Act - Try to select 4 images
            var tiles = page.Locator(".selectable-tile");
            await tiles.Nth(0).ClickAsync();
            await tiles.Nth(1).ClickAsync();
            await tiles.Nth(2).ClickAsync();
            await tiles.Nth(3).ClickAsync(); // This should NOT select

            // Assert - Only 3 tiles should have the 'selected' class
            var selectedTiles = page.Locator(".selectable-tile.selected");
            await Assertions.Expect(selectedTiles).ToHaveCountAsync(3);

            // Assert - Selection count should show 3 of 3
            var selectionCount = page.Locator(".selection-count-number");
            await Assertions.Expect(selectionCount).ToContainTextAsync("3");

            // Assert - Fourth tile should NOT be selected
            await Assertions.Expect(tiles.Nth(3)).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("selected"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task CanDeselectByTappingSelectedImage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload a place image
            var uniqueLabel = $"DeselectTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Select the tile
            var selectableTile = page.Locator(".selectable-tile").First;
            await selectableTile.ClickAsync();

            // Verify it's selected
            await Assertions.Expect(selectableTile).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("selected"));

            // Act - Click again to deselect
            await selectableTile.ClickAsync();

            // Assert - The tile should no longer have 'selected' class
            await Assertions.Expect(selectableTile).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("selected"));

            // Assert - Selection count should be 0
            var selectionCount = page.Locator(".selection-count-number");
            await Assertions.Expect(selectionCount).ToContainTextAsync("0");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ShowScheduleButtonAppearsAfterSelection()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload a place image
            var uniqueLabel = $"ShowBtnTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - Show Schedule button should NOT be visible initially
            var showScheduleButton = page.Locator(".show-schedule-button");
            await Assertions.Expect(showScheduleButton).Not.ToBeVisibleAsync();

            // Act - Select an image
            var selectableTile = page.Locator(".selectable-tile").First;
            await selectableTile.ClickAsync();

            // Assert - Show Schedule button should now be visible
            await Assertions.Expect(showScheduleButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 5000 });
            await Assertions.Expect(showScheduleButton).ToContainTextAsync("Show Schedule");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ScheduleDisplayShowsImagesWithCorrectLabels()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload 2 place images with known labels
            var label1 = $"Activity1_{Guid.NewGuid().ToString()[..8]}";
            var label2 = $"Activity2_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, label1, "Places", clearStorageFirst: true);
            await UploadTestImageAsync(page, label2, "Places", clearStorageFirst: false);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Select both images
            var tiles = page.Locator(".selectable-tile");
            await tiles.Nth(0).ClickAsync();
            await tiles.Nth(1).ClickAsync();

            // Click Show Schedule button
            var showScheduleButton = page.Locator(".show-schedule-button");
            await showScheduleButton.ClickAsync();

            // Wait for navigation to schedule display
            await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/schedule"), new PageWaitForURLOptions { Timeout = 10000 });
            await page.WaitForSelectorAsync(".schedule-display, .auto-advance-display", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - Schedule items should be displayed with Now and Next labels
            var itemLabels = page.Locator(".item-label");
            await Assertions.Expect(itemLabels.First).ToContainTextAsync("Now");
            await Assertions.Expect(itemLabels.Nth(1)).ToContainTextAsync("Next");

            // Assert - The schedule items should have images visible
            var scheduleItems = page.Locator(".schedule-item");
            await Assertions.Expect(scheduleItems).ToHaveCountAsync(2);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task BackButtonReturnsToSelection()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Upload a place image
            var uniqueLabel = $"BackBtnTest_{Guid.NewGuid().ToString()[..8]}";
            await UploadTestImageAsync(page, uniqueLabel, "Places", clearStorageFirst: true);

            // Navigate to Plan the Day page
            await page.GotoAsync($"{_fixture.BaseUrl}/plan");
            await page.WaitForSelectorAsync(".image-grid", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Select the image and go to schedule display
            var selectableTile = page.Locator(".selectable-tile").First;
            await selectableTile.ClickAsync();
            var showScheduleButton = page.Locator(".show-schedule-button");
            await showScheduleButton.ClickAsync();

            // Wait for schedule display page
            await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/schedule"), new PageWaitForURLOptions { Timeout = 10000 });
            await page.WaitForSelectorAsync(".schedule-display, .auto-advance-display, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Act - Click the back button
            var backButton = page.Locator(".back-button");
            await backButton.ClickAsync();

            // Assert - Should navigate back to /plan
            await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/plan"), new PageWaitForURLOptions { Timeout = 10000 });
            Assert.EndsWith("/plan", page.Url);

            // Assert - The Plan the Day page content should be visible
            var heading = page.Locator("h1");
            await Assertions.Expect(heading).ToContainTextAsync("Plan the Day");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Helper method to upload a test image with the specified label and category.
    /// </summary>
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
    /// </summary>
    private static async Task<string> CreateTestImageAsync()
    {
        byte[] pngBytes =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x01,
            0x08, 0x02,
            0x00, 0x00, 0x00,
            0x90, 0x77, 0x53, 0xDE,
            0x00, 0x00, 0x00, 0x0C,
            0x49, 0x44, 0x41, 0x54,
            0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, 0x00,
            0x01, 0xA8, 0x00, 0x85,
            0xA5, 0xEF, 0x73, 0x4F,
            0x00, 0x00, 0x00, 0x00,
            0x49, 0x45, 0x4E, 0x44,
            0xAE, 0x42, 0x60, 0x82
        ];

        var tempPath = Path.Combine(Path.GetTempPath(), $"test_image_{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(tempPath, pngBytes);
        return tempPath;
    }
}
