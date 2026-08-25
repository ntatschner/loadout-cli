using Loadout.Models.Projects;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What the person asked for, once the screen has closed.
/// <para>
/// The launcher cannot do the interesting things while it owns the terminal.
/// Starting an agent, opening a shell, running a command — each of those needs
/// the terminal to itself, and a widget toolkit that is still drawing over it
/// will fight for the cursor. So the screen records what was chosen and stops,
/// and the caller acts once the toolkit has given the terminal back.
/// </para>
/// </summary>
internal enum LauncherAction
{
    /// <summary>Nothing was chosen; the launcher was closed.</summary>
    Quit,

    /// <summary>Start an agent against the selected project.</summary>
    Launch,

    /// <summary>Reopen a previous conversation for the selected project.</summary>
    Resume,

    /// <summary>Open a development shell in the project's directory.</summary>
    Shell,

    /// <summary>Run a command from the catalogue, as though it had been typed.</summary>
    Command,
}

/// <summary>
/// The chosen action and everything needed to carry it out.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Project">Which project it applies to, where one is involved.</param>
/// <param name="Agent">Which agent to start, for <see cref="LauncherAction.Launch"/>.</param>
/// <param name="CommandPath">
/// What to run, for <see cref="LauncherAction.Command"/>. The same string
/// somebody would have typed, so the parser can take it unaltered.
/// </param>
internal sealed record LauncherIntent(
    LauncherAction Action,
    ProjectResolution? Project = null,
    string? Agent = null,
    string? CommandPath = null)
{
    /// <summary>Closing the launcher without asking for anything.</summary>
    internal static readonly LauncherIntent Quit = new(LauncherAction.Quit);
}
