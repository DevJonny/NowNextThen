using Microsoft.Playwright;
using NowThenNext.Tests.E2E.Fixtures;
using NowThenNext.Tests.E2E.Helpers;

namespace NowThenNext.Tests.E2E.Tests;

[Collection("BlazorApp")]
public class ZonesTests
{
    private readonly BlazorAppFixture _fixture;

    public ZonesTests(BlazorAppFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<IPage> OpenZonesWithCleanStorageAsync()
    {
        var page = await _fixture.CreatePageAsync();
        await page.GotoAsync($"{_fixture.BaseUrl}/zones");
        await page.WaitForSelectorAsync(".zone-tile", new PageWaitForSelectorOptions { Timeout = 60000 });
        await page.ClearLocalStorageAsync();
        return page;
    }

    [Fact]
    public async Task HomeButton_NavigatesToZones_ShowingFourZones()
    {
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync(_fixture.BaseUrl);
            await page.WaitForSelectorAsync("nav a", new PageWaitForSelectorOptions { Timeout = 60000 });

            await page.Locator("a[href='zones']:has-text('How do I feel?')").ClickAsync();

            await page.WaitForURLAsync($"{_fixture.BaseUrl}/zones", new PageWaitForURLOptions { Timeout = 10000 });
            await Assertions.Expect(page.Locator(".zone-tile")).ToHaveCountAsync(4);
            foreach (var zone in new[] { "Green", "Yellow", "Red", "Blue" })
                await Assertions.Expect(page.Locator($".zone-tile:has-text('{zone}')")).ToBeVisibleAsync();
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ChoosingAFeeling_ShowsItLarge_AndRecordsItInHistory()
    {
        var page = await OpenZonesWithCleanStorageAsync();

        try
        {
            await page.Locator(".zone-tile[data-zone='blue']").ClickAsync();
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/zones/blue", new PageWaitForURLOptions { Timeout = 10000 });
            await Assertions.Expect(page.Locator(".feeling-tile")).ToHaveCountAsync(4);

            await page.Locator(".feeling-tile[data-feeling='tired']").ClickAsync();

            await page.WaitForURLAsync($"{_fixture.BaseUrl}/zones/blue/tired", new PageWaitForURLOptions { Timeout = 10000 });
            await Assertions.Expect(page.Locator(".feeling-display-name")).ToHaveTextAsync("Tired");
            await Assertions.Expect(page.Locator(".feeling-display-zone")).ToContainTextAsync("Blue zone");

            await page.Locator("a.feeling-display-again").ClickAsync();
            await page.Locator("a[href='zones/history']").ClickAsync();

            var entries = page.Locator("[data-testid='history-entry']");
            await Assertions.Expect(entries).ToHaveCountAsync(1, new LocatorAssertionsToHaveCountOptions { Timeout = 10000 });
            await Assertions.Expect(entries.First).ToContainTextAsync("Tired");
            await Assertions.Expect(page.Locator(".history-summary-tile[data-zone='blue'] .history-summary-count")).ToHaveTextAsync("1");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task ClearHistory_AfterConfirming_ShowsEmptyState()
    {
        var page = await OpenZonesWithCleanStorageAsync();

        try
        {
            await page.Locator(".zone-tile[data-zone='green']").ClickAsync();
            await page.Locator(".feeling-tile[data-feeling='happy']").ClickAsync();
            await page.WaitForURLAsync($"{_fixture.BaseUrl}/zones/green/happy", new PageWaitForURLOptions { Timeout = 10000 });

            await page.GotoAsync($"{_fixture.BaseUrl}/zones/history");
            await page.WaitForSelectorAsync("[data-testid='history-entry']", new PageWaitForSelectorOptions { Timeout = 60000 });

            await page.Locator(".history-clear-button").ClickAsync();
            await page.Locator(".modal-confirm-button").ClickAsync();

            await Assertions.Expect(page.Locator("[data-testid='history-empty']")).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
            Assert.Null(await page.GetLocalStorageItemAsync("feelings-log"));
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Theory]
    [InlineData("zones/purple")]
    [InlineData("zones/green/angry")]
    public async Task UnknownZoneOrMismatchedFeeling_RedirectsToZones(string path)
    {
        var page = await _fixture.CreatePageAsync();

        try
        {
            await page.GotoAsync($"{_fixture.BaseUrl}/{path}");

            await page.WaitForURLAsync($"{_fixture.BaseUrl}/zones", new PageWaitForURLOptions { Timeout = 60000 });
            await Assertions.Expect(page.Locator(".zone-tile")).ToHaveCountAsync(4);
        }
        finally
        {
            await page.CloseAsync();
        }
    }
}
