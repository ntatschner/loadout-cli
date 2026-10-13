namespace Loadout.Models.Voice;

/// <summary>What kind of thing a mark points at.</summary>
public enum VoiceMarkKind
{
    /// <summary>A commit's message.</summary>
    Commit,

    /// <summary>A file in a repository.</summary>
    File,
}

/// <summary>
/// One piece of writing the person has said is their own, pinned to the text
/// as it was when they said so.
/// </summary>
/// <remarks>
/// Kept on this machine and never in a repository, so provenance stays out of
/// git history. A file's mark lapses when its text changes: the person vouched
/// for that text, not for whatever the file holds now.
/// </remarks>
public sealed class VoiceMark
{
    /// <summary>What it points at.</summary>
    public VoiceMarkKind Kind { get; set; }

    /// <summary>The commit's full hash, or the file's path relative to its repository.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>The repository it belongs to, as a full path.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>The SHA-256 of the text when it was marked.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>When it was marked or last re-confirmed.</summary>
    public DateTimeOffset Marked { get; set; }
}

/// <summary>The local list of marks.</summary>
public sealed class VoiceMarkList
{
    /// <summary>Every mark, in the order they were made.</summary>
    public List<VoiceMark> Marks { get; set; } = [];
}
