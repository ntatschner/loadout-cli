using System.Text;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>A group of facts bound for one memory topic.</summary>
/// <param name="Name">Topic name, taken from the heading the facts sat under.</param>
/// <param name="Description">One line saying where the topic came from.</param>
/// <param name="Kind">Which sort of memory this is, inferred from the heading.</param>
/// <param name="Facts">The lines themselves, verbatim.</param>
public sealed record CompressionTopic(
    string Name,
    string Description,
    MemoryKind Kind,
    IReadOnlyList<string> Facts);

/// <summary>What compressing an instruction file would do, or did.</summary>
/// <param name="SourcePath">The instruction file read.</param>
/// <param name="Topics">Topics that would be written.</param>
/// <param name="BytesBefore">Size of the always-loaded file as it stands.</param>
/// <param name="BytesAfter">Size it would be left at.</param>
/// <param name="Considered">How many list items were examined.</param>
/// <param name="Rejected">Why the rest were left where they are, by verdict.</param>
/// <param name="Withheld">
/// Credential-shaped lines that were deliberately not moved, counted by the
/// name of the pattern that matched. Names only, never the text: reprinting a
/// suspected secret to report it is the one thing a screening step must not do.
/// </param>
/// <param name="Applied">False when nothing was written.</param>
public sealed record CompressionPlan(
    string SourcePath,
    IReadOnlyList<CompressionTopic> Topics,
    long BytesBefore,
    long BytesAfter,
    int Considered,
    IReadOnlyDictionary<FactVerdict, int> Rejected,
    IReadOnlyDictionary<string, int> Withheld,
    bool Applied)
{
    /// <summary>Facts across every topic, which is the number worth reporting.</summary>
    public int Facts => Topics.Sum(t => t.Facts.Count);

    /// <summary>What the session stops paying for on every launch.</summary>
    public long BytesSaved => Math.Max(0, BytesBefore - BytesAfter);
}

/// <summary>
/// Moves durable facts out of an always-loaded instruction file and into the
/// memory store.
/// <para>
/// The context compiler inlines instructions in full but memory only by its
/// index, so a standing fact costs a session its whole length every launch
/// while it lives in instructions, and costs one index line once it lives in
/// memory. That difference is the entire point: this does not delete anything,
/// it moves what a session rarely needs out of what a session always reads.
/// </para>
/// <para>
/// Two rules keep it honest, both borrowed from <see cref="InstructionSplitter"/>
/// because they are what make an automatic rewrite trustworthy. Content is
/// moved <b>verbatim and never reworded</b> — no model summarises anything, so
/// the result cannot say something the source did not. And nothing is removed
/// from the source until it has been read back out of the memory store, so a
/// failed write costs nothing rather than losing the only copy.
/// </para>
/// <para>
/// Every candidate is screened for credentials before it is grouped. The memory
/// store screens too and refuses a whole topic on one bad line, which is right
/// for a direct write and wrong here: a single credential-shaped URL in a large
/// instruction file would otherwise block all hundred-odd good facts and give
/// no way forward. Screening first means the offending line simply stays where
/// it already is, disclosed no further than it already was.
/// </para>
/// <para>
/// Only list items are considered. A bullet is a self-contained claim that can
/// be lifted without leaving a hole; a paragraph usually cannot, and pulling
/// sentences out of prose is how an automatic tool turns a readable document
/// into a confusing one.
/// </para>
/// </summary>
public sealed class MemoryCompressor
{
    /// <summary>
    /// Below this a topic is not worth creating. A memory file holding one
    /// fact costs an index line to save a bullet, which is not a saving.
    /// </summary>
    public const int MinimumFactsPerTopic = 2;

    private readonly IMemoryService _memory;

    public MemoryCompressor(IMemoryService memory) => _memory = memory;

    /// <summary>Works out what would move, without writing anything.</summary>
    public Task<OperationResult<CompressionPlan>> PlanAsync(
        string sourcePath,
        int minimumFactsPerTopic = MinimumFactsPerTopic,
        CancellationToken ct = default) =>
        RunAsync(sourcePath, null, null, minimumFactsPerTopic, apply: false, ct);

    /// <summary>
    /// Writes the topics, verifies them, and only then shortens the source.
    /// </summary>
    public Task<OperationResult<CompressionPlan>> ApplyAsync(
        string sourcePath,
        string workspaceRoot,
        string slug,
        int minimumFactsPerTopic = MinimumFactsPerTopic,
        CancellationToken ct = default) =>
        RunAsync(sourcePath, workspaceRoot, slug, minimumFactsPerTopic, apply: true, ct);

