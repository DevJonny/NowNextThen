using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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
    private bool focusCancel;
    private bool restoreFocus;
    private bool clearCompleted;
    private ElementReference backLink;
    private ElementReference clearButton;
    private ElementReference cancelButton;

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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (focusCancel)
        {
            focusCancel = false;
            await cancelButton.FocusAsync();
        }
        else if (restoreFocus)
        {
            restoreFocus = false;
            await (clearCompleted ? backLink : clearButton).FocusAsync();
        }
    }

    private void OpenClearConfirmation()
    {
        ShowClearConfirmation = true;
        focusCancel = true;
    }

    private void CancelClear()
    {
        ShowClearConfirmation = false;
        clearCompleted = false;
        restoreFocus = true;
    }

    private void OnDialogKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            CancelClear();
        }
    }

    private async Task ConfirmClearAsync()
    {
        await FeelingsLog.ClearAsync();
        ShowClearConfirmation = false;
        clearCompleted = true;
        restoreFocus = true;
        await LoadAsync();
    }
}
