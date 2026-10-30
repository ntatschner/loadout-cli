namespace Loadout.Core.Teams.Daemon;

/// <summary>What running a command from Studio would do.</summary>
public enum StudioEffect
{
    /// <summary>Reads and changes nothing, so a watch-only dashboard may run it.</summary>
    Reads,

    /// <summary>Changes something, opens something on this machine, or spends on an agent.</summary>
    Changes,

    /// <summary>
    /// Asks as it goes, or starts an agent session, so Studio runs it in a
    /// terminal it hosts rather than as a job that nobody can answer.
    /// </summary>
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

    private const string Rules =
        "is trigger housekeeping that is done rarely and best tried with --dry-run first, which the Trigger rules page does not need to offer";

    private static StudioReach ChangesWith(params string[] options) => new(CommandsPage, StudioEffect.Changes, ChangesOnlyWith: options);

    /// <summary>Every command path, and how Studio reaches it.</summary>
    public static IReadOnlyDictionary<string, StudioReach> Reach { get; } = new Dictionary<string, StudioReach>(StringComparer.Ordinal)
    {
        ["setup"] = new("settings", StudioEffect.Session),
        ["start"] = new("projects", StudioEffect.Session),
        ["doctor"] = new("settings", StudioEffect.Changes, ChangesOnlyWith: ["--fix"]),
        ["status"] = new("spend", StudioEffect.Reads),
        ["events"] = new("events", StudioEffect.Changes, ChangesOnlyWith: ["--mark-read"]),
        ["launch"] = new("sessions", StudioEffect.Session),
        ["here"] = Terminal("launches the agent for the directory a terminal is in, and a page has no current directory"),
        ["completion"] = Terminal("writes a shell completion script for a shell to read"),
        ["handoff"] = new("context", StudioEffect.Changes),
        ["protect"] = new("settings", StudioEffect.Changes),
        ["desktop"] = new("settings", StudioEffect.Changes),
        // Without --yes it can only report: a job cannot be asked to confirm,
        // so the command refuses to install unasked.
        ["update"] = new("settings", StudioEffect.Changes, ChangesOnlyWith: ["--yes"]),
        ["migrate"] = new("settings", StudioEffect.Changes),
        ["backup list"] = new("settings", StudioEffect.Reads),
        ["backup restore"] = new("settings", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["share candidates"] = new("context", StudioEffect.Reads),
        ["share promote"] = new("context", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["pack list"] = new("settings", StudioEffect.Reads),
        ["pack add"] = new("settings", StudioEffect.Changes),
        ["pack approve"] = new("settings", StudioEffect.Changes),
        ["pack update"] = new("settings", StudioEffect.Changes),
        ["pack remove"] = new("settings", StudioEffect.Changes),
        ["task list"] = new("projects", StudioEffect.Reads),
        ["task show"] = Terminal("prints one task for a terminal or a script; Studio's task board opens each task in full through the same command, in a dialog rather than a form"),
        ["task shorten"] = Terminal("a one-off tidy of titles written before the 72-character limit, best tried with --dry-run first, which no page needs to offer"),
        ["task classify"] = Terminal("a one-off pass that types the tasks and ideas recorded before there were types, best tried with --dry-run first, which no page needs to offer"),
        ["task apply"] = Terminal("reads its changes from a file or standard input, which a page has neither of; Studio makes the same changes through its own batch route"),
        ["task declare"] = new("projects", StudioEffect.Changes),
        ["task propose-done-when"] = new("teams", StudioEffect.Changes),
        ["task move"] = new("projects", StudioEffect.Changes),
        ["task remove"] = new("projects", StudioEffect.Changes),
        ["idea add"] = new("ideas", StudioEffect.Changes),
        ["idea list"] = new("ideas", StudioEffect.Reads),
        ["idea show"] = new("ideas", StudioEffect.Reads),
        ["idea refine"] = new("ideas", StudioEffect.Changes),
        ["backfill contract"] = new("ideas", StudioEffect.Changes),
        ["idea answer"] = new("ideas", StudioEffect.Changes),
        ["idea choose"] = new("ideas", StudioEffect.Changes),
        ["idea keep"] = new("ideas", StudioEffect.Changes),
        ["idea drop"] = new("ideas", StudioEffect.Changes),
        ["idea improve"] = new("ideas", StudioEffect.Changes),
        ["idea accept"] = new("ideas", StudioEffect.Changes),
        ["idea dismiss"] = new("ideas", StudioEffect.Changes),
        ["idea move"] = new("ideas", StudioEffect.Changes),
        ["idea remove"] = new("ideas", StudioEffect.Changes),
        ["idea note remove"] = new("ideas", StudioEffect.Changes),
        ["idea apply"] = Terminal("reads its changes from a file or standard input, which a page has neither of; Studio makes the same changes through its own batch route"),
        ["idea dump add"] = new("ideas", StudioEffect.Changes),
        ["idea dump list"] = new("ideas", StudioEffect.Reads),
        ["idea dump show"] = new("ideas", StudioEffect.Reads),
        ["idea dump split"] = new("ideas", StudioEffect.Changes),
        ["idea dump apply"] = new("ideas", StudioEffect.Changes),
        ["tools search"] = new("teams", StudioEffect.Reads),
        ["tools show"] = new("teams", StudioEffect.Reads),
        ["tools submit"] = new("teams", StudioEffect.Changes),
        ["tools used"] = new("teams", StudioEffect.Changes),
        ["tools audit"] = new("teams", StudioEffect.Reads),
        ["tools health"] = new("teams", StudioEffect.Reads),
        ["tools verify"] = new("teams", StudioEffect.Changes),
        ["tools promote"] = new("teams", StudioEffect.Changes),
        ["tools deprecate"] = new("teams", StudioEffect.Changes),
        ["tools retire"] = new("teams", StudioEffect.Changes),
        ["tools trust"] = Terminal("is a standing agreement for a script to run unattended, made in a terminal on purpose"),
        ["team list"] = new("teams", StudioEffect.Reads),
        ["team show"] = new("teams", StudioEffect.Reads),
        ["team new"] = new("teams", StudioEffect.Changes),
        ["team edit"] = new("teams", StudioEffect.Changes),
        ["team remove"] = new("teams", StudioEffect.Changes),
        ["team run"] = new("teams", StudioEffect.Changes),
        ["team resume"] = new("teams", StudioEffect.Changes),
        ["team handover"] = new("teams", StudioEffect.Changes),
        ["team runs"] = new("teams", StudioEffect.Reads),
        ["team runs remove"] = new("teams", StudioEffect.Changes),
        ["team runs prune"] = new("teams", StudioEffect.Changes),
        ["team runs restore"] = new("teams", StudioEffect.Changes),
        ["team blocked"] = new("teams", StudioEffect.Reads),
        ["team blocked list"] = new("teams", StudioEffect.Reads),
        ["team blocked show"] = new("teams", StudioEffect.Reads),
        ["team blocked resolve"] = new("teams", StudioEffect.Changes),
        ["team runs close"] = new("teams", StudioEffect.Changes),
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
        ["team office check"] = Terminal("checks the art the office is drawn with, and the office is the classic dashboard's: Studio does not draw rooms"),
        ["team schedule add"] = new("teams", StudioEffect.Changes),
        ["team schedule list"] = new("teams", StudioEffect.Reads),
        ["team schedule remove"] = new("teams", StudioEffect.Changes),
        ["team queue add"] = new("teams", StudioEffect.Changes),
        ["team queue list"] = new("teams", StudioEffect.Reads),
        ["team queue move"] = new("teams", StudioEffect.Changes),
        ["team queue remove"] = new("teams", StudioEffect.Changes),
        ["team queue plan"] = new("teams", StudioEffect.Reads),
        ["team queue settings"] = new("teams", StudioEffect.Changes),
        ["team queue run-next"] = new("teams", StudioEffect.Changes),
        ["team queue run-now"] = new("home", StudioEffect.Changes),
        ["team queue window"] = new("teams", StudioEffect.Changes),
        ["team gate"] = new("home", StudioEffect.Changes),
        ["team message"] = new("teams", StudioEffect.Changes),
        ["team halt"] = new("home", StudioEffect.Changes),
        ["team budget"] = new("teams", StudioEffect.Changes),
        ["team name"] = Terminal("names a run's room in the office, which only the classic dashboard draws; Studio does not draw rooms"),
        ["team pr"] = new("teams", StudioEffect.Changes),
        ["pr list"] = new("teams", StudioEffect.Reads),
        ["pr open"] = new("teams", StudioEffect.Changes),
        ["pr ready"] = new("teams", StudioEffect.Changes),
        ["pr merge"] = new("teams", StudioEffect.Changes),
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

        // Trigger rules have a place of their own on the Teams page, where
        // they are written, tried and removed, with the allow-list and the
        // teams' suggestions beside them; what they need of a person is in
        // Needs you with what runs need. Moving a schedule, pruning the
        // journal and keeping a token stay in the terminal, for the reasons
        // each gives.
        ["trigger list"] = new("teams", StudioEffect.Reads),
        ["trigger events"] = new("teams", StudioEffect.Reads),
        ["trigger add"] = new("teams", StudioEffect.Changes),
        ["trigger remove"] = new("teams", StudioEffect.Changes),
        ["trigger resume"] = new("teams", StudioEffect.Changes),
        ["trigger stale"] = new("teams", StudioEffect.Reads),
        ["trigger release"] = new("teams", StudioEffect.Changes),
        ["trigger adopt"] = Terminal(Rules),
        ["trigger journal"] = new("teams", StudioEffect.Reads),
        ["trigger test"] = new("teams", StudioEffect.Reads),
        ["trigger suggested"] = new("teams", StudioEffect.Reads),
        ["trigger accept"] = new("teams", StudioEffect.Changes),
        ["trigger prune"] = Terminal(Rules),
        ["trigger allow"] = new("teams", StudioEffect.Changes),
        ["trigger disallow"] = new("teams", StudioEffect.Changes),
        ["trigger allowed"] = new("teams", StudioEffect.Reads),
        ["trigger sources"] = new("teams", StudioEffect.Reads),
        ["trigger approvals"] = new("teams", StudioEffect.Reads),
        ["trigger approve"] = new("teams", StudioEffect.Changes),
        ["trigger reject"] = new("teams", StudioEffect.Changes),
        ["trigger token set"] = Terminal("takes a token from standard input or a hidden prompt, and a page should not carry a credential in a form"),
        ["trigger token clear"] = Terminal("forgets the token 'trigger token set' keeps, and the two belong together in the terminal"),
        ["dashboard"] = Terminal("serves a dashboard page, and this is one"),
        ["dashboard sign-out"] = new("settings", StudioEffect.Changes),
        ["dashboard pair"] = Terminal("prints a sign-in link that lets whoever opens it in, which belongs on one screen rather than in a job's kept output"),
        ["dashboard signal"] = Terminal("is run by the agent's own hook inside a session, to say it is waiting"),
        ["hook session-start"] = Terminal("is run by the agent's own hook as a conversation starts, to file it under its launch"),
        ["daemon"] = Terminal("is the daemon serving this page"),
        ["daemon stop"] = new("settings", StudioEffect.Changes),
        ["daemon restart"] = new("settings", StudioEffect.Changes),
        ["daemon pause"] = new("settings", StudioEffect.Changes),
        ["daemon resume"] = new("settings", StudioEffect.Changes),
        ["daemon continue"] = new("settings", StudioEffect.Changes),
        ["daemon autostart enable"] = new("settings", StudioEffect.Changes),
        ["daemon autostart disable"] = new("settings", StudioEffect.Changes),
        ["daemon autostart show"] = new("settings", StudioEffect.Reads),
        ["spend refresh"] = new("spend", StudioEffect.Changes),
        ["checkpoint create"] = new("settings", StudioEffect.Changes),
        ["checkpoint list"] = new("settings", StudioEffect.Reads),
        ["checkpoint restore"] = new("settings", StudioEffect.Changes, ChangesOnlyWith: ["--apply"]),
        ["checkpoint remove"] = new("settings", StudioEffect.Changes),
        ["memory list"] = new("memory", StudioEffect.Reads),
        ["memory find"] = new("memory", StudioEffect.Reads),
        ["memory review"] = new("memory", StudioEffect.Session),
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
        ["config list"] = new("settings", StudioEffect.Reads),
        ["config get"] = new("settings", StudioEffect.Reads),
        ["config set"] = new("settings", StudioEffect.Changes),
        ["config edit"] = new("settings", StudioEffect.Changes),
        ["mcp list"] = new("settings", StudioEffect.Reads),
        ["mcp add"] = new("settings", StudioEffect.Changes),
        ["mcp remove"] = new("settings", StudioEffect.Changes),
        ["mcp serve"] = Terminal("is the MCP server, speaking a protocol on standard input and output to an agent"),
        ["drift"] = new("projects", StudioEffect.Changes, ChangesOnlyWith: ["--fix"]),
        ["code"] = new("projects", StudioEffect.Changes),
        ["commands"] = new("settings", StudioEffect.Reads),
        ["sessions"] = new("spend", StudioEffect.Reads),
        ["running"] = new("spend", StudioEffect.Reads),
        ["session list"] = new("sessions", StudioEffect.Reads),
        ["session rename"] = new("sessions", StudioEffect.Changes),
        ["session end"] = new("sessions", StudioEffect.Changes),
        ["resume"] = new("spend", StudioEffect.Session),

        // The palette's search, on every page. Placed with Home, where the
        // palette is first met.
        ["find"] = new("home", StudioEffect.Reads),
        // Asked from the palette with a leading '?', which is on every page.
        // Reads unless the agent is asked, which spends a turn.
        ["ask"] = new("home", StudioEffect.Changes, ChangesOnlyWith: ["--use-agent"]),
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
        ["voice show"] = new("context", StudioEffect.Reads),
        ["voice check"] = new("context", StudioEffect.Reads),
        ["voice mark"] = new("context", StudioEffect.Changes),
        ["voice learn"] = new("context", StudioEffect.Session),
        ["voice runs"] = new("context", StudioEffect.Reads),
        ["voice render"] = new("context", StudioEffect.Changes),
        ["voice guide list"] = new("context", StudioEffect.Reads),
        ["voice guide show"] = new("context", StudioEffect.Reads),
        ["voice guide draft"] = new("context", StudioEffect.Session),
        ["voice time show"] = new("context", StudioEffect.Reads),
        ["voice time set"] = new("context", StudioEffect.Changes),
        ["voice time unset"] = new("context", StudioEffect.Changes),
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
        ["project onboard"] = new("projects", StudioEffect.Session),
        ["project proposal"] = new("projects", StudioEffect.Changes, ChangesOnlyWith: ["--apply", "--discard"]),
        ["workspace status"] = new("projects", StudioEffect.Reads),
        ["workspace sync"] = new("projects", StudioEffect.Changes),
        ["workspace save"] = new("projects", StudioEffect.Changes),
        ["workspace open"] = new("projects", StudioEffect.Changes),
        ["secret set"] = Terminal("asks for the value at a masked prompt, so the secret never sits in a page or a command line"),
        ["secret test"] = new("settings", StudioEffect.Reads),
        ["secret remove"] = new("settings", StudioEffect.Changes),
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
