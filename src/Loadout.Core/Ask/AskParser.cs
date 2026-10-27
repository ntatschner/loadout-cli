using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Tasks;

namespace Loadout.Core.Ask;

/// <summary>The kinds of request Loadout answers itself.</summary>
public enum AskShape
{
    /// <summary>What was spent over a period, across projects or for one.</summary>
    Spend,

    /// <summary>Open tasks, listed or ranked.</summary>
    Tasks,

    /// <summary>Queue tasks for a team, and start it when asked.</summary>
    TeamQueue,

    /// <summary>Open terminals, each with a task loaded.</summary>
    Terminals,
}

/// <summary>A request with its slots filled, ready for ordinary code to run.</summary>
/// <param name="Shape">What kind of request it is.</param>
/// <param name="Period">The days asked about, or null when none were named.</param>
/// <param name="Project">The one project asked about, or null for all of them.</param>
/// <param name="Count">How many were asked for, or null when no number was given.</param>
/// <param name="Pick">How tasks should be ranked.</param>
/// <param name="Team">The team named, or null when none was.</param>
/// <param name="Start">Whether the team should be started once its queue is filled.</param>
/// <param name="Named">
/// Particular tasks, in order, when the request means those rather than
/// whichever rank best. The parser never fills it; the agent can.
/// </param>
public sealed record AskRequest(
    AskShape Shape,
    DateSpan? Period = null,
    string? Project = null,
    int? Count = null,
    TaskPick Pick = TaskPick.Any,
    string? Team = null,
    bool Start = false,
    IReadOnlyList<AskTaskRef>? Named = null);

/// <summary>One task, by the project it is on and its id.</summary>
public sealed record AskTaskRef(string Project, string Task);

/// <summary>A built-in phrasing close to what was typed, offered when nothing was placed.</summary>
/// <param name="Shape">Which kind of request it is.</param>
/// <param name="Rewrite">The request written so the parser places it, ready to be asked as it stands.</param>
public sealed record AskNearMiss(AskShape Shape, string Rewrite);

/// <summary>What the parser made of a request.</summary>
/// <param name="Request">The request, or null when it could not be placed.</param>
/// <param name="NearMisses">The closest built-in phrasings, when it could not.</param>
public sealed record AskPlacement(AskRequest? Request, IReadOnlyList<AskNearMiss> NearMisses);

/// <summary>Names the parser can recognise in a request.</summary>
/// <param name="Projects">Each project's slug, with the other names it answers to.</param>
/// <param name="Teams">The teams that can be run.</param>
public sealed record AskVocabulary(
    IReadOnlyList<(string Slug, IReadOnlyList<string> Names)> Projects,
    IReadOnlyList<string> Teams)
{
    /// <summary>No names known.</summary>
    public static readonly AskVocabulary Empty = new([], []);
}

/// <summary>
/// Places a typed request into one of the shapes Loadout answers itself, and
/// fills in its slots.
/// </summary>
/// <remarks>
/// <para>
/// Words, not a model. Each shape is recognised from the words that name it,
/// and each slot from a small pattern, so the same request places the same way
/// every time and costs nothing. That is the point: the agent is the exception,
/// used only when somebody chooses it, and every phrasing taught here is one
/// that never needs it.
/// </para>
/// <para>
/// When nothing places, the parser does not guess. It says which shapes came
/// closest, written out as requests it would place, so a near miss teaches the
/// phrasing rather than hiding it.
/// </para>
/// </remarks>
public static partial class AskParser
{
    private static readonly string[] SpendWords = ["spend", "spent", "spending", "cost", "costs", "bill", "billed", "dollars", "money"];
    private static readonly string[] TerminalWords = ["terminal", "terminals", "shell", "shells", "console", "consoles"];
    private static readonly string[] SessionWords = ["session", "sessions"];
    private static readonly string[] OpenVerbs = ["open", "start", "launch", "spin", "give"];
    private static readonly string[] TeamWords = ["team", "teams", "queue", "queued"];
    private static readonly string[] TaskWords = ["task", "tasks", "todo", "todos", "work", "jobs"];
    private static readonly string[] ListVerbs = ["list", "show", "rank", "which", "what", "pending", "open"];
    private static readonly string[] StartVerbs = ["start", "run", "kick", "go"];
    private static readonly string[] EasyWords = ["easy", "easiest", "simple", "simplest", "quick", "quickest", "small", "smallest"];
    private static readonly string[] AllWords = ["all", "every", "each", "across"];

