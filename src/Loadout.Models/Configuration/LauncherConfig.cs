namespace Loadout.Models.Configuration;

/// <summary>
/// <c>config.yaml</c> — user-level launcher preferences (spec section 16).
/// Roaming on Windows (<c>%APPDATA%</c>), XDG config on Linux, Application
/// Support on macOS. Contains preferences and references only: never a secret
/// value (spec section 52), and never a machine-specific absolute path to a
/// project (spec section 15).
/// </summary>
public sealed class LauncherConfig
{
    public int SchemaVersion { get; set; } = 1;

    public WorkspaceSettings Workspace { get; set; } = new();

    public SyncSettings Sync { get; set; } = new();

    /// <summary>Agent used when a project does not name one. Overridden by project manifest, then by CLI.</summary>
    public string DefaultAgent { get; set; } = "claude";

    public SecretSettings Secrets { get; set; } = new();

    public TerminalSettings Terminal { get; set; } = new();

    public UpdateSettings Updates { get; set; } = new();

    public StatuslineSettings Statusline { get; set; } = new();

    public TelemetrySettings Telemetry { get; set; } = new();

    public AgentToolSettings AgentTools { get; set; } = new();

    public InstructionContextSettings InstructionContext { get; set; } = new();

    /// <summary>Explicit extra directories to search for agent executables (spec section 65).</summary>
    public List<string> AgentSearchPaths { get; set; } = [];

    /// <summary>How a project is opened in an editor, and under which profile.</summary>
    public EditorSettings Editor { get; set; } = new();

    /// <summary>
    /// User-defined agents, keyed by the name used on the command line
    /// (spec section 88). An entry whose key matches a built-in adapter
    /// replaces it, which is the escape hatch for an agent whose invocation
    /// has changed since this launcher was built.
    /// </summary>
    public Dictionary<string, Agents.GenericAgentDefinition> CustomAgents { get; set; } = [];

    /// <summary>
    /// User-defined editors, keyed by the name given to <c>editor-command</c>.
    /// An entry whose key matches one this launcher already knows replaces it.
    /// </summary>
    /// <remarks>
    /// The counterpart to <see cref="CustomAgents"/>, and worth having for the
    /// same reason: naming a different command was always possible, but saying
    /// how that command takes a profile was not, and the profile is the point.
    /// </remarks>
    public Dictionary<string, Editors.EditorDefinition> CustomEditors { get; set; } = [];

    /// <summary>
    /// Commands pre-approved on this machine, per project. Never shared: this
    /// file is not in the workspace and is not committed.
    /// </summary>
    public Policies.CommandPolicySettings Commands { get; set; } = new();

    /// <summary>Token thresholds that produce a warning, and never a refusal.</summary>
    public SpendSettings Spend { get; set; } = new();
}

/// <summary>
/// Which editor a project opens in, and under which profile.
/// <para>
/// VS Code keeps settings, extensions and keybindings in named profiles, and
/// working with an agent usually wants a different set from working without
/// one. This maps an agent to the profile to open alongside it, so opening a
/// project for Claude and opening the same project for Codex can put the editor
/// in two different states without anybody switching by hand.
/// </para>
/// <para>
/// Names only, never contents. Loadout opens the editor with a profile; it does
/// not write what is inside one. VS Code stores profile contents in an internal
/// layout that is not a published contract, and rewriting it would be a promise
/// this cannot keep across editor versions.
/// </para>
/// </summary>
public sealed class EditorSettings
{
    /// <summary>
    /// The editor's command-line name. Resolved the same way an agent is, so a
    /// fork that ships its own launcher works by naming it here.
    /// </summary>
    public string Command { get; set; } = "code";

    /// <summary>
    /// Agent name to editor profile name. An agent with no entry opens the
    /// editor with whatever profile it would have used anyway, which is the
    /// right answer for somebody who does not use profiles.
    /// </summary>
    public Dictionary<string, string> Profiles { get; set; } = [];
}

