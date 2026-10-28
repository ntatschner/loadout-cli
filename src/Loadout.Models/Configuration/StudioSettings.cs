namespace Loadout.Models.Configuration;

/// <summary>
/// How Studio, the app the daemon serves, looks and is laid out for this
/// person.
/// </summary>
/// <remarks>
/// <para>
/// In Loadout's config rather than the browser, so every browser signed in to
/// this machine opens Studio the same way, and a new one does not start from
/// nothing. Single-letter shortcuts and Ambient fullscreen stay in the
/// browser: they are about the keyboard and the screen in front of you.
/// </para>
/// <para>
/// Every one is null until somebody chooses, which Studio reads as its
/// default, so a value is only ever written by a person.
/// </para>
/// </remarks>
public sealed class StudioSettings
{
    /// <summary>The Lens halo's style: orbit or gyroscope. Null is orbit.</summary>
    public string? Halo { get; set; }

    /// <summary>The Lens layout as Studio stores it, as JSON. Null is the layout as drawn.</summary>
    public string? LayoutLens { get; set; }

    /// <summary>The Map layout as Studio stores it, as JSON. Null is the layout as drawn.</summary>
    public string? LayoutMap { get; set; }

    /// <summary>dark, light, hc-dark or hc-light. Null follows the operating system.</summary>
    public string? Theme { get; set; }

    /// <summary>cyan, ice, magenta or violet. Null is cyan.</summary>
    public string? Accent { get; set; }

    /// <summary>compact, comfortable or roomy. Null is comfortable.</summary>
    public string? Density { get; set; }

    /// <summary>full, reduced or none. Null is full; the operating system's reduced motion still wins.</summary>
    public string? Motion { get; set; }

    /// <summary>
    /// What is behind every page: plain, map (the field map, still) or
    /// map-animated. Null is plain. Pure Focus, Ambient and high contrast
    /// show the plain ground whatever this says.
    /// </summary>
    public string? Backdrop { get; set; }

    /// <summary>
    /// The kinds Studio's Map and Lens show, as <see cref="StudioMap.Kinds"/>
    /// names them, comma-separated. Null is In flight's: runs, sessions and
    /// tasks.
    /// </summary>
    public string? MapKinds { get; set; }

    /// <summary>
    /// The states Studio's Map and Lens show, as <see cref="StudioMap.States"/>
    /// names them, comma-separated. Null is In flight's: waiting, running and
    /// failed.
    /// </summary>
    public string? MapStates { get; set; }

    /// <summary>
    /// The projects Studio's Map and Lens hide, comma-separated, with
    /// <see cref="StudioMap.NoProject"/> for work outside any project. A list
    /// of what to hide, so a project added later shows. Null hides nothing.
    /// </summary>
    public string? MapProjects { get; set; }

    /// <summary>
    /// The retired studio-map-scope, read from a file written before the
    /// three filters and never written back.
    /// </summary>
    /// <remarks>
    /// Read once: all becomes Everything's kinds and states, unless the file
    /// already says otherwise, and inflight is what the three unset already
    /// mean. The getter is always null, so the next save drops the old value
    /// and only the new keys remain.
    /// </remarks>
    public string? MapScope
    {
        get => null;
        set
        {
            if (string.Equals(value?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                MapKinds ??= string.Join(", ", StudioMap.Kinds);
                MapStates ??= string.Join(", ", StudioMap.States);
            }
        }
    }

    /// <summary>Minutes a working session is quiet before Studio reads it as idle. Null is ten.</summary>
    public int? IdleAfter { get; set; }

    /// <summary>
    /// Minutes a hosted session waiting on its own background work may go
    /// with nothing reporting back before Studio reads it as waiting for you.
    /// Null is thirty.
    /// </summary>
    public int? BackgroundLimit { get; set; }

    /// <summary>
    /// Each list's saved query, by view: "tasks" to "state:doing sort:-updated".
    /// A view missing here has never been saved, and opens with its built-in
    /// default.
    /// </summary>
    /// <remarks>
    /// Kept with Studio's settings though not only Studio's: the TUI opens a
    /// list with it, and 'task list --saved' applies it, so a filter set on one
    /// screen is the one every screen starts from.
    /// </remarks>
    public Dictionary<string, string> Views { get; set; } = [];
}
