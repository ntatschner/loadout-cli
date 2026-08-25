namespace Loadout.Tui;

/// <summary>
/// The groups commands are filed under.
/// <para>
/// Constants rather than free text, so a typo is a build error instead of a
/// category with one command in it that nobody notices.
/// </para>
/// </summary>
public static class CommandCategory
{
    /// <summary>Starting or continuing work, which is what the launcher is for.</summary>
    public const string Start = "Start and continue";

    /// <summary>The registry of projects and the repositories behind them.</summary>
    public const string Projects = "Projects";

    /// <summary>Whether this machine and these projects are in good order.</summary>
    public const string Health = "Health and repair";

    /// <summary>The central workspace, and moving between machines.</summary>
    public const string Workspace = "Workspace and lifecycle";

    /// <summary>What an agent is given: instructions, memory, profiles, servers.</summary>
    public const string AgentConfiguration = "Agent configuration";

    /// <summary>Keeping agent state out of repositories, and credentials out of sight.</summary>
    public const string Safety = "Safety";

    /// <summary>Where the launcher meets the rest of the desktop and shell.</summary>
    public const string Integration = "Integration";

    /// <summary>Settings, updates and the launcher's own housekeeping.</summary>
    public const string Administration = "Administration";

    /// <summary>Every category, in the order they are worth reading.</summary>
    public static IReadOnlyList<string> All =>
    [
        Start,
        Projects,
        Health,
        AgentConfiguration,
        Workspace,
        Safety,
        Integration,
        Administration,
    ];
}

/// <summary>One thing the launcher can do, as the command line describes it.</summary>
/// <param name="Path">
/// What would be typed after <c>loadout</c>, such as <c>memory compress</c>.
/// </param>
/// <param name="Description">The one-line description the command declares.</param>
/// <param name="TerminalOnly">
/// Why this cannot usefully be run from a menu, or null when it can. A command
/// that emits a shell script to be piped somewhere, or that an agent invokes
/// rather than a person, is listed with its reason rather than hidden: a
/// command nobody can find is indistinguishable from one that does not exist.
/// </param>
/// <param name="Category">
/// The group it belongs to, for grouped help and a palette that can be read
/// rather than scrolled. Empty only for a command that has not declared one,
/// which a test forbids.
/// </param>
/// <param name="Intent">
/// Words somebody might search for when they do not know the name. Nobody
/// looking to undo a mistake searches for "backup restore".
/// </param>
/// <param name="Mutates">Whether running it can change files or configuration.</param>
/// <param name="RequiresNetwork">Whether running it contacts the network.</param>
/// <param name="Example">One example of it in use, or empty.</param>
public sealed record CatalogueEntry(
    string Path,
    string Description,
    string? TerminalOnly,
    string Category = "",
    string Intent = "",
    bool Mutates = false,
    bool RequiresNetwork = false,
    string Example = "")
{
    /// <summary>
    /// Whether this command matches what somebody typed, by name or by intent.
    /// </summary>
    /// <remarks>
    /// The path is matched first because somebody typing a command name means
    /// it. Intent words are matched second, so searching "undo" reaches backup
    /// restore without "restore" having to be guessed at.
    /// </remarks>
    public bool Matches(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return Path.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Description.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Intent.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Category.Contains(text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when running this from the launcher makes sense.</summary>
    public bool Runnable => TerminalOnly is null;

    /// <summary>The branch it belongs to, or empty for a top-level command.</summary>
    public string Group
    {
        get
        {
            var space = Path.IndexOf(' ', StringComparison.Ordinal);

            return space < 0 ? string.Empty : Path[..space];
        }
    }
}

/// <summary>
/// Every command the tool has, and a way to run one.
/// <para>
/// Built from the same registration the command line parses, rather than from a
/// second list kept alongside it. The launcher used to offer about a fifth of
/// what the command line could do, and closing that by hand would have created
/// exactly the kind of list that drifts — the allowlist guarding bare-name
/// launch had already done so, silently, four times in one sitting.
/// </para>
/// </summary>
public interface ICommandCatalogue
{
    /// <summary>Every registered command, in registration order.</summary>
    IReadOnlyList<CatalogueEntry> Commands { get; }

    /// <summary>
    /// Runs one, as though it had been typed. Returns its exit code.
    /// </summary>
    Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default);
}
