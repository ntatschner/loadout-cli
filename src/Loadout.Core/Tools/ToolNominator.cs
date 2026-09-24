using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Teams;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Tools;

/// <summary>Something the nominator thinks is worth the Creator's look.</summary>
/// <param name="Rule">Which of the seven rules found it.</param>
/// <param name="Key">What makes it this nomination and no other, so it is filed once.</param>
/// <param name="Summary">One line saying what it is.</param>
/// <param name="Text">Where it was seen, for the Creator to follow up.</param>
/// <param name="Script">The script, where it is a remedy, or the paragraph, where it is a prompt.</param>
/// <param name="Also">
/// Other keys it may have been filed under before, so a cluster that grows by
/// a shelf is still filed once.
/// </param>
/// <param name="Capabilities">What sort of tool it would be, where that is not a script: workflow, prompt or integration.</param>
public sealed record ToolNomination(
    int Rule,
    string Key,
    string Summary,
    string Text,
    string? Script,
    IReadOnlyList<string>? Also = null,
    IReadOnlyList<string>? Capabilities = null);

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
                    new ToolSubmission(
                        "candidate", text, Tool: tool, By: "nominator", Script: one.Script, Capabilities: one.Capabilities, Summary: one.Summary),
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
        var read = Finished();
        var evidence = read.Evidence;
        var found = new List<ToolNomination>();

        found.AddRange(SameOnTwoShelves(shelves));
        found.AddRange(RevisedAndRunTwice(shelves, evidence));
        found.AddRange(Commands(evidence, lessons));
        found.AddRange(Workflows(evidence));
        found.AddRange(Prompts(read.Paragraphs));
        found.AddRange(Integrations(read.Uses));

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
        var rest = nomination.Key[(nomination.Key.IndexOf(' ', StringComparison.Ordinal) + 1)..];

        switch (nomination.Rule)
        {
            // A workflow is covered by a tool that runs every step of it.
            case 5:
                return rest.Split(" | ").All(step => Runs(tool.Script, Named(step) ?? step));

            // A prompt by a prompt tool whose text already says it.
            case 6:
                return tool.Record.Capabilities.Contains("prompt", StringComparer.OrdinalIgnoreCase)
                    && Fold(tool.Script).Contains(Fold(nomination.Script ?? string.Empty), StringComparison.Ordinal);

            // An integration by a tool that names the server or host.
            case 7:
                var name = rest[(rest.IndexOf(' ', StringComparison.Ordinal) + 1)..];
                return tool.Record.Capabilities.Any(one => string.Equals(one, name, StringComparison.OrdinalIgnoreCase))
                    || tool.Script.Contains(name, StringComparison.OrdinalIgnoreCase);
        }

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

    /// <summary>An instruction paragraph a run's brief or report carried, as it was written and folded.</summary>
    private sealed record Paragraph(string Run, string Team, string Text, string Folded);

    /// <summary>An MCP server or HTTP host a node of a run used.</summary>
    private sealed record Use(string Run, string Team, string Integration);

    private sealed record Read(List<Seen> Evidence, List<Paragraph> Paragraphs, List<Use> Uses);

    /// <summary>What finished runs left: passing evidence, the paragraphs of their papers, and what their nodes called.</summary>
    /// <remarks>
    /// Integrations come from two places. The papers RunDocuments lists are
    /// briefs, reports, policies, questions and answers, and of those only a
    /// report's evidence names what a node called. The tool calls themselves
    /// are in each node's stream-*.jsonl beside them, which RunDocuments
    /// deliberately does not list; runs from before streams were kept have
    /// only the evidence.
    /// </remarks>
    private Read Finished()
    {
        var seen = new List<Seen>();
        var paragraphs = new List<Paragraph>();
        var uses = new List<Use>();

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

            // In the order they were written, so the evidence of one run reads
            // as the sequence it happened in.
            foreach (var document in documents
                .Where(one => one.Kind is "report" or "brief")
                .OrderBy(one => one.Written)
                .ThenBy(one => one.Name, StringComparer.Ordinal))
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

                paragraphs.AddRange(ParagraphsOf(text).Select(one => new Paragraph(run.RunId, run.Team, one, Fold(one))));

                if (document.Kind == "report" && ReportReader.Read(text) is { Succeeded: true } report)
                {
                    var passed = report.Value!.Evidence.Where(one => one.Result == EvidenceResult.Pass).ToList();

                    seen.AddRange(passed.Select(one => new Seen(run.RunId, run.Team, one)));
                    uses.AddRange(passed
                        .SelectMany(one => IntegrationsIn(one.Ref + " " + one.Note))
                        .Select(one => new Use(run.RunId, run.Team, one)));
                }
            }

            uses.AddRange(Called(run.Directory).Select(one => new Use(run.RunId, run.Team, one)));
        }

        return new Read(seen, paragraphs, uses);
    }

    /// <summary>What the nodes of a run called, from their streams.</summary>
    private static IEnumerable<string> Called(string directory)
    {
        var found = new List<string>();

        try
        {
            foreach (var stream in Directory.EnumerateFiles(directory, "stream-*.jsonl"))
            {
                foreach (var line in File.ReadLines(stream))
                {
                    if (NodeStream.Parse(line) is { Kind: "tool", Tool: { } tool } step)
                    {
                        found.AddRange(IntegrationsIn(tool + " " + step.Target));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return found;
    }

    /// <summary>The MCP servers and HTTP hosts a line names, as "mcp server" and "http host".</summary>
    /// <remarks>
    /// Loadout's own server is left out, because every node of every team
    /// talks to it, and so is this machine, which is not an integration.
    /// </remarks>
    private static IEnumerable<string> IntegrationsIn(string text)
    {
        foreach (Match server in McpTool().Matches(text))
        {
            var name = server.Groups[1].Value.ToLowerInvariant();

            if (name != "loadout")
            {
                yield return "mcp " + name;
            }
        }

        foreach (Match url in Url().Matches(text))
        {
            if (Uri.TryCreate(url.Value, UriKind.Absolute, out var uri) && !uri.IsLoopback && uri.Host.Length > 0)
            {
                yield return "http " + uri.Host.ToLowerInvariant();
            }
        }
    }

    /// <summary>Every paragraph of every string in a JSON paper, with its whitespace made single spaces.</summary>
    private static IEnumerable<string> ParagraphsOf(string json)
    {
        var strings = new List<string>();

        try
        {
            using var document = JsonDocument.Parse(json);
            Strings(document.RootElement, strings);
        }
        catch (JsonException)
        {
            return [];
        }

        return strings
            .SelectMany(one => BlankLine().Split(one))
            .Select(one => string.Join(' ', one.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(one => one.Length > 0);
    }

    private static void Strings(JsonElement element, List<string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                into.Add(element.GetString() ?? string.Empty);
                break;
            case JsonValueKind.Array:
                foreach (var one in element.EnumerateArray())
                {
                    Strings(one, into);
                }

                break;
            case JsonValueKind.Object:
                foreach (var one in element.EnumerateObject())
                {
                    Strings(one.Value, into);
                }

                break;
        }
    }

    /// <summary>A paragraph with its case and whitespace folded, so two copies of it compare equal.</summary>
    private static string Fold(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

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

    /// <summary>The fewest steps that make a workflow rather than a command.</summary>
    public const int FewestSteps = 3;

    /// <summary>The fewest words that make an instruction rather than a phrase.</summary>
    public const int FewestWords = 30;

    /// <summary>
    /// Rule 5: the same ordered run of command shapes passing in runs of two or
    /// more teams.
    /// </summary>
    /// <remarks>
    /// The longest run two teams share, not every three-step window of it, so
    /// one five-step workflow is one nomination rather than three. A command
    /// repeated back to back is one step: running the tests three times over is
    /// a retry, not a workflow.
    /// </remarks>
    private static IEnumerable<ToolNomination> Workflows(List<Seen> evidence)
    {
        var runs = evidence
            .Where(one => one.Evidence.Kind == EvidenceKind.Command)
            .GroupBy(one => one.Run, StringComparer.Ordinal)
            .Select(run => (Run: run.Key, run.First().Team, Steps: Collapsed([.. run.Select(one => Shape(one.Evidence.Ref)).Where(one => one.Length > 0)])))
            .Where(one => one.Steps.Count >= FewestSteps)
            .OrderBy(one => one.Run, StringComparer.Ordinal)
            .ToList();

        var shared = new HashSet<string>(StringComparer.Ordinal);

        for (var a = 0; a < runs.Count; a++)
        {
            for (var b = a + 1; b < runs.Count; b++)
            {
                if (!string.Equals(runs[a].Team, runs[b].Team, StringComparison.OrdinalIgnoreCase))
                {
                    shared.UnionWith(Common(runs[a].Steps, runs[b].Steps).Select(one => string.Join(" | ", one)));
                }
            }
        }

        foreach (var workflow in shared.Order(StringComparer.Ordinal))
        {
            var steps = workflow.Split(" | ");
            var where = runs.Where(one => Contains(one.Steps, steps)).ToList();
            var teams = where.Select(one => one.Team).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            yield return new ToolNomination(
                5,
                "5 " + workflow,
                $"Runs {steps.Length} steps in order: {string.Join(", then ", steps.Select(one => $"'{one}'"))}.",
                $"The same {steps.Length} steps passed in order in runs of {teams} teams: {string.Join(", ", where.Select(one => one.Run))}.",
                null,
                Capabilities: ["workflow"]);
        }
    }

    /// <summary>Steps with a command repeated back to back counted once.</summary>
    private static List<string> Collapsed(List<string> steps) =>
        [.. steps.Where((one, at) => at == 0 || !string.Equals(one, steps[at - 1], StringComparison.Ordinal))];

    /// <summary>Every run of steps two sequences share that cannot be made longer at either end.</summary>
    private static IEnumerable<string[]> Common(List<string> a, List<string> b)
    {
        // Longest common run ending at each pair of positions.
        var length = new int[a.Count + 1, b.Count + 1];

        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
            {
                length[i, j] = string.Equals(a[i - 1], b[j - 1], StringComparison.Ordinal) ? length[i - 1, j - 1] + 1 : 0;
            }
        }

        for (var i = 1; i <= a.Count; i++)
        {
            for (var j = 1; j <= b.Count; j++)
            {
                var ends = i == a.Count || j == b.Count || length[i + 1, j + 1] == 0;

                if (length[i, j] >= FewestSteps && ends)
                {
                    yield return [.. a.Skip(i - length[i, j]).Take(length[i, j])];
                }
            }
        }
    }

    private static bool Contains(List<string> steps, string[] run) =>
        Enumerable.Range(0, steps.Count - run.Length + 1)
            .Any(at => steps.Skip(at).Take(run.Length).SequenceEqual(run, StringComparer.Ordinal));

    /// <summary>
    /// Rule 6: the same instruction paragraph, of at least thirty words, in the
    /// briefs or reports of runs of two or more teams.
    /// </summary>
    /// <remarks>
    /// The paragraph is carried as it was first written rather than folded, so
    /// the secret screen sees it with its case intact, and the rest of the
    /// paper it came from is not carried at all: where it came from is the
    /// runs, never the task around it.
    /// </remarks>
    private static IEnumerable<ToolNomination> Prompts(List<Paragraph> paragraphs)
    {
        var repeated = paragraphs
            .Where(one => one.Folded.Split(' ').Length >= FewestWords)
            .GroupBy(one => one.Folded, StringComparer.Ordinal)
            .OrderBy(one => one.Key, StringComparer.Ordinal);

        foreach (var paragraph in repeated)
        {
            var teams = paragraph.Select(one => one.Team).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            if (teams < 2)
            {
                continue;
            }

            var runs = paragraph.Select(one => one.Run).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var first = paragraph.First().Text;
            var words = first.Split(' ');

            yield return new ToolNomination(
                6,
                "6 " + RemedyCeiling.Fingerprint(paragraph.Key),
                $"An instruction of {words.Length} words beginning '{string.Join(' ', words.Take(8))}...'.",
                $"The same instruction paragraph was handed to or written by nodes in runs of {teams} teams: {string.Join(", ", runs)}. "
                + "It is carried as the script.",
                first,
                Capabilities: ["prompt"]);
        }
    }

    /// <summary>Rule 7: the same MCP server or HTTP host used by nodes of two or more teams.</summary>
    private static IEnumerable<ToolNomination> Integrations(List<Use> uses)
    {
        foreach (var integration in uses.GroupBy(one => one.Integration, StringComparer.Ordinal).OrderBy(one => one.Key, StringComparer.Ordinal))
        {
            var teams = integration.Select(one => one.Team).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            if (teams < 2)
            {
                continue;
            }

            var runs = integration.Select(one => one.Run).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            var (sort, name) = (integration.Key[..integration.Key.IndexOf(' ', StringComparison.Ordinal)], integration.Key[(integration.Key.IndexOf(' ', StringComparison.Ordinal) + 1)..]);
            var what = sort == "mcp" ? $"the MCP server '{name}'" : $"the HTTP host '{name}'";

            yield return new ToolNomination(
                7,
                "7 " + integration.Key,
                $"Talks to {what}.",
                $"Nodes of {teams} teams used {what}: {string.Join(", ", runs)}.",
                null,
                Capabilities: ["integration", name]);
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

    [GeneratedRegex(@"\bmcp__([A-Za-z0-9-]+(?:_[A-Za-z0-9-]+)*)__", RegexOptions.None, 1000)]
    private static partial Regex McpTool();

    [GeneratedRegex(@"https?://[^\s'""`<>()]+", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex Url();

    [GeneratedRegex(@"\r?\n[ \t]*\r?\n", RegexOptions.None, 1000)]
    private static partial Regex BlankLine();
}
