using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

/// <summary>
/// E2E tests for the signed-out Cloud Sync UI.
/// Google sign-in uses a popup that can't be automated, so only the signed-out state is covered.
/// </summary>
[Collection("BlazorApp")]
public class CloudSyncTests
{
    private readonly BlazorAppFixture _fixture;

    public CloudSyncTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task SyncIndicator_ShowsSignedOutStateOnHomeScreen()
    {
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();
            await page.ReloadAsync();
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });

            var indicator = page.Locator("button.sync-indicator");
            await Assertions.Expect(indicator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            await Assertions.Expect(indicator).ToHaveAttributeAsync("aria-label", "Cloud sync — sign in via Settings");
            await Assertions.Expect(indicator.Locator(".sync-dot.is-signed-out")).ToBeVisibleAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SyncIndicator_NavigatesToSettings()
    {
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            await page.Locator("button.sync-indicator").ClickAsync();

            await page.WaitForURLAsync($"{_fixture.BaseUrl}/settings", new PageWaitForURLOptions { Timeout = 10000 });
            await Assertions.Expect(page.Locator("h2:has-text('Cloud Sync')")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task SettingsCloudSync_ShowsSignInWhenSignedOut()
    {
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("h1", new PageWaitForSelectorOptions { Timeout = 60000 });
            await page.ClearLocalStorageAsync();

            await page.GotoAsync($"{_fixture.BaseUrl}/settings");
            await page.WaitForSelectorAsync("h2:has-text('Cloud Sync')", new PageWaitForSelectorOptions { Timeout = 60000 });

            await Assertions.Expect(page.Locator(".sync-status-badge.sync-signed-out")).ToHaveTextAsync("Not signed in");
            await Assertions.Expect(page.Locator("button:has-text('Sign in to Google Drive')")).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator("button:has-text('Sync now')")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("button:has-text('Sign out')")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator(".sync-last-synced")).ToHaveCountAsync(0);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
