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
