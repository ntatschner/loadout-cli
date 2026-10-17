namespace Loadout.Core.Teams.Daemon;

/// <summary>What running a command from Studio would do.</summary>
public enum StudioEffect
{
    /// <summary>Reads and changes nothing, so a watch-only dashboard may run it.</summary>
    Reads,

    /// <summary>Changes something, opens something on this machine, or spends on an agent.</summary>
    Changes,

    /// <summary>Starts an agent session, which Studio hosts from its Sessions page rather than as a job.</summary>
    Session,

    /// <summary>Is never run from Studio; the page shows the command and why.</summary>
    TerminalOnly,
}

/// <summary>Where Studio offers one command, and what running it would do.</summary>
/// <param name="Page">The Studio page that does it, or <c>commands</c> for the Commands page alone.</param>
/// <param name="Effect">What running it does.</param>
/// <param name="Reason">For a terminal-only command, why, worded to follow its name.</param>
public sealed record StudioReach(string Page, StudioEffect Effect, string? Reason = null);

/// <summary>
/// Every command Loadout has, and where Studio reaches it.
/// </summary>
/// <remarks>
/// <para>
/// Studio is complete when every command is either done by one of its pages
/// or deliberately left to the terminal with a reason, and this is the list
/// that says which. A test holds it to the command catalogue in both
/// directions, so a new command that nobody placed fails the suite rather
/// than going missing from the page.
/// </para>
/// <para>
/// The effect is written here rather than read from each command's
/// metadata, because the metadata only says whether a command writes
/// Loadout's state. Opening an editor, sending a test notification and
/// paying an agent for a round write nothing of Loadout's, and a watch-only
/// dashboard must still not do them. A watch-only server runs only what
/// this list says reads.
/// </para>
/// <para>
/// A command whose page is <c>commands</c> is reachable from the Commands
/// page's generic form and nowhere better yet. Those are what is left to do.
/// </para>
/// </remarks>
public static class StudioCoverage
{
    /// <summary>The page that offers every command through a form built from its options.</summary>
    public const string CommandsPage = "commands";

    private static StudioReach Terminal(string reason) => new(CommandsPage, StudioEffect.TerminalOnly, reason);

