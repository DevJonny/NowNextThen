using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

/// <summary>
/// E2E tests for the image upload workflow.
/// Tests the complete upload flow from file selection to image appearing in library.
/// </summary>
[Collection("BlazorApp")]
public class UploadTests
{
    private readonly BlazorAppFixture _fixture;

    public UploadTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UploadArea_ClickTriggersFileInputViaJsInterop()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Replace triggerFileInputClick with a spy to capture the call
            await page.EvaluateAsync("window._triggerFileInputCalledWith = null; window.triggerFileInputClick = (id) => { window._triggerFileInputCalledWith = id; }");

            // Act - Click the upload area like a real user would
            await page.Locator(".upload-area").ClickAsync();

            // Assert - The JS interop should have been called with the correct input ID
            var calledWith = await page.EvaluateAsync<string>("window._triggerFileInputCalledWith");
            Assert.Equal("file-upload", calledWith);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task UploadArea_HasFileInputElement()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act & Assert - Verify the hidden file input exists and accepts image files
            var fileInput = page.Locator("#file-upload");
            await Assertions.Expect(fileInput).ToHaveCountAsync(1);
            await Assertions.Expect(fileInput).ToHaveAttributeAsync("accept", new System.Text.RegularExpressions.Regex("image"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ImagePreview_DisplaysAfterFileSelection()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - Upload a test image
            var testImagePath = await CreateTestImageAsync();
            await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

            // Assert - Preview image should be visible
            await page.WaitForSelectorAsync(".preview-image", new PageWaitForSelectorOptions { Timeout = 10000 });
            var previewImage = page.Locator(".preview-image");
            await Assertions.Expect(previewImage).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Assert - Upload area should no longer be visible (replaced by preview)
            var uploadArea = page.Locator(".upload-area");
            await Assertions.Expect(uploadArea).Not.ToBeVisibleAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task CategorySelector_SwitchesBetweenPlacesAndFood()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // First, upload an image to see the category selector
            var testImagePath = await CreateTestImageAsync();
            await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

            // Wait for preview and category buttons
            await page.WaitForSelectorAsync(".category-buttons", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Act - Click Food category button
            var foodButton = page.Locator("button:has-text('Food')");
            await foodButton.ClickAsync();

            // Assert - Food button should have selected class
            await Assertions.Expect(foodButton).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("category-selected"));

            // Act - Click Places category button
            var placesButton = page.Locator("button:has-text('Places')");
            await placesButton.ClickAsync();

            // Assert - Places button should have selected class
            await Assertions.Expect(placesButton).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("category-selected"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task LabelInput_AcceptsAndDisplaysText()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // First, upload an image to see the label input
            var testImagePath = await CreateTestImageAsync();
            await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

            // Wait for the label input to appear
            await page.WaitForSelectorAsync("#image-label", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Act - Type a label
            var labelInput = page.Locator("#image-label");
            await labelInput.FillAsync("Test Park");

            // Assert - Input should contain the entered text
            await Assertions.Expect(labelInput).ToHaveValueAsync("Test Park");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ConfirmButton_SavesImageAndShowsSuccessMessage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Upload an image
            var testImagePath = await CreateTestImageAsync();
            await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

            // Wait for preview and fill in details
            await page.WaitForSelectorAsync(".confirm-button", new PageWaitForSelectorOptions { Timeout = 10000 });

            var labelInput = page.Locator("#image-label");
            await labelInput.FillAsync("Test Location");

            // Ensure Places category is selected (default)
            var placesButton = page.Locator("button:has-text('Places')");
            await placesButton.ClickAsync();

            // Act - Click confirm button
            var confirmButton = page.Locator(".confirm-button");
            await confirmButton.ClickAsync();

            // Assert - Success message should appear
            await page.WaitForSelectorAsync(".success-message", new PageWaitForSelectorOptions { Timeout = 15000 });
            var successMessage = page.Locator(".success-message");
            await Assertions.Expect(successMessage).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(successMessage).ToContainTextAsync("saved successfully");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task UploadedImage_AppearsInCorrectLibraryView()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/upload");
            await page.WaitForSelectorAsync(".upload-area", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Upload an image with a unique label
            var uniqueLabel = $"TestPlace_{Guid.NewGuid().ToString()[..8]}";

            var testImagePath = await CreateTestImageAsync();
            await page.Locator("#file-upload").SetInputFilesAsync(testImagePath);

            await page.WaitForSelectorAsync(".confirm-button", new PageWaitForSelectorOptions { Timeout = 10000 });

            // Fill label and select Places category
            var labelInput = page.Locator("#image-label");
            await labelInput.FillAsync(uniqueLabel);

            var placesButton = page.Locator("button:has-text('Places')");
            await placesButton.ClickAsync();

            // Confirm upload
            var confirmButton = page.Locator(".confirm-button");
            await confirmButton.ClickAsync();

            // Wait for success message
            await page.WaitForSelectorAsync(".success-message", new PageWaitForSelectorOptions { Timeout = 15000 });

            // Act - Navigate to Places library
            await page.GotoAsync($"{_fixture.BaseUrl}/places");
            await page.WaitForSelectorAsync(".image-grid, .empty-state", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Assert - The uploaded image should appear with its label
            var imageLabel = page.Locator($"text={uniqueLabel}");
            await Assertions.Expect(imageLabel).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    /// <summary>
    /// Creates a simple test image file for upload testing.
    /// Returns the path to the created image.
    /// </summary>
    private static async Task<string> CreateTestImageAsync()
    {
        // Create a simple valid PNG image (a small colored square)
        // PNG header and a minimal 1x1 red pixel image
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
