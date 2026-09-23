using System.Text.RegularExpressions;
using Loadout.Core.Teams;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Tools;

/// <summary>Something the nominator thinks is worth the Creator's look.</summary>
/// <param name="Rule">Which of the four rules found it.</param>
/// <param name="Key">What makes it this nomination and no other, so it is filed once.</param>
/// <param name="Summary">One line saying what it is.</param>
/// <param name="Text">Where it was seen, for the Creator to follow up.</param>
/// <param name="Script">The script, where it is a remedy.</param>
/// <param name="Also">
/// Other keys it may have been filed under before, so a cluster that grows by
/// a shelf is still filed once.
/// </param>
public sealed record ToolNomination(
    int Rule,
    string Key,
    string Summary,
    string Text,
    string? Script,
    IReadOnlyList<string>? Also = null);

/// <summary>
/// Finds what finished work keeps doing again, and files it for the Creator.
/// </summary>
/// <remarks>
/// <para>
/// A nomination is not a tool. It says something recurred and where; whether
/// it is general enough to become one is the Creator's question, answered
/// against the promotion bar. That is why a nomination skips the genericity
/// check a submission gets - it quotes the project it came from, on purpose,
/// so the Creator can find it - and why it still goes through the secret
/// screen, which no reason overrides.
/// </para>
/// <para>
/// Reads only runs that have finished and never a tool-works run, so it
/// cannot disturb work in progress or nominate its own output.
/// </para>
/// </remarks>
public sealed partial class ToolNominator
{
    private readonly IToolRegistry _registry;
    private readonly IRunJournal _journal;
    private readonly IRemedyBook _remedies;
    private readonly IPlatformPaths _paths;

    public ToolNominator(IToolRegistry registry, IRunJournal journal, IRemedyBook remedies, IPlatformPaths paths)
    {
        _registry = registry;
        _journal = journal;
        _remedies = remedies;
        _paths = paths;
    }

    /// <summary>How many runs back it reads.</summary>
    public int Depth { get; init; } = 200;

