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
/// <param name="ChangesOnlyWith">
/// For a command that changes something, the options without which it only
/// reports what it would do, as 'drift' does until it is given '--fix'.
/// Empty for one that always changes something.
/// </param>
public sealed record StudioReach(
    string Page,
    StudioEffect Effect,
    string? Reason = null,
    IReadOnlyList<string>? ChangesOnlyWith = null)
{
    /// <summary>Whether these words make the command only read.</summary>
    /// <remarks>
    /// A preview with <c>--dry-run</c> reads, and so does a command run
    /// without the option that makes it act. Each option is matched by name,
    /// given bare or with its value joined by '='.
    /// </remarks>
    public bool Reads(IReadOnlyList<string> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        if (Effect == StudioEffect.Reads || words.Contains("--dry-run", StringComparer.Ordinal))
        {
            return true;
        }

        return Effect == StudioEffect.Changes
            && ChangesOnlyWith is { Count: > 0 } acting
            && !words.Any(word => acting.Contains(word.Split('=', 2)[0], StringComparer.Ordinal));
    }
}

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

    private static StudioReach ChangesWith(params string[] options) => new(CommandsPage, StudioEffect.Changes, ChangesOnlyWith: options);

    /// <summary>Every command path, and how Studio reaches it.</summary>
    public static IReadOnlyDictionary<string, StudioReach> Reach { get; } = new Dictionary<string, StudioReach>(StringComparer.Ordinal)
    {
        ["setup"] = new("commands", StudioEffect.Changes),
        ["start"] = new("commands", StudioEffect.Session),
        ["doctor"] = ChangesWith("--fix"),
        ["status"] = new("spend", StudioEffect.Reads),
        ["launch"] = new("sessions", StudioEffect.Session),
        ["here"] = Terminal("launches the agent for the directory a terminal is in, and a page has no current directory"),
        ["completion"] = Terminal("writes a shell completion script for a shell to read"),
        ["handoff"] = new("context", StudioEffect.Changes),
        ["protect"] = new("commands", StudioEffect.Changes),
        ["desktop"] = new("commands", StudioEffect.Changes),
        ["update"] = Terminal("replaces the program serving this page; run it in a terminal, then loadout daemon restart"),
        ["migrate"] = new("commands", StudioEffect.Changes),
        ["backup list"] = new("commands", StudioEffect.Reads),
        ["backup restore"] = ChangesWith("--apply"),
        ["share candidates"] = new("commands", StudioEffect.Reads),
        ["share promote"] = ChangesWith("--apply"),
        ["pack list"] = new("commands", StudioEffect.Reads),
        ["pack add"] = new("commands", StudioEffect.Changes),
        ["pack approve"] = new("commands", StudioEffect.Changes),
        ["pack update"] = new("commands", StudioEffect.Changes),
        ["pack remove"] = new("commands", StudioEffect.Changes),
        ["task list"] = new("commands", StudioEffect.Reads),
        ["task declare"] = new("commands", StudioEffect.Changes),
        ["task remove"] = new("commands", StudioEffect.Changes),
        ["idea add"] = new("ideas", StudioEffect.Changes),
        ["idea list"] = new("ideas", StudioEffect.Reads),
        ["idea show"] = new("ideas", StudioEffect.Reads),
        ["idea refine"] = new("ideas", StudioEffect.Changes),
        ["idea answer"] = new("ideas", StudioEffect.Changes),
        ["idea choose"] = new("ideas", StudioEffect.Changes),
        ["idea keep"] = new("ideas", StudioEffect.Changes),
        ["idea drop"] = new("ideas", StudioEffect.Changes),
        ["idea improve"] = new("ideas", StudioEffect.Changes),
        ["idea accept"] = new("ideas", StudioEffect.Changes),
        ["idea dismiss"] = new("ideas", StudioEffect.Changes),
        ["idea remove"] = new("ideas", StudioEffect.Changes),
        ["idea dump add"] = new("ideas", StudioEffect.Changes),
        ["idea dump list"] = new("ideas", StudioEffect.Reads),
        ["idea dump show"] = new("ideas", StudioEffect.Reads),
        ["idea dump split"] = new("ideas", StudioEffect.Changes),
        ["idea dump apply"] = new("ideas", StudioEffect.Changes),
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
        ["team list"] = new("teams", StudioEffect.Reads),
        ["team show"] = new("teams", StudioEffect.Reads),
        ["team new"] = new("teams", StudioEffect.Changes),
        ["team edit"] = new("teams", StudioEffect.Changes),
        ["team remove"] = new("teams", StudioEffect.Changes),
        ["team run"] = new("teams", StudioEffect.Changes),
        ["team resume"] = new("teams", StudioEffect.Changes),
        ["team runs"] = new("teams", StudioEffect.Reads),
        ["team runs remove"] = new("teams", StudioEffect.Changes),
        ["team runs prune"] = new("teams", StudioEffect.Changes),
        ["team runs restore"] = new("teams", StudioEffect.Changes),
        ["team restore"] = new("teams", StudioEffect.Changes),
        ["team bin"] = new("teams", StudioEffect.Reads),
        ["team bin empty"] = new("teams", StudioEffect.Changes),
        ["team status"] = new("teams", StudioEffect.Reads),
        ["team log"] = new("teams", StudioEffect.Reads),
        ["team outbox"] = new("teams", StudioEffect.Reads),
        ["team remedies"] = new("teams", StudioEffect.Reads),
        ["team capabilities"] = new("teams", StudioEffect.Reads),
        ["team remedy show"] = new("teams", StudioEffect.Reads),
        ["team remedy trust"] = Terminal("is a standing agreement for a script to run unattended, made in a terminal on purpose"),
        ["team remedy requests"] = new("teams", StudioEffect.Changes, ChangesOnlyWith: ["--approve", "--refuse"]),
        ["team office check"] = new("teams", StudioEffect.Reads),
        ["team schedule add"] = new("teams", StudioEffect.Changes),
        ["team schedule list"] = new("teams", StudioEffect.Reads),
        ["team schedule remove"] = new("teams", StudioEffect.Changes),
        ["team gate"] = new("home", StudioEffect.Changes),
        ["team message"] = new("teams", StudioEffect.Changes),
        ["team halt"] = new("home", StudioEffect.Changes),
        ["team budget"] = new("teams", StudioEffect.Changes),
        ["team name"] = new("teams", StudioEffect.Changes),
        ["team pr"] = new("teams", StudioEffect.Changes),
        ["team say"] = new("teams", StudioEffect.Changes),
        ["team attach set"] = Terminal("sets the passphrase this page must be given separately, so it cannot be set from the page it guards"),
        ["team attach clear"] = Terminal("removes the passphrase this page must be given separately, so it cannot be cleared from the page it guards"),
        ["team attach show"] = new("teams", StudioEffect.Reads),
        ["team notify set"] = new("teams", StudioEffect.Changes),
        ["team notify test"] = new("teams", StudioEffect.Changes),
        ["team notify show"] = new("teams", StudioEffect.Reads),
        ["team notify clear"] = new("teams", StudioEffect.Changes),
        ["team webhook enable"] = new("teams", StudioEffect.Changes),
        ["team webhook disable"] = new("teams", StudioEffect.Changes),
        ["team webhook show"] = new("teams", StudioEffect.Reads),
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
        ["spend refresh"] = new("spend", StudioEffect.Changes),
        ["checkpoint create"] = new("commands", StudioEffect.Changes),
        ["checkpoint list"] = new("commands", StudioEffect.Reads),
        ["checkpoint restore"] = ChangesWith("--apply"),
        ["checkpoint remove"] = new("commands", StudioEffect.Changes),
        ["memory list"] = new("memory", StudioEffect.Reads),
        ["memory find"] = new("memory", StudioEffect.Reads),
        ["memory review"] = new("commands", StudioEffect.Changes),
        ["memory write"] = new("memory", StudioEffect.Changes),
        ["memory show"] = new("memory", StudioEffect.Reads),
        ["memory replace"] = new("memory", StudioEffect.Changes),
        ["memory audit"] = new("memory", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["memory reindex"] = new("memory", StudioEffect.Changes),
        ["memory import"] = new("memory", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["memory compress"] = new("memory", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["memory delete"] = new("memory", StudioEffect.Changes),
        ["rules list"] = new("context", StudioEffect.Reads),
        ["rules budget"] = new("context", StudioEffect.Reads),
        ["rules audit"] = new("context", StudioEffect.Reads),
        ["rules split"] = new("context", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["config list"] = new("commands", StudioEffect.Reads),
        ["config get"] = new("commands", StudioEffect.Reads),
        ["config set"] = new("commands", StudioEffect.Changes),
        ["config edit"] = new("commands", StudioEffect.Changes),
        ["mcp list"] = new("commands", StudioEffect.Reads),
        ["mcp add"] = new("commands", StudioEffect.Changes),
        ["mcp remove"] = new("commands", StudioEffect.Changes),
        ["mcp serve"] = Terminal("is the MCP server, speaking a protocol on standard input and output to an agent"),
        ["drift"] = new("projects", StudioEffect.Changes, ChangesOnlyWith: ["--fix"]),
        ["code"] = new("projects", StudioEffect.Changes),
        ["commands"] = new("commands", StudioEffect.Reads),
        ["sessions"] = new("spend", StudioEffect.Reads),
        ["running"] = new("spend", StudioEffect.Reads),
        ["resume"] = new("commands", StudioEffect.Session),
        ["usage"] = new("spend", StudioEffect.Reads),
        ["launches"] = new("spend", StudioEffect.Reads),
        ["instructions list"] = new("context", StudioEffect.Reads),
        ["instructions show"] = new("context", StudioEffect.Reads),
        ["instructions explain"] = new("context", StudioEffect.Reads),
        ["instructions audit"] = new("context", StudioEffect.Reads),
        ["instructions stats"] = new("context", StudioEffect.Reads),
        ["instructions probe"] = new("context", StudioEffect.Changes),
        ["instructions export"] = new("context", StudioEffect.Changes),
        ["instructions validate"] = new("context", StudioEffect.Reads),
        ["instructions new"] = new("context", StudioEffect.Changes),
        ["style list"] = new("context", StudioEffect.Reads),
        ["style show"] = new("context", StudioEffect.Reads),
        ["style use"] = new("context", StudioEffect.Changes),
        ["voice show"] = new("commands", StudioEffect.Reads),
        ["voice check"] = new("commands", StudioEffect.Reads),
        ["voice mark"] = new("commands", StudioEffect.Changes),
        ["voice learn"] = new("commands", StudioEffect.Changes),
        ["voice runs"] = new("commands", StudioEffect.Reads),
        ["voice render"] = new("commands", StudioEffect.Changes),
        ["voice guide list"] = new("commands", StudioEffect.Reads),
        ["voice guide show"] = new("commands", StudioEffect.Reads),
        ["voice guide draft"] = new("commands", StudioEffect.Changes),
        ["docs audit"] = new("context", StudioEffect.Reads),
        ["docs export"] = new("context", StudioEffect.Changes),
        ["docs find"] = new("context", StudioEffect.Reads),
        ["docs refresh"] = new("context", StudioEffect.Changes),
        ["docs ci"] = new("context", StudioEffect.Changes),
        ["telemetry serve"] = Terminal("is a server that runs until Ctrl+C"),
        ["telemetry status"] = new("spend", StudioEffect.Reads),
        ["statusline"] = Terminal("renders the agent's status line, which the agent runs itself"),
        ["statusline install"] = Terminal("changes the agent's own settings file to call the status line, which belongs in the terminal the agent runs in"),
        ["statusline uninstall"] = Terminal("changes the agent's own settings file, which belongs in the terminal the agent runs in"),
        ["statusline show"] = Terminal("renders the agent's status line, which the agent runs itself"),
        ["repo check"] = new("projects", StudioEffect.Reads),
        ["profile list"] = new("context", StudioEffect.Reads),
        ["list"] = new("projects", StudioEffect.Reads),
        ["project list"] = new("projects", StudioEffect.Reads),
        ["project new"] = new("projects", StudioEffect.Changes),
        ["project add"] = new("projects", StudioEffect.Changes),
        ["project remove"] = new("projects", StudioEffect.Changes),
        ["project discover"] = new("projects", StudioEffect.Reads),
        ["project open"] = new("projects", StudioEffect.Changes),
        ["project worktrees"] = new("projects", StudioEffect.Reads),
        ["project clone"] = new("projects", StudioEffect.Changes),
        ["project relocate"] = new("projects", StudioEffect.Changes),
        ["project show"] = new("projects", StudioEffect.Reads),
        ["project survey"] = new("projects", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["project link"] = new("projects", StudioEffect.Changes),
        ["project context"] = new("projects", StudioEffect.Changes),
        ["project onboard"] = new("commands", StudioEffect.Session),
        ["project proposal"] = new("projects", StudioEffect.Changes, ChangesOnlyWith: ["--apply", "--discard"]),
        ["workspace status"] = new("projects", StudioEffect.Reads),
        ["workspace sync"] = new("projects", StudioEffect.Changes),
        ["workspace save"] = new("projects", StudioEffect.Changes),
        ["workspace open"] = new("projects", StudioEffect.Changes),
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
