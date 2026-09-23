using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>One thing a node did, as its own stream recorded it.</summary>
/// <param name="At">When.</param>
/// <param name="Kind">said, tool, answered, refused, started or ended.</param>
/// <param name="Tool">The tool it called, where it called one.</param>
/// <param name="Target">The one thing that call was pointed at.</param>
/// <param name="Text">What it said, or what came back, cut to a readable length.</param>
/// <param name="Sub">Whether a subagent inside the node did this rather than the node itself.</param>
public sealed record NodeStep(
    DateTimeOffset At,
    string Kind,
    string? Tool = null,
    string? Target = null,
    string? Text = null,
    bool Sub = false);

/// <summary>
/// Everything a node did, rather than the few lines the journal kept.
/// </summary>
/// <remarks>
/// <para>
/// The journal is deliberately thin: one line every few seconds, repeats
/// dropped, so that reading a run does not mean reading everything. That is
/// right for watching and wrong for working out what went on, and the two
/// wants are different enough to deserve different files.
/// </para>
/// <para>
/// In the launcher's own vocabulary rather than the agent's. Every adapter
/// already translates its agent's stream into these, so nothing here reads
/// anybody's JSON and nothing here would have to change for a second agent -
/// and a node's stream never carries whatever its agent felt like printing.
/// </para>
/// </remarks>
public static class NodeStream
{
    /// <summary>How much of any one thing said is kept.</summary>
    /// <remarks>
    /// A node that reads a large file and quotes it back would otherwise put
    /// the whole file in here, several times over. The first few hundred
    /// characters are what says which step this was; the rest is the file,
    /// which is on disk already.
    /// </remarks>
    public const int MostPerStep = 600;

    /// <summary>How many steps are handed to a page at once.</summary>
    /// <remarks>
    /// The last of them rather than the first. A trajectory is read from the
    /// end, because the interesting part of a node that went wrong is where
    /// it stopped.
    /// </remarks>
    public const int MostSteps = 2000;

    /// <summary>What a node's stream is called.</summary>
    /// <remarks>
    /// Not .json, so the papers do not list it: a trajectory is not a document
    /// the run wrote for anybody, and it is the one file here that can reach
    /// tens of megabytes.
    /// </remarks>
    public static string FileFor(string node) =>
        $"stream-{NodePermissions.FileSafe(node)}.jsonl";

    /// <summary>Where a node's stream is kept.</summary>
    public static string PathFor(string directory, string node) =>
        Path.Combine(directory, FileFor(node));

    /// <summary>One step, as the line that goes in the file.</summary>
    /// <remarks>
    /// Written by hand rather than by reflection so the names in the file are
    /// a decision rather than whatever a record's properties are called. A
    /// file somebody may read in a year should not rename itself because a
    /// property did.
    /// </remarks>
    public static string Line(NodeStep step)
    {
        ArgumentNullException.ThrowIfNull(step);

        return JsonSerializer.Serialize(new
        {
            at = step.At,
            kind = step.Kind,
            tool = step.Tool,
            target = step.Target,
            text = Cut(step.Text),
            sub = step.Sub,
        });
    }

    /// <summary>As much of something said as is worth keeping.</summary>
    public static string? Cut(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var tidied = text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

        return tidied.Length <= MostPerStep
            ? tidied
            : tidied[..MostPerStep] + "…";
    }

    /// <summary>
    /// A node's trajectory, most recent last, with anything credential-shaped
    /// taken out.
    /// </summary>
    /// <returns>
    /// The steps, and on the result's own terms nothing about how many were
    /// skipped - <see cref="Count"/> answers that, because a caller asking
    /// "what is new" needs to know what the total became.
    /// </returns>
    /// <remarks>
    /// A half-written last line is ordinary while a node is still going, so a
    /// line that will not parse is skipped rather than failing the read. The
    /// same is true of the journal and for the same reason.
    /// </remarks>
    /// <param name="directory">The run's own directory.</param>
    /// <param name="node">Whose stream.</param>
    /// <param name="most">How many of the most recent steps at once.</param>
    /// <param name="after">
    /// How many steps the caller already has. A page watching a live node asks
    /// again every few seconds, and sending it the same two thousand steps each
    /// time is most of a megabyte a minute to say nothing.
    /// </param>
    public static OperationResult<IReadOnlyList<NodeStep>> Read(
        string directory,
        string node,
        int most = MostSteps,
        int after = 0)
    {
        if (string.IsNullOrWhiteSpace(node))
        {
            return OperationResult<IReadOnlyList<NodeStep>>.Fail(
                "No node was named.", ExitCode.InvalidArguments);
        }

        var path = PathFor(directory, node);

        if (!File.Exists(path))
        {
            return OperationResult<IReadOnlyList<NodeStep>>.Fail(
                $"{node} recorded no stream. Runs from before this was written down have none.",
                ExitCode.ProjectNotFound);
        }

        List<string> lines;

        try
        {
            // Shared, because the node may still be writing to it.
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(file);

            lines = [];

            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<NodeStep>>.Fail(
                $"{node}'s stream could not be read: {ex.Message}", ExitCode.GeneralFailure);
        }

        // What is new, if the caller said what it had; otherwise the last
        // of them, because a trajectory is read from the end - the interesting
        // part of a node that went wrong is where it stopped.
        //
        // A count larger than the file means the file was replaced or the
        // caller is confused, and starting again is the only honest answer.
        var fresh = after > 0 && after <= lines.Count ? lines[after..] : lines;
        var wanted = fresh.Count > most ? fresh[^most..] : fresh;
        var steps = new List<NodeStep>(wanted.Count);

        foreach (var line in wanted)
        {
            if (Parse(line) is { } step)
            {
                steps.Add(step);
            }
        }

        try
        {
            return OperationResult<IReadOnlyList<NodeStep>>.Ok(
                [.. steps.Select(step => step with
                {
                    Target = SecretRedactor.Redact(step.Target),
                    Text = SecretRedactor.Redact(step.Text),
                })]);
        }
        catch (RegexMatchTimeoutException)
        {
            // The rule the papers and the patch both follow: what cannot be
            // checked is not shown.
            return OperationResult<IReadOnlyList<NodeStep>>.Fail(
                $"{node}'s stream could not be checked for credentials in a reasonable time, "
                + "so it is not being shown.",
                ExitCode.PolicyViolation);
        }
    }

