using Microsoft.AspNetCore.Components;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Pages;

public partial class FeelingsHistoryPage
{
    [Inject]
    private IFeelingsLogService FeelingsLog { get; set; } = default!;

    [Inject]
    private TimeProvider TimeProvider { get; set; } = default!;

    private List<FeelingHistoryDay> Days = [];
    private Dictionary<Zone, int> ZoneCounts = [];
    private bool IsLoaded;
    private bool ShowClearConfirmation;

    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        var entries = await FeelingsLog.GetHistoryAsync();
        var nowUtc = TimeProvider.GetUtcNow().UtcDateTime;
        var timeZone = TimeProvider.LocalTimeZone;

        Days = FeelingsHistory.GroupByDay(entries, nowUtc, timeZone);
        ZoneCounts = FeelingsHistory.CountByZone(entries, nowUtc, timeZone);
        IsLoaded = true;
    }

    private async Task ConfirmClearAsync()
    {
        await FeelingsLog.ClearAsync();
        ShowClearConfirmation = false;
        await LoadAsync();
    }
}
