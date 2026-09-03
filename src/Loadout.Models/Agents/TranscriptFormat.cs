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

    /// <summary>Whether enough is described to read anything at all.</summary>
    public bool IsUsable =>
        Root.Length > 0
        && Files.Length > 0
        && Session.Id.Length > 0
        && Session.Directory.Length > 0;
}
