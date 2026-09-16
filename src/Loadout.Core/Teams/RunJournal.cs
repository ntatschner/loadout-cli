using System.Globalization;
using System.Text.Json;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One thing that happened during a run, as the journal recorded it.</summary>
/// <param name="At">When.</param>
/// <param name="Node">Which node, or null for the run itself.</param>
/// <param name="Kind">What happened: node.launched, node.turn, report.checked, merge.done and the rest.</param>
/// <param name="Data">Whatever that kind carries, still as JSON because each kind carries its own shape.</param>
public sealed record RunEvent(DateTimeOffset At, string? Node, string Kind, JsonElement Data)
{
    /// <summary>A string from the event's data, or null.</summary>
    public string? Text(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>A number from the event's data, or null.</summary>
    public decimal? Number(string name) =>
        Data.ValueKind == JsonValueKind.Object && Data.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
            ? number
            : null;
}

/// <summary>Where a node got to.</summary>
/// <param name="Node">The node, as the run named it.</param>
/// <param name="Role">The role it played.</param>
/// <param name="State">working, done, blocked, failed, needs-decision, or ended.</param>
/// <param name="Turns">Exchanges with the model, summed across its turns.</param>
/// <param name="CostUsd">What it spent.</param>
/// <param name="LastSeen">When it last said anything.</param>
/// <param name="Branch">The branch it worked on, when it had one of its own.</param>
/// <param name="Decision">The one word it handed back, when its role hands one back.</param>
/// <param name="Denials">Tool calls its own permission rules refused.</param>
/// <param name="Started">When it was launched, for how long it has been at it.</param>
/// <param name="Doing">
/// The last thing it said it was doing. Emptied once it reports, because a
/// node that has answered is not still doing the last thing anybody saw, and
/// leaving it there is how a finished run looks busy.
/// </param>
public sealed record RunNode(
    string Node,
    string Role,
    string State,
    int Turns,
    decimal CostUsd,
    DateTimeOffset LastSeen,
    string? Branch = null,
    string? Decision = null,
    int Denials = 0,
    DateTimeOffset? Started = null,
    string? Doing = null)
{
    /// <summary>How long it has been going, or how long it took.</summary>
    public TimeSpan? Took =>
        Started is { } began && LastSeen > began ? LastSeen - began : null;
}

/// <summary>A run, read back from what it wrote down.</summary>
public sealed record RunSummary(
    string RunId,
    string Directory,
    string Team,
    string Goal,
    string Autonomy,
    DateTimeOffset Started,
    DateTimeOffset? Finished,
    string? Ended,
    decimal CostUsd,
    int Rounds,
    IReadOnlyList<RunNode> Nodes,
    IReadOnlyList<string> Merged,
    IReadOnlyList<string> Branches,
    int RoundLimit = 0)
{
    /// <summary>Whether the run is still going, as far as its journal knows.</summary>
    /// <remarks>
    /// A run whose coordinator was killed says nothing more and still reads
    /// as running. What settles it is whether anything has been written
    /// lately, which is the caller's to judge against the clock.
    /// </remarks>
    public bool Running => Finished is null;

    /// <summary>When the run last wrote anything at all.</summary>
    public DateTimeOffset LastSeen =>
        Nodes.Count == 0 ? Started : Nodes.Max(node => node.LastSeen) > Started ? Nodes.Max(node => node.LastSeen) : Started;

    /// <summary>How long the run has been going, or how long it took.</summary>
    public TimeSpan Elapsed => (Finished ?? LastSeen) - Started;

    /// <summary>
    /// The longest this run should still take, or null when there is nothing
    /// to base it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A ceiling, not a prediction, and the difference matters enough to be
    /// the reason this is shaped the way it is. What a lead will ask for next
    /// is not known to anybody, so the only defensible arithmetic is: this
    /// run has taken this long for the rounds it has used, and it may use its
    /// remaining rounds. Whoever shows this must say which of the two it is.
    /// </para>
    /// <para>
    /// Null until a round has finished, because an estimate from no samples
    /// is a number with nothing behind it, and a finished run has nothing
    /// left to estimate.
    /// </para>
    /// </remarks>
    public TimeSpan? AtMostRemaining
    {
        get
        {
            if (!Running || Rounds <= 0 || RoundLimit <= Rounds)
            {
                return null;
            }

            return Elapsed / Rounds * (RoundLimit - Rounds);
        }
    }
}

/// <summary>Reads what a run wrote down.</summary>
public interface IRunJournal
{
    /// <summary>Runs on this machine, newest first.</summary>
    IReadOnlyList<string> List(int limit = 20);

    /// <summary>Every event of one run, in order. A line that will not parse is left out rather than throwing.</summary>
    OperationResult<IReadOnlyList<RunEvent>> Read(string runId);

    /// <summary>One run, folded into where everything got to.</summary>
    OperationResult<RunSummary> Summarise(string runId);

