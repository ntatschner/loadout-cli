using System.Text.RegularExpressions;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>
/// The names a task's type goes by, and the rules that guess one when nobody
/// gives it.
/// </summary>
/// <remarks>
/// <para>
/// The rules read only the words and where the work was captured. That is cheap
/// and predictable, and a guess is only a first answer: the agent refining an
/// idea revises it, and a person's choice outranks both.
/// </para>
/// <para>
/// Each type counts the distinct words of its own it finds, and the most wins.
/// A tie goes to the type earlier in <see cref="Ties"/>, so the same words
/// always give the same type. Nothing found leaves the type to where the work
/// was captured: an idea on the workspace-wide list belongs to no project yet,
/// so it is most likely a new one; anything else is taken to be a feature.
/// </para>
/// </remarks>
public static partial class TaskTypes
{
    /// <summary>Every type, in the order a person is offered them.</summary>
    public static readonly IReadOnlyList<TaskType> All = Enum.GetValues<TaskType>();

    /// <summary>Every type as the command line and JSON name it.</summary>
    public static readonly IReadOnlyList<string> Names = [.. All.Select(Name)];

    /// <summary>What the history says when a person changes a task's type.</summary>
    public static string ChangedReason(TaskType type) => $"type set to {Words(type)}";

    /// <summary>What the history says when <c>task classify</c> gives a task the rules' type.</summary>
    public static string GuessedReason(TaskType type) => $"type guessed as {Words(type)} by task classify";

    /// <summary>The type as the command line and JSON name it: <c>bug-fix</c>, <c>ci-cd</c>.</summary>
    public static string Name(TaskType type) => type switch
    {
        TaskType.NewProject => "new-project",
        TaskType.BugFix => "bug-fix",
        TaskType.CiCd => "ci-cd",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>The type as a sentence names it: <c>bug fix</c>, <c>CI/CD</c>.</summary>
    public static string Words(TaskType type) => type switch
    {
        TaskType.NewProject => "new project",
        TaskType.BugFix => "bug fix",
        TaskType.CiCd => "CI/CD",
        _ => type.ToString().ToLowerInvariant(),
    };

    /// <summary>Who gave a type, as the command line and JSON name it.</summary>
    public static string Name(TaskTypeSource source) => source.ToString().ToLowerInvariant();

    /// <summary>
    /// A type from its name, its words or its enum spelling, whatever the case;
    /// null when it is none of them.
    /// </summary>
    public static TaskType? Parse(string? said)
    {
        if (said is not { Length: > 0 } || said.Trim() is not { Length: > 0 } trimmed)
        {
            return null;
        }

        foreach (var type in All)
        {
            if (string.Equals(trimmed, Name(type), StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, Words(type), StringComparison.OrdinalIgnoreCase)
                || string.Equals(trimmed, type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>The type the rules give work captured with these words, here.</summary>
    /// <param name="text">Everything said about it: its title, detail and, for an idea, what was asked.</param>
    /// <param name="kind">Work, or an idea.</param>
    /// <param name="project">The list it was captured on, or null for the workspace-wide one.</param>
    public static TaskType Guess(string? text, TaskKind kind, string? project)
    {
        var words = text ?? string.Empty;
        TaskType? best = null;
        var most = 0;

        foreach (var type in Ties)
        {
            var found = Rules[type]
                .Matches(words)
                .Select(match => match.Value.ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Count();

            if (found > most)
            {
                best = type;
                most = found;
            }
        }

        if (best is { } guessed)
        {
            return guessed;
        }

        return kind == TaskKind.Idea && project is null ? TaskType.NewProject : TaskType.Feature;
    }

    /// <summary>
    /// The order a tie is settled in: what says most about the work first. A
    /// broken release is a bug before it is a release.
    /// </summary>
    private static readonly IReadOnlyList<TaskType> Ties =
    [
        TaskType.BugFix,
        TaskType.Release,
        TaskType.CiCd,
        TaskType.Docs,
        TaskType.Maintenance,
        TaskType.NewProject,
    ];

    private static readonly Dictionary<TaskType, Regex> Rules = new()
    {
        [TaskType.BugFix] = BugFixWords(),
        [TaskType.Release] = ReleaseWords(),
        [TaskType.CiCd] = CiCdWords(),
        [TaskType.Docs] = DocsWords(),
        [TaskType.Maintenance] = MaintenanceWords(),
        [TaskType.NewProject] = NewProjectWords(),
    };

    [GeneratedRegex(
        @"\b(?:bugs?|fix(?:es|ed|ing)?|broken|crash(?:es|ed|ing)?|fails?|failing|failed|failures?|errors?|regressions?|wrong|flaky|exceptions?|doesn'?t work|not working|hangs?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BugFixWords();

    [GeneratedRegex(
        @"\b(?:releases?|releasing|publish(?:es|ed|ing)?|changelog|release notes|version bump|winget|nuget push)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseWords();

    [GeneratedRegex(
        @"\b(?:ci|ci/cd|pipelines?|github actions|workflow files?|build matrix|ci legs?|runners?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CiCdWords();

    [GeneratedRegex(
        @"\b(?:docs|documentation|document(?:ed|ing)?|readme|guide|write-?up)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DocsWords();

    [GeneratedRegex(
        @"\b(?:upgrade|bump|dependenc(?:y|ies)|dependabot|refactor(?:ing)?|tidy|clean ?up|rename|deprecat(?:e|ed|ion)|chore|housekeeping)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MaintenanceWords();

    [GeneratedRegex(
        @"\b(?:new (?:project|app|application|tool|repo|repository|service|site|website)|from scratch)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NewProjectWords();
}
