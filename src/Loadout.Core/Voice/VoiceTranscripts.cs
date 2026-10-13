using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Agents;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Voice;

/// <summary>What one session's transcript held that the person typed.</summary>
/// <param name="Session">The session's id, which is its file's name.</param>
/// <param name="Text">The prompts, one paragraph each, with pasted text and code taken out.</param>
public sealed record VoiceTranscript(string Session, string Text);

/// <summary>
/// Reads what the person typed to Claude Code, for guides about word choice
/// and spelling.
/// </summary>
/// <remarks>
/// <para>
/// A transcript holds far more than the person's words: tool results, task
/// notifications, messages from other sessions, accepted suggestions, and
/// prompts sent by Loadout and other programs. Claude marks each prompt with
/// where it came from, and only <c>typed</c> or <c>queued</c> prompts from a
/// human origin are kept. Headless runs, which is how Loadout talks to an
/// agent, are marked <c>sdk</c> and are never read. So are prompts typed
/// in the Claude desktop app, which arrive marked <c>sdk</c> as well: a
/// program can send one that looks the same, so none of them is trusted.
/// </para>
/// <para>
/// A prompt given on the command line when a session starts is recorded as
/// typed, exactly like one somebody typed, and Loadout gives one to the
/// sessions it onboards a project or a first project with. Nothing in the
/// transcript tells them apart, so a session's opening prompt, the one before
/// any reply, is never read. That loses the person's own opening prompts too,
/// about one prompt in thirty, and is the price of never learning from text
/// Loadout wrote.
/// </para>
/// <para>
/// Pasted text is wrapped in <c>pasted_content</c> tags and taken out, and so
/// is anything inside a code fence: neither is the person's prose. Text pasted
/// without either is read as theirs, which is a stated limit.
/// </para>
/// </remarks>
public sealed partial class VoiceTranscripts
{
    /// <summary>The newest sessions read.</summary>
    public const int Sessions = 200;

    private static readonly string[] Typed = ["typed", "queued"];

    private readonly IEnvironmentProvider _environment;

    public VoiceTranscripts(IEnvironmentProvider environment) => _environment = environment;

    /// <summary>Where Claude keeps its transcripts.</summary>
    public string Root => AgentHome.ClaudeProjects(_environment);

    [GeneratedRegex(@"<pasted_content id=""(?<id>[^""]*)"">.*?</pasted_content id=""\k<id>"">", RegexOptions.Singleline)]
    private static partial Regex Pasted();

    [GeneratedRegex(@"<pasted_content\b.*", RegexOptions.Singleline)]
    private static partial Regex PastedUnclosed();

    [GeneratedRegex(@"```.*?(```|\z)", RegexOptions.Singleline)]
    private static partial Regex Fenced();

    /// <summary>
    /// The newest sessions' typed prompts, newest first, leaving out sessions
    /// with nothing typed in them.
    /// </summary>
    public async Task<IReadOnlyList<VoiceTranscript>> ReadAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(Root))
        {
            return [];
        }

        List<FileInfo> files;

        try
        {
            // One level down, as the session listing reads it: subagent
            // transcripts sit deeper, and their prompts were written by an agent.
            files = new DirectoryInfo(Root)
                .EnumerateDirectories()
                .SelectMany(project => project.EnumerateFiles("*.jsonl", SearchOption.TopDirectoryOnly))
                .Where(f => !f.Name.StartsWith("agent-", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(Sessions)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var read = new List<VoiceTranscript>();

        foreach (var file in files)
        {
            string[] lines;

            try
            {
                lines = await File.ReadAllLinesAsync(file.FullName, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (TypedText(lines) is { } text)
            {
                read.Add(new VoiceTranscript(Path.GetFileNameWithoutExtension(file.Name), text));
            }
        }

        return read;
    }

    /// <summary>
    /// What the person typed in one transcript, one prompt to a paragraph, or
    /// null when there is nothing.
    /// </summary>
    public static string? TypedText(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var prompts = new List<string>();
        var replied = false;
        var opening = true;

        foreach (var line in lines)
        {
            JsonElement root;

            try
            {
                using var document = JsonDocument.Parse(line);
                root = document.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue;
            }

            if (root.ValueKind != JsonValueKind.Object || String(root, "type") is not { } type)
            {
                continue;
            }

            if (type == "assistant")
            {
                replied = true;

                continue;
            }

            if (type != "user" || !IsTyped(root))
            {
                continue;
            }

            // The opening prompt, which may have been given on the command line.
            if (opening && !replied)
            {
                opening = false;

                continue;
            }

            opening = false;

            if (Clean(TextOf(root)) is { Length: > 0 } text)
            {
                prompts.Add(text);
            }
        }

        return prompts.Count > 0 ? string.Join("\n\n", prompts) : null;
    }

    /// <summary>Takes out pasted text and code, and drops what is left if it is markup.</summary>
    public static string? Clean(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var kept = Fenced().Replace(PastedUnclosed().Replace(Pasted().Replace(text, " "), " "), " ").Trim();

        // Slash commands and other injected blocks arrive as markup.
        return kept.StartsWith('<') ? null : kept;
    }

    private static bool IsTyped(JsonElement root) =>
        String(root, "promptSource") is { } source
        && Typed.Contains(source, StringComparer.Ordinal)
        && root.TryGetProperty("origin", out var origin)
        && origin.ValueKind == JsonValueKind.Object
        && String(origin, "kind") == "human"
        && !True(root, "isMeta")
        && !True(root, "isSidechain");

    private static string? TextOf(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message)
            || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString();
        }

        if (content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var text = new StringBuilder();

        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && String(block, "type") == "text"
                && String(block, "text") is { } part)
            {
                text.Append(part).Append('\n');
            }
        }

        return text.ToString();
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool True(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
