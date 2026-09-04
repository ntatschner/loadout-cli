namespace Loadout.Models.Agents;

/// <summary>Which lines of a transcript carry what a session listing needs.</summary>
/// <remarks>
/// Paths are dotted, as in <c>payload.session_id</c>, and name properties inside
/// the JSON object on one line. Nothing here is a query language: a path walks
/// objects by name and stops, because every transcript format seen so far puts
/// what is wanted at a fixed place and a language nobody asked for is a language
/// that has to be documented, tested and kept.
/// </remarks>
public sealed class TranscriptSessionFormat
{
    /// <summary>Path to the session identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Path to the directory the session ran in.</summary>
    public string Directory { get; set; } = string.Empty;

    /// <summary>Path to a title, when the format records one.</summary>
    public string? Title { get; set; }

    /// <summary>Path to the branch, when the format records one.</summary>
    public string? Branch { get; set; }

    /// <summary>
    /// Whether the identifier and directory are on the first line, or somewhere
    /// in the file.
    /// </summary>
    /// <remarks>
    /// Both are real. Codex opens a rollout with a metadata entry; other formats
    /// put the working directory on every line. Saying which avoids reading a
    /// whole conversation to build a menu, which is the difference between a
    /// listing that is instant and one that is not.
    /// </remarks>
    public bool FirstLineOnly { get; set; } = true;
}

/// <summary>Where an accounting record keeps its numbers.</summary>
/// <remarks>
/// <para>
/// One path per field, and no fallbacks. Claude's own reader has one — a cache
/// figure that is sometimes a nested object and sometimes a flat number — and
/// that cannot be said here. A description language that grew alternatives would
/// be on its way to being a programming language, and an agent whose format
/// needs one is an agent that has earned a reader written by hand.
/// </para>
/// <para>
/// Everything except the token counts is optional. Without a timestamp the day
/// is taken from the file; without a model the counts are filed under "unknown";
/// without an identifier nothing can be told apart from a repeat, so repeats are
/// counted twice — which is worth configuring away, because agents do write the
/// same accounting more than once.
/// </para>
/// </remarks>
public sealed class TranscriptUsageFormat
{
    /// <summary>Path to the moment the record was written.</summary>
    public string? Timestamp { get; set; }

    /// <summary>Path to the directory the session was working in.</summary>
    public string? Directory { get; set; }

    /// <summary>Path to the model that answered.</summary>
    public string? Model { get; set; }

    /// <summary>Path to something that identifies the record, so a repeat can be seen.</summary>
    public string? Id { get; set; }

    /// <summary>Path to ordinary input tokens.</summary>
    public string? Input { get; set; }

    /// <summary>Path to tokens the model produced.</summary>
    public string? Output { get; set; }

    /// <summary>Path to input tokens served from cache.</summary>
    public string? CacheRead { get; set; }

    /// <summary>Path to input tokens written to the five-minute cache.</summary>
    public string? CacheWrite5m { get; set; }

    /// <summary>Path to input tokens written to the one-hour cache.</summary>
    public string? CacheWrite1h { get; set; }

    /// <summary>Path to the part of the output spent on extended thinking.</summary>
    public string? Thinking { get; set; }

    /// <summary>Whether enough is described to count anything.</summary>
    public bool IsUsable =>
        !string.IsNullOrWhiteSpace(Input) || !string.IsNullOrWhiteSpace(Output);
}

/// <summary>
/// How to read one agent's transcripts, described rather than compiled in.
/// </summary>
/// <remarks>
/// <para>
/// Being a first-class agent has needed three classes: the adapter, a session
/// reader and a usage reader. The adapter is genuinely agent-specific — flags,
/// environment, how a context file is handed over. The other two are the same
/// job every time: find files matching a pattern, read JSON lines, take five
/// named values. That is describable, and describing it is what lets somebody
/// add an agent this launcher has never heard of and still get session listing,
/// resume and token accounting.
/// </para>
/// <para>
/// It also moves a standing liability. Transcript formats are undocumented and
/// change without notice, so every reader compiled in is something that breaks
/// quietly, for whoever uses that agent, until somebody ships a fix. A described
/// format is one somebody can correct on their own machine the same afternoon.
/// </para>
/// </remarks>
public sealed class TranscriptFormat
{
    /// <summary>
    /// Directory holding the transcripts. <c>~</c> and <c>${HOME}</c> are
    /// expanded; anything else is taken literally.
    /// </summary>
    public string Root { get; set; } = string.Empty;

    /// <summary>Filename pattern within the root, such as <c>rollout-*.jsonl</c>.</summary>
    public string Files { get; set; } = "*.jsonl";

    /// <summary>Whether to look in directories under the root as well.</summary>
    public bool Recursive { get; set; } = true;

    /// <summary>Where a session's identity is written.</summary>
    public TranscriptSessionFormat Session { get; set; } = new();

    /// <summary>
    /// Where the token counts are, when the agent records any.
    /// </summary>
    /// <remarks>
    /// Separate from the session block because the two are separately useful. An
    /// agent can be listed without being counted, and describing only what is
    /// true of it beats describing what is convenient.
    /// </remarks>
    public TranscriptUsageFormat? Usage { get; set; }

    /// <summary>Whether the files can be found at all.</summary>
    private bool HasFiles => Root.Length > 0 && Files.Length > 0;

    /// <summary>Whether enough is described to list sessions.</summary>
    public bool IsUsable =>
        HasFiles
        && Session.Id.Length > 0
        && Session.Directory.Length > 0;

    /// <summary>Whether enough is described to count tokens.</summary>
    public bool CanCount => HasFiles && Usage is { } usage && usage.IsUsable;
}
