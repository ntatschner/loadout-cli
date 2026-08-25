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

    /// <summary>Bring one or more repositories into the registry.</summary>
    AddProject,

    /// <summary>Reveal the project's directory in the platform file manager.</summary>
    FileManager,

    /// <summary>Fetch a registered project that is not on this machine yet.</summary>
    Clone,

    /// <summary>
    /// Look at what is wrong with the project, and put right what can be.
    /// <para>
    /// A screen of its own rather than a dialog over the launcher, because
    /// inspecting a repository and applying a fix are both slow enough to
    /// freeze a screen that tried to do them while still drawing it.
    /// </para>
    /// </summary>
    Problems,

    /// <summary>Check the machine over, and put right what can be.</summary>
    MachineCheck,

    /// <summary>Show the launcher's settings, and where everything is kept.</summary>
    Settings,

    /// <summary>Show where projects have drifted from their recorded configuration.</summary>
    Drift,
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
/// <param name="SessionId">
/// Which previous conversation to reopen, for
/// <see cref="LauncherAction.Resume"/>. Null means "let the picker ask",
/// which is what choosing Resume without picking a session means.
/// </param>
internal sealed record LauncherIntent(
    LauncherAction Action,
    ProjectResolution? Project = null,
    string? Agent = null,
    string? CommandPath = null,
    string? SessionId = null)
{
    /// <summary>Closing the launcher without asking for anything.</summary>
    internal static readonly LauncherIntent Quit = new(LauncherAction.Quit);
}
