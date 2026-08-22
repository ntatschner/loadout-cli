namespace AgentWorkspace.Models.Projects;

/// <summary>
/// <c>projects/&lt;slug&gt;/project.yaml</c> in the central workspace repository
/// (spec section 14). This is the shared, machine-independent definition of a
/// project.
/// <para>
/// It deliberately contains no absolute local paths. Where the repository lives
/// on any given machine is recorded in <see cref="Configuration.MachineConfig"/>
/// instead (spec section 15), so the same manifest works unchanged on a Windows
/// desktop, a Linux workstation and a Mac.
/// </para>
/// <para>
/// YAML-bound types are mutable classes with parameterless constructors because
/// that is what the deserialiser requires; they are not used as value objects.
/// </para>
/// </summary>
public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Stable project UUID. The primary identity (spec section 29); survives renames and remote changes.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Short lowercase handle used on the command line, e.g. <c>starstats</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Alternative handles that also resolve to this project (spec section 70).</summary>
    public List<string> Aliases { get; set; } = [];

    public ProjectRepository Repository { get; set; } = new();

    public ProjectAgents Agents { get; set; } = new();

    public ProjectContext Context { get; set; } = new();

    /// <summary>
    /// Named context slices, keyed by the value passed to --profile
    /// (spec section 34). The key "default" is used when none is named.
    /// </summary>
    public Dictionary<string, ContextProfile> Profiles { get; set; } = [];

    public ProjectLaunch Launch { get; set; } = new();

    /// <summary>
    /// Environment variables for the agent process, keyed by variable name
    /// (spec section 53). Values are references, not secrets.
    /// </summary>
    public Dictionary<string, EnvironmentBinding> Environment { get; set; } = [];

    public ProjectWorkspace Workspace { get; set; } = new();
}

/// <summary>Where the application source lives, described independently of any machine.</summary>
public sealed class ProjectRepository
{
    /// <summary>Canonical remote URL. Used as a secondary identity key (spec section 29).</summary>
    public string Remote { get; set; } = string.Empty;

    public string DefaultBranch { get; set; } = "main";
}

/// <summary>Which agents this project supports and how each is configured.</summary>
public sealed class ProjectAgents
{
    /// <summary>Agent launched when none is named on the command line.</summary>
    public string Default { get; set; } = "claude";

    public List<string> Enabled { get; set; } = [];

    /// <summary>
    /// Per-agent settings keyed by agent name. Kept as a loose map rather than
    /// typed properties so a new adapter can be added without changing the
    /// schema — the adapter owns the shape of its own section (spec section 30:
    /// agent-specific logic stays out of core).
    /// </summary>
    public Dictionary<string, Dictionary<string, object>> Settings { get; set; } = [];
}

/// <summary>Context files pulled in when compiling this project's agent context (spec section 33).</summary>
public sealed class ProjectContext
{
    /// <summary>Workspace-relative paths to shared instruction files, e.g. <c>global/instructions/security.md</c>.</summary>
    public List<string> Global { get; set; } = [];

    /// <summary>Workspace-relative paths under this project, e.g. <c>context/architecture.md</c>.</summary>
    public List<string> Project { get; set; } = [];
}

/// <summary>How the agent process is started.</summary>
public sealed class ProjectLaunch
{
    /// <summary>Either <c>repository</c> (the application clone) or a workspace-relative path.</summary>
    public string WorkingDirectory { get; set; } = "repository";
}

/// <summary>Per-project overrides of the workspace sync policy (spec section 45).</summary>
public sealed class ProjectWorkspace
{
    public bool SyncOnLaunch { get; set; } = true;

    /// <summary>One of <c>prompt</c>, <c>always</c>, <c>never</c>.</summary>
    public string SaveOnExit { get; set; } = "prompt";
}
