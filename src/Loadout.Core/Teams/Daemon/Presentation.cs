using Loadout.Core.Instructions;
using Loadout.Models.Configuration;

namespace Loadout.Core.Teams.Daemon;

/// <summary>How much the dashboard draws beyond what it has to say.</summary>
public enum Presentation
{
    /// <summary>Everything the page can do with a window, for somebody watching one.</summary>
    Rich,

    /// <summary>The page with nothing on it that is not information.</summary>
    Plain,
}

/// <summary>
/// What the page should look like, from what this person has already said
/// about how they want to be written to.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard was built plain from its first commit, because the semantics
/// had to be right before anything was laid on top of them and because a page
/// that is legible to a screen reader is a page whose structure is honest. It
/// stayed plain for everybody, which is a different decision and not one
/// anybody made: somebody with no stated difficulty reading a screen is being
/// handed the presentation designed for the hardest case.
/// </para>
/// <para>
/// So the plain page becomes what it was always for, and is served to the
/// people who said they need it - and everybody else gets the page with its
/// chrome on. Nothing is detected. The only thing consulted is the profile in
/// <c>config.yaml</c> that a person wrote themselves, and the flag that
/// overrides it for one run.
/// </para>
/// <para>
/// Which profiles mean plain is a judgement, and here it is: the
/// <c>screen-reader</c> and <c>low-vision</c> presets, and a profile asking
/// for no colour. Those are the three that say something about reading a
/// screen. <c>colour-blind</c>, <c>dyslexia</c>, <c>adhd</c> and
/// <c>plain-language</c> deliberately do not: none of them is helped by taking
/// the chrome away, and two of them are about prose rather than pictures. What
/// they do carry - a colour-safe palette, less motion - is honoured inside the
/// rich page instead, because those are settings about how a thing is drawn
/// and not about whether to draw it.
/// </para>
/// <para>
/// Both pages are the same markup and meet the same bar. The rich one adds no
/// meaning that is carried by colour alone, removes no heading and no live
/// region, and is still the page that has to pass the audit. Richness here is
/// depth, weight and movement, never a word taken away.
/// </para>
/// </remarks>
public static class Presenting
{
    /// <summary>The page to serve somebody with this profile.</summary>
    /// <param name="settings">The profile as configured, or null where none was read.</param>
    public static Presentation For(AccessibilitySettings? settings)
    {
        if (settings is null || !AccessibilityProfile.IsSet(settings))
        {
            // Nothing said. The ordinary case, and the one the rich page is
            // for.
            return Presentation.Rich;
        }

        var profile = AccessibilityProfile.Resolve(settings);

        if (Named(profile.Preset, AccessibilityPresets.ScreenReader)
            || Named(profile.Preset, AccessibilityPresets.LowVision))
        {
            return Presentation.Plain;
        }

        // "No colour" is the one display setting that cannot be honoured
        // inside a rich page: the chrome is largely made of colour, and drawn
        // without it what is left is a plain page with worse spacing.
        return Named(profile.Display.Colour, "none") ? Presentation.Plain : Presentation.Rich;
    }

    /// <summary>
    /// How much the page may move, from the profile, as the page's own word
    /// for it.
    /// </summary>
    /// <remarks>
    /// The browser is asked separately, through <c>prefers-reduced-motion</c>,
    /// and the page takes the quieter of the two. This can only ever reduce
    /// what the operating system already allows, never restore it.
    /// </remarks>
    public static string Motion(AccessibilitySettings? settings) =>
        settings is null ? "full" : AccessibilityProfile.Resolve(settings).Display.Motion switch
        {
            "none" => "none",
            "reduced" => "reduced",
            _ => "full",
        };

    /// <summary>
    /// Whether the palette has to survive a colour deficiency, as the page's
    /// own word for it.
    /// </summary>
    /// <remarks>
    /// The page already avoids red against green everywhere, because that is
    /// the pair a common deficiency cannot separate and it was cheaper to
    /// never use it than to have two palettes. This says so out loud, so the
    /// rich page's additions are held to the same rule rather than
    /// rediscovering it.
    /// </remarks>
    public static string Colour(AccessibilitySettings? settings) =>
        settings is not null && AccessibilityProfile.Resolve(settings).Display.ColourSafe ? "safe" : "full";

    private static bool Named(string? value, string name) =>
        string.Equals(value?.Trim(), name, StringComparison.OrdinalIgnoreCase);
}
