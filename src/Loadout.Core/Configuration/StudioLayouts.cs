namespace Loadout.Core.Configuration;

/// <summary>The panels each of Studio's Home views is made of, by the names its layouts use.</summary>
/// <remarks>
/// Named once, here, for the config keys to check against; Studio's own copy
/// is pinned to these by a test, so a panel added on one side and not the
/// other fails the build rather than a layout.
/// </remarks>
public static class StudioLayouts
{
    /// <summary>The Lens, in the order it is drawn.</summary>
    public static readonly IReadOnlyList<string> Lens = ["needs", "focus", "all", "start", "signal"];

    /// <summary>The Map, in the order it is drawn.</summary>
    public static readonly IReadOnlyList<string> Map = ["status", "map", "strip"];
}
