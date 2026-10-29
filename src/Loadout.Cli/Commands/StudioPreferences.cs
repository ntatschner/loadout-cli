using Loadout.Core.Configuration;
using Loadout.Core.Lists;
using Loadout.Models.Configuration;

namespace Loadout.Cli.Commands;

/// <summary>Studio's settings as the page reads them: what is set, and the default where nothing is.</summary>
internal static class StudioPreferences
{
    /// <summary>The settings, with each one's config key so the page can show the command that changes it.</summary>
    /// <remarks>
    /// A theme left unset is said to be unset rather than filled in, because
    /// unset means "follow the operating system", which only the browser can
    /// answer.
    /// </remarks>
    public static object Read(StudioSettings? studio) => new
    {
        halo = studio?.Halo ?? "orbit",
        theme = studio?.Theme,
        accent = studio?.Accent ?? "cyan",
        density = studio?.Density ?? "comfortable",
        motion = studio?.Motion ?? "full",
        backdrop = studio?.Backdrop ?? "plain",
        idleAfter = studio?.IdleAfter ?? 10,
        layoutLens = studio?.LayoutLens,
        layoutMap = studio?.LayoutMap,
        mapKinds = studio?.MapKinds,
        mapStates = studio?.MapStates,
        mapProjects = studio?.MapProjects,
        panels = new { lens = StudioLayouts.Lens, map = StudioLayouts.Map },
        views = ListViews.All.ToDictionary(view => view.Name, view => ListViews.Saved(studio, view.Name)),
    };
}
