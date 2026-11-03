using Loadout.Core.Instructions;
using Loadout.Core.Tasks;
using Loadout.Models.Instructions;
using Loadout.Models.Tasks;

namespace Loadout.Core.Capabilities;

/// <summary>A check as the library settles it, ready for a profile to run.</summary>
/// <param name="Id">How it is named, the same as a built-in's when it overrides one.</param>
/// <param name="Name">What it is in a few words, or empty to keep a built-in's.</param>
/// <param name="Types">The types of work it matters to, or empty to keep a built-in's.</param>
/// <param name="Files">Globs of which any match means it is present. Empty when it does not look at files.</param>
/// <param name="GitHub">The GitHub setting it reads, or empty when it does not read one.</param>
/// <param name="Clash">Why it cannot be trusted because two specialists declare it, or null when it can.</param>
public sealed record DeclaredCheck(
    string Id,
    string Name,
    IReadOnlyList<TaskType> Types,
    IReadOnlyList<string> Files,
    string GitHub,
    string? Clash);

/// <summary>
/// The checks specialists declare in their frontmatter, as one list: what is
/// valid, who wins, and how a declared glob reads a repository's files.
/// </summary>
/// <remarks>
/// <para>
/// Declarative only: a check names files and a GitHub setting the profile
/// already reads, and never a command. A profile is built at the start of an
/// unattended run and what it says can release held work, so it has to stay
/// fast, safe offline and free of side effects, whoever wrote the specialist.
/// </para>
/// <para>
/// A check has nowhere to name a template, so it can report a gap but cannot
/// offer to fill one. Offers can hold unattended work, and that power stays
/// with the templates Loadout ships and tests.
/// </para>
/// </remarks>
public static class DeclaredChecks
{
    /// <summary>A declared GitHub setting for the deployment environments.</summary>
    public const string Environments = "environments";

    /// <summary>A declared GitHub setting for the default branch's protection.</summary>
    public const string BranchProtection = "branch-protection";

    /// <summary>A declared GitHub setting for published releases.</summary>
    public const string Releases = "releases";

    /// <summary>The GitHub settings a check may read: those a profile fetches anyway.</summary>
    public static readonly IReadOnlyList<string> GitHubSettings = [Environments, BranchProtection, Releases];

    /// <summary>Why a check as written cannot be used, or null when it can.</summary>
    public static string? Problem(SpecialistCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);

        if (check.Id.Length == 0)
        {
            return "has no id";
        }

        if (check.Files.Count > 0 && check.GitHub.Length > 0)
        {
            return $"'{check.Id}' names both files and a GitHub setting; a check looks at one or the other";
        }

        if (check.GitHub.Length > 0 && !GitHubSettings.Contains(check.GitHub, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{check.Id}' reads the GitHub setting '{check.GitHub}'; it can read {string.Join(", ", GitHubSettings)}";
        }

        if (check.Types.FirstOrDefault(type => TaskTypes.Parse(type) is null) is { } unknown)
        {
            return $"'{check.Id}' names the type '{unknown}'; the types are {string.Join(", ", TaskTypes.Names)}";
        }

        // Overriding a built-in may change only which work it matters to, and
        // keep how it decides. A new check has nothing to fall back on.
        if (CapabilityChecks.Find(check.Id) is null)
        {
            if (check.Files.Count == 0 && check.GitHub.Length == 0)
            {
                return $"'{check.Id}' names no files and no GitHub setting, so it has nothing to look at";
            }

            if (check.Types.Count == 0)
            {
                return $"'{check.Id}' names no types, so no work would ever meet it";
            }
        }

        return null;
    }

    /// <summary>The usable checks across a library, one for each id, ordered by id.</summary>
    /// <remarks>
    /// A built-in specialist's check gives way to one from anywhere else. Two
    /// from outside the built-ins are a clash nobody can settle by rule, so the
    /// check comes back marked and its profile result is unknown. Unknown holds
    /// nothing and offers nothing, so a clash can never block work.
    /// </remarks>
    public static IReadOnlyList<DeclaredCheck> Resolve(IEnumerable<SpecialistDocument> specialists)
    {
        ArgumentNullException.ThrowIfNull(specialists);

        var declared = specialists
            .OrderBy(specialist => specialist.Id, StringComparer.Ordinal)
            .SelectMany(specialist => specialist.CheckList
                .Where(check => Problem(check) is null)
                .DistinctBy(check => check.Id, StringComparer.OrdinalIgnoreCase)
                .Select(check => (Specialist: specialist, Check: check)));

        var settled = new List<DeclaredCheck>();

        foreach (var group in declared
                     .GroupBy(one => one.Check.Id, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var outside = group.Where(one => one.Specialist.Origin != SpecialistOrigin.BuiltIn).ToList();

            if (outside.Count > 1)
            {
                var names = string.Join(" and ", outside.Select(one => one.Specialist.Id));

                settled.Add(new DeclaredCheck(
                    group.Key,
                    outside[0].Check.Name,
                    [.. outside.SelectMany(one => Types(one.Check)).Distinct()],
                    [],
                    string.Empty,
                    $"{names} both declare this check, so it is not run. Take it out of one of them."));

                continue;
            }

            var winner = outside.Count == 1 ? outside[0].Check : group.First().Check;

            settled.Add(new DeclaredCheck(
                winner.Id,
                winner.Name,
                Types(winner),
                winner.Files,
                winner.GitHub.ToLowerInvariant(),
                null));
        }

        return settled;
    }

    /// <summary>Whether a declared glob matches a path relative to the repository's root.</summary>
    /// <remarks>
    /// Each side gets a leading folder because <see cref="RuleService.Matches"/>
    /// strips leading dots from a path and not from a glob, so
    /// <c>.github/CODEOWNERS</c> would never match the file it names. That file
    /// is just what a check is likely to look for.
    /// </remarks>
    public static bool Matches(string glob, string path)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(path);

        var relative = glob.Trim().Replace('\\', '/');

        if (relative.StartsWith("./", StringComparison.Ordinal))
        {
            relative = relative[2..];
        }

        return relative.Length > 0 && RuleService.Matches($"root/{relative}", $"root/{path}");
    }

    private static IReadOnlyList<TaskType> Types(SpecialistCheck check) =>
        [.. check.Types.Select(type => TaskTypes.Parse(type)!.Value)];
}
