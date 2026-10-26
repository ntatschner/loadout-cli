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
    /// What Studio's tube map draws: inflight (live work, and failures for a
    /// day) or all (every line Home has). Null is inflight.
    /// </summary>
    public string? MapScope { get; set; }

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
