namespace Loadout.Tui.Terminal;

/// <summary>
/// The commands the launcher runs on somebody's behalf, spelled the way they
/// would have typed them.
/// <para>
/// One definition, and a test asserts every one of these is a command that
/// actually exists. A menu entry naming a command that does not fails only when
/// somebody chooses it, and then fails as "Unknown command", which reads like
/// their mistake rather than ours. That is exactly what happened: the settings
/// entry ran <c>config show</c>, and the command is <c>config list</c>.
/// </para>
/// <para>
/// This is the same second-list problem the command catalogue exists to
/// prevent, reintroduced one layer up. The catalogue keeps the palette honest
/// because it is built while the commands are registered; these are typed out
/// by hand, so they need checking instead.
/// </para>
/// </summary>
internal static class LauncherCommands
{
    /// <summary>Opens a project in the editor under the profile for its agent.</summary>
    internal const string Editor = "code";

    /// <summary>Reopens a previous conversation.</summary>
    internal const string Resume = "resume";

    /// <summary>Fetches a registered project that is not on this machine yet.</summary>
    internal const string Clone = "project clone";

    /// <summary>Every one of them, for the test that checks they are real.</summary>
    internal static IReadOnlyList<string> All => [Editor, Resume, Clone];
}
