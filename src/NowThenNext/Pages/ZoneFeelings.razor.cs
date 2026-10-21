using Microsoft.AspNetCore.Components;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Pages;

public partial class ZoneFeelings
{
    [Inject]
    private IFeelingsLogService FeelingsLog { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string ZoneSlug { get; set; } = string.Empty;

    private ZoneInfo? Zone;
    private bool IsChoosing;

    protected override void OnParametersSet()
    {
        Zone = ZonesData.GetBySlug(ZoneSlug);
        IsChoosing = false;
        if (Zone == null)
            Navigation.NavigateTo("zones", replace: true);
    }

    private async Task ChooseAsync(Feeling feeling)
    {
        // Ignore a second tap while the first is being saved, so one choice logs once.
        if (IsChoosing || Zone == null) return;
        IsChoosing = true;

        try
        {
            await FeelingsLog.LogAsync(feeling.Id);
        }
        catch (Exception ex)
        {
            // Still show the feeling: the child's choice matters more than the log.
            Console.Error.WriteLine($"Failed to log feeling: {ex.Message}");
        }

        Navigation.NavigateTo($"zones/{Zone.Slug}/{feeling.Id}");
    }
}
