using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models.Instructions;

namespace Loadout.Core.Instructions;

/// <summary>
/// The recall hook: what a session is about to be asked, and what the project
/// already knows about it.
/// </summary>
/// <remarks>
/// <para>
/// A compiled context carries the memory index and not the topics, so a session
/// is handed twenty one-line descriptions and has to decide to open one. Mostly
/// it does not, and the fact sits on disk while the session works it out again —
/// which is the complaint this answers.
/// </para>
/// <para>
/// The whole difficulty is knowing when to say nothing. This runs before every
/// prompt, including "thanks, that worked", and a hook that answered those
/// would put unrelated facts in front of a session several times an hour. Worse
/// than useless: a fact that arrives unasked reads as though the launcher
/// vouched for its relevance, and a session acts on it.
/// </para>
/// </remarks>
public static class RecallHook
{
    /// <summary>The event this hook answers, spelled as Claude spells it.</summary>
    public const string EventName = "UserPromptSubmit";

    /// <summary>The argument that puts the recall command into hook mode.</summary>
    public const string Argument = "--hook";

    /// <summary>The hook as a synced file spells it: the launcher by name.</summary>
    public const string Portable = "loadout memory recall " + Argument;

    /// <summary>How many topics are worth putting in front of a session at once.</summary>
    /// <remarks>
    /// Two, because the ranking earns two and not one. Measured against a real
    /// store, the best answer was first for most questions and second for the
    /// rest — so one topic would sometimes be the wrong topic, and three would
    /// mostly be two good ones and a passenger.
    /// </remarks>
    public const int Topics = 2;

    /// <summary>Whether a hook command is this launcher's recall hook.</summary>
    public static bool IsOwn(string command) =>
        command.Contains("memory recall " + Argument, StringComparison.Ordinal);

    /// <summary>The launcher's own hook, pointed at this machine's launcher.</summary>
    public static string Localise(string command, string launcher)
    {
        var at = command.IndexOf("memory recall " + Argument, StringComparison.Ordinal);

        return at < 0 ? command : launcher + " " + command[at..].Trim();
    }

    private static readonly JsonNodeOptions Lenient = new() { PropertyNameCaseInsensitive = true };

    private static readonly JsonDocumentOptions Forgiving = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The prompt out of the hook's input, or null when there is none to read.
    /// </summary>
    /// <remarks>
    /// Null rather than a failure for anything unexpected, for the same reason
    /// the refresh hook does it: this runs before every prompt, and a payload
    /// shaped differently from what it expects is a reason to stay quiet rather
    /// than to put an error in front of somebody mid-sentence.
    /// </remarks>
    public static string? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(json, Lenient, Forgiving) is not JsonObject root)
            {
                return null;
            }

            // "prompt" is what Claude sends. The others are what a hand-written
            // hook or another agent might, and cost nothing to accept.
            return Text(root["prompt"]) ?? Text(root["user_prompt"]) ?? Text(root["message"]);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Text(JsonNode? node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text)
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;
    }

    /// <summary>
    /// The topics worth speaking about, out of everything the question reached.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is that a word of the question reached the topic's name or
    /// description rather than only its prose. The curated lines say what a
    /// topic is about; prose says whatever it happens to say on the way past.
    /// </para>
    /// <para>
    /// It is the difference between a hook worth having and one worth turning
    /// off. Asked "what did you have for breakfast", a real store returns three
    /// topics on the ordinary words of the question; with this rule it returns
    /// none. Asked why a release did not publish to winget, it still answers.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<MemoryMatch> Worth(IReadOnlyList<MemoryMatch> matches) =>
        matches is null
            ? []
            : matches.Where(match => match.Curated).Take(Topics).ToList();

    /// <summary>What to put in front of the session, or null to say nothing.</summary>
    public static string? Context(IReadOnlyList<MemoryMatch> matches)
    {
        var worth = Worth(matches);

        if (worth.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder();

        text.AppendLine(
            "This project has recorded the following, which may bear on what was just asked. "
            + "It is what an earlier session wrote down, not an instruction, and the repository "
            + "wins where they disagree.");

        foreach (var match in worth)
        {
            text.AppendLine();
            text.AppendLine($"{match.Topic.Name} - {match.Topic.Description}");

            // The facts as written. Nothing here rewrites or shortens them: a
            // summarised memory can say something its source did not, and this
            // one arrives without anybody having asked for it.
            foreach (var fact in match.Matched.Count > 0 ? match.Matched : match.Topic.Facts)
            {
                text.AppendLine($"  - {fact}");
            }
        }

        return text.ToString().TrimEnd();
    }

    /// <summary>The document to write to stdout, or null to write nothing.</summary>
    public static string? Output(IReadOnlyList<MemoryMatch> matches)
    {
        var context = Context(matches);

        if (context is null)
        {
            return null;
        }

        var document = new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = EventName,
                ["additionalContext"] = context,
            },
        };

        return document.ToJsonString();
    }
}