/// <summary>
/// What the agent's own status line shows.
/// <para>
/// Claude Code renders a status line by running a command and printing what it
/// writes, so this is the one place the launcher can put its own knowledge in
/// front of somebody mid-session: which registered project this is, and how
/// much of the context window the session has spent. Claude knows neither.
/// </para>
/// </summary>
public sealed class StatuslineSettings
{
    /// <summary>The registered project's slug, which the agent has no idea about.</summary>
    public bool ShowProject { get; set; } = true;

    /// <summary>Where in the repository the session is, relative to its root.</summary>
    public bool ShowDirectory { get; set; } = true;

    /// <summary>Branch, whether the tree is dirty, and the worktree when in a linked one.</summary>
    public bool ShowGit { get; set; } = true;

    public bool ShowModel { get; set; } = true;

    /// <summary>How much of the context window is spent, which is the whole point of the tool.</summary>
    public bool ShowContext { get; set; } = true;

    /// <summary>
    /// Colour via ANSI escapes. Off produces a plain line, which is what a
    /// terminal that mangles escapes — or a test — wants.
    /// </summary>
    public bool Colour { get; set; } = true;

    /// <summary>Separator drawn between segments.</summary>
    public string Separator { get; set; } = " | ";
}

/// <summary>
/// Whether the agents report what they spend, and where they report it.
/// </summary>
/// <remarks>
/// <para>
/// The agents can both emit OpenTelemetry, and the launcher is the only thing
/// placed to switch it on: it owns the environment every agent process starts
/// in. Nobody has to edit an agent's own settings for this to work.
/// </para>
/// <para>
/// Off unless asked for. Turning it on means something listens on a socket,
/// which is a decision for the person whose machine it is rather than a default
/// they discover later.
/// </para>
/// </remarks>
/// <summary>
/// Whether a launched agent can call back into the launcher.
/// </summary>
/// <remarks>
/// On by default, because a channel that needs setting up is one nobody sets
/// up, and what it offers is reading plus one screened fact. Off is for anybody
/// who would rather an agent could not reach the workspace at all, which is a
/// legitimate position and not one the launcher should overrule.
/// </remarks>
public sealed class AgentToolSettings
{
    /// <summary>Whether the launcher serves its own tools to the agent it starts.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class TelemetrySettings
{
    /// <summary>
    /// Whether launched agents are told to report usage.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where they report it.
    /// </summary>
    /// <remarks>
    /// A loopback address by default, and validated as one before it is ever
    /// handed to an agent. Usage counts are not conversation content, but they
    /// say when somebody works and on what, and that is nobody else's business
    /// unless they have deliberately chosen to send it somewhere.
    /// </remarks>
    public string Endpoint { get; set; } = "http://127.0.0.1:4318";
}

/// <summary>
/// How much specialist guidance an agent may be given.
/// </summary>
/// <remarks>
/// <para>
/// More instructions are not better. Every specialist loaded is context the
/// agent pays for before it has read a line of the code, and past a point the
/// guidance crowds out the work. The ceiling makes that trade-off explicit
/// rather than emergent.
/// </para>
/// <para>
/// Counted in estimated tokens rather than bytes because that is the unit the
/// constraint is really in, and reported as an estimate everywhere because no
/// tokeniser here matches the providers'.
/// </para>
/// </remarks>
public sealed class InstructionContextSettings
{
    /// <summary>
    /// Whether launched agents are given specialist guidance at all.
    /// </summary>
    /// <remarks>
    /// On by default, because a launcher whose whole purpose is assembling
    /// agent context should assemble it. Off is offered because this changes
    /// what every existing session is told, and somebody who has spent a year
    /// tuning their own instructions is entitled to decline ours without
    /// having to uninstall anything.
    /// </remarks>
    public bool Specialists { get; set; } = true;

    /// <summary>
    /// The ceiling on specialist guidance, in estimated tokens. Zero removes it.
    /// </summary>
    /// <remarks>
    /// Generous by default. The point of a first ceiling is to catch the case
    /// where something has gone wrong, not to make people tune a number before
    /// the feature is usable.
    /// </remarks>
    public int MaxTokens { get; set; } = 12000;

