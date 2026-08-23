namespace Loadout.Models.Instructions;

/// <summary>What a memory entry is about, which decides how it is treated.</summary>
public enum MemoryKind
{
    /// <summary>Facts about the project: architecture, conventions, constraints.</summary>
    Project,

    /// <summary>Decisions and their rationale.</summary>
    Decision,

    /// <summary>Traps, known issues and hard-won debugging lessons.</summary>
    Lesson,

    /// <summary>Pointers to external material.</summary>
    Reference,
}

/// <summary>
/// One memory topic file.
/// <para>
/// Memory holds durable facts a session should not have to rediscover:
/// architecture, decisions and their reasons, non-obvious build behaviour,
/// recurring traps. It is explicitly not for task status, logs or file copies,
/// which go stale within days and then actively mislead.
/// </para>
/// </summary>
/// <param name="Name">File name without extension; how the index links to it.</param>
/// <param name="Path">Absolute path to the file.</param>
/// <param name="Description">One-line summary used by the index.</param>
/// <param name="Kind">What sort of knowledge it holds.</param>
/// <param name="Facts">
/// The individual facts, however the topic states them.
/// <para>
/// A bullet each is the tidiest form, but a topic that makes one point at
/// length in prose is stating a fact just as much as a list is, and treating
/// the second kind as empty would report real content as missing.
/// </para>
/// </param>
/// <param name="Links">Names this topic references with wiki-style links.</param>
/// <param name="Bytes">Size on disk.</param>
/// <param name="WrittenUtc">Last write time, used to spot topics nobody has revisited.</param>
public sealed record MemoryTopic(
    string Name,
    string Path,
    string Description,
    MemoryKind Kind,
    IReadOnlyList<string> Facts,
    IReadOnlyList<string> Links,
    long Bytes,
    DateTimeOffset WrittenUtc);

/// <summary>How serious a memory finding is.</summary>
public enum MemoryFindingSeverity
{
    Info,
    Warning,

    /// <summary>Something that must be dealt with, such as a credential in memory.</summary>
    Error,
}

/// <summary>One thing worth saying about the state of memory.</summary>
/// <param name="Topic">The topic it concerns, or null for an index-level finding.</param>
/// <param name="Severity">How serious it is.</param>
/// <param name="Kind">Short machine-readable category, for JSON consumers.</param>
/// <param name="Detail">
/// What is wrong, in a sentence. Never contains a matched credential value:
/// a finding that leaked the secret it found would defeat its own purpose.
/// </param>
public sealed record MemoryFinding(
    string? Topic,
    MemoryFindingSeverity Severity,
    string Kind,
    string Detail);

/// <summary>The result of auditing a project's memory.</summary>
/// <param name="Slug">Project audited.</param>
/// <param name="Topics">Topics found.</param>
/// <param name="Findings">Everything worth reporting.</param>
/// <param name="IndexPath">Where the index lives, whether or not it exists.</param>
/// <param name="HasIndex">Whether an index file was found.</param>
public sealed record MemoryAudit(
    string Slug,
    IReadOnlyList<MemoryTopic> Topics,
    IReadOnlyList<MemoryFinding> Findings,
    string IndexPath,
    bool HasIndex)
{
    public IEnumerable<MemoryFinding> Errors =>
        Findings.Where(f => f.Severity == MemoryFindingSeverity.Error);

    public IEnumerable<MemoryFinding> Warnings =>
        Findings.Where(f => f.Severity == MemoryFindingSeverity.Warning);

    /// <summary>The word printed at the end of the report.</summary>
    public string Verdict => Errors.Any()
        ? "ACTION REQUIRED"
        : Warnings.Any() ? "NEEDS ATTENTION" : "HEALTHY";
}

/// <summary>What a cleanup did, or would do.</summary>
/// <param name="RemovedTopics">Topic files deleted because they held no facts.</param>
/// <param name="RemovedBullets">
/// Exact duplicate facts removed, as "topic: text". Only exact repeats within a
/// topic: anything requiring judgement is reported by the audit and left alone.
/// </param>
/// <param name="RemovedIndexLines">Index entries deleted because their target was gone.</param>
/// <param name="Applied">False for a preview.</param>
public sealed record MemoryCleanup(
    IReadOnlyList<string> RemovedTopics,
    IReadOnlyList<string> RemovedBullets,
    IReadOnlyList<string> RemovedIndexLines,
    bool Applied)
{
    /// <summary>Whether there is anything to do.</summary>
    public bool IsEmpty =>
        RemovedTopics.Count == 0 && RemovedBullets.Count == 0 && RemovedIndexLines.Count == 0;

    /// <summary>Files a cleanup would touch, so they can be captured in a backup first.</summary>
    public int Count => RemovedTopics.Count + RemovedBullets.Count + RemovedIndexLines.Count;
}

/// <summary>What an import brought across, or would.</summary>
/// <param name="SourcePath">Where the memory was read from.</param>
/// <param name="Imported">Topics copied into the workspace.</param>
/// <param name="Skipped">Topics left behind, with the reason for each.</param>
/// <param name="Applied">False for a preview.</param>
public sealed record MemoryImport(
    string SourcePath,
    IReadOnlyList<MemoryTopic> Imported,
    IReadOnlyDictionary<string, string> Skipped,
    bool Applied)
{
    /// <summary>Facts brought across, which is the number that matters.</summary>
    public int Facts => Imported.Sum(topic => topic.Facts.Count);
}
