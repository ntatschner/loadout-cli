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

    public RemoteControlSettings RemoteControl { get; set; } = new();

    public AttributionSettings Attribution { get; set; } = new();

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

    /// <summary>What a newly registered project is given, unless it says otherwise.</summary>
    public OnboardingSettings Onboarding { get; set; } = new();

    /// <summary>How this person wants to be written to and asked.</summary>
    public AccessibilitySettings Accessibility { get; set; } = new();

    /// <summary>How Studio looks and is laid out: see <see cref="StudioSettings"/>.</summary>
    public StudioSettings Studio { get; set; } = new();
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
    /// Whether a crossed spending threshold is flagged here.
    /// </summary>
    /// <remarks>
    /// Off unless somebody set a threshold, in the sense that it has nothing to
    /// show until then. What it draws is the answer worked out at launch and
    /// refreshed in the background, never a scan on the prompt's own time: that
    /// takes seconds, and this line is redrawn several times a minute.
    /// </remarks>
    public bool ShowSpend { get; set; } = true;

    /// <summary>
    /// Whether the number of specialists composed into this session is shown.
    /// </summary>
    /// <remarks>
    /// Read from what the launch wrote down, never resolved here: working the
    /// set out takes about half a second, and this line is redrawn on every
    /// prompt.
    /// </remarks>
    public bool ShowSpecialists { get; set; } = true;

    /// <summary>
    /// Colour via ANSI escapes. Off produces a plain line, which is what a
    /// terminal that mangles escapes — or a test — wants.
    /// </summary>
    public bool Colour { get; set; } = true;

    /// <summary>Separator drawn between segments.</summary>
    public string Separator { get; set; } = " | ";

    /// <summary>
    /// Whether the line says what the session is waiting on and since when:
    /// you, a tool, a permission prompt, a wake-up or background work.
    /// </summary>
    /// <remarks>
    /// On by default, with the last write, because these two answer the
    /// question somebody glancing at a parked session actually has. The rest
    /// of the time segments are off until asked for.
    /// </remarks>
    public bool ShowWaiting { get; set; } = true;

    /// <summary>How long since the agent last changed a file.</summary>
    public bool ShowLastWrite { get; set; } = true;

    /// <summary>The time of day, for a terminal that has no clock of its own on screen.</summary>
    public bool ShowClock { get; set; }

    /// <summary>How long the session has been running.</summary>
    public bool ShowSession { get; set; }

    /// <summary>How long the last completed turn took.</summary>
    public bool ShowTurn { get; set; }

    /// <summary>How long since the last commit on the current branch.</summary>
    public bool ShowLastCommit { get; set; }

    /// <summary>How the time segments are written.</summary>
    public StatuslineTimeSettings Time { get; set; } = new();

    /// <summary>
    /// When the waiting segment turns amber and then red, as "10m,30m". A
    /// value that cannot be read means no colour change, never an error.
    /// </summary>
    public string? WaitingAfter { get; set; } = "10m,30m";

    /// <summary>When the last-write segment changes colour, in the same form.</summary>
    public string? LastWriteAfter { get; set; } = "30m";

    /// <summary>When the last-commit segment changes colour; unset means never.</summary>
    public string? LastCommitAfter { get; set; }

    /// <summary>
    /// A copy that shares nothing with this one. A copy written out member by
    /// member drops every setting added after it was written, silently.
    /// </summary>
    public StatuslineSettings Copy()
    {
        var copy = (StatuslineSettings)MemberwiseClone();

        copy.Time = Time.Copy();

        return copy;
    }
}

/// <summary>How the status line writes a time.</summary>
public sealed class StatuslineTimeSettings
{
    /// <summary>A copy that shares nothing with this one.</summary>
    public StatuslineTimeSettings Copy() => (StatuslineTimeSettings)MemberwiseClone();