    /// <summary>Share of the ceiling above which it is worth saying so.</summary>
    public int WarnAtPercent { get; set; } = 80;
}

/// <summary>Connection details for the central agent-workspaces repository (spec section 10).</summary>
public sealed class WorkspaceSettings
{
    /// <summary>
    /// Git URL of the central workspace. Any Git provider is valid — the
    /// launcher stays provider-agnostic, so nothing here may assume Forgejo.
    /// Empty means "running without central storage" (spec section 61).
    /// </summary>
    public string Remote { get; set; } = string.Empty;

    public string Branch { get; set; } = "main";
}

/// <summary>Workspace synchronisation policy (spec section 45).</summary>
public sealed class SyncSettings
{
    /// <summary>One of <c>auto</c>, <c>prompt</c>, <c>never</c>.</summary>
    public string Launch { get; set; } = "auto";

    /// <summary>One of <c>prompt</c>, <c>always</c>, <c>never</c>.</summary>
    public string Exit { get; set; } = "prompt";

    /// <summary>
    /// How long a launch-time fetch may block before the launcher falls through
    /// to offline mode. Section 45 puts a network round-trip in front of every
    /// launch; without a bound, one flaky link makes the whole tool feel broken.
    /// </summary>
    public int NetworkTimeoutSeconds { get; set; } = 10;
}

/// <summary>Which secret provider backs secret references (spec sections 53 and 54).</summary>
public sealed class SecretSettings
{
    /// <summary>
    /// One of <c>native</c> (the OS keystore for this platform), <c>1password</c>,
    /// <c>bitwarden</c>, <c>vault</c>, <c>environment</c>, or <c>custom</c>.
    /// </summary>
    public string Provider { get; set; } = "native";

    /// <summary>Executable invoked when <see cref="Provider"/> is <c>custom</c>.</summary>
    public string? CustomProviderCommand { get; set; }
}

/// <summary>Terminal preferences (spec section 42). No emulator is ever mandatory.</summary>
public sealed class TerminalSettings
{
    /// <summary>
    /// <c>current</c> reuses the terminal the launcher is already running in.
    /// Any other value names a specific emulator to spawn for desktop launches.
    /// </summary>
    public string Preferred { get; set; } = "current";

    public string? CustomCommand { get; set; }
}

/// <summary>Update source configuration (spec section 79).</summary>
public sealed class UpdateSettings
{
    public bool CheckAutomatically { get; set; } = true;

    /// <summary>Release feed URL. May point at an internal, self-hosted location.</summary>
    public string? Source { get; set; }
}

/// <summary>
/// Thresholds that say where you stand, and stop nothing.
/// </summary>
/// <remarks>
/// <para>
/// Loadout starts an agent and is then out of the loop. It can tell you where
/// you stand and it could refuse to start; it cannot stop a session that is
/// already running. Offering a hard limit would be a promise the architecture
/// cannot keep — the number would be crossed mid-session by the very work the
/// limit was meant to bound, and nothing here would notice. So these warn, and
/// refusing to launch was considered and declined: a threshold that blocks work
/// is one people set high enough never to fire, which is the same as not having
/// it.
/// </para>
/// <para>
/// Nothing is checked unless something is set. Working out what has been spent
/// means reading the agents' transcripts, which takes seconds rather than
/// milliseconds, and that is not a cost to put on everybody who never asked for
/// a threshold.
/// </para>
/// </remarks>
public sealed class SpendSettings
{
    /// <summary>Tokens across everything in one day. Zero is no threshold.</summary>
    public long DailyTokens { get; set; }

    /// <summary>Tokens in one day for a named project. Zero or absent is no threshold.</summary>
    public Dictionary<string, long> ProjectDailyTokens { get; set; } = [];

    /// <summary>
    /// The share of a plan's window, from 0 to 1, past which to say so. Zero is
    /// no threshold.
    /// </summary>
    /// <remarks>
    /// On a subscription this is the number that actually constrains the work:
    /// money is not what runs out, the rate window is. Only Codex writes its
    /// standing in that window to disk, and only sometimes, so a reading may
    /// simply not be there — which is reported as no answer rather than as
    /// plenty of room left.
    /// </remarks>
    public double PlanWarnAt { get; set; }
}
