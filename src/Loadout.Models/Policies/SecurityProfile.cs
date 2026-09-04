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

    /// <summary>
    /// Tools the agent may use without being asked. Empty means the agent's own
    /// default.
    /// </summary>
    /// <remarks>
    /// Not applied from a shared profile, and the launcher says so rather than
    /// dropping it quietly. This pre-approves rather than restricts, so a
    /// workspace carrying it would suppress approval prompts on the machine of
    /// everyone who uses that workspace — the thing the paragraph above rules
    /// out. It is kept as a field so an existing file still loads and can be
    /// explained, not because it is honoured.
    /// </remarks>
    public List<string> AllowedTools { get; set; } = [];

    /// <summary>Tools the agent may not use.</summary>
    public List<string> DisallowedTools { get; set; } = [];

    /// <summary>
    /// Shell commands the agent may not run, as commands rather than tool
    /// names: <c>git push</c>, <c>terraform apply</c>, <c>rm</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Denial is the only half of command policy that belongs in a shared file.
    /// It tightens, so the worst a bad entry can do is stop something that
    /// should have run — visibly, and on the machine of whoever hits it.
    /// Pre-approval is the opposite: it removes somebody else's prompt, on
    /// their machine, without their say-so. That lives in machine-local
    /// configuration and nowhere else.
    /// </para>
    /// <para>
    /// A denial covers the command and anything that starts with it, so
    /// <c>git push</c> also stops <c>git push --force</c> and denying
    /// <c>git</c> stops all of it. Matching is on whole words, so denying
    /// <c>rm</c> does not stop <c>rmdir</c>.
    /// </para>
    /// </remarks>
    public List<string> DeniedCommands { get; set; } = [];

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
