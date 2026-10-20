using Microsoft.AspNetCore.Components;
using NowThenNext.Models;
using NowThenNext.Services;

namespace NowThenNext.Pages;

public partial class FeelingDisplay
{
    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Parameter]
    public string ZoneSlug { get; set; } = string.Empty;

    [Parameter]
    public string FeelingId { get; set; } = string.Empty;

    private ZoneInfo? Zone;
    private Feeling? Feeling;

    protected override void OnParametersSet()
    {
        Zone = ZonesData.GetBySlug(ZoneSlug);
        Feeling = ZonesData.GetFeeling(FeelingId);

        if (Zone == null || Feeling == null || Feeling.Zone != Zone.Zone)
        {
            Zone = null;
            Feeling = null;
            Navigation.NavigateTo("zones", replace: true);
        }
    }
}