    /// <summary>Places a request, or says what it came close to.</summary>
    /// <param name="query">What was typed, with or without a leading '?'.</param>
    /// <param name="vocabulary">Project and team names to recognise.</param>
    /// <param name="today">Today, for periods like "last tuesday".</param>
    public static AskPlacement Place(string query, AskVocabulary vocabulary, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(vocabulary);

        var text = Normalise(query);
        var words = Words(text);

        var project = ProjectIn(text, vocabulary);
        var count = CountIn(text);
        var pick = PickIn(text, words);
        var period = AskDates.Find(text, today);

        var spend = Any(words, SpendWords) || text.Contains("how much", StringComparison.Ordinal);
        var terminals = Any(words, TerminalWords) || (Any(words, SessionWords) && Any(words, OpenVerbs) && !Any(words, TeamWords));
        var team = Any(words, TeamWords);
        var tasks = Any(words, TaskWords);

        AskRequest? request = null;

        if (spend && !terminals && !(team && Any(words, StartVerbs)))
        {
            request = new AskRequest(AskShape.Spend, Period: period, Project: project);
        }
        else if (terminals)
        {
            request = new AskRequest(AskShape.Terminals, Project: project, Count: count, Pick: pick);
        }
        else if (team)
        {
            request = new AskRequest(
                AskShape.TeamQueue,
                Project: project,
                Count: count,
                Pick: pick,
                Team: TeamIn(text, vocabulary),
                Start: Any(words, StartVerbs));
        }
        else if (tasks && (Any(words, ListVerbs) || pick != TaskPick.Any || count is not null))
        {
            request = new AskRequest(AskShape.Tasks, Project: project, Count: count, Pick: pick);
        }

        if (request is not null)
        {
            return new AskPlacement(request, []);
        }

        return new AskPlacement(null, NearMisses(words, period, project, count, pick));
    }

    /// <summary>The request in lower case, without the '?' that asks it, and with spacing tidied.</summary>
    internal static string Normalise(string query) =>
        Spaces().Replace(query.Trim().TrimStart('?').Trim().ToLowerInvariant(), " ");

    /// <summary>
    /// The shapes closest to what was typed, best first, each written as a
    /// request that places.
    /// </summary>
    /// <remarks>
    /// Every shape is offered when no word points at any of them, because a
    /// near-miss list that is empty teaches nothing.
    /// </remarks>
    private static List<AskNearMiss> NearMisses(
        HashSet<string> words,
        DateSpan? period,
        string? project,
        int? count,
        TaskPick pick)
    {
        var scored = new (AskShape Shape, int Score)[]
        {
            (AskShape.Spend, Hits(words, SpendWords) + (period is null ? 0 : 2)),
            (AskShape.Tasks, Hits(words, TaskWords) + Hits(words, ListVerbs) + (pick == TaskPick.Any ? 0 : 1)),
            (AskShape.TeamQueue, Hits(words, TeamWords) + Hits(words, StartVerbs)),
            (AskShape.Terminals, Hits(words, TerminalWords) + Hits(words, SessionWords)),
        };

        var ordered = scored.Any(one => one.Score > 0)
            ? scored.Where(one => one.Score > 0).OrderByDescending(one => one.Score).ThenBy(one => one.Shape)
            : scored.AsEnumerable();

        return ordered
            .Select(one => new AskNearMiss(one.Shape, Rewrite(one.Shape, period, project, count, pick)))
            .ToList();
    }