    /// <summary>
    /// Files every nomination not already filed: a candidate for the Creator,
    /// or, where an active tool already covers it, an idea about that tool for
    /// the Refiner, as a use it can weigh.
    /// </summary>
    /// <param name="lessons">The text of every memory topic of kind lesson.</param>
    /// <returns>Each nomination found, with what filing it came to; null where it was filed before.</returns>
    public IReadOnlyList<(ToolNomination Nomination, OperationResult<ToolSubmitted>? Filed)> Scan(IReadOnlyList<string> lessons)
    {
        // A note ends "key " and every key the nomination was filed under.
        var filed = _registry.Audit()
            .Where(one => one.Action == "nominate")
            .Select(one => one.Note ?? string.Empty)
            .Where(note => note.Contains(" key ", StringComparison.Ordinal))
            .SelectMany(note => note[(note.IndexOf(" key ", StringComparison.Ordinal) + 5)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);

        bool Filed(string key) => filed.Contains(KeyOf(key));

        return
        [
            .. Sorted(lessons).Select(sorted =>
            {
                var (one, tool) = sorted;
                var prefix = tool is null ? string.Empty : "hint " + tool + " ";

                var keys = new[] { one.Key }.Concat(one.Also ?? []).ToList();

                if (keys.Any(key => Filed(prefix + key)))
                {
                    return (one, (OperationResult<ToolSubmitted>?)null);
                }

                var text = tool is null
                    ? one.Text
                    : $"{one.Text} The active tool {tool} already covers this, so it is a use of that tool rather than a new one.";

                return (one, _registry.Nominate(
                    new ToolSubmission("candidate", text, Tool: tool, By: "nominator", Script: one.Script, Summary: one.Summary),
                    // Every member's key, so the cluster is still recognised
                    // after the script it was keyed by leaves it.
                    string.Join(' ', keys.Select(key => KeyOf(prefix + key)))));
            }),
        ];
    }

    /// <summary>Every nomination the rules find, leaving out what an active tool already covers.</summary>
    public IReadOnlyList<ToolNomination> Find(IReadOnlyList<string> lessons) =>
        [.. Sorted(lessons).Where(one => one.CoveredBy is null).Select(one => one.Nomination)];

    /// <summary>Every nomination the rules find, with the active tool that covers it, if one does.</summary>
    private List<(ToolNomination Nomination, string? CoveredBy)> Sorted(IReadOnlyList<string> lessons)
    {
        ArgumentNullException.ThrowIfNull(lessons);

        var shelves = Shelves();
        var evidence = Evidence();
        var found = new List<ToolNomination>();

        found.AddRange(SameOnTwoShelves(shelves));
        found.AddRange(RevisedAndRunTwice(shelves, evidence));
        found.AddRange(Commands(evidence, lessons));

        var active = _registry.Offerable();

        // What an active tool already does is the Refiner's to hear about,
        // not the Creator's to build again.
        return [.. found.Select(one => (one, active.FirstOrDefault(tool => Covers(tool, one))?.Record.Name))];
    }

    /// <summary>Whether an active tool already does what a nomination found.</summary>
    /// <remarks>
    /// A remedy is compared script to script. A command has no script, and a
    /// sentence about it shares too little with a tool's script to overlap,
    /// so it is covered where the tool's examples run the same shape, its
    /// script runs the command, or its capabilities name it.
    /// </remarks>
    private static bool Covers(ToolOffered tool, ToolNomination nomination)
    {
        if (nomination.Script is { } script)
        {
            return ToolOverlap.Score(
                new ToolShape([], nomination.Summary, script),
                new ToolShape(tool.Record.Capabilities, tool.Record.Summary, tool.Script)).Overlaps;
        }

        var shape = nomination.Key[(nomination.Key.IndexOf(' ', StringComparison.Ordinal) + 1)..];
        var named = Named(shape) ?? shape;

        return tool.Version.Examples.Any(one => string.Equals(Shape(one.Command), shape, StringComparison.Ordinal))
            || Runs(tool.Script, named)
            || tool.Record.Capabilities.Any(one => string.Equals(one, named, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a script runs a command: its words, whole, where a command starts.</summary>
    /// <remarks>
    /// Where a command starts is the start of a line or after a pipe, a
    /// separator or an opening bracket, so "make sure" in a comment or a
    /// quoted sentence is not running make, and "remake" is not either.
    /// </remarks>
    private static bool Runs(string script, string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape);
        var pattern = @"(?:^|[|;&({])[ \t]*(?:[^\s|;&(){}'""#]*[/\\])?" + string.Join(@"[ \t]+", words) + @"(?![\w.-])";

        return Regex.IsMatch(
            script, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <summary>A command with its arguments replaced, so two runs of it compare equal.</summary>
    public static string Shape(string command)
    {
        var words = (command ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0)
        {
            return string.Empty;
        }

        var shaped = new List<string> { Path.GetFileName(words[0]).ToLowerInvariant() };

        foreach (var word in words.Skip(1))
        {
            if (Flag().Match(word) is { Success: true } flag)
            {
                shaped.Add(flag.Groups[1].Value + (word.Contains('=', StringComparison.Ordinal) ? "=<arg>" : string.Empty));
            }
            else
            {
                shaped.Add(Plain().IsMatch(word) ? word : "<arg>");
            }
        }

        return string.Join(' ', shaped);
    }

    /// <summary>The words a lesson has to name for a command shape to count as named.</summary>
    private static string? Named(string shape)
    {
        var head = shape.Split(' ').TakeWhile(one => one != "<arg>" && !one.StartsWith('-')).ToList();

        // A bare program name is in too many lessons to mean anything.
        return head.Count >= 2 ? string.Join(' ', head) : null;
    }

    private static string KeyOf(string key) =>
        RemedyCeiling.Fingerprint(key)[..16];

    private sealed record Shelved(string Team, Remedy Remedy, string Script);

    private sealed record Seen(string Run, string Team, ReportEvidence Evidence);

    private List<Shelved> Shelves()
    {
        var work = Path.Combine(_paths.Paths.State, "teams", "work");
        var shelved = new List<Shelved>();

        if (!Directory.Exists(work))
        {
            return shelved;
        }

        List<string> teams;

        try
        {
            teams = [.. Directory.EnumerateDirectories(work).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return shelved;
        }

        foreach (var team in teams)
        {
            // One team's shelf that cannot be read is passed over, and only it.
            try
            {
                if (string.Equals(team, ScheduleService.ToolWorksTeam, StringComparison.OrdinalIgnoreCase)
                    || _remedies.All(team) is not { Succeeded: true } all)
                {
                    continue;
                }

                foreach (var remedy in all.Value!)
                {
                    if (_remedies.ScriptOf(team, remedy) is { Succeeded: true } script)
                    {
                        shelved.Add(new Shelved(team, remedy, script.Value!));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return shelved;
    }

    private List<Seen> Evidence()
    {
        var seen = new List<Seen>();

        foreach (var id in _journal.List(Depth))
        {
            RunSummary run;
            IReadOnlyList<RunDocument> documents;

            // A run folder that cannot be listed is passed over, and only it:
            // the other runs are still worth reading. Summarising lists the
            // folder too, so both are inside the guard.
            try
            {
                if (_journal.Summarise(id) is not { Succeeded: true } summarised
                    || summarised.Value! is not { Finished: not null } finished
                    || string.Equals(finished.Team, ScheduleService.ToolWorksTeam, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                run = finished;
                documents = RunDocuments.In(run.Directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var document in documents.Where(one => one.Kind == "report"))
            {
                string text;

                try
                {
                    text = File.ReadAllText(Path.Combine(run.Directory, document.Name));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if (ReportReader.Read(text) is { Succeeded: true } report)
                {
                    seen.AddRange(report.Value!.Evidence
                        .Where(one => one.Result == EvidenceResult.Pass)
                        .Select(one => new Seen(run.RunId, run.Team, one)));
                }
            }
        }

        return seen;
    }

    /// <summary>Rule 1: one remedy, or near enough, kept by two or more teams.</summary>
    private static IEnumerable<ToolNomination> SameOnTwoShelves(List<Shelved> shelves)
    {
        var group = Enumerable.Range(0, shelves.Count).ToArray();

        int Root(int one) => group[one] == one ? one : group[one] = Root(group[one]);

        for (var a = 0; a < shelves.Count; a++)
        {
            for (var b = a + 1; b < shelves.Count; b++)
            {
                if (string.Equals(shelves[a].Team, shelves[b].Team, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var same = string.Equals(
                    RemedyCeiling.Fingerprint(shelves[a].Script),
                    RemedyCeiling.Fingerprint(shelves[b].Script),
                    StringComparison.Ordinal)
                    || ToolOverlap.Score(ShapeOf(shelves[a]), ShapeOf(shelves[b])).Overlaps;

                if (same)
                {
                    group[Root(b)] = Root(a);
                }
            }
        }

        foreach (var cluster in Enumerable.Range(0, shelves.Count).GroupBy(Root))
        {
            var members = cluster.Select(one => shelves[one]).ToList();

            if (members.Select(one => one.Team).Distinct(StringComparer.OrdinalIgnoreCase).Count() < 2)
            {
                continue;
            }

            var names = members.Select(one => one.Team + "/" + one.Remedy.Name).Order(StringComparer.Ordinal).ToList();

            // Keyed by what the scripts are rather than who keeps them, so a
            // third shelf joining the cluster is the same nomination.
            var keys = members
                .Select(one => "1 " + RemedyCeiling.Fingerprint(one.Script))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            yield return new ToolNomination(
                1,
                keys[0],
                members[0].Remedy.What,
                $"The same remedy is kept on {names.Count} team shelves: {string.Join(", ", names)}.",
                members[0].Script,
                keys[1..]);
        }
    }

    /// <summary>Rule 2: a remedy improved at least once and seen to pass in two or more of its team's runs.</summary>
    /// <remarks>
    /// The file by its whole name, and only in the owning team's runs: another
    /// team's fix.ps1 is another script, and so is this team's prefix.ps1.
    /// </remarks>
    private static IEnumerable<ToolNomination> RevisedAndRunTwice(List<Shelved> shelves, List<Seen> evidence)
    {
        foreach (var shelved in shelves.Where(one => one.Remedy.Revision >= 1))
        {
            var file = Path.GetFileName(shelved.Remedy.Script);

            if (file.Length == 0)
            {
                continue;
            }

            var runs = evidence
                .Where(one => string.Equals(one.Team, shelved.Team, StringComparison.OrdinalIgnoreCase)
                    && Words(one.Evidence.Ref + " " + one.Evidence.Note)
                        .Any(word => string.Equals(Path.GetFileName(word), file, StringComparison.OrdinalIgnoreCase)))
                .Select(one => one.Run)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (runs.Count >= 2)
            {
                yield return new ToolNomination(
                    2,
                    $"2 {shelved.Team}/{shelved.Remedy.Name}",
                    shelved.Remedy.What,
                    $"{shelved.Team}/{shelved.Remedy.Name}, revised {shelved.Remedy.Revision} times, passed in runs {string.Join(", ", runs)}.",
                    shelved.Script);
            }
        }
    }

    /// <summary>
    /// Rule 3: one command shape passing in runs of two or more teams. Rule 4:
    /// a lesson naming a command that passed, which is a second use the runs
    /// alone did not show.
    /// </summary>
    private static IEnumerable<ToolNomination> Commands(List<Seen> evidence, IReadOnlyList<string> lessons)
    {
        var shapes = evidence
            .Where(one => one.Evidence.Kind == EvidenceKind.Command)
            .GroupBy(one => Shape(one.Evidence.Ref), StringComparer.Ordinal)
            .Where(one => one.Key.Length > 0)
            .OrderBy(one => one.Key, StringComparer.Ordinal);

        foreach (var shape in shapes)
        {
            var teams = shape.Select(one => one.Team).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var runs = shape.Select(one => one.Run).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

            if (teams >= 2 && runs.Count >= 2)
            {
                yield return new ToolNomination(
                    3,
                    "3 " + shape.Key,
                    $"Runs '{shape.Key}'.",
                    $"'{shape.Key}' passed in runs of {teams} teams: {string.Join(", ", runs)}.",
                    null);
            }
            else if (Named(shape.Key) is { } named
                && lessons.Any(one => one.Contains(named, StringComparison.OrdinalIgnoreCase)))
            {
                yield return new ToolNomination(
                    4,
                    "4 " + shape.Key,
                    $"Runs '{shape.Key}'.",
                    $"A lesson names '{named}', and '{shape.Key}' passed in runs {string.Join(", ", runs)}.",
                    null);
            }
        }
    }

    /// <summary>The words of a command line, without the quotes around a path.</summary>
    private static IEnumerable<string> Words(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(one => one.Trim('\'', '"', '`', ',', ';'));

    private static ToolShape ShapeOf(Shelved shelved) =>
        new([shelved.Remedy.Kind], shelved.Remedy.What, shelved.Script);

    [GeneratedRegex(@"^(--?[A-Za-z][A-Za-z0-9-]*)(=.*)?$", RegexOptions.None, 1000)]
    private static partial Regex Flag();

    [GeneratedRegex(@"^[a-z][a-z-]*$", RegexOptions.None, 1000)]
    private static partial Regex Plain();
}
