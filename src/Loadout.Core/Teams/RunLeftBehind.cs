using System.Text.Json;

using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>One thing a node produced, and which node produced it.</summary>
/// <param name="Node">The node, as its own report named it.</param>
/// <param name="Kind">What sort of thing: a commit, a decision, a plan, a file.</param>
/// <param name="Ref">How to find it - a commit hash, a branch, a path, a verdict.</param>
/// <param name="Note">What the node said about it.</param>
public sealed record Delivered(string Node, string Kind, string Ref, string? Note);

/// <summary>One thing a node showed to say the work does what was asked.</summary>
/// <remarks>Not <c>Shown</c>: the terminal already has one of those.</remarks>
/// <param name="Node">The node that ran it.</param>
/// <param name="Kind">A command, a test, an observation, a review.</param>
/// <param name="Ref">What was run or looked at.</param>
/// <param name="Result">How it came out, in the contract's own spelling.</param>
/// <param name="Note">What the node said about it.</param>
public sealed record Proof(string Node, string Kind, string Ref, string Result, string? Note);

/// <summary>A decision the run stopped for, and how it went.</summary>
/// <param name="At">When.</param>
/// <param name="What">What was being decided.</param>
/// <param name="Outcome">What was decided.</param>
/// <param name="Detail">Whatever else the event carried, as words.</param>
public sealed record Decided(DateTimeOffset At, string What, string Outcome, string? Detail);

/// <summary>
/// What a run left behind: what it produced, what it showed for it, and what
/// was decided along the way.
/// </summary>
/// <param name="Delivered">Everything any node said it produced.</param>
/// <param name="Evidence">Everything any node offered as proof it works.</param>
/// <param name="Decisions">Every gate, permission and answer, in order.</param>
/// <param name="Unreadable">Reports that would not parse, by file name.</param>
public sealed record LeftBehind(
    IReadOnlyList<Delivered> Delivered,
    IReadOnlyList<Proof> Evidence,
    IReadOnlyList<Decided> Decisions,
    IReadOnlyList<string> Unreadable);

/// <summary>
/// Reads back what a run produced, from the reports and the journal it already
/// wrote.
/// </summary>
/// <remarks>
/// <para>
/// None of this is new bookkeeping. Every node hands back a report against
/// <c>report/1</c> listing what it produced and what it showed for it, and the
/// journal records every gate and permission as it happens. Across the runs on
/// the machine this was written against there were twenty-eight deliverables
/// and a hundred and eight pieces of evidence sitting on disk, and nothing
/// read any of it back - <c>team status</c> offered a node, a state, a count of
/// exchanges and a cost, while its own help text promised "what it left
/// behind".
/// </para>
/// <para>
/// A report that will not parse is named rather than skipped. A run whose
/// implementer's report is unreadable is a run that produced something nobody
/// can see, and that is worth saying out loud rather than quietly showing one
/// fewer line.
/// </para>
/// </remarks>
public static class RunLeftBehind
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>What the run in this directory left behind.</summary>
    /// <param name="directory">The run's own directory, as the journal names it.</param>
    /// <param name="events">The run's events, for the decisions.</param>
    public static LeftBehind In(string directory, IReadOnlyList<RunEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var delivered = new List<Delivered>();
        var shown = new List<Proof>();
        var unreadable = new List<string>();

        foreach (var file in Reports(directory))
        {
            Report? report;

            try
            {
                report = JsonSerializer.Deserialize<Report>(File.ReadAllText(file), Json);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                unreadable.Add(Path.GetFileName(file));
                continue;
            }

            if (report is null)
            {
                unreadable.Add(Path.GetFileName(file));
                continue;
            }

            foreach (var one in report.Deliverables ?? [])
            {
                delivered.Add(new Delivered(
                    report.Node, Spelt(one.Kind), one.Ref, Said(one.Note)));
            }

            foreach (var one in report.Evidence ?? [])
            {
                shown.Add(new Proof(
                    report.Node, Spelt(one.Kind), one.Ref, Spelt(one.Result), Said(one.Note)));
            }
        }

        return new LeftBehind(delivered, shown, Decisions(events), unreadable);
    }

    /// <summary>
    /// The report files in a run's directory, oldest first.
    /// </summary>
    /// <remarks>
    /// The lead's is called <c>final-report.json</c> and everybody else's is
    /// <c>report-&lt;node&gt;-&lt;round&gt;.json</c>. Which node wrote which is
    /// taken from inside the file rather than from its name, because a node
    /// called <c>implementer/1</c> is a file called <c>implementer-1</c> and
    /// turning one back into the other is a guess.
    /// </remarks>
    private static IEnumerable<string> Reports(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*report*.json")
                .Where(one => Path.GetFileName(one).StartsWith("report-", StringComparison.Ordinal)
                    || string.Equals(Path.GetFileName(one), "final-report.json", StringComparison.Ordinal))
                .OrderBy(one => File.GetLastWriteTimeUtc(one))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>
    /// Every point the run stopped and something was decided, in order.
    /// </summary>
    /// <remarks>
    /// A merge is here as well as the gate that allowed it, because "allowed"
    /// and "done" are different facts and a run can be told yes and still fail
    /// to merge.
    /// </remarks>
    private static IReadOnlyList<Decided> Decisions(IReadOnlyList<RunEvent> events)
    {
        var out_ = new List<Decided>();

        foreach (var one in events)
        {
            switch (one.Kind)
            {
                case "gate.decided":
                    out_.Add(new Decided(one.At, one.Word("gate") ?? "a gate",
                        one.Yes("allowed") ? "allowed" : "refused",
                        one.Word("branch")));
                    break;

                case "gate.refused":
                    out_.Add(new Decided(one.At, one.Word("gate") ?? "a gate", "refused",
                        one.Word("decisions")));
                    break;

                case "merge.done":
                    out_.Add(new Decided(one.At,
                        $"merge {one.Word("branch")} into {one.Word("target")}", "merged",
                        one.Yes("FastForward") ? "fast forward" : null));
                    break;

                case "permission.asked":
                    out_.Add(new Decided(one.At,
                        $"{one.Word("node")} wanted {one.Word("tool")} {one.Word("target")}".Trim(),
                        one.Yes("allowed") ? "allowed" : "refused",
                        one.Word("reason")));
                    break;

                case "node.answered":
                    out_.Add(new Decided(one.At, $"a question from {one.Node ?? "a node"}",
                        one.Yes("allowed") ? "answered yes" : "answered",
                        one.Word("Tool")));
                    break;

                case "request.refused":
                    out_.Add(new Decided(one.At, $"{one.Word("Node")} was asked for",
                        "refused", one.Word("reason")));
                    break;

                default:
                    break;
            }
        }

        return out_;
    }


    /// <summary>An enum in the contract's own spelling rather than C#'s.</summary>
    private static string Spelt<T>(T value)
        where T : struct, Enum =>
        JsonSerializer.Serialize(value, Json).Trim('"');

    /// <summary>A note, or nothing where it was only whitespace.</summary>
    private static string? Said(string? note) =>
        note is { Length: > 0 } && note.Trim().Length > 0 ? note.Trim() : null;
}