    /// <summary>Every command path, and how Studio reaches it.</summary>
    public static IReadOnlyDictionary<string, StudioReach> Reach { get; } = new Dictionary<string, StudioReach>(StringComparer.Ordinal)
    {
        ["setup"] = new("commands", StudioEffect.Changes),
        ["start"] = new("commands", StudioEffect.Session),
        ["doctor"] = new("commands", StudioEffect.Changes),
        ["status"] = new("commands", StudioEffect.Reads),
        ["launch"] = new("sessions", StudioEffect.Session),
        ["here"] = Terminal("launches the agent for the directory a terminal is in, and a page has no current directory"),
        ["completion"] = Terminal("writes a shell completion script for a shell to read"),
        ["handoff"] = new("commands", StudioEffect.Changes),
        ["protect"] = new("commands", StudioEffect.Changes),
        ["desktop"] = new("commands", StudioEffect.Changes),
        ["update"] = Terminal("replaces the program serving this page; run it in a terminal, then loadout daemon restart"),
        ["migrate"] = new("commands", StudioEffect.Changes),
        ["backup list"] = new("commands", StudioEffect.Reads),
        ["backup restore"] = new("commands", StudioEffect.Changes),
        ["share candidates"] = new("commands", StudioEffect.Reads),
        ["share promote"] = new("commands", StudioEffect.Changes),
        ["pack list"] = new("commands", StudioEffect.Reads),
        ["pack add"] = new("commands", StudioEffect.Changes),
        ["pack approve"] = new("commands", StudioEffect.Changes),
        ["pack update"] = new("commands", StudioEffect.Changes),
        ["pack remove"] = new("commands", StudioEffect.Changes),
        ["task list"] = new("commands", StudioEffect.Reads),
        ["task declare"] = new("commands", StudioEffect.Changes),
        ["task remove"] = new("commands", StudioEffect.Changes),
        ["idea add"] = new("commands", StudioEffect.Changes),
        ["idea list"] = new("commands", StudioEffect.Reads),
        ["idea show"] = new("commands", StudioEffect.Reads),
        ["idea refine"] = new("commands", StudioEffect.Changes),
        ["idea answer"] = new("commands", StudioEffect.Changes),
        ["idea choose"] = new("commands", StudioEffect.Changes),
        ["idea keep"] = new("commands", StudioEffect.Changes),
        ["idea drop"] = new("commands", StudioEffect.Changes),
        ["idea improve"] = new("commands", StudioEffect.Changes),
        ["idea accept"] = new("commands", StudioEffect.Changes),
        ["idea dismiss"] = new("commands", StudioEffect.Changes),
        ["idea remove"] = new("commands", StudioEffect.Changes),
        ["idea dump add"] = new("commands", StudioEffect.Changes),
        ["idea dump list"] = new("commands", StudioEffect.Reads),
        ["idea dump show"] = new("commands", StudioEffect.Reads),
        ["idea dump split"] = new("commands", StudioEffect.Changes),
        ["idea dump apply"] = new("commands", StudioEffect.Changes),
        ["tools search"] = new("commands", StudioEffect.Reads),
        ["tools show"] = new("commands", StudioEffect.Reads),
        ["tools submit"] = new("commands", StudioEffect.Changes),
        ["tools used"] = new("commands", StudioEffect.Changes),
        ["tools audit"] = new("commands", StudioEffect.Reads),
        ["tools health"] = new("commands", StudioEffect.Reads),
        ["tools verify"] = new("commands", StudioEffect.Changes),
        ["tools promote"] = new("commands", StudioEffect.Changes),
        ["tools deprecate"] = new("commands", StudioEffect.Changes),
        ["tools retire"] = new("commands", StudioEffect.Changes),
        ["tools trust"] = Terminal("is a standing agreement for a script to run unattended, made in a terminal on purpose"),
        ["team list"] = new("commands", StudioEffect.Reads),
        ["team show"] = new("commands", StudioEffect.Reads),
        ["team new"] = new("commands", StudioEffect.Changes),
        ["team edit"] = new("commands", StudioEffect.Changes),
        ["team remove"] = new("commands", StudioEffect.Changes),
        ["team run"] = new("commands", StudioEffect.Changes),
        ["team resume"] = new("commands", StudioEffect.Changes),
        ["team runs"] = new("commands", StudioEffect.Reads),
        ["team runs remove"] = new("commands", StudioEffect.Changes),
        ["team runs prune"] = new("commands", StudioEffect.Changes),
        ["team runs restore"] = new("commands", StudioEffect.Changes),
        ["team restore"] = new("commands", StudioEffect.Changes),
        ["team bin"] = new("commands", StudioEffect.Reads),
        ["team bin empty"] = new("commands", StudioEffect.Changes),
        ["team status"] = new("commands", StudioEffect.Reads),
        ["team log"] = new("commands", StudioEffect.Reads),
        ["team outbox"] = new("commands", StudioEffect.Reads),
        ["team remedies"] = new("commands", StudioEffect.Reads),
        ["team capabilities"] = new("commands", StudioEffect.Reads),
        ["team remedy show"] = new("commands", StudioEffect.Reads),
        ["team remedy trust"] = Terminal("is a standing agreement for a script to run unattended, made in a terminal on purpose"),
        ["team remedy requests"] = new("commands", StudioEffect.Changes),
        ["team office check"] = new("commands", StudioEffect.Reads),
        ["team schedule add"] = new("commands", StudioEffect.Changes),
        ["team schedule list"] = new("commands", StudioEffect.Reads),
        ["team schedule remove"] = new("commands", StudioEffect.Changes),
        ["team gate"] = new("home", StudioEffect.Changes),
        ["team message"] = new("commands", StudioEffect.Changes),
        ["team halt"] = new("home", StudioEffect.Changes),
        ["team budget"] = new("commands", StudioEffect.Changes),
        ["team name"] = new("commands", StudioEffect.Changes),
        ["team pr"] = new("commands", StudioEffect.Changes),
        ["team say"] = new("commands", StudioEffect.Changes),
        ["team attach set"] = Terminal("sets the passphrase this page must be given separately, so it cannot be set from the page it guards"),
        ["team attach clear"] = Terminal("removes the passphrase this page must be given separately, so it cannot be cleared from the page it guards"),
        ["team attach show"] = new("commands", StudioEffect.Reads),
        ["team notify set"] = new("commands", StudioEffect.Changes),
        ["team notify test"] = new("commands", StudioEffect.Changes),
        ["team notify show"] = new("commands", StudioEffect.Reads),
        ["team notify clear"] = new("commands", StudioEffect.Changes),
        ["team webhook enable"] = new("commands", StudioEffect.Changes),
        ["team webhook disable"] = new("commands", StudioEffect.Changes),
        ["team webhook show"] = new("commands", StudioEffect.Reads),
        ["dashboard"] = Terminal("serves a dashboard page, and this is one"),
        ["dashboard sign-out"] = new("settings", StudioEffect.Changes),
        ["daemon"] = Terminal("is the daemon serving this page"),
        ["daemon stop"] = new("commands", StudioEffect.Changes),
        ["daemon restart"] = new("commands", StudioEffect.Changes),
        ["daemon pause"] = new("commands", StudioEffect.Changes),
        ["daemon resume"] = new("commands", StudioEffect.Changes),
        ["daemon continue"] = new("commands", StudioEffect.Changes),
        ["daemon autostart enable"] = new("commands", StudioEffect.Changes),
        ["daemon autostart disable"] = new("commands", StudioEffect.Changes),
        ["daemon autostart show"] = new("commands", StudioEffect.Reads),
        ["spend refresh"] = new("commands", StudioEffect.Changes),
        ["checkpoint create"] = new("commands", StudioEffect.Changes),
        ["checkpoint list"] = new("commands", StudioEffect.Reads),
        ["checkpoint restore"] = new("commands", StudioEffect.Changes),
        ["checkpoint remove"] = new("commands", StudioEffect.Changes),
        ["memory list"] = new("commands", StudioEffect.Reads),
        ["memory find"] = new("commands", StudioEffect.Reads),
        ["memory review"] = new("commands", StudioEffect.Changes),
        ["memory write"] = new("commands", StudioEffect.Changes),
        ["memory show"] = new("commands", StudioEffect.Reads),
        ["memory replace"] = new("memory", StudioEffect.Changes),
        ["memory audit"] = new("commands", StudioEffect.Changes),
        ["memory reindex"] = new("commands", StudioEffect.Changes),
        ["memory import"] = new("commands", StudioEffect.Changes),
        ["memory compress"] = new("commands", StudioEffect.Changes),
        ["memory delete"] = new("commands", StudioEffect.Changes),
        ["rules list"] = new("commands", StudioEffect.Reads),
        ["rules budget"] = new("commands", StudioEffect.Reads),
        ["rules audit"] = new("commands", StudioEffect.Reads),
        ["rules split"] = new("commands", StudioEffect.Changes),
        ["config list"] = new("commands", StudioEffect.Reads),
        ["config get"] = new("commands", StudioEffect.Reads),
        ["config set"] = new("commands", StudioEffect.Changes),
        ["config edit"] = new("commands", StudioEffect.Changes),
        ["mcp list"] = new("commands", StudioEffect.Reads),
        ["mcp add"] = new("commands", StudioEffect.Changes),
        ["mcp remove"] = new("commands", StudioEffect.Changes),
        ["mcp serve"] = Terminal("is the MCP server, speaking a protocol on standard input and output to an agent"),
        ["drift"] = new("commands", StudioEffect.Changes),
        ["code"] = new("commands", StudioEffect.Changes),
        ["commands"] = new("commands", StudioEffect.Reads),
        ["sessions"] = new("commands", StudioEffect.Reads),
        ["running"] = new("commands", StudioEffect.Reads),
        ["resume"] = new("commands", StudioEffect.Session),
        ["usage"] = new("commands", StudioEffect.Reads),
        ["launches"] = new("commands", StudioEffect.Reads),
        ["instructions list"] = new("commands", StudioEffect.Reads),
        ["instructions show"] = new("commands", StudioEffect.Reads),
        ["instructions explain"] = new("commands", StudioEffect.Reads),
        ["instructions audit"] = new("commands", StudioEffect.Reads),
        ["instructions stats"] = new("commands", StudioEffect.Reads),
        ["instructions probe"] = new("commands", StudioEffect.Changes),
        ["instructions export"] = new("commands", StudioEffect.Changes),
        ["instructions validate"] = new("commands", StudioEffect.Reads),
        ["instructions new"] = new("commands", StudioEffect.Changes),
        ["style list"] = new("commands", StudioEffect.Reads),
        ["style show"] = new("commands", StudioEffect.Reads),
        ["style use"] = new("commands", StudioEffect.Changes),
        ["voice show"] = new("commands", StudioEffect.Reads),
        ["voice check"] = new("commands", StudioEffect.Reads),
        ["voice mark"] = new("commands", StudioEffect.Changes),
        ["voice learn"] = new("commands", StudioEffect.Changes),
        ["voice runs"] = new("commands", StudioEffect.Reads),
        ["voice render"] = new("commands", StudioEffect.Changes),
        ["voice guide list"] = new("commands", StudioEffect.Reads),
        ["voice guide show"] = new("commands", StudioEffect.Reads),
        ["voice guide draft"] = new("commands", StudioEffect.Changes),
        ["docs audit"] = new("commands", StudioEffect.Reads),
        ["docs export"] = new("commands", StudioEffect.Changes),
        ["docs find"] = new("commands", StudioEffect.Reads),
        ["docs refresh"] = new("commands", StudioEffect.Changes),
        ["docs ci"] = new("commands", StudioEffect.Changes),
        ["telemetry serve"] = Terminal("is a server that runs until Ctrl+C"),
        ["telemetry status"] = new("commands", StudioEffect.Reads),
        ["statusline"] = Terminal("renders the agent's status line, which the agent runs itself"),
        ["statusline install"] = Terminal("changes the agent's own settings file to call the status line, which belongs in the terminal the agent runs in"),
        ["statusline uninstall"] = Terminal("changes the agent's own settings file, which belongs in the terminal the agent runs in"),
        ["statusline show"] = Terminal("renders the agent's status line, which the agent runs itself"),
        ["repo check"] = new("commands", StudioEffect.Reads),
        ["profile list"] = new("commands", StudioEffect.Reads),
        ["list"] = new("commands", StudioEffect.Reads),
        ["project list"] = new("commands", StudioEffect.Reads),
        ["project new"] = new("commands", StudioEffect.Changes),
        ["project add"] = new("commands", StudioEffect.Changes),
        ["project remove"] = new("commands", StudioEffect.Changes),
        ["project discover"] = new("commands", StudioEffect.Reads),
        ["project open"] = new("commands", StudioEffect.Changes),
        ["project worktrees"] = new("commands", StudioEffect.Reads),
        ["project clone"] = new("commands", StudioEffect.Changes),
        ["project relocate"] = new("commands", StudioEffect.Changes),
        ["project show"] = new("commands", StudioEffect.Reads),
        ["project survey"] = new("commands", StudioEffect.Changes),
        ["project link"] = new("commands", StudioEffect.Changes),
        ["project context"] = new("commands", StudioEffect.Changes),
        ["project onboard"] = new("commands", StudioEffect.Session),
        ["project proposal"] = new("commands", StudioEffect.Changes),
        ["workspace status"] = new("commands", StudioEffect.Reads),
        ["workspace sync"] = new("commands", StudioEffect.Changes),
        ["workspace save"] = new("commands", StudioEffect.Changes),
        ["workspace open"] = new("commands", StudioEffect.Changes),
        ["secret set"] = Terminal("asks for the value at a masked prompt, so the secret never sits in a page or a command line"),
        ["secret test"] = new("commands", StudioEffect.Reads),
        ["secret remove"] = new("commands", StudioEffect.Changes),
    };

    /// <summary>
    /// Options Studio never passes, by command, with why. Each is an
    /// agreement that has to be made in a terminal, on a command whose other
    /// uses are fine from the page.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Refused { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["tools verify"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["--agree"] = "agreeing to a tool's tests is made in a terminal on purpose",
            },
        };
}