    /// <summary>
    /// Clock times ("write 14:02") rather than ages ("write 3m ago"). An age
    /// only moves when the line is redrawn, so a clock time is the honest
    /// choice where redraws are rare.
    /// </summary>
    public bool Absolute { get; set; }

    /// <summary>24-hour times ("14:05") rather than 12-hour ("2:05pm").</summary>
    public bool Hour24 { get; set; } = true;
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

/// <summary>
/// Whether sessions start with the agent's own remote control on.
/// </summary>
/// <remarks>
/// Off unless set. The agent does the remoting, through its own service, so
/// switching this on sends nothing anywhere through Loadout; it is the agent's
/// switch, passed for every session so nobody has to remember it.
/// </remarks>
public sealed class RemoteControlSettings
{
    /// <summary>Whether interactive sessions start with remote control on.</summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// Whether an agent's own attribution trailers are left as the agent writes them.
/// </summary>
/// <remarks>
/// Off unless set, so Loadout clears them. Claude Code's own instructions ask
/// for a Co-Authored-By line on every commit and a footer on every pull
/// request, and somebody whose rules say otherwise then reads the agent
/// explaining, launch after launch, why it is leaving them out. Clearing the
/// setting removes the instruction, so there is nothing to explain.
/// </remarks>
public sealed class AttributionSettings
{
    /// <summary>Whether the agent adds its own commit and pull request attribution.</summary>
    public bool Keep { get; set; }
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

    /// <summary>
    /// The named style in force wherever a project or profile has not chosen
    /// one, or empty for none.
    /// </summary>
    /// <remarks>
    /// The one gap in the style layers. Your personal style is in force
    /// everywhere and a codebase's in its own project, but a named style held
    /// only where a project had chosen it, so somebody who writes the same way
    /// across most of their work had to choose it project by project. A
    /// project's own choice still wins, the same way a project's agent wins
    /// over <c>default-agent</c>.
    /// </remarks>
    public string DefaultStyle { get; set; } = string.Empty;

    /// <summary>
    /// Whether launched agents are told to follow their instructions without
    /// saying that they are.
    /// </summary>
    /// <remarks>
    /// On by default. An agent handed rules tends to announce each one as it
    /// applies it, and a sentence saying a rule was followed costs the reader
    /// attention and tells them nothing. Off is offered for the same reason
    /// as <see cref="Specialists"/>: it changes what every session is told.
    /// </remarks>
    public bool QuietRules { get; set; } = true;
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

/// <summary>
/// What a new project is set up with, so the answers are given once.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a question registering a project currently makes you
/// answer later, usually after something has surprised you. None of it is new
/// behaviour; it is the same settings, filled in at the moment the project
/// exists rather than the third time it matters.
/// </para>
/// <para>
/// Blanks only. A project that names its own agent chose that, and a
/// machine-wide preference is not grounds to reconsider it — so nothing here
/// overwrites a value that is already there, and what does get filled in is
/// reported rather than applied quietly.
/// </para>
/// <para>
/// Deliberately absent: anything that reaches off this machine. The rule
/// everywhere else here is that outward-facing things are confirmed rather than
/// switched on for somebody, and a default is the opposite of confirming.
/// </para>
/// </remarks>
public sealed class OnboardingSettings
{
    /// <summary>Agent a new project launches. Empty leaves the built-in default.</summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>Model a new project pins, spelled as its agent spells it.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Model per mode, for the modes worth distinguishing.</summary>
    public Dictionary<string, string> ModelByMode { get; set; } = [];

    /// <summary>Editor profile a new project opens under.</summary>
    public string EditorProfile { get; set; } = string.Empty;

    /// <summary>
    /// What happens to a new project's onboarding: <c>first-launch</c> queues it
    /// for the next session started without a task of its own, <c>skip</c>
    /// records it as skipped. Empty means <c>first-launch</c>.
    /// </summary>
    public string Run { get; set; } = string.Empty;
}
