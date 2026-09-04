using System.Text;
using System.Text.RegularExpressions;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>
/// Reads, writes and audits a project's durable memory.
/// <para>
/// Memory lives in the workspace repository rather than in a machine-local
/// directory, which is the one substantive departure from the toolkit this is
/// modelled on. Keeping it local means every machine relearns the same facts
/// and nothing is reviewable; keeping it in the workspace means a fact learned
/// on one machine is available on the next, and a wrong one can be corrected in
/// a pull request like any other mistake.
/// </para>
/// </summary>
public interface IMemoryService
{
    /// <summary>Topics for a project, ordered by name.</summary>
    /// <param name="workspaceRoot">
    /// Root of the workspace to read from, passed rather than resolved so this
    /// service and its caller cannot disagree about which workspace is meant.
    /// </param>
    /// <param name="slug">Project whose memory to read.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default);

    /// <summary>
    /// Audits memory for the things that make it untrustworthy: credentials,
    /// duplicates, staleness, oversize, and index entries pointing nowhere.
    /// </summary>
    Task<OperationResult<MemoryAudit>> AuditAsync(
        string workspaceRoot,
        string slug,
        int staleMonths = 6,
        CancellationToken ct = default);

    /// <summary>Creates or replaces a topic and refreshes the index.</summary>
    /// <param name="workspaceRoot">Root of the workspace to write into.</param>
    /// <param name="slug">Project the topic belongs to.</param>
    /// <param name="name">Topic name, which is also the file name.</param>
    /// <param name="description">The one line that reaches a session's context.</param>
    /// <param name="kind">What sort of fact this is.</param>
    /// <param name="facts">The facts themselves.</param>
    /// <param name="acknowledgedSimilar">
    /// That the writer has seen the topics already covering this ground and
    /// meant to start a new one anyway.
    /// <para>
    /// A new topic beside an existing one on the same subject is how memory
    /// comes to contradict itself: nothing is overwritten, both are indexed, and
    /// a later session is given two answers with nothing to choose between them.
    /// Contradictions arrive one fact at a time, at the moment something could
    /// have been shown, so this is where the showing happens. Writing to a name
    /// that already exists never asks — that is the extending this exists to
    /// encourage.
    /// </para>
    /// </param>
    /// <param name="scope">
    /// Who the fact is true for, which decides where it is kept. A fact about
    /// this machine written under the project scope is a fact that syncs to
    /// machines it is false on, which is the failure the scopes exist to stop.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<MemoryTopic>> WriteAsync(
        string workspaceRoot,
        string slug,
        string name,
        string description,
        MemoryKind kind,
        IReadOnlyList<string> facts,
        bool acknowledgedSimilar = false,
        MemoryScope scope = MemoryScope.Project,
        CancellationToken ct = default);

    /// <summary>
    /// Rewrites the index from the topics actually present, which is also how a
    /// missing or drifted index gets repaired.
    /// </summary>
    Task<OperationResult> RebuildIndexAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default);

    /// <summary>
    /// Removes what can be removed without judgement: topics holding no facts,
    /// facts repeated word for word inside one topic, and index lines pointing
    /// at files that are gone.
    /// <para>
    /// Mechanical only. Prose is never rewritten and a near-duplicate is never
    /// merged, because deciding which of two similar facts is the right one is
    /// exactly the judgement a regular expression should not be making on
    /// somebody's behalf. Everything else the audit finds stays for a person.
    /// </para>
    /// </summary>
    Task<OperationResult<MemoryCleanup>> CleanAsync(
        string workspaceRoot,
        string slug,
        bool apply,
        CancellationToken ct = default);

    /// <summary>Files a cleanup would write to, for capturing a backup first.</summary>
    IReadOnlyList<string> CleanupPaths(string workspaceRoot, string slug);

    /// <summary>
    /// The index as text, for inclusion in a compiled context. Null when the
    /// project has no memory.
    /// <para>
    /// The index alone, never the topics. The point of an index is that a
    /// session pays for one short list and then reads only the topic it
    /// actually needs; inlining every topic would reintroduce exactly the cost
    /// this exists to avoid.
    /// </para>
    /// </summary>
    Task<OperationResult<string?>> ReadIndexAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed partial class MemoryService : IMemoryService
{
    /// <summary>
    /// Written into every topic this service creates, and skipped when reading
    /// facts back out: it is a standing instruction about how to treat the
    /// file, not something learned about the project.
    /// </summary>
    private const string RepositoryWinsNotice =
        "The repository is authoritative. If one of these disagrees with the code, "
        + "the code wins and this file is what needs correcting.";

    /// <summary>An index should stay an index. Past this it has become content.</summary>
    private const long MaximumIndexBytes = 8 * 1024;

    /// <summary>A topic past this size is doing too many jobs and should be split.</summary>
    private const long MaximumTopicBytes = 16 * 1024;

    /// <summary>
    /// Bullets shorter than this are not compared for duplication. Short lines
    /// repeat innocently ("Use British spelling"), and flagging them trains
    /// people to ignore the audit.
    /// </summary>
    private const int MinimumComparableLength = 25;

    private readonly TimeProvider _time;
    private readonly string? _machineRoot;

    /// <param name="time">The clock, for judging staleness.</param>
    /// <param name="machineRoot">
    /// Where this machine keeps what is only true of it, which is outside the
    /// workspace on purpose. Null where there is no machine-local store — the
    /// scope is then reported as unavailable rather than quietly falling back to
    /// the workspace, which would sync the one thing it exists to keep local.
    /// </param>
    public MemoryService(TimeProvider time, string? machineRoot = null)
    {
        _time = time;
        _machineRoot = machineRoot;
    }

    private static string DirectoryFor(string workspaceRoot, string slug) =>
        Path.Combine(workspaceRoot, "projects", slug, "memory");

    private static string IndexFor(string workspaceRoot, string slug) =>
        Path.Combine(DirectoryFor(workspaceRoot, slug), "MEMORY.md");

    /// <summary>
    /// Where a scope's topics live.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The project scope sits under the project, as it always has. The user
    /// scope sits at the top of the workspace, because it is about the person
    /// rather than any one project and is as true on the next machine as on this
    /// one — so it should travel with the workspace exactly as project memory
    /// does.
    /// </para>
    /// <para>
    /// The machine scope sits outside the workspace altogether. That is the
    /// whole reason it is a separate scope: the workspace is a Git repository
    /// that syncs, and "the Restart Manager is disabled here" is false on the
    /// next machine. A fact that cannot travel must live somewhere that cannot
    /// carry it.
    /// </para>
    /// </remarks>
    private string? DirectoryFor(string workspaceRoot, string slug, MemoryScope scope) => scope switch
    {
        MemoryScope.Project => DirectoryFor(workspaceRoot, slug),
        MemoryScope.User => Path.Combine(workspaceRoot, "memory"),

        // Null rather than a guess. Without somewhere machine-local to write,
        // the honest answer is that this scope is unavailable, not that it lives
        // in the workspace after all — which would sync the one thing it exists
        // to keep local.
        _ => _machineRoot is { Length: > 0 } root ? Path.Combine(root, "memory") : null,
    };

    private string? IndexFor(string workspaceRoot, string slug, MemoryScope scope) =>
        DirectoryFor(workspaceRoot, slug, scope) is { } directory
            ? Path.Combine(directory, "MEMORY.md")
            : null;

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default)
    {
        // Every scope, because a session working on this project is subject to
        // all three: what is true of the project, what is true of this person's
        // work, and what is true of this machine. Listing only one would be
        // listing a third of what the session is actually given.
        var all = new List<MemoryTopic>();

        foreach (var scope in Enum.GetValues<MemoryScope>())
        {
            var scoped = await ListAsync(workspaceRoot, slug, scope, ct).ConfigureAwait(false);

            if (scoped.Succeeded)
            {
                all.AddRange(scoped.Value!);
            }
        }

        return OperationResult<IReadOnlyList<MemoryTopic>>.Ok(
            all.OrderBy(topic => topic.Scope)
                .ThenBy(topic => topic.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>Topics in one scope.</summary>
    private async Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
        string workspaceRoot,
        string slug,
        MemoryScope scope,
        CancellationToken ct)
    {
        var directory = DirectoryFor(workspaceRoot, slug, scope);

        if (directory is null || !Directory.Exists(directory))
        {
            return OperationResult<IReadOnlyList<MemoryTopic>>.Ok([]);
        }

        var topics = new List<MemoryTopic>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.md"))
        {
            ct.ThrowIfCancellationRequested();

            // The index is not a topic. Including it would make it appear in
            // its own listing and in every duplicate comparison.
            if (Path.GetFileName(file).Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parsed = await ParseAsync(file, ct).ConfigureAwait(false);

            if (parsed.Succeeded)
            {
                // Stamped here rather than read from the file. Which scope a
                // topic belongs to is decided by where it is, and a scope
                // written into the frontmatter could disagree with that.
                topics.Add(parsed.Value! with { Scope = scope });
            }
        }

        return OperationResult<IReadOnlyList<MemoryTopic>>.Ok(
            topics.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    internal static async Task<OperationResult<MemoryTopic>> ParseAsync(
        string path,
        CancellationToken ct = default)
    {
        string text;

        try
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<MemoryTopic>.Fail($"Could not read '{path}': {ex.Message}");
        }

        var description = string.Empty;
        var kind = MemoryKind.Project;

        var match = Frontmatter().Match(text);

        if (match.Success)
        {
            foreach (var line in match.Groups["front"].Value.Split('\n'))
            {
                var separator = line.IndexOf(':', StringComparison.Ordinal);

                if (separator <= 0)
                {
                    continue;
                }

                var key = line[..separator].Trim().ToLowerInvariant();
                var value = line[(separator + 1)..].Trim().Trim('"', '\'');

                if (key == "description")
                {
                    description = value;
                }
                else if (key is "type" or "kind")
                {
                    kind = value.ToLowerInvariant() switch
                    {
                        "decision" or "decisions" => MemoryKind.Decision,
                        "lesson" or "lessons" => MemoryKind.Lesson,
                        "reference" => MemoryKind.Reference,
                        _ => MemoryKind.Project,
                    };
                }
            }
        }

        var facts = ExtractFacts(text, match.Success ? text[match.Length..] : text);

        var links = WikiLink().Matches(text)
            .Select(m => m.Groups["name"].Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return OperationResult<MemoryTopic>.Ok(new MemoryTopic(
            Path.GetFileNameWithoutExtension(path),
            path,
            description,
            kind,
            facts,
            links,
            Encoding.UTF8.GetByteCount(text),
            new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)));
    }

    /// <summary>
    /// Pulls the individual facts out of a topic.
    /// <para>
    /// Bullets when there are any, and paragraphs otherwise. A topic that makes
    /// its point in prose is not an empty one, and reading it as empty would
    /// have the audit report real content as missing and an import refuse to
    /// bring it across.
    /// </para>
    /// </summary>
    private static List<string> ExtractFacts(string text, string body)
    {
        var facts = Bullet().Matches(text)
            .Select(m => m.Groups["text"].Value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (facts.Count > 0)
        {
            return facts;
        }

        return Paragraph().Split(body.Trim())
            .Select(paragraph => paragraph.Trim())
            .Where(IsFact)
            .Select(paragraph => WhiteSpace().Replace(paragraph, " "))
            .ToList();
    }

    /// <summary>
    /// Whether a paragraph carries content rather than structure.
    /// <para>
    /// Headings, comments and the standing note this service writes into every
    /// topic are not facts about the project, and counting them would make an
    /// empty topic look occupied.
    /// </para>
    /// </summary>
    private static bool IsFact(string paragraph) =>
        paragraph.Length > 0
        && !paragraph.StartsWith('#')
        && !paragraph.StartsWith("<!--", StringComparison.Ordinal)
        && !paragraph.StartsWith("---", StringComparison.Ordinal)
        && !paragraph.StartsWith(RepositoryWinsNotice[..40], StringComparison.Ordinal);

    /// <inheritdoc />
    public async Task<OperationResult<MemoryAudit>> AuditAsync(
        string workspaceRoot,
        string slug,
        int staleMonths = 6,
        CancellationToken ct = default)
    {
        var listed = await ListAsync(workspaceRoot, slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<MemoryAudit>.Fail(listed.Error!, listed.ExitCode);
        }

        var topics = listed.Value!;
        var findings = new List<MemoryFinding>();
        var indexPath = IndexFor(workspaceRoot, slug);
        var hasIndex = File.Exists(indexPath);

        AuditIndex(findings, topics, indexPath, hasIndex);
        AuditTopics(findings, topics, staleMonths);
        AuditDuplicates(findings, topics);
        AuditLinks(findings, topics);

        return OperationResult<MemoryAudit>.Ok(
            new MemoryAudit(slug, topics, findings, indexPath, hasIndex));
    }

    private static void AuditIndex(
        List<MemoryFinding> findings,
        IReadOnlyList<MemoryTopic> topics,
        string indexPath,
        bool hasIndex)
    {
        if (!hasIndex)
        {
            if (topics.Count > 0)
            {
                findings.Add(new MemoryFinding(null, MemoryFindingSeverity.Warning, "index-missing",
                    $"{topics.Count} topic(s) exist with no MEMORY.md to find them by. "
                    + "Rebuild it with: loadout memory reindex"));
            }

            return;
        }

        var size = new FileInfo(indexPath).Length;

        if (size > MaximumIndexBytes)
        {
            findings.Add(new MemoryFinding(null, MemoryFindingSeverity.Warning, "index-oversized",
                $"MEMORY.md is {size / 1024}KB. It should be a short index; content belongs in a topic."));
        }

        string text;

        try
        {
            text = File.ReadAllText(indexPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            findings.Add(new MemoryFinding(null, MemoryFindingSeverity.Warning, "index-unreadable",
                $"MEMORY.md could not be read: {ex.Message}"));

            return;
        }

        var names = topics.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (Match link in IndexLink().Matches(text))
        {
            var target = Path.GetFileNameWithoutExtension(link.Groups["file"].Value);

            if (!names.Contains(target))
            {
                // An index that points at a deleted topic sends a reader
                // looking for something that is not there, which is worse than
                // having no index entry at all.
                findings.Add(new MemoryFinding(null, MemoryFindingSeverity.Warning, "index-dead-link",
                    $"MEMORY.md links to '{link.Groups["file"].Value}', which does not exist."));
            }
        }

        foreach (var topic in topics.Where(t => !text.Contains(t.Name, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Info, "index-missing-entry",
                "not listed in MEMORY.md, so nothing points at it."));
        }
    }

    private void AuditTopics(
        List<MemoryFinding> findings,
        IReadOnlyList<MemoryTopic> topics,
        int staleMonths)
    {
        var staleBefore = _time.GetUtcNow().AddMonths(-Math.Max(1, staleMonths));

        foreach (var topic in topics)
        {
            if (topic.Facts.Count == 0)
            {
                findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Warning, "empty",
                    "holds no facts."));
            }

            if (topic.Bytes > MaximumTopicBytes)
            {
                findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Warning, "oversized",
                    $"is {topic.Bytes / 1024}KB. Consider splitting it by subject."));
            }

            // A description that cannot be decided from is nearly as costly as
            // none: the index line is paid for on every launch either way, and
            // the topic goes unopened either way. Reported separately from the
            // missing case because the fix is different — one is writing a line,
            // the other is rewriting one somebody thought was fine.
            if (!string.IsNullOrWhiteSpace(topic.Description)
                && MemoryDescriptionClassifier.Classify(topic.Name, topic.Description)
                    is var indexLine and not DescriptionVerdict.Decidable)
            {
                findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Info, "vague-description",
                    $"cannot be chosen from its index line: "
                    + $"{MemoryDescriptionClassifier.Explain(indexLine)}."));
            }

            if (string.IsNullOrWhiteSpace(topic.Description))
            {
                findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Info, "no-description",
                    "has no description, so the index cannot say what it is for."));
            }

            foreach (var fact in topic.Facts)
            {
                var patterns = SecretScanner.Match(fact);

                if (patterns.Count > 0)
                {
                    // Names the pattern, never the value. A finding that
                    // printed the credential it found would put it into logs
                    // and terminal scrollback, which is the whole problem.
                    findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Error, "credential",
                        $"contains something shaped like a credential ({string.Join(", ", patterns)}). "
                        + "Remove it and rotate the value."));
                }

                var verdict = MemoryFactClassifier.Classify(fact);

                if (verdict != FactVerdict.Durable)
                {
                    // Reported rather than removed. The classifier is a good
                    // filter and not an oracle, and silently deleting somebody's
                    // note because a regular expression disliked its phrasing
                    // would be a worse failure than keeping a weak one.
                    findings.Add(new MemoryFinding(
                        topic.Name,
                        MemoryFindingSeverity.Info,
                        verdict.ToString().ToLowerInvariant(),
                        $"\"{Truncate(fact)}\" {MemoryFactClassifier.Explain(verdict)}"));
                }

                var dated = DatedFact().Match(fact);

                if (dated.Success
                    && DateTimeOffset.TryParse(dated.Value, out var when)
                    && when < staleBefore)
                {
                    findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Info, "stale",
                        $"dated {when:yyyy-MM-dd}: \"{Truncate(fact)}\". Check it still holds."));
                }
            }
        }
    }

    private static void AuditDuplicates(
        List<MemoryFinding> findings,
        IReadOnlyList<MemoryTopic> topics)
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var topic in topics)
        {
            foreach (var fact in topic.Facts)
            {
                if (fact.Length < MinimumComparableLength)
                {
                    continue;
                }

                var key = Normalise(fact);

                if (seen.TryGetValue(key, out var other))
                {
                    findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Warning, "duplicate",
                        $"repeats a fact already in '{other}': \"{Truncate(fact)}\"."));
                }
                else
                {
                    seen[key] = topic.Name;
                }
            }
        }
    }

    private static void AuditLinks(List<MemoryFinding> findings, IReadOnlyList<MemoryTopic> topics)
    {
        var names = topics.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var topic in topics)
        {
            foreach (var link in topic.Links.Where(l => !names.Contains(l)))
            {
                findings.Add(new MemoryFinding(topic.Name, MemoryFindingSeverity.Info, "dead-link",
                    $"links to [[{link}]], which does not exist."));
            }
        }
    }

    /// <summary>
    /// How much of a new topic has to land on an existing one before it is
    /// worth stopping for.
    /// </summary>
    /// <remarks>
    /// Two distinct words rather than one. One is "build", or "the launcher",
    /// which half a store has in common and which would stop every write; two is
    /// the point at which the topics are plausibly about the same thing. A check
    /// that interrupts every write is one whose override becomes a habit.
    /// </remarks>
    private const int SharedWordsWorthAsking = 2;

    /// <summary>Existing topics that look like they already cover this ground.</summary>
    private async Task<IReadOnlyList<MemoryMatch>> NeighboursAsync(
        string workspaceRoot,
        string slug,
        string name,
        IReadOnlyList<string> facts,
        CancellationToken ct)
    {
        var existing = await ListAsync(workspaceRoot, slug, ct).ConfigureAwait(false);

        if (existing.Failed || existing.Value is not { Count: > 0 } topics)
        {
            return [];
        }

        // The name and the facts together, because either alone misses a case:
        // a topic named for its subject with facts that never repeat the word,
        // and a topic whose name says little.
        var query = name.Replace('-', ' ') + " " + string.Join(' ', facts);

        return MemorySearch.Rank(topics, query, limit: 3)
            .Where(match => match.Terms >= SharedWordsWorthAsking)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryTopic>> WriteAsync(
        string workspaceRoot,
        string slug,
        string name,
        string description,
        MemoryKind kind,
        IReadOnlyList<string> facts,
        bool acknowledgedSimilar = false,
        MemoryScope scope = MemoryScope.Project,
        CancellationToken ct = default)
    {
        var safeName = Slugify(name);

        if (safeName.Length == 0)
        {
            return OperationResult<MemoryTopic>.Fail(
                "A topic name is required.", ExitCode.InvalidArguments);
        }

        // Refuses to write a credential rather than writing it and flagging it
        // afterwards. Once it is on disk and committed it is disclosed, and an
        // audit finding does not undo that.
        //
        // First of the two refusals, and the order matters: a write carrying
        // both a credential and a weak description has to be turned away for the
        // credential. Told to fix its description instead, the caller fixes it
        // and writes the credential on the second attempt.
        foreach (var fact in facts)
        {
            var patterns = SecretScanner.Match(fact);

            if (patterns.Count > 0)
            {
                return OperationResult<MemoryTopic>.Fail(
                    $"That looks like it contains a credential ({string.Join(", ", patterns)}). "
                    + "Memory is committed to the workspace repository, so it will not be written.",
                    ExitCode.PolicyViolation);
            }
        }

        // Only the index reaches a compiled context, so this one line is all a
        // session has to decide whether the topic is worth opening. Refused
        // here, where whoever is writing it still has the subject in mind:
        // afterwards it is a chore nobody comes back for, and the topic goes
        // unread rather than being found to be wrong.
        var indexLine = MemoryDescriptionClassifier.Classify(safeName, description);

        if (indexLine != DescriptionVerdict.Decidable)
        {
            return OperationResult<MemoryTopic>.Fail(
                $"That description will not do: {MemoryDescriptionClassifier.Explain(indexLine)}. "
                + "Only this line reaches a session's context, so say what question the topic "
                + "answers, as in \"why installers fail with 1603 over a running app\".",
                ExitCode.InvalidArguments);
        }

        if (DirectoryFor(workspaceRoot, slug, scope) is not { } directory)
        {
            return OperationResult<MemoryTopic>.Fail(
                "There is no machine-local store on this machine, so a machine-scoped fact has "
                + "nowhere to go that would not be committed to the workspace.",
                ExitCode.ConfigurationInvalid);
        }

        var path = Path.Combine(directory, safeName + ".md");

        // Only when a new topic is being started. Writing to a name that
        // already exists is the extending this exists to encourage, and asking
        // about it would train people to pass the flag every time.
        if (!acknowledgedSimilar && !File.Exists(path))
        {
            var near = await NeighboursAsync(workspaceRoot, slug, safeName, facts, ct)
                .ConfigureAwait(false);

            if (near.Count > 0)
            {
                return OperationResult<MemoryTopic>.Fail(
                    $"'{safeName}' would be a new topic beside ones already covering this:"
                    + Environment.NewLine
                    + string.Join(
                        Environment.NewLine,
                        near.Select(match => $"  {match.Topic.Name} — {match.Topic.Description}"))
                    + Environment.NewLine
                    + "Add the fact to one of those instead, or write it again saying this is "
                    + "genuinely separate.",
                    ExitCode.InvalidArguments);
            }
        }

        var builder = new StringBuilder()
            .AppendLine("---")
            .AppendLine($"name: {safeName}")
            .AppendLine($"description: {description}")
            .AppendLine("metadata:")
            .AppendLine($"  type: {kind.ToString().ToLowerInvariant()}")
            .AppendLine("---")
            .AppendLine()
            .AppendLine(RepositoryWinsNotice)
            .AppendLine();

        foreach (var fact in facts)
        {
            builder.AppendLine($"- {fact.Trim()}");
        }

        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(path, builder.ToString(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<MemoryTopic>.Fail($"Could not write '{path}': {ex.Message}");
        }

        var rebuild = await RebuildIndexAsync(workspaceRoot, slug, ct).ConfigureAwait(false);

        if (rebuild.Failed)
        {
            return OperationResult<MemoryTopic>.Fail(rebuild.Error!, rebuild.ExitCode);
        }

        return await ParseAsync(path, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RebuildIndexAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default)
    {
        // Each scope keeps its own index beside its own topics. One index over
        // all three would have to live somewhere, and wherever that was would
        // be either synced when it should not be or absent when it should not
        // be.
        foreach (var scope in Enum.GetValues<MemoryScope>())
        {
            var rebuilt = await RebuildIndexAsync(workspaceRoot, slug, scope, ct)
                .ConfigureAwait(false);

            if (rebuilt.Failed)
            {
                return rebuilt;
            }
        }

        return OperationResult.Ok();
    }

    /// <summary>Rewrites one scope's index from the topics it actually holds.</summary>
    private async Task<OperationResult> RebuildIndexAsync(
        string workspaceRoot,
        string slug,
        MemoryScope scope,
        CancellationToken ct)
    {
        if (IndexFor(workspaceRoot, slug, scope) is not { } indexPath)
        {
            return OperationResult.Ok();
        }

        var listed = await ListAsync(workspaceRoot, slug, scope, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult.Fail(listed.Error!, listed.ExitCode);
        }

        var topics = listed.Value!;

        if (topics.Count == 0)
        {
            return OperationResult.Ok();
        }

        var builder = new StringBuilder()
            .AppendLine(scope switch
            {
                MemoryScope.User => "# What is true of this person's work",
                MemoryScope.Machine => "# What is true of this machine",
                _ => "# Project memory index",
            })
            .AppendLine()
            .AppendLine(scope switch
            {
                MemoryScope.User => "Durable facts that hold whatever the project.",
                MemoryScope.Machine =>
                    "Durable facts about this computer. Not committed anywhere, because they "
                    + "are false on any other.",
                _ => "Durable facts about this project. Each line links to one topic.",
            })
            .AppendLine();

        foreach (var topic in topics)
        {
            var description = string.IsNullOrWhiteSpace(topic.Description)
                ? "memory topic"
                : topic.Description;

            builder.AppendLine($"- [{topic.Name}]({topic.Name}.md) - {description}");
        }

        try
        {
            await File
                .WriteAllTextAsync(indexPath, builder.ToString(), ct)
                .ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write the memory index: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> CleanupPaths(string workspaceRoot, string slug)
    {
        var directory = DirectoryFor(workspaceRoot, slug);

        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.md").ToList()
            : [];
    }

    /// <inheritdoc />
    public async Task<OperationResult<MemoryCleanup>> CleanAsync(
        string workspaceRoot,
        string slug,
        bool apply,
        CancellationToken ct = default)
    {
        var listed = await ListAsync(workspaceRoot, slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<MemoryCleanup>.Fail(listed.Error!, listed.ExitCode);
        }

        var removedTopics = new List<string>();
        var removedBullets = new List<string>();

        foreach (var topic in listed.Value!)
        {
            ct.ThrowIfCancellationRequested();

            if (topic.Facts.Count == 0)
            {
                removedTopics.Add(topic.Name);

                if (apply)
                {
                    TryDelete(topic.Path);
                }

                continue;
            }

            var duplicates = await RemoveDuplicateBulletsAsync(topic, apply, ct)
                .ConfigureAwait(false);

            removedBullets.AddRange(duplicates.Select(b => $"{topic.Name}: {Truncate(b)}"));
        }

        var removedIndexLines = await PruneIndexAsync(
            workspaceRoot,
            slug,
            listed.Value.Where(t => !removedTopics.Contains(t.Name)).ToList(),
            apply,
            ct).ConfigureAwait(false);

        return OperationResult<MemoryCleanup>.Ok(
            new MemoryCleanup(removedTopics, removedBullets, removedIndexLines, apply));
    }

    /// <summary>
    /// Removes facts repeated word for word within one topic.
    /// <para>
    /// Exact repeats only, compared after normalisation but removed only when
    /// the raw line matches too. Two facts that merely say similar things are a
    /// question for a person, and answering it by deleting one would lose
    /// whichever wording was better.
    /// </para>
    /// </summary>
    private static async Task<List<string>> RemoveDuplicateBulletsAsync(
        MemoryTopic topic,
        bool apply,
        CancellationToken ct)
    {
        var removed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();

        string text;

        try
        {
            text = await File.ReadAllTextAsync(topic.Path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return removed;
        }

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var fact = Bullet().Match(line);

            if (!fact.Success)
            {
                kept.Add(line);
                continue;
            }

            if (seen.Add(fact.Groups["text"].Value.Trim()))
            {
                kept.Add(line);
                continue;
            }

            removed.Add(fact.Groups["text"].Value.Trim());
        }

        if (apply && removed.Count > 0)
        {
            try
            {
                await File.WriteAllTextAsync(topic.Path, string.Join("\n", kept), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        return removed;
    }

    /// <summary>Drops index lines whose target no longer exists.</summary>
    private static async Task<List<string>> PruneIndexAsync(
        string workspaceRoot,
        string slug,
        IReadOnlyList<MemoryTopic> topics,
        bool apply,
        CancellationToken ct)
    {
        var path = IndexFor(workspaceRoot, slug);
        var removed = new List<string>();

        if (!File.Exists(path))
        {
            return removed;
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return removed;
        }

        var names = topics.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kept = new List<string>();

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var link = IndexLink().Match(line);

            if (!link.Success
                || names.Contains(Path.GetFileNameWithoutExtension(link.Groups["file"].Value)))
            {
                kept.Add(line);
                continue;
            }

            removed.Add(line.Trim());
        }

        if (apply && removed.Count > 0)
        {
            try
            {
                await File.WriteAllTextAsync(path, string.Join("\n", kept), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        return removed;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reported as removed by the preview and left alone here. A cleanup
            // that threw halfway would be worse than one that skipped a file.
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> ReadIndexAsync(
        string workspaceRoot,
        string slug,
        CancellationToken ct = default)
    {
        // All three indexes, one after another, because a session working on
        // this project is subject to all three. Given only the project's, it
        // would go on rediscovering what this person and this machine already
        // know — which is the whole reason the other two scopes exist.
        var parts = new List<string>();

        foreach (var scope in Enum.GetValues<MemoryScope>())
        {
            if (IndexFor(workspaceRoot, slug, scope) is not { } path || !File.Exists(path))
            {
                continue;
            }

            try
            {
                var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(scope == MemoryScope.Project
                        ? text.TrimEnd()
                        : $"{Heading(scope)}{Environment.NewLine}{Environment.NewLine}{text.TrimEnd()}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<string?>.Fail($"Could not read '{path}': {ex.Message}");
            }
        }

        return OperationResult<string?>.Ok(parts.Count == 0
            ? null
            : string.Join(Environment.NewLine + Environment.NewLine, parts));
    }

    /// <summary>
    /// What to call a scope in the compiled index.
    /// </summary>
    /// <remarks>
    /// The project's own index keeps no heading, so a project with nothing else
    /// looks exactly as it always has. The other two are labelled, because a
    /// session told "the Restart Manager is disabled" needs to know that is a
    /// claim about the machine rather than about the code it is reading.
    /// </remarks>
    private static string Heading(MemoryScope scope) => scope switch
    {
        MemoryScope.User => "## Also true of this person's work, whatever the project",
        _ => "## Also true of this machine only, and not of any other",
    };

    private static string Truncate(string value) =>
        value.Length <= 70 ? value : value[..70] + "...";

    /// <summary>
    /// Reduces a fact to a comparison key: case and punctuation differences
    /// are not different facts.
    /// </summary>
    private static string Normalise(string value) =>
        NonComparable().Replace(value.ToLowerInvariant(), " ").Trim();

    internal static string Slugify(string value)
    {
        var cleaned = new string(value
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray());

        return cleaned.Trim('-').ToLowerInvariant();
    }

    [GeneratedRegex(@"\A---\r?\n(?<front>.*?)\r?\n---[ \t]*\r?\n", RegexOptions.Singleline, 1000)]
    private static partial Regex Frontmatter();

    [GeneratedRegex(@"(?m)^[ \t]*[-*][ \t]+(?<text>.+)$", RegexOptions.None, 1000)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\[\[(?<name>[^\]]+)\]\]", RegexOptions.None, 1000)]
    private static partial Regex WikiLink();

    /// <summary>Paragraphs are separated by a blank line.</summary>
    [GeneratedRegex(@"(\r?\n){2,}", RegexOptions.None, 1000)]
    private static partial Regex Paragraph();

    /// <summary>
    /// Collapses the line breaks inside a paragraph, so a fact wrapped across
    /// four lines is compared and reported as the one sentence it is.
    /// </summary>
    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex WhiteSpace();

    [GeneratedRegex(@"\((?<file>[^)]+\.md)\)", RegexOptions.None, 1000)]
    private static partial Regex IndexLink();

    [GeneratedRegex(@"\b(20\d{2})-(\d{2})-(\d{2})\b", RegexOptions.None, 1000)]
    private static partial Regex DatedFact();

    [GeneratedRegex(@"[^a-z0-9 ]+", RegexOptions.None, 1000)]
    private static partial Regex NonComparable();
}
