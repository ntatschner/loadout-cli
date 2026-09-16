namespace Loadout.Models.Configuration;

/// <summary>
/// <c>machines.yaml</c> — everything about this machine specifically
/// (spec section 15). Stored in local state and never synchronised to the
/// central workspace, because absolute paths are meaningless on other machines
/// and leak the local filesystem layout.
/// </summary>
public sealed class MachineConfig
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Identifies this machine in workspace commit messages and audit records.</summary>
    public string MachineName { get; set; } = string.Empty;

    /// <summary>
    /// Directories scanned by project discovery (spec section 64). Only these
    /// are ever walked: the launcher must never crawl arbitrary disks, which is
    /// also what keeps it clear of macOS Full Disk Access (spec section 85).
    /// </summary>
    public List<string> DiscoveryRoots { get; set; } = [];

    /// <summary>Where <c>loadout project clone</c> puts new clones on this machine.</summary>
    public string? DefaultCloneRoot { get; set; }

    /// <summary>Local path and launch history per project, keyed by project slug.</summary>
    public Dictionary<string, MachineProjectEntry> Projects { get; set; } = [];

    /// <summary>What a team run may do here, whatever a team file asks for.</summary>
    public MachineTeams Teams { get; set; } = new();
}

/// <summary>
/// This machine's ceiling on team runs.
/// </summary>
/// <remarks>
/// Machine-local because it is a decision, and the file a team is described in
/// is shared: anybody who can push to the workspace can edit a team, so a team
/// file may ask and only this may grant. The same split as command policy and
/// as specialist packs, and worth having a third time because the failure it
/// prevents is the same one — a change that reaches your machine because it
/// reached somebody else's repository.
/// </remarks>
public sealed class MachineTeams
{
    /// <summary>
    /// Outward actions a team may allow its nodes in an autonomous run, each
    /// named exactly as the team file names it.
    /// </summary>
    /// <remarks>
    /// Empty by default, and empty means none. A fresh machine does not push
    /// anything unattended because a file somebody else edited said it could.
    /// </remarks>
    public List<string> OutwardAllowed { get; set; } = [];

    /// <summary>
    /// Teams something outside this machine may start, each named exactly.
    /// </summary>
    /// <remarks>
    /// Empty by default, and empty means none. Turning the webhook on grants
    /// nothing by itself: a token says who is asking, and this says what they
    /// may ask for. Two separate acts, because "let my git hook start the docs
    /// crew" and "let anything with the token start anything" are different
    /// decisions and only one of them is usually meant.
    /// </remarks>
    public List<string> WebhookTeams { get; set; } = [];

    /// <summary>
    /// The address the dashboard and its webhook listen on.
    /// </summary>
    /// <remarks>
    /// Loopback unless somebody changed it, and changing it is the deliberate
    /// act of putting a port on the network. Never inferred from the webhook
    /// being on: a machine that accepts triggered runs from its own git hook
    /// wants nothing bound outside itself.
    /// </remarks>
    public string WebhookListen { get; set; } = "127.0.0.1";
}

/// <summary>This machine's view of one project.</summary>
public sealed class MachineProjectEntry
{
    /// <summary>Project UUID, so the mapping survives a slug rename.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Absolute path to the local clone.</summary>
    public string Path { get; set; } = string.Empty;

    public DateTimeOffset? LastLaunchedUtc { get; set; }

    public int LaunchCount { get; set; }

    public bool Pinned { get; set; }

    /// <summary>Agent last chosen here, used to pre-select in the picker.</summary>
    public string? LastAgent { get; set; }
}
