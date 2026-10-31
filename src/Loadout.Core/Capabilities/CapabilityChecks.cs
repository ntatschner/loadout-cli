using Loadout.Models.Tasks;

namespace Loadout.Core.Capabilities;

/// <summary>One thing a project can have or lack, and which kinds of work it matters to.</summary>
/// <param name="Id">How the check is named everywhere: <c>ci</c>, <c>release</c>.</param>
/// <param name="Name">What it is, in a few words.</param>
/// <param name="AsksGitHub">Whether only GitHub can answer it in full, so it is unknown until GitHub is asked.</param>
/// <param name="Types">The types of work it matters to; a gap is shown against these and no others.</param>
public sealed record CapabilityCheck(string Id, string Name, bool AsksGitHub, IReadOnlyList<TaskType> Types);

/// <summary>The checks a profile runs, in the order it shows them.</summary>
/// <remarks>
/// <para>
/// Which types a check matters to lives here rather than in the code that
/// reads a type, so a type gains or loses a check by changing a list. docs,
/// maintenance and new-project raise none on the first day: a new project has
/// no repository to look at, and neither of the others depends on CI or a
/// release to be done.
/// </para>
/// <para>
/// Built in for now. The plan opens this to checks declared by specialists;
/// those will join this list and never replace how a state is decided.
/// </para>
/// </remarks>
public static class CapabilityChecks
{
    public const string Ci = "ci";
    public const string Release = "release";
    public const string Environments = "environments";
    public const string BranchProtection = "branch-protection";

    /// <summary>Every built-in check.</summary>
    public static readonly IReadOnlyList<CapabilityCheck> BuiltIn =
    [
        new(Ci, "CI on push and pull request", false, [TaskType.CiCd, TaskType.Release]),
        new(Release, "A release workflow", true, [TaskType.Release]),
        new(Environments, "Deployment environments", true, [TaskType.Release, TaskType.CiCd]),
        new(BranchProtection, "Protection on the default branch", true, [TaskType.CiCd]),
    ];

    /// <summary>The check by its id, or null.</summary>
    public static CapabilityCheck? Find(string id) =>
        BuiltIn.FirstOrDefault(check => string.Equals(check.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The checks that matter to a type of work.</summary>
    public static IReadOnlyList<CapabilityCheck> For(TaskType type) => [.. BuiltIn.Where(check => check.Types.Contains(type))];
}
