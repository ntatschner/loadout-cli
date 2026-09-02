using Loadout.Core.Security;
using Spectre.Console;

namespace Loadout.Tui;

/// <summary>
/// Text on its way to the screen, with credentials taken out of it.
/// Used by both surfaces: the launcher draws with it, and the command line
/// uses it for the lines it writes outside <c>CommandOutput.Fail</c>.
/// </summary>
/// <remarks>
/// <para>
/// The command line redacts the failures it returns, because those go through
/// <c>CommandOutput.Fail</c>. Nothing covered the lines either surface writes
/// on the way past: fifty-six of them rendered an error or a remote intact —
/// the workspace remote on the launcher's settings screen and in the setup
/// wizard, a project's remote before a clone on both surfaces, and the text of
/// several dozen failures, most of which are Git's own stderr. Git quotes the
/// remote it could not reach when authentication fails, and a remote can carry
/// a token.
/// </para>
/// <para>
/// It lives here rather than in the command line because the launcher is the
/// lower of the two — the command line already references it — and one helper
/// that both call is worth more than two that can drift apart.
/// </para>
/// <para>
/// Escaping is not redaction and never was. <see cref="Markup.Escape"/> stops
/// a bracket in a filename being read as markup; it has no opinion about what
/// the text says. Both are wanted, always in this order, which is the reason
/// this is one call rather than two that somebody has to remember to pair.
/// </para>
/// </remarks>
public static class Shown
{
    /// <summary>Redacts, then escapes. Never one without the other.</summary>
    public static string Safely(string? value) =>
        Markup.Escape(SecretRedactor.Redact(value));

    /// <summary>
    /// Redacts without escaping, for the screens drawn by Terminal.Gui.
    /// </summary>
    /// <remarks>
    /// Those set a view's text directly and never interpret markup, so escaping
    /// there would not protect anything — it would put literal brackets on the
    /// screen. The redaction is the half that matters, and it is the half that
    /// applies to both.
    /// </remarks>
    public static string Plainly(string? value) => SecretRedactor.Redact(value);
}
