namespace Loadout.Models.Policies;

/// <summary>
/// Patterns that must not appear in an application repository
/// (spec sections 9 and 49).
/// <para>
/// Lives at <c>policies/forbidden-repository-files.yaml</c> in the central
/// workspace so one organisation-wide rule covers every project, and so
/// changing it is a reviewable commit rather than a setting on somebody's
/// laptop.
/// </para>
/// </summary>
public sealed class RepositoryPolicy
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Git pathspecs that should never be tracked. Matching uses git's own
    /// pathspec engine rather than a reimplemented glob, so the rules behave
    /// exactly as they would in a .gitignore.
    /// </summary>
    public List<string> Forbidden { get; set; } = [];

    /// <summary>
    /// Patterns exempted from <see cref="Forbidden"/>.
    /// <para>
    /// Spec section 9 is explicit that a project may deliberately choose to
    /// version something like AGENTS.md. Without an exemption list the only way
    /// to allow that would be to weaken the rule for everyone.
    /// </para>
    /// </summary>
    public List<string> Allowed { get; set; } = [];

    /// <summary>
    /// The rules applied when the workspace defines none, taken from the lists
    /// in spec sections 9 and 49.
    /// </summary>
    public static RepositoryPolicy CreateDefault() => new()
    {
        Forbidden =
        [
            ".claude/**",
            ".codex/**",
            ".cursor/**",
            ".windsurf/**",
            ".continue/**",
            ".roo/**",
            ".serena/**",
            ".aider*",
            ".ai/**",
            ".agent/**",
            "CLAUDE.md",
            "CLAUDE.local.md",
            "AGENTS.override.md",
        ],

        // .serena is forbidden knowing that Serena disagrees. It writes its
        // own .serena/.gitignore excluding only /cache and
        // /project.local.yml, which means it intends project.yml and
        // memories/ to be committed. That is a reasonable position and it is
        // not this launcher's: memory an agent recorded belongs in the
        // workspace, where every machine and every agent can read it, not in
        // one application repository. A project that wants Serena's layout
        // instead says so in Allowed, which is what Allowed is for.
        //
        // AGENTS.md is deliberately absent from the forbidden list. Spec
        // section 9 names it as the example of a file a project may legitimately
        // choose to version, so the default must not fight that choice.
        Allowed = [],
    };
}

/// <summary>How a path relates to the policy.</summary>
public enum PolicyFindingKind
{
    /// <summary>Tracked by Git. The violation the policy exists to catch.</summary>
    Tracked,

    /// <summary>Present but untracked and not ignored, so one <c>git add .</c> away from being committed.</summary>
    UntrackedAndVisible,

    /// <summary>Present but ignored. Working as intended.</summary>
    Ignored,
}

/// <summary>One path the policy has something to say about.</summary>
/// <param name="Path">Repository-relative path.</param>
/// <param name="Kind">How it relates to the policy.</param>
/// <param name="Pattern">The forbidden pattern it matched.</param>
public sealed record PolicyFinding(string Path, PolicyFindingKind Kind, string Pattern);

/// <summary>The outcome of checking one repository (spec sections 49 and 97).</summary>
/// <param name="RepositoryPath">The repository that was checked.</param>
/// <param name="Findings">Everything matched, whatever its severity.</param>
/// <param name="HasGlobalExcludes">Whether a global Git exclude file is configured (spec section 50).</param>
/// <param name="HasPreCommitHook">Whether the launcher's pre-commit hook is installed (spec section 51).</param>
/// <param name="HookNeedsUpgrade">The hook is the launcher own but was written by an older version.</param>
public sealed record PolicyReport(
    string RepositoryPath,
    IReadOnlyList<PolicyFinding> Findings,
    bool HasGlobalExcludes,
    bool HasPreCommitHook,
    bool HookNeedsUpgrade = false)
{
    /// <summary>Tracked agent files: the repository is not clean.</summary>
    public IReadOnlyList<PolicyFinding> Violations =>
        Findings.Where(f => f.Kind == PolicyFindingKind.Tracked).ToList();

    /// <summary>Files that are not committed yet but easily could be.</summary>
    public IReadOnlyList<PolicyFinding> Warnings =>
        Findings.Where(f => f.Kind == PolicyFindingKind.UntrackedAndVisible).ToList();

    /// <summary>True when nothing forbidden is tracked.</summary>
    public bool IsCompliant => Violations.Count == 0;

    /// <summary>The single word printed at the end of the report.</summary>
    public string Verdict => Violations.Count > 0
        ? "NON-COMPLIANT"
        : Warnings.Count > 0
            ? "WARNING"
            : "COMPLIANT";
}
