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