    private async Task<OperationResult<CompressionPlan>> RunAsync(
        string sourcePath,
        string? workspaceRoot,
        string? slug,
        int minimumFactsPerTopic,
        bool apply,
        CancellationToken ct)
    {
        if (!File.Exists(sourcePath))
        {
            return OperationResult<CompressionPlan>.Fail(
                $"There is no instruction file at '{sourcePath}'.", ExitCode.ProjectNotFound);
        }

        string[] lines;

        try
        {
            lines = await File.ReadAllLinesAsync(sourcePath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<CompressionPlan>.Fail(
                $"Could not read {sourcePath}: {ex.Message}");
        }

        var before = new FileInfo(sourcePath).Length;

        var (topics, taken, considered, rejected, withheld) =
            Gather(lines, Path.GetFileNameWithoutExtension(sourcePath), minimumFactsPerTopic);

        // Removing the chosen lines is what shrinks the file, so the projected
        // size is measured from the text that would actually be left rather
        // than estimated from what was taken.
        var remaining = Rebuild(lines, taken);
        var after = Encoding.UTF8.GetByteCount(remaining);

        var plan = new CompressionPlan(
            sourcePath, topics, before, after, considered, rejected, withheld, Applied: false);

        if (!apply || topics.Count == 0)
        {
            return OperationResult<CompressionPlan>.Ok(plan);
        }

        var written = await WriteAndVerifyAsync(topics, workspaceRoot!, slug!, ct)
            .ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<CompressionPlan>.Fail(written.Error!, written.ExitCode);
        }

        try
        {
            await File.WriteAllTextAsync(sourcePath, remaining, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The facts are safely in memory by this point, so the worst case
            // is a duplicate rather than a loss. Say so, because a silent
            // failure here would leave the file unchanged and look like the
            // whole thing did nothing.
            return OperationResult<CompressionPlan>.Fail(
                $"The facts were written to memory, but {sourcePath} could not be shortened: "
                + $"{ex.Message}. The instructions still hold their own copy.");
        }

        return OperationResult<CompressionPlan>.Ok(plan with { Applied = true, BytesAfter = after });
    }

    /// <summary>
    /// Writes each topic and reads it straight back.
    /// <para>
    /// The read-back is the safety rule: nothing may be removed from the
    /// instruction file on the strength of a write that only claimed to
    /// succeed.
    /// </para>
    /// </summary>
    private async Task<OperationResult> WriteAndVerifyAsync(
        IReadOnlyList<CompressionTopic> topics,
        string workspaceRoot,
        string slug,
        CancellationToken ct)
    {
        foreach (var topic in topics)
        {
            // Compression is not the case the similarity check exists for. It
            // moves facts somebody already wrote, in bulk, from a preview they
            // have already seen and approved; every topic it creates is grouped
            // by the heading its facts sat under, so of course they resemble
            // each other. Stopping here would ask a question about a decision
            // already made.
            var write = await _memory
                .WriteAsync(
                    workspaceRoot,
                    slug,
                    topic.Name,
                    topic.Description,
                    topic.Kind,
                    topic.Facts,
                    acknowledgedSimilar: true,
                    ct)
                .ConfigureAwait(false);

            if (write.Failed)
            {
                return OperationResult.Fail(write.Error!, write.ExitCode);
            }

            var stored = write.Value!.Facts;

            var missing = topic.Facts
                .Where(fact => !stored.Contains(fact, StringComparer.Ordinal))
                .ToList();

            if (missing.Count > 0)
            {
                return OperationResult.Fail(
                    $"Memory topic '{topic.Name}' was written but {missing.Count} fact(s) did not "
                    + "read back from it, so the instructions were left untouched.");
            }
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Walks the document, grouping durable list items under the heading they
    /// appear beneath.
    /// </summary>
    private static (
        List<CompressionTopic> Topics,
        HashSet<int> Taken,
        int Considered,
        Dictionary<FactVerdict, int> Rejected,
        Dictionary<string, int> Withheld)
        Gather(string[] lines, string fallbackName, int minimumFactsPerTopic)
    {
        var groups = new List<(string Heading, List<(int Index, string Text)> Items)>();
        var rejected = new Dictionary<FactVerdict, int>();
        var withheld = new Dictionary<string, int>(StringComparer.Ordinal);

        var heading = fallbackName;
        var considered = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.TrimStart();

            if (trimmed.StartsWith('#'))
            {
                heading = trimmed.TrimStart('#').Trim();
                continue;
            }

            if (!IsListItem(trimmed))
            {
                continue;
            }

            // Indented items belong to the item above them, so lifting one on
            // its own would strand the qualification it was carrying.
            if (line.Length - trimmed.Length > 0)
            {
                continue;
            }

            considered++;

            var text = trimmed[1..].Trim();
            var verdict = MemoryFactClassifier.Classify(text);

            if (verdict != FactVerdict.Durable)
            {
                rejected[verdict] = rejected.GetValueOrDefault(verdict) + 1;
                continue;
            }

            // Screened before it is grouped, so a credential never reaches the
            // memory store and never reaches the workspace repository, which is
            // pushed to a remote.
            var patterns = Security.SecretScanner.Match(text);

            if (patterns.Count > 0)
            {
                foreach (var pattern in patterns)
                {
                    withheld[pattern] = withheld.GetValueOrDefault(pattern) + 1;
                }

                continue;
            }

            var group = groups.FirstOrDefault(g => g.Heading == heading);

            if (group.Items is null)
            {
                group = (heading, []);
                groups.Add(group);
            }

            group.Items.Add((i, text));
        }

        var topics = new List<CompressionTopic>();
        var taken = new HashSet<int>();

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, items) in groups)
        {
            if (items.Count < minimumFactsPerTopic)
            {
                continue;
            }

            topics.Add(new CompressionTopic(
                HeadingName.Unique(HeadingName.From(name), used),
                $"Moved out of {fallbackName} so it is recalled rather than reread every session.",
                KindFor(name),
                items.Select(i => i.Text).ToList()));

            foreach (var (index, _) in items)
            {
                taken.Add(index);
            }
        }

        return (topics, taken, considered, rejected, withheld);
    }