    /// <summary>The directory a run wrote into.</summary>
    string DirectoryOf(string runId);
}

/// <inheritdoc />
public sealed class RunJournal : IRunJournal
{
    private readonly Platform.Abstractions.IPlatformPaths _paths;

    public RunJournal(Platform.Abstractions.IPlatformPaths paths) => _paths = paths;

    private string Root => Path.Combine(_paths.Paths.State, "teams", "runs");

    /// <inheritdoc />
    public string DirectoryOf(string runId) => Path.Combine(Root, runId);

    /// <inheritdoc />
    public IReadOnlyList<string> List(int limit = 20)
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        // The identifier starts with the time it began, so its own name
        // sorts them and nothing has to be opened to order them.
        return Directory.EnumerateDirectories(Root)
            .Select(Path.GetFileName)
            .Where(name => name is { Length: > 0 })
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .Take(limit)
            .ToList()!;
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<RunEvent>> Read(string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var path = Path.Combine(DirectoryOf(runId), "journal.jsonl");

        if (!File.Exists(path))
        {
            return OperationResult<IReadOnlyList<RunEvent>>.Fail(
                $"No run named '{runId}' on this machine.", Models.ExitCode.ProjectNotFound);
        }

        string[] lines;

        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<RunEvent>>.Fail($"The run's journal could not be read: {ex.Message}");
        }

        var events = new List<RunEvent>();

        foreach (var line in lines)
        {
            if (Parse(line) is { } parsed)
            {
                events.Add(parsed);
            }
        }

        return OperationResult<IReadOnlyList<RunEvent>>.Ok(events);
    }

    /// <summary>
    /// One line as an event, or null.
    /// </summary>
    /// <remarks>
    /// A half-written last line is the ordinary case while a run is going:
    /// the journal is appended to as things happen, and a reader that threw
    /// on it could not be used to watch one.
    /// </remarks>
    public static RunEvent? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var at = root.TryGetProperty("at", out var when) && when.TryGetDateTimeOffset(out var moment)
                ? moment
                : DateTimeOffset.MinValue;

            var node = root.TryGetProperty("node", out var who) && who.ValueKind == JsonValueKind.String
                ? who.GetString()
                : null;

            var data = root.TryGetProperty("data", out var carried)
                ? carried.Clone()
                : default;

            return new RunEvent(at, node, kind.GetString()!, data);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public OperationResult<RunSummary> Summarise(string runId)
    {
        var read = Read(runId);

        if (read.Failed)
        {
            return OperationResult<RunSummary>.Fail(read.Error!, read.ExitCode);
        }

        return OperationResult<RunSummary>.Ok(Fold(runId, DirectoryOf(runId), read.Value!));
    }

