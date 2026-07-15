using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

/// <summary>
/// Smoke tests for the home page functionality.
/// </summary>
[Collection("BlazorApp")]
public class HomePageTests
{
    private readonly BlazorAppFixture _fixture;

    public HomePageTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HomePage_LoadsSuccessfully()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Act
            await page.GotoAsync(_fixture.BaseUrl);

            // Wait for Blazor WASM to fully initialize - look for the app content
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });

            // Clear localStorage for test isolation
            await page.ClearLocalStorageAsync();

            // Assert - verify the app title is displayed (with arrow format: "Now → Next → Then")
            var title = await page.TextContentAsync("h1");
            Assert.Contains("Now", title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Then", title, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Next", title, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task HomePage_DisplaysAllSixMainButtons()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Act
            await page.GotoAsync(_fixture.BaseUrl);

            // Wait for Blazor WASM to fully initialize - look for navigation buttons
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Assert - verify all six buttons are visible by their text content
            // Button order per US-006: Places + Plan the Day (pair), Food + Food Choices (pair), Activities + Activity Choices (pair)
            // Favorites is now a header icon, not a menu button
            var placesButton = page.Locator("a[href='places']:has-text('Places')");
            var planButton = page.Locator("a:has-text('Plan the Day')");
            var foodButton = page.Locator("a[href='food']:has-text('Food')");
            var foodChoicesButton = page.Locator("a[href='food-choices']:has-text('Food Choices')");
            var activitiesButton = page.Locator("a[href='activities']:has-text('Activities')");
            var activityChoicesButton = page.Locator("a[href='activity-choices']:has-text('Activity Choices')");

            await Assertions.Expect(placesButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(planButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(foodButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(foodChoicesButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(activitiesButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(activityChoicesButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task HomePage_MenuButtonsAreGroupedInPairs()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            // Act
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Assert - verify that buttons are grouped with group containers
            var menuGroups = page.Locator(".menu-group");
            await Assertions.Expect(menuGroups).ToHaveCountAsync(4, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });

            // Verify first 3 groups have exactly 2 buttons each
            for (int i = 0; i < 3; i++)
            {
                var groupButtons = menuGroups.Nth(i).Locator(".menu-button");
                await Assertions.Expect(groupButtons).ToHaveCountAsync(2);
            }

            // Verify 4th group (Learning) has exactly 2 buttons (Phonics + Learning Cards)
            var learningButtons = menuGroups.Nth(3).Locator(".menu-button");
            await Assertions.Expect(learningButtons).ToHaveCountAsync(2);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task PlanTheDayButton_NavigatesToPlanPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Plan the Day button
            var planButton = page.Locator("a:has-text('Plan the Day')");
            await planButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/plan", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the plan page
            Assert.EndsWith("/plan", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FoodButton_NavigatesToFoodPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Food button (navigates to food library)
            // Use exact match to avoid matching "Food Choices"
            var foodButton = page.Locator("a[href='food']:has-text('Food')");
            await foodButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/food", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the food page
            Assert.EndsWith("/food", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task PlacesButton_NavigatesToPlacesPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Places button (navigates to places library)
            var placesButton = page.Locator("a:has-text('Places')");
            await placesButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/places", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the places page
            Assert.EndsWith("/places", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FavoritesHeartIcon_IsVisibleAndNavigatesToFavoritesPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Assert - verify the favorites heart icon is visible in header
            var favoritesButton = page.Locator("a.favorites-button");
            await Assertions.Expect(favoritesButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Act - click the favorites button
            await favoritesButton.ClickAsync();

            // Wait for navigation to favorites page
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/favorites", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we navigated to favorites
            Assert.EndsWith("/favorites", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task FoodChoicesButton_NavigatesToFoodChoicesPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Food Choices button
            var foodChoicesButton = page.Locator("a[href='food-choices']:has-text('Food Choices')");
            await foodChoicesButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/food-choices", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the food choices page
            Assert.EndsWith("/food-choices", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ActivitiesButton_NavigatesToActivitiesPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Activities button
            var activitiesButton = page.Locator("a[href='activities']:has-text('Activities')");
            await activitiesButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/activities", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the activities page
            Assert.EndsWith("/activities", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ActivityChoicesButton_NavigatesToActivityChoicesPage()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Act - click the Activity Choices button
            var activityChoicesButton = page.Locator("a[href='activity-choices']:has-text('Activity Choices')");
            await activityChoicesButton.ClickAsync();

            // Wait for navigation
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/activity-choices", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we're on the activity choices page
            Assert.EndsWith("/activity-choices", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SettingsGearIcon_IsVisibleAndClickable()
    {
        // Arrange
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            // Assert - verify the settings gear icon is visible
            var settingsButton = page.Locator("a.settings-button");
            await Assertions.Expect(settingsButton).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

            // Act - click the settings button
            await settingsButton.ClickAsync();

            // Wait for navigation to settings page
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/settings", new PageWaitForURLOptions { Timeout = 10000 });

            // Assert - verify we navigated to settings
            Assert.EndsWith("/settings", page.Url);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
