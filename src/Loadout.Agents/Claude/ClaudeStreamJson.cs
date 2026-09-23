using System.Globalization;
using System.Text.Json;
using Loadout.Models.Agents;

namespace Loadout.Agents.Claude;

/// <summary>
/// Claude Code's stream-json wire format, in both directions: the one message
/// shape the launcher sends, and the event lines it reads back.
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of the launcher's knowledge of that format, kept in one
/// place so a change to it is a change here. The shapes come from driving
/// Claude Code 2.1.270 with <c>-p --input-format stream-json --output-format
/// stream-json --verbose</c> and reading what came back, not from
/// documentation: the documented event list omits several that a real run
/// produces (<c>hook_started</c>, <c>rate_limit_event</c>,
/// <c>thinking_tokens</c>, <c>permission_denied</c>), and the result event
/// carries twenty-seven keys where the documentation names eight.
/// </para>
/// <para>
/// Every line becomes at least one event. An unrecognised type is a
/// <see cref="HeadlessOther"/>, a line that is not JSON is a
/// <see cref="HeadlessUnparsed"/>, and neither is an error: an agent that
/// prints something new has changed, and the run needs to be able to say so.
/// </para>
/// </remarks>
internal static class ClaudeStreamJson
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    /// <summary>
    /// One message from the person, as a single line ready to write to the
    /// agent's input.
    /// </summary>
    /// <remarks>
    /// The serialiser escapes every newline inside the text, so the result is
    /// always one line, which is what a line-delimited protocol needs and
    /// what a message containing a code block would otherwise break.
    /// </remarks>
    public static string UserMessage(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return JsonSerializer.Serialize(
            new { type = "user", message = new { role = "user", content = text } },
            Compact);
    }

    /// <summary>Reads one line the agent wrote as the events it carries.</summary>
    /// <remarks>
    /// One line can carry more than one event: an assistant message holds a
    /// list of content blocks, and a text block followed by a tool call in the
    /// same message is two things the run wants to know about separately.
    /// </remarks>
    public static IReadOnlyList<HeadlessEvent> Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var trimmed = line.Trim();

        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return [new HeadlessUnparsed(line)];
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(trimmed);
        }
        catch (JsonException)
        {
            return [new HeadlessUnparsed(line)];
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return [new HeadlessUnparsed(line)];
            }

            var type = Text(root, "type");
            var subtype = Text(root, "subtype");

            return type switch
            {
                "system" when subtype == "init" => [Started(line, root)],
                "system" when subtype == "permission_denied" =>
                    [new HeadlessPermissionDenied(line, Text(root, "tool_name") ?? Text(root, "tool"))],
                "assistant" => AssistantBlocks(line, root),
                "user" => UserBlocks(line, root),
                "result" => [Result(line, root, subtype)],
                _ => [new HeadlessOther(line, type ?? "(no type)", subtype)],
            };
        }
    }

    private static HeadlessStarted Started(string line, JsonElement root)
    {
        var servers = new List<HeadlessMcpServer>();

        if (root.TryGetProperty("mcp_servers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var server in list.EnumerateArray())
            {
                servers.Add(new HeadlessMcpServer(
                    Text(server, "name") ?? "(unnamed)",
                    Text(server, "status") ?? "(no status)"));
            }
        }

        return new HeadlessStarted(
            line,
            Text(root, "session_id") ?? string.Empty,
            Text(root, "model"),
            Text(root, "permissionMode"),
            servers);
    }

    private static IReadOnlyList<HeadlessEvent> AssistantBlocks(string line, JsonElement root)
    {
        var fromSubagent = root.TryGetProperty("parent_tool_use_id", out var parent)
            && parent.ValueKind == JsonValueKind.String;

        var events = new List<HeadlessEvent>();

        foreach (var block in ContentBlocks(root))
        {
            switch (Text(block, "type"))
            {
                case "text":
                    events.Add(new HeadlessText(line, Text(block, "text") ?? string.Empty, fromSubagent));
                    break;

                case "tool_use":
                    var input = block.TryGetProperty("input", out var i) ? i.GetRawText() : "{}";
                    events.Add(new HeadlessToolUse(line, Text(block, "name") ?? "(unnamed)", input, fromSubagent));
                    break;

                default:
                    // Thinking blocks and anything newer. Worth a trace of
                    // their presence, not their contents.
                    break;
            }
        }

        return events.Count > 0 ? events : [new HeadlessOther(line, "assistant", FirstBlockType(root))];
    }

    private static IReadOnlyList<HeadlessEvent> UserBlocks(string line, JsonElement root)
    {
        var events = new List<HeadlessEvent>();

        foreach (var block in ContentBlocks(root))
        {
            if (Text(block, "type") != "tool_result")
            {
                continue;
            }

            var isError = block.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;

            events.Add(new HeadlessToolResult(
                line,
                Text(block, "tool_use_id") ?? string.Empty,
                isError,
                ResultContent(block)));
        }

        return events.Count > 0 ? events : [new HeadlessOther(line, "user", FirstBlockType(root))];
    }

    private static HeadlessResult Result(string line, JsonElement root, string? subtype)
    {
        var usage = root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object
            ? new HeadlessUsage(
                Number(u, "input_tokens"),
                Number(u, "cache_creation_input_tokens"),
                Number(u, "cache_read_input_tokens"),
                Number(u, "output_tokens"))
            : new HeadlessUsage(0, 0, 0, 0);

        var denials = new List<HeadlessDenial>();

        if (root.TryGetProperty("permission_denials", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var denial in list.EnumerateArray())
            {
                denials.Add(new HeadlessDenial(
                    Text(denial, "tool_name") ?? "(unnamed)",
                    Text(denial, "tool_use_id") ?? string.Empty,
                    denial.TryGetProperty("tool_input", out var ti) ? ti.GetRawText() : "{}"));
            }
        }

        var errors = new List<string>();

        if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
        {
            foreach (var error in errs.EnumerateArray())
            {
                errors.Add(error.ValueKind == JsonValueKind.String ? error.GetString()! : error.GetRawText());
            }
        }

        string? structured = null;

        if (root.TryGetProperty("structured_output", out var so) && so.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
        {
            structured = so.GetRawText();
        }

        return new HeadlessResult(
            line,
            subtype ?? "(no subtype)",
            root.TryGetProperty("is_error", out var ie) && ie.ValueKind == JsonValueKind.True,
            (int)Number(root, "num_turns"),
            TimeSpan.FromMilliseconds(Number(root, "duration_ms")),
            Money(root, "total_cost_usd"),
            usage,
            structured,
            denials,
            errors,
            Text(root, "session_id"));
    }

    private static IEnumerable<JsonElement> ContentBlocks(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || !message.TryGetProperty("content", out var content))
        {
            yield break;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                yield return block;
            }
        }
    }

    private static string? FirstBlockType(JsonElement root) =>
        ContentBlocks(root).Select(block => Text(block, "type")).FirstOrDefault();

    /// <summary>
    /// A tool result's content is either a string or a list of text blocks,
    /// depending on the tool. Either way the run wants the text.
    /// </summary>
    private static string ResultContent(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            return string.Join(
                "\n",
                content.EnumerateArray()
                    .Select(part => Text(part, "text") ?? part.GetRawText()));
        }

        return content.GetRawText();
    }

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt64(out var number)
            ? number
            : 0;

    /// <summary>
    /// Money as decimal, parsed from the number's own text rather than through
    /// a double, so 0.0641688 stays 0.0641688.
    /// </summary>
    private static decimal Money(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
        && decimal.TryParse(value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture, out var money)
            ? money
            : 0m;
}
