namespace Loadout.Models.Policies;

/// <summary>How much of the filesystem an agent may touch (spec section 58).</summary>
public enum FilesystemAccess
{
    /// <summary>The repository working directory, which is the ordinary case.</summary>
    Repository,

    /// <summary>Read but do not write.</summary>
    ReadOnly,

    /// <summary>Narrower than the repository, for production work.</summary>
    Restricted,
}

/// <summary>How freely an agent may reach the network (spec section 58).</summary>
public enum NetworkAccess
{
    Standard,
    Restricted,
    Allowlist,
}

/// <summary>How readily an agent may act without being asked (spec section 58).</summary>
public enum ApprovalPolicy
{
    Normal,
    Strict,
}

/// <summary>
/// A generic security posture that each adapter translates into whatever its
/// agent actually supports (spec section 58).
/// <para>
/// Deliberately expressed in the launcher's own vocabulary rather than any
/// agent's. Claude speaks of permission modes and Codex of sandboxes; a project
/// should be able to say "production work is read-only" once and have both
/// honour it as far as they can.
/// </para>
/// <para>
/// A profile may only ever tighten. There is no value here that loosens an
/// agent's defaults, and the adapters never emit the flags that bypass
/// permissions or sandboxing — a configuration file in a shared repository is
/// the wrong place to be able to disable somebody's safety controls.
/// </para>
/// </summary>
public sealed class SecurityProfile
{
    public string Description { get; set; } = string.Empty;

    public FilesystemAccess Filesystem { get; set; } = FilesystemAccess.Repository;

    public NetworkAccess Network { get; set; } = NetworkAccess.Standard;

    public ApprovalPolicy Approvals { get; set; } = ApprovalPolicy.Normal;

    /// <summary>Tools the agent may use. Empty means the agent's own default.</summary>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>Tools the agent may not use.</summary>
    public List<string> DisallowedTools { get; set; } = [];

    /// <summary>The three profiles named in spec section 58, used when the workspace defines none.</summary>
    public static IReadOnlyDictionary<string, SecurityProfile> CreateDefaults() =>
        new Dictionary<string, SecurityProfile>(StringComparer.OrdinalIgnoreCase)
        {
            ["normal"] = new()
            {
                Description = "Ordinary development",
                Filesystem = FilesystemAccess.Repository,
                Network = NetworkAccess.Standard,
                Approvals = ApprovalPolicy.Normal,
            },

            ["review"] = new()
            {
                Description = "Reading and reviewing, no changes",
                Filesystem = FilesystemAccess.ReadOnly,
                Network = NetworkAccess.Restricted,
                Approvals = ApprovalPolicy.Normal,
            },

            ["production"] = new()
            {
                Description = "Production investigation",
                Filesystem = FilesystemAccess.Restricted,
                Network = NetworkAccess.Allowlist,
                Approvals = ApprovalPolicy.Strict,
            },
        };
}
