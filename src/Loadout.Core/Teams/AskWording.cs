using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Security;

namespace Loadout.Core.Teams;

/// <summary>
/// A permission question as each surface shows it: the plain line, the part
/// the role refused, what the command does that is worth a second look, and
/// the command itself set apart.
/// </summary>
/// <param name="Line">
/// Who wants to do what, in a sentence: "implementer (builder) wants to change
/// src/Foo.cs".
/// </param>
/// <param name="Command">
/// What it will actually run or fetch, to be shown apart from the words. Null
/// where the line already names everything, as it does for a file.
/// </param>
/// <param name="Blocked">The part of a chained command the role does not allow.</param>
/// <param name="Flags">What the command does that deserves a look, as phrases.</param>
public sealed record AskView(
    string Line,
    string? Command,
    string? Blocked,
    IReadOnlyList<string> Flags)
{
    /// <summary>The line, what was not allowed and the warnings, without the command.</summary>
    public string Summary
    {
        get
        {
            var text = new StringBuilder(Line).Append('.');

            text.Append(Blocked is { Length: > 0 } part
                ? $" Its role does not allow '{part}'."
                : " Nothing in its role allows that.");

            if (Careful(Flags) is { } careful)
            {
                text.Append(' ').Append(careful);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// The line and the warnings, as a question: what a list of things waiting
    /// shows, where the command and the refused part are one click away.
    /// </summary>
    /// <remarks>
    /// The rail, the event history and the Teams window's run view showed the
    /// whole one-sentence question, command and all, and a long command made
    /// every one of them a paragraph. The warnings stay because they are what
    /// somebody glancing at a list most needs to see.
    /// </remarks>
    public string Brief
    {
        get
        {
            var text = new StringBuilder(Line).Append('.');

            if (Careful(Flags) is { } careful)
            {
                text.Append(' ').Append(careful);
            }

            return text.Append(" Let it?").ToString();
        }
    }

    /// <summary>The warnings as one sentence, or null where there are none.</summary>
    public static string? Careful(IReadOnlyList<string>? flags) =>
        flags is { Count: > 0 }
            ? $"Careful: the command {AskWording.Joined(flags)}."
            : null;

    /// <summary>
    /// The whole question over several lines, for a terminal: the words first,
    /// then the command indented on its own, then what is being asked.
    /// </summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder(Line).Append('.').Append('\n');

            text.Append(Blocked is { Length: > 0 } part
                ? $"Not allowed by its role: {part}\n"
                : "Nothing in its role allows that.\n");

            if (Careful(Flags) is { } careful)
            {
                text.Append(careful).Append('\n');
            }

            if (Command is { Length: > 0 } command)
            {
                text.Append('\n').Append("    ").Append(command.Replace("\n", "\n    ", StringComparison.Ordinal)).Append('\n');
            }

            return text.Append('\n').Append("Let it?").ToString();
        }
    }
}

/// <summary>
/// Turns a call a node wants to make into words a person can answer.
/// </summary>
/// <remarks>
/// <para>
/// The question used to be the raw command line and nothing else:
/// "implementer (builder) wants to use Bash for 'ls build &amp;&amp; rm -rf
/// build'". Somebody deciding on a phone had to read the shell to know what
/// they were agreeing to, and the part that made the role say no was nowhere.
/// </para>
/// <para>
/// The plain line comes from the agent's own description of a shell call
/// where it gave one, and from fixed wording otherwise. The description is the
/// supervised agent's words, so it is never the only thing shown: the command
/// is always beside it, and the warnings come from the command, whatever the
/// description says. They are word lists and they miss things - a script that
/// pushes inside itself reads as running a script - which is why they are
/// called warnings and never decide anything.
/// </para>
/// </remarks>
public static partial class AskWording
{
    /// <summary>The longest description shown, in characters.</summary>
    public const int DescriptionLimit = 120;

    /// <summary>The command writes a file.</summary>
    public const string Writes = "writes to files";

    /// <summary>The command deletes something.</summary>
    public const string Deletes = "deletes files";

    /// <summary>The command sends work somewhere other people see it.</summary>
    public const string Pushes = "pushes or publishes";

    /// <summary>The command reaches past this machine.</summary>
    public const string Network = "uses the network";

    private static readonly string[] WriteWords =
    [
        "tee", "set-content", "add-content", "out-file", "cp", "mv", "copy-item", "move-item",
        "git commit", "sed -i",
    ];

    private static readonly string[] DeleteWords =
    [
        "rm", "rmdir", "del", "erase", "remove-item", "ri", "git clean", "git rm", "git branch -d",
        "git branch -D", "git reset --hard",
    ];

    private static readonly string[] PushWords =
    [
        "git push", "gh pr merge", "gh pr create", "gh release", "dotnet nuget push", "npm publish",
    ];

    private static readonly string[] NetworkWords =
    [
        "curl", "wget", "invoke-webrequest", "iwr", "invoke-restmethod", "irm", "ssh", "scp", "gh api",
        "git fetch", "git pull", "git clone", "git push",
    ];

    // Words that run the next word rather than being the command themselves.
    private static readonly string[] Runners = ["sudo", "xargs", "env", "command", "time", "nohup", "exec"];

    /// <summary>Whether a tool is a shell, whose input is a command line.</summary>
    public static bool IsShell(string tool) => tool is "Bash" or "PowerShell";

    /// <summary>
    /// What a call does, as the end of "wants to ...", or null for a tool
    /// with no wording here, which keeps the sentence it always had.
    /// </summary>
    public static string? Said(string tool, string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(tool) || Input(inputJson) is not { } input)
        {
            return null;
        }

        string? Text(string name) =>
            input.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
                ? Clean(text)
                : null;

        return tool switch
        {
            "Bash" or "PowerShell" => Text("description") is { } described
                ? Lowered(described)
                : Ran(Text("command")),
            "Edit" or "MultiEdit" => Text("file_path") is { } file ? $"change {file}" : null,
            "Write" => Text("file_path") is { } file ? $"create or overwrite {file}" : null,
            "Read" => Text("file_path") is { } file ? $"read {file}" : null,
            "NotebookEdit" => Text("notebook_path") is { } file ? $"change the notebook {file}" : null,
            "Glob" => Text("pattern") is { } pattern ? $"look for files matching {pattern}" : null,
            "Grep" => Text("pattern") is { } pattern ? $"search files for '{pattern}'" : null,
            "WebFetch" => Text("url") is { } url ? $"fetch a page from {Host(url)}" : null,
            "WebSearch" => Text("query") is { } query ? $"search the web for '{query}'" : null,
            "Task" or "Agent" => Text("description") is { } what
                ? $"start a helper agent to {Lowered(what)}"
                : "start a helper agent",
            _ => null,
        };
    }

    /// <summary>What a command does that deserves a look, from the command alone.</summary>
    public static IReadOnlyList<string> Flags(string tool, string? command)
    {
        if (!IsShell(tool) || command is not { Length: > 0 })
        {
            return [];
        }

        var parts = NodePermissions.Parts(NodePermissions.Literal(command)) ?? [command];
        var found = new List<string>();

        void Add(string flag)
        {
            if (!found.Contains(flag))
            {
                found.Add(flag);
            }
        }

        foreach (var part in parts)
        {
            var (words, writes) = NodePermissions.Redirected(part);

            if (writes)
            {
                Add(Writes);
            }

            words = Runner(NodePermissions.WithoutDirectory(NodePermissions.Unassigned(words)));

            if (Starts(words, DeleteWords))
            {
                Add(Deletes);
            }

            if (Starts(words, WriteWords))
            {
                Add(Writes);
            }

            if (Starts(words, PushWords))
            {
                Add(Pushes);
            }

            if (Starts(words, NetworkWords))
            {
                Add(Network);
            }
        }

        // In a fixed order, so the same command always reads the same way.
        return [.. ((string[])[Deletes, Pushes, Writes, Network]).Where(found.Contains)];
    }

    /// <summary>The question, as one sentence for the surfaces that show one.</summary>
    /// <remarks>
    /// Null for an ask written before any of this was carried, or about a
    /// tool with no wording here, so its caller keeps the sentence it always had.
    /// </remarks>
    public static string? Question(PendingAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (View(ask) is not { } view)
        {
            return null;
        }

        var text = new StringBuilder(view.Summary);

        if (view.Command is { Length: > 0 } command)
        {
            text.Append($" The command: '{command}'.");
        }

        return text.Append(" Let it").ToString();
    }

    /// <summary>
    /// The question in its parts, or null for one that has no wording here:
    /// a lead's question, a remedy, an MCP tool, or an ask from before.
    /// </summary>
    public static AskView? View(PendingAsk ask)
    {
        ArgumentNullException.ThrowIfNull(ask);

        if (ask.Asked is { Length: > 0 } || ask.Said is not { Length: > 0 } said)
        {
            return null;
        }

        return new AskView(
            $"{ask.Node} ({ask.Role}) wants to {said}",
            IsShell(ask.Tool) || ask.Tool == "WebFetch" ? ask.Target : null,
            ask.Blocked,
            ask.Flags ?? []);
    }

    /// <summary>
    /// What a rule agrees to, in words: "Bash(git status:*)" is "'git status'
    /// commands".
    /// </summary>
    public static string Rule(string rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var open = rule.IndexOf('(', StringComparison.Ordinal);

        if (open <= 0 || !rule.EndsWith(')'))
        {
            return rule;
        }

        var tool = rule[..open];
        var inside = rule[(open + 1)..^1];

        if (IsShell(tool))
        {
            return inside.EndsWith(":*", StringComparison.Ordinal)
                ? $"'{inside[..^2]}' commands"
                : $"'{inside}'";
        }

        if (tool == "WebFetch" && inside.EndsWith("/*", StringComparison.Ordinal))
        {
            return $"pages from {inside[..^2]}";
        }

        return rule;
    }

    /// <summary>
    /// Agent-written text made fit to put in front of somebody deciding: one
    /// line, no control characters or backticks, nothing secret, and short.
    /// </summary>
    /// <remarks>
    /// Null where nothing is left, or where the redactor gave up on it, which
    /// it does on a long run with nothing to break it: a description that
    /// cannot be checked for secrets is not shown.
    /// </remarks>
    public static string? Clean(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var plain = new StringBuilder(Math.Min(text.Length, 400));

        foreach (var c in text.Length > 400 ? text[..400] : text)
        {
            plain.Append(char.IsControl(c) || c == '`' ? ' ' : c);
        }

        var single = Spaces().Replace(plain.ToString(), " ").Trim();

        try
        {
            single = SecretRedactor.Redact(single);
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }

        if (single.Length > DescriptionLimit)
        {
            single = single[..DescriptionLimit].TrimEnd() + "…";
        }

        return single.Length > 0 ? single : null;
    }

    /// <summary>"a", "a and b", "a, b and c".</summary>
    internal static string Joined(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };

    private static Dictionary<string, JsonElement>? Input(string? inputJson)
    {
        if (string.IsNullOrWhiteSpace(inputJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(inputJson);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().ToDictionary(one => one.Name, one => one.Value.Clone(), StringComparer.Ordinal)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "List the build output" reads as "wants to list the build output". A
    // description starting with a name - "README tidy-up", "Git status" kept
    // as written where its second letter is a capital too.
    private static string Lowered(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
            ? char.ToLower(text[0], CultureInfo.InvariantCulture) + text[1..]
            : text;

    // A shell call with no description: what each part runs, by name.
    private static string? Ran(string? command)
    {
        if (command is not { Length: > 0 })
        {
            return null;
        }

        var names = new List<string>();

        foreach (var part in NodePermissions.Parts(NodePermissions.Literal(command)) ?? [command])
        {
            var words = Runner(NodePermissions.WithoutDirectory(
                NodePermissions.Unassigned(NodePermissions.Redirected(part).Words)));
            var split = words.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

            if (split.Length == 0 || split[0] is "cd" or "pushd" or "popd")
            {
                continue;
            }

            var name = split.Length > 1 && Subcommand().IsMatch(split[1]) && Tools().IsMatch(split[0])
                ? $"{split[0]} {split[1]}"
                : split[0];

            if (!names.Contains(name))
            {
                names.Add(name);
            }
        }

        return names.Count == 0
            ? "run a command"
            : "run " + string.Join(", then ", names.Select(one => Clean(one) ?? "a command"));
    }

    private static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var site) && site.Host.Length > 0 ? site.Host : url;

    // Whether a command starts with one of the words, case aside for the
    // cmdlets and exactly for git's flags, where -d and -D differ.
    private static bool Starts(string words, IEnumerable<string> list)
    {
        foreach (var one in list)
        {
            var comparison = one.Any(char.IsUpper) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (words.Length >= one.Length
                && words.StartsWith(one, comparison)
                && (words.Length == one.Length || char.IsWhiteSpace(words[one.Length])))
            {
                return true;
            }
        }

        return false;
    }

    private static string Runner(string words)
    {
        var rest = words.TrimStart();

        while (true)
        {
            var space = rest.IndexOf(' ', StringComparison.Ordinal);
            var first = space < 0 ? rest : rest[..space];

            if (space < 0 || !Runners.Contains(first, StringComparer.Ordinal))
            {
                return rest;
            }

            rest = rest[(space + 1)..].TrimStart();
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("^[a-z][a-z0-9_-]*$")]
    private static partial Regex Subcommand();

    // The tools whose second word says what they do: "git status" rather than "git".
    [GeneratedRegex("^(git|gh|dotnet|npm|npx|pnpm|yarn|docker|cargo|kubectl|az|pip|go|make|loadout)$")]
    private static partial Regex Tools();
}