    /// <summary>A request of the given shape, written in the parser's own words, with whatever slots were found.</summary>
    internal static string Rewrite(AskShape shape, DateSpan? period, string? project, int? count, TaskPick pick)
    {
        var scope = project is null ? "across all projects" : $"for {project}";
        var ranked = pick switch
        {
            TaskPick.Easy => "easy ",
            TaskPick.NeedsWorkFirst => "needs-work-first ",
            _ => string.Empty,
        };

        return shape switch
        {
            AskShape.Spend => $"how much did I spend {Period(period)} {scope}",
            AskShape.Tasks => $"list {count ?? 5} {ranked}open tasks {scope}",
            AskShape.TeamQueue => $"queue {count ?? 3} {(ranked.Length > 0 ? ranked : "easy ")}tasks for a team {scope} and start it",
            AskShape.Terminals => $"open {count ?? 2} terminals with {(ranked.Length > 0 ? ranked : "needs-work-first ")}tasks {scope}",
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        static string Period(DateSpan? period) => period switch
        {
            null => "in the last 7 days",
            { From: var from, Until: var until } when from == until => $"on {from:yyyy-MM-dd}",
            { From: var from } => $"in the last {Math.Max(1, period.Until.DayNumber - from.DayNumber + 1)} days",
        };
    }

    private static string? ProjectIn(string text, AskVocabulary vocabulary)
    {
        // Longest name first, so "loadout-cli" is not read as "loadout".
        return vocabulary.Projects
            .SelectMany(project => project.Names.Append(project.Slug).Select(name => (project.Slug, Name: name.ToLowerInvariant())))
            .Where(one => one.Name.Length > 0)
            .OrderByDescending(one => one.Name.Length)
            .FirstOrDefault(one => Regex.IsMatch(text, $@"(?<![\w-]){Regex.Escape(one.Name)}(?![\w-])", RegexOptions.CultureInvariant))
            .Slug;
    }

    private static string? TeamIn(string text, AskVocabulary vocabulary) =>
        vocabulary.Teams
            .OrderByDescending(name => name.Length)
            .FirstOrDefault(name => Regex.IsMatch(
                text,
                $@"(?<![\w-]){Regex.Escape(name.ToLowerInvariant())}(?![\w-])",
                RegexOptions.CultureInvariant));

    private static int? CountIn(string text)
    {
        // A number that is part of a period ("last 10 days") or a date is not
        // a count of anything.
        var cleaned = Periods().Replace(text, " ");

        foreach (Match match in Counted().Matches(cleaned))
        {
            if (AskWords.Number(match.Groups["n"].Value) is { } number and > 0 and <= 50)
            {
                return number;
            }
        }

        return null;
    }

    private static TaskPick PickIn(string text, HashSet<string> words)
    {
        if (Any(words, EasyWords))
        {
            return TaskPick.Easy;
        }

        return text.Contains("needs work", StringComparison.Ordinal)
            || text.Contains("need work", StringComparison.Ordinal)
            || text.Contains("needs-work", StringComparison.Ordinal)
            || text.Contains("most urgent", StringComparison.Ordinal)
            || text.Contains("most important", StringComparison.Ordinal)
            || text.Contains("oldest", StringComparison.Ordinal)
            || (text.Contains("first", StringComparison.Ordinal) && Any(words, TaskWords))
            ? TaskPick.NeedsWorkFirst
            : TaskPick.Any;
    }

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(text).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);

    private static bool Any(HashSet<string> words, string[] wanted) => wanted.Any(words.Contains);

    private static int Hits(HashSet<string> words, string[] wanted) => wanted.Count(words.Contains);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\b(?:last|past)\s+\w+\s+(?:days?|weeks?)\b|\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}(?:st|nd|rd|th)?\s+(?:of\s+)?(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\b|\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\s+\d{1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Periods();

    [GeneratedRegex(@"\b(?<n>\d{1,2}|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|a couple|a few)\b", RegexOptions.CultureInvariant)]
    private static partial Regex Counted();
}

/// <summary>Numbers as people type them in a request.</summary>
public static class AskWords
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.Ordinal)
    {
        ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6,
        ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11, ["twelve"] = 12,
        ["a couple"] = 2, ["a few"] = 3,
    };

    /// <summary>The number a word or figure means, or null when it is neither.</summary>
    public static int? Number(string text) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var figure)
            ? figure
            : Named.TryGetValue(text, out var named) ? named : null;
}