    /// <summary>Folds a run's events into where everything got to.</summary>
    public static RunSummary Fold(string runId, string directory, IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var team = string.Empty;
        var goal = string.Empty;
        var autonomy = string.Empty;
        var started = events.Count > 0 ? events[0].At : DateTimeOffset.MinValue;
        DateTimeOffset? finished = null;
        string? ended = null;
        var cost = 0m;
        var rounds = 0;
        var limit = 0;
        var merged = new List<string>();

        // Insertion order, because that is the order the run briefed them
        // and the order somebody reading it will expect.
        var nodes = new Dictionary<string, RunNode>(StringComparer.Ordinal);

        void Set(string name, Func<RunNode, RunNode> change)
        {
            nodes[name] = change(nodes.TryGetValue(name, out var existing)
                ? existing
                : new RunNode(name, "?", "working", 0, 0m, DateTimeOffset.MinValue));
        }

        foreach (var entry in events)
        {
            switch (entry.Kind)
            {
                case "run.started":
                    team = entry.Text("team") ?? team;
                    goal = entry.Text("goal") ?? goal;
                    autonomy = entry.Text("autonomy") ?? autonomy;
                    limit = (int)(entry.Number("rounds") ?? limit);
                    started = entry.At;
                    break;

                case "round.started":
                    // A run still going has no ending to count rounds from,
                    // and how far through it is is the thing somebody
                    // watching most wants.
                    rounds = (int)(entry.Number("round") ?? rounds + 1);
                    limit = (int)(entry.Number("of") ?? limit);
                    break;

                case "run.finished":
                    finished = entry.At;
                    ended = entry.Text("ended");
                    cost = entry.Number("cost") ?? cost;
                    rounds = (int)(entry.Number("rounds") ?? rounds);

                    if (entry.Data.ValueKind == JsonValueKind.Object
                        && entry.Data.TryGetProperty("merged", out var landed)
                        && landed.ValueKind == JsonValueKind.Array)
                    {
                        // The ending lists what merged and so does each
                        // merge as it happens, and a run read after it
                        // finished has both.
                        foreach (var branch in landed.EnumerateArray()
                            .Where(b => b.ValueKind == JsonValueKind.String)
                            .Select(b => b.GetString()!)
                            .Where(branch => !merged.Contains(branch, StringComparer.Ordinal)))
                        {
                            merged.Add(branch);
                        }
                    }

                    break;

                case "node.launched" when entry.Node is { Length: > 0 } launched:
                    Set(launched, node => node with
                    {
                        Role = entry.Text("role") ?? node.Role,
                        Branch = entry.Text("worktree") ?? node.Branch,
                        State = "working",
                        LastSeen = entry.At,
                        Started = node.Started ?? entry.At,
                    });

                    break;

                case "node.doing" when entry.Node is { Length: > 0 } busy:
                    Set(busy, node => node with { Doing = entry.Text("doing"), LastSeen = entry.At });
                    break;

                case "node.turn" when entry.Node is { Length: > 0 } turned:
                    Set(turned, node => node with
                    {
                        Turns = node.Turns + (int)(entry.Number("turns") ?? 0),
                        CostUsd = node.CostUsd + (entry.Number("cost") ?? 0m),
                        Denials = node.Denials + (int)(entry.Number("denials") ?? 0),
                        LastSeen = entry.At,
                    });

                    break;

                case "report.checked" when entry.Node is { Length: > 0 } reported:
                    Set(reported, node => node with
                    {
                        State = entry.Text("status") ?? node.State,
                        LastSeen = entry.At,
                        Doing = null,
                    });

                    break;

                case "node.ended" when entry.Node is { Length: > 0 } finishedNode:
                    Set(finishedNode, node => node with { LastSeen = entry.At, Doing = null });
                    break;

                case "merge.done" when entry.Text("branch") is { Length: > 0 } branch:
                    if (!merged.Contains(branch, StringComparer.Ordinal))
                    {
                        merged.Add(branch);
                    }

                    break;
            }
        }

        // The run's own cost is what it wrote at the end; while it is still
        // going, the nodes' own figures are the best there is.
        var running = nodes.Values.Sum(node => node.CostUsd);

        return new RunSummary(
            runId,
            directory,
            team,
            goal,
            autonomy,
            started,
            finished,
            ended,
            cost > 0m ? cost : running,
            rounds,
            nodes.Values.ToList(),
            merged,
            nodes.Values.Where(node => node.Branch is { Length: > 0 }).Select(node => node.Branch!).ToList(),
            limit);
    }

    /// <summary>One event as a line somebody can read.</summary>
    public static string Describe(RunEvent entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var who = entry.Node is { Length: > 0 } node ? node : "run";

        var what = entry.Kind switch
        {
            "run.started" => $"started {entry.Text("team")}, {entry.Text("autonomy")}: {entry.Text("goal")}",
            "run.finished" => $"finished: {entry.Text("ended")}",
            "round.started" => $"round {entry.Number("round")} of {entry.Number("of")}",
            "node.doing" => entry.Text("doing") ?? "working",
            "permission.asked" => (entry.Data.TryGetProperty("allowed", out var yes)
                    && yes.ValueKind == JsonValueKind.True ? "allowed " : "refused ")
                + entry.Text("tool")
                + (entry.Text("target") is { Length: > 0 } at ? $" {at}" : string.Empty),
            "node.launched" => $"launched as {entry.Text("role")}"
                + (entry.Text("worktree") is { Length: > 0 } tree ? $" on {tree}" : string.Empty),
            "node.turn" => $"turn {entry.Number("attempt")}: {entry.Number("turns")} exchange(s), "
                + $"${entry.Number("cost"):0.00}"
                + (entry.Number("denials") is > 0 ? $", {entry.Number("denials")} denial(s)" : string.Empty),
            "report.checked" => $"reported {entry.Text("status")}, {entry.Text("outcome")}",
            "report.unreadable" => $"gave no report: {entry.Text("Error")}",
            "node.ended" => $"ended, exit {entry.Number("exit")}",
            "node.failed" => $"could not start: {entry.Text("error")}",
            "request.refused" => $"asked for a node it may not: {entry.Text("reason")}",
            "gate.reminded" => "told it finished without the merge gate",
            "gate.refused" => "the merge gate was not satisfied",
            "gate.opened" => $"merge gate open for {entry.Text("branch")}",
            "gate.decided" => $"merge {(entry.Data.TryGetProperty("allowed", out var a) && a.ValueKind == JsonValueKind.True ? "allowed" : "refused")}",
            "merge.done" => $"merged {entry.Text("branch")} into {entry.Text("target")}",
            "merge.conflicted" => $"{entry.Text("branch")} conflicted",
            "merge.failed" => $"{entry.Text("branch")} could not be merged: {entry.Text("error")}",
            "worktree.tidied" => $"cleared away {entry.Text("branch")}",
            "decision" => $"decided: {entry.Text("question")} -> {entry.Text("answer")}",
            _ => entry.Kind,
        };

        return $"{entry.At.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  {who,-16} {what}";
    }
}
