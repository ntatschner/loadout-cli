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

    /// <summary>Explicit extra directories to search for agent executables (spec section 65).</summary>
    public List<string> AgentSearchPaths { get; set; } = [];

    /// <summary>
    /// User-defined agents, keyed by the name used on the command line
    /// (spec section 88). An entry whose key matches a built-in adapter
    /// replaces it, which is the escape hatch for an agent whose invocation
    /// has changed since this launcher was built.
    /// </summary>
    public Dictionary<string, Agents.GenericAgentDefinition> CustomAgents { get; set; } = [];
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
