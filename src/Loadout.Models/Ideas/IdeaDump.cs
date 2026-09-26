namespace Loadout.Models.Ideas;

/// <summary>
/// Notes dropped in wholesale, from wherever they were kept, and what an agent
/// proposed splitting them into. Kept at <c>ideas/dumps/&lt;id&gt;.yaml</c>
/// beside the list they were dropped on.
/// </summary>
/// <remarks>
/// <para>
/// The text is kept exactly as it arrived, and each piece the agent proposes
/// carries the excerpt it came from, word for word. The agent chooses where the
/// cuts go and gives each piece a title; it never rewrites what somebody wrote.
/// A piece whose excerpt is not in the dump is refused, because a paraphrase
/// that reads as the person's own words is worse than no split at all.
/// </para>
/// <para>
/// Nothing is recorded until the person applies the split, and they may take
/// only some of it.
/// </para>
/// </remarks>
public sealed class IdeaDump
{
    public int SchemaVersion { get; set; } = 1;

    public string Id { get; set; } = string.Empty;

    /// <summary>Everything that was dropped in, verbatim.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Where it came from: a file's name, or <c>pasted</c>.</summary>
    public string Source { get; set; } = string.Empty;

    public DateTimeOffset CapturedUtc { get; set; }

    /// <summary>The pieces proposed, in the order they appear in the text.</summary>
    public List<DumpItem> Items { get; set; } = [];

    /// <summary>When the last split was proposed, or null before there is one.</summary>
    public DateTimeOffset? SplitUtc { get; set; }

    /// <summary>What went wrong with the last split, when something did.</summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>One piece of a dump, as the agent proposed it.</summary>
public sealed class DumpItem
{
    /// <summary>1, 2 and on, as the person picks them.</summary>
    public int Number { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The part of the dump this piece is, word for word.</summary>
    public string Excerpt { get; set; } = string.Empty;

    /// <summary>
    /// Whether it is ready to be worked on as it stands, or needs shaping first.
    /// </summary>
    public DumpItemKind Kind { get; set; } = DumpItemKind.Idea;

    /// <summary>The registered project the agent thinks it belongs to, or empty.</summary>
    public string Project { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    /// <summary>Where it was recorded when applied, as <c>list/id</c>, or empty until then.</summary>
    public string Recorded { get; set; } = string.Empty;
}

/// <summary>What a piece of a dump becomes.</summary>
public enum DumpItemKind
{
    /// <summary>Something to flesh out first, with <c>idea refine</c>.</summary>
    Idea,

    /// <summary>Clear enough to work through as it is.</summary>
    Task,
}

/// <summary>The shape an agent's split of a dump must take.</summary>
public static class DumpSchema
{
    public const string Version = "dump/1";

    /// <summary>The schema for <see cref="Version"/>. No <c>$schema</c> key, as with the others.</summary>
    public const string Version1 = """
        {
          "title": "dump/1",
          "type": "object",
          "additionalProperties": false,
          "required": ["contract", "items"],
          "properties": {
            "contract": { "const": "dump/1" },
            "items": { "type": "array", "items": {
              "type": "object", "additionalProperties": false, "required": ["title", "excerpt", "kind", "project", "reason"],
              "properties": {
                "title": { "type": "string" },
                "excerpt": { "type": "string" },
                "kind": { "enum": ["idea", "task"] },
                "project": { "type": "string" },
                "reason": { "type": "string" } } } }
          }
        }
        """;
}