    /// <summary>How many steps a node's stream holds, without reading them.</summary>
    /// <remarks>
    /// Counted by line rather than parsed, because the caller asking is asking
    /// "is there anything new", and a line that will not parse still moves the
    /// answer along.
    /// </remarks>
    public static int Count(string directory, string node)
    {
        var path = PathFor(directory, node);

        if (!File.Exists(path))
        {
            return 0;
        }

        try
        {
            using var file = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            using var reader = new StreamReader(file);

            var lines = 0;

            while (reader.ReadLine() is { } line)
            {
                if (line.Length > 0)
                {
                    lines++;
                }
            }

            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>The longest gap between two steps that is counted as work.</summary>
    /// <remarks>
    /// A node waiting on a person, or on a tool that has hung, leaves a gap of
    /// minutes or hours. Counting that as thinking would say a node spent two
    /// hours thinking when it spent two hours waiting, so anything longer is
    /// counted as neither - the same trick Tokdash uses for the same reason.
    /// </remarks>
    public static readonly TimeSpan Longest = TimeSpan.FromMinutes(2);

    /// <summary>Where a node's time went, as far as its own stream can say.</summary>
    /// <param name="Thinking">Time that ended in the node deciding to do something.</param>
    /// <param name="Tools">Time that ended in a tool answering.</param>
    /// <param name="Writing">Time that ended in the node saying something.</param>
    /// <param name="Idle">Gaps too long to be any of those.</param>
    public sealed record Spending(
        TimeSpan Thinking,
        TimeSpan Tools,
        TimeSpan Writing,
        TimeSpan Idle)
    {
        /// <summary>Everything that was accounted for as work.</summary>
        public TimeSpan Working => Thinking + Tools + Writing;

        /// <summary>Whether there is anything worth showing.</summary>
        public bool Any => Working > TimeSpan.Zero || Idle > TimeSpan.Zero;
    }

    /// <summary>
    /// Where a node's time went, from the gaps between the things it did.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each gap is put down to whatever ended it: a gap ending in a tool call
    /// is the model deciding to make it, a gap ending in a tool's answer is
    /// that tool running, and a gap ending in the node saying something is the
    /// model writing it.
    /// </para>
    /// <para>
    /// This is an account of when things arrived rather than of what a model
    /// was doing, and the difference is worth stating rather than glossing:
    /// "thinking" here includes the time the answer took to stream, because
    /// nothing outside the model can tell those apart. It answers "where did
    /// the twenty minutes go", which is the question, and not "how long did it
    /// reason for", which nothing here can answer.
    /// </para>
    /// </remarks>
    public static Spending Spent(IReadOnlyList<NodeStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var thinking = TimeSpan.Zero;
        var tools = TimeSpan.Zero;
        var writing = TimeSpan.Zero;
        var idle = TimeSpan.Zero;

        for (var i = 1; i < steps.Count; i++)
        {
            var gap = steps[i].At - steps[i - 1].At;

            if (gap <= TimeSpan.Zero)
            {
                continue;
            }

            if (gap > Longest)
            {
                idle += gap;

                continue;
            }

            switch (steps[i].Kind)
            {
                case "tool":
                    thinking += gap;
                    break;

                case "answered":
                case "failed":
                case "refused":
                    tools += gap;
                    break;

                case "said":
                    writing += gap;
                    break;

                default:
                    idle += gap;
                    break;
            }
        }

        return new Spending(thinking, tools, writing, idle);
    }

    /// <summary>One line back into a step, or null where it will not parse.</summary>
    public static NodeStep? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new NodeStep(
                root.TryGetProperty("at", out var at) && at.TryGetDateTimeOffset(out var when)
                    ? when
                    : DateTimeOffset.MinValue,
                Text(root, "kind") ?? "other",
                Text(root, "tool"),
                Text(root, "target"),
                Text(root, "text"),
                root.TryGetProperty("sub", out var sub) && sub.ValueKind == JsonValueKind.True);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var found) && found.ValueKind == JsonValueKind.String
            ? found.GetString()
            : null;
}