    /// <summary>
    /// Rebuilds the document without the lines that moved, dropping a heading
    /// left with nothing under it.
    /// </summary>
    private static string Rebuild(string[] lines, HashSet<int> taken)
    {
        var kept = new List<string>(lines.Length);

        for (var i = 0; i < lines.Length; i++)
        {
            if (!taken.Contains(i))
            {
                kept.Add(lines[i]);
            }
        }

        var builder = new StringBuilder();

        for (var i = 0; i < kept.Count; i++)
        {
            var trimmed = kept[i].TrimStart();

            // A heading whose whole body moved to memory is now a promise the
            // document does not keep.
            if (trimmed.StartsWith('#') && NothingFollows(kept, i))
            {
                continue;
            }

            builder.AppendLine(kept[i]);
        }

        return builder.ToString();
    }

    /// <summary>Whether a heading is followed by anything but blanks and another heading.</summary>
    private static bool NothingFollows(List<string> lines, int index)
    {
        for (var i = index + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].TrimStart();

            if (trimmed.Length == 0)
            {
                continue;
            }

            return trimmed.StartsWith('#');
        }

        return true;
    }

    private static bool IsListItem(string trimmed) =>
        trimmed.StartsWith("- ", StringComparison.Ordinal)
        || trimmed.StartsWith("* ", StringComparison.Ordinal);

    /// <summary>
    /// Reads the sort of memory from the heading. Wrong only costs a label:
    /// the fact is stored and recalled either way.
    /// </summary>
    private static MemoryKind KindFor(string heading)
    {
        var lower = heading.ToLowerInvariant();

        if (lower.Contains("decision", StringComparison.Ordinal)
            || lower.Contains("rationale", StringComparison.Ordinal)
            || lower.Contains("why", StringComparison.Ordinal))
        {
            return MemoryKind.Decision;
        }

        if (lower.Contains("lesson", StringComparison.Ordinal)
            || lower.Contains("gotcha", StringComparison.Ordinal)
            || lower.Contains("pitfall", StringComparison.Ordinal)
            || lower.Contains("trap", StringComparison.Ordinal)
            || lower.Contains("known issue", StringComparison.Ordinal))
        {
            return MemoryKind.Lesson;
        }

        if (lower.Contains("reference", StringComparison.Ordinal)
            || lower.Contains("link", StringComparison.Ordinal)
            || lower.Contains("further reading", StringComparison.Ordinal))
        {
            return MemoryKind.Reference;
        }

        return MemoryKind.Project;
    }
}
