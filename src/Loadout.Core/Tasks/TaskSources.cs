using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>
/// How a task's source is written: <c>run:&lt;run id&gt;</c>,
/// <c>idea:&lt;idea id&gt;</c> or <c>transcript:&lt;session id&gt;</c>.
/// </summary>
/// <remarks>
/// One piece of text rather than two fields, so it travels through every
/// surface that already takes a task's fields as text: a batch, a command-line
/// option and a tool argument all carry it the same way.
/// </remarks>
public static class TaskSources
{
    /// <summary>The words each kind is written with, in the order they are documented.</summary>
    public static IReadOnlyList<string> Kinds => ["run", "idea", "transcript"];

    /// <summary>A source read from what somebody wrote; or why it is not one.</summary>
    public static (TaskSource? Source, string? Refusal) Parse(string? said)
    {
        var text = said?.Trim() ?? string.Empty;
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var kind = colon > 0 ? text[..colon].Trim().ToLowerInvariant() : string.Empty;
        var id = colon > 0 ? text[(colon + 1)..].Trim() : string.Empty;

        TaskSourceKind? parsed = kind switch
        {
            "run" => TaskSourceKind.Run,
            "idea" => TaskSourceKind.Idea,
            "transcript" => TaskSourceKind.Transcript,
            _ => null,
        };

        // An id is a name, never prose: a space would mean somebody wrote a
        // sentence where the run or idea it came from was asked for.
        return parsed is { } found && id.Length > 0 && !id.Any(char.IsWhiteSpace)
            ? (new TaskSource { Kind = found, Id = id }, null)
            : (null, $"'{text}' is not a source. Name one as run:<run id>, idea:<idea id> or transcript:<session id>.");
    }

    /// <summary>What an agent writes to name the conversation it is in, without knowing its id.</summary>
    public const string ThisConversation = "this-conversation";

    /// <summary>
    /// A source as an agent gives it, where <see cref="ThisConversation"/> names
    /// the conversation the tool is serving; or why it is not one.
    /// </summary>
    /// <param name="said">What the agent wrote.</param>
    /// <param name="conversation">The session id of the conversation being served, or null when it is not known.</param>
    public static (TaskSource? Source, string? Refusal) Parse(string? said, string? conversation)
    {
        if (!string.Equals(said?.Trim(), ThisConversation, StringComparison.OrdinalIgnoreCase))
        {
            return Parse(said);
        }

        return conversation is { Length: > 0 } id && !id.Any(char.IsWhiteSpace)
            ? (new TaskSource { Kind = TaskSourceKind.Transcript, Id = id.Trim() }, null)
            : (null, $"'{ThisConversation}' needs to know which conversation this is, and nothing here says. "
                + "Name it as transcript:<session id>, or leave the source out.");
    }

    /// <summary>A source written out, as <see cref="Parse(string?)"/> reads it.</summary>
    public static string Name(TaskSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return $"{source.Kind.ToString().ToLowerInvariant()}:{source.Id}";
    }

    /// <summary>Whether two sources name the same run, idea or conversation.</summary>
    public static bool Same(TaskSource? left, TaskSource? right) =>
        left is not null && right is not null && left.Kind == right.Kind
        && string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
}
