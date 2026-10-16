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

    /// <summary>Minutes a working session is quiet before Studio reads it as idle. Null is ten.</summary>
    public int? IdleAfter { get; set; }
}
