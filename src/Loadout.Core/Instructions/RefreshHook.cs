using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models.Results;

namespace Loadout.Core.Instructions;

/// <summary>What an agent's after-edit hook handed over.</summary>
/// <param name="Files">The files the tool changed. Empty when it changed none.</param>
/// <param name="WorkingDirectory">Where the agent is running, or null when unsaid.</param>
public sealed record RefreshHookInput(IReadOnlyList<string> Files, string? WorkingDirectory)
{
    /// <summary>The first file changed, for callers that take one.</summary>
    public string? FilePath => Files.Count > 0 ? Files[0] : null;

    /// <summary>Nothing to refresh.</summary>
    public static readonly RefreshHookInput None = new([], null);
}

/// <summary>Which agent's hook is speaking, and so what it expects back.</summary>
public enum HookDialect
{
    /// <summary>Claude Code: reads a JSON document with <c>hookSpecificOutput</c>.</summary>
    Claude,

    /// <summary>Anything else: plain text, for a hook that shows or ignores stdout.</summary>
    Generic,
}

/// <summary>
/// The two ends of the after-edit hook: reading what Claude sends, and writing
/// what Claude will show the agent.
/// </summary>
/// <remarks>
/// <para>
/// Claude Code runs a <c>PostToolUse</c> hook with a JSON document on stdin
/// naming the tool and its input, and reads a JSON document from stdout; an
/// <c>additionalContext</c> string in it is added to the conversation. That
/// is the only channel by which a change made during a session can be pushed
/// at the agent rather than waited for, and this is what goes through it.
/// </para>
/// <para>
/// Nothing is written when nothing changed. An edit inside a method changes
/// no line on the map, so the hook stays silent, and the agent is told about
/// a type it added or renamed and about nothing else. A hook that spoke after
/// every edit would be a tax on every edit.
/// </para>
/// </remarks>
public static class RefreshHook
{
    /// <summary>The event this hook answers, spelled as Claude spells it.</summary>
    public const string EventName = "PostToolUse";

    /// <summary>The tools whose input names a file that changed.</summary>
    public const string Matcher = "Edit|Write|MultiEdit";

    /// <summary>The argument that puts the refresh command into hook mode.</summary>
    public const string Argument = "--hook";

    /// <summary>The hook as a synced file spells it: the launcher by name.</summary>
    public const string Portable = "loadout docs refresh " + Argument;

    /// <summary>Whether a hook command is this launcher's refresh hook, however it names the launcher.</summary>
    public static bool IsOwn(string command) =>
        command.Contains("docs refresh " + Argument, StringComparison.Ordinal);

    /// <summary>
    /// The launcher's own hook, pointed at this machine's launcher.
    /// </summary>
    /// <remarks>
    /// Whatever preceded <c>docs refresh</c> — a bare name, a path from
    /// another machine, a host and a dll from a development build — is
    /// replaced by how this launcher starts here. The arguments after it,
    /// which name the project, are kept.
    /// </remarks>
    public static string Localise(string command, string launcher)
    {
        var at = command.IndexOf("docs refresh " + Argument, StringComparison.Ordinal);

        return at < 0 ? command : launcher + " " + command[at..].Trim();
    }

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Reads the file a tool changed out of the hook's input.</summary>
    /// <remarks>
    /// Null for the file rather than a failure for anything unexpected. The
    /// hook runs after every tool call the matcher lets through, and a
    /// payload shaped differently from what this expects is a reason to say
    /// nothing, not a reason to put an error in front of the agent.
    /// </remarks>
    public static RefreshHookInput Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return RefreshHookInput.None;
        }

        try
        {
            if (JsonNode.Parse(json, nodeOptions: null, Lenient) is not JsonObject root)
            {
                return RefreshHookInput.None;
            }

            // The shapes agents actually send. Claude nests the path under the
            // tool's input; Cursor puts it at the top; a script of somebody's
            // own may send a list. Read whichever is there rather than one.
            var files = new List<string>();

            if (root["tool_input"] is JsonObject input)
            {
                Add(files, input["file_path"]);
                AddAll(files, input["files"]);
            }

            Add(files, root["file_path"]);
            Add(files, root["path"]);
            AddAll(files, root["files"]);

            var cwd = Text(root["cwd"]) ?? Text(root["workspace_root"]);

            if (cwd is null && root["workspace_roots"] is JsonArray roots && roots.Count > 0)
            {
                cwd = Text(roots[0]);
            }

            return new RefreshHookInput([.. files.Distinct(StringComparer.Ordinal)], cwd);
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException)
        {
            return RefreshHookInput.None;
        }
    }

    private static void Add(List<string> files, JsonNode? node)
    {
        if (Text(node) is { } file)
        {
            files.Add(file);
        }
    }

    private static void AddAll(List<string> files, JsonNode? node)
    {
        if (node is JsonArray list)
        {
            foreach (var item in list)
            {
                Add(files, item);
            }
        }
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0
            ? text
            : null;

    /// <summary>What to write to stdout in a dialect, or null to write nothing.</summary>
    public static string? Output(SymbolRefresh refresh, HookDialect dialect) =>
        dialect == HookDialect.Claude ? Output(refresh) : Context(refresh);

    /// <summary>
    /// What to tell the agent, or null when there is nothing worth a line.
    /// </summary>
    public static string? Context(SymbolRefresh refresh)
    {
        ArgumentNullException.ThrowIfNull(refresh);

        if (refresh.Changes.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder("The map of the code changed with that edit:");

        foreach (var change in refresh.Changes)
        {
            // A bare newline whatever the platform: this is read by a model
            // through a JSON string, not by a console.
            text.Append('\n');

            if (change.After is null)
            {
                text.Append($"`{change.Directory}` no longer holds any types.");
            }
            else
            {
                // The new line whole, rather than a diff of names: the line is
                // what the map shows, and a session replacing its copy needs
                // the replacement rather than instructions for making it.
                text.Append(change.After);
            }
        }

        return text.ToString();
    }

    /// <summary>The document to write to stdout, or null to write nothing.</summary>
    public static string? Output(SymbolRefresh refresh)
    {
        var context = Context(refresh);

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

/// <summary>Where the hook was installed, for reporting back.</summary>
/// <param name="Path">The settings file that was written.</param>
/// <param name="Command">The command Claude will run after each edit.</param>
/// <param name="AlreadyThere">Whether the file already carried it, so nothing changed.</param>
public sealed record RefreshHookInstallation(string Path, string Command, bool AlreadyThere);

/// <summary>
/// Adds and removes the after-edit hook in a Claude settings file.
/// </summary>
/// <remarks>
/// <para>
/// Edited as a document, never regenerated, for the reason the status line
/// installer gives: the file belongs to Claude and may hold anything, and a
/// settings file this corrupted would break the agent entirely. Other hooks
/// under the same event survive, and so does everything else in the file.
/// </para>
/// <para>
/// The hook is recognised by its argument rather than its whole command, so a
/// launcher that has moved is updated in place rather than installed twice.
/// </para>
/// </remarks>
public static class RefreshHookInstaller
{
    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The command to install: the launcher by name, in hook mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By name and not by path. The settings file this goes into lives in the
    /// workspace, which syncs between machines, and a path is true of one
    /// machine: the first install wrote a development build's <c>dotnet.exe</c>
    /// and its dll, which would have been wrong everywhere else. The launcher
    /// substitutes its own location when it hands the file to the agent, so
    /// the file need never say where anything is.
    /// </para>
    /// <para>
    /// The project is named on the command so the hook need not work it out
    /// from the directory on every edit: that costs two git processes and a
    /// listing of every project, and the file it lives in is that project's
    /// own.
    /// </para>
    /// </remarks>
    public static string CommandFor(string? slug = null) =>
        RefreshHook.Portable + (slug is { Length: > 0 } ? " --project " + slug : string.Empty);

    /// <summary>
    /// How long Claude waits for the hook, in seconds.
    /// </summary>
    /// <remarks>
    /// Well under Claude's own default, and well over what a refresh costs: a
    /// file's worth on most edits, one bounded scan on the first edit at a
    /// new commit. A hook that hangs past this is cut off and the agent told,
    /// which is the right outcome for something that should have taken half
    /// a second.
    /// </remarks>
    public const int TimeoutSeconds = 30;

    /// <summary>Writes the hook, creating the file when it does not exist.</summary>
    public static async Task<OperationResult<RefreshHookInstallation>> InstallAsync(
        string settingsPath,
        string? slug = null,
        CancellationToken ct = default)
    {
        var read = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<RefreshHookInstallation>.Fail(read.Error!, read.ExitCode);
        }

        var root = read.Value!;
        var command = CommandFor(slug);

        if (root["hooks"] is not JsonObject hooks)
        {
            hooks = new JsonObject();
            root["hooks"] = hooks;
        }

        if (hooks[RefreshHook.EventName] is not JsonArray entries)
        {
            entries = new JsonArray();
            hooks[RefreshHook.EventName] = entries;
        }

        foreach (var entry in entries.OfType<JsonObject>())
        {
            foreach (var hook in Hooks(entry))
            {
                if (IsOurs(hook))
                {
                    // Present means present in full. An entry written before
                    // the timeout existed is brought up to date rather than
                    // reported as already there.
                    if (CommandOf(hook) == command && hook["timeout"] is JsonValue)
                    {
                        return OperationResult<RefreshHookInstallation>.Ok(
                            new RefreshHookInstallation(settingsPath, command, AlreadyThere: true));
                    }

                    hook["command"] = command;
                    hook["timeout"] = TimeoutSeconds;

                    var updated = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

                    return updated.Failed
                        ? OperationResult<RefreshHookInstallation>.Fail(updated.Error!, updated.ExitCode)
                        : OperationResult<RefreshHookInstallation>.Ok(
                            new RefreshHookInstallation(settingsPath, command, AlreadyThere: false));
                }
            }
        }

        entries.Add(new JsonObject
        {
            ["matcher"] = RefreshHook.Matcher,
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = command,
                ["timeout"] = TimeoutSeconds,
            }),
        });

        var written = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<RefreshHookInstallation>.Fail(written.Error!, written.ExitCode)
            : OperationResult<RefreshHookInstallation>.Ok(
                new RefreshHookInstallation(settingsPath, command, AlreadyThere: false));
    }

    /// <summary>Removes the hook. True when one was there to remove.</summary>
    public static async Task<OperationResult<bool>> UninstallAsync(
        string settingsPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(settingsPath))
        {
            return OperationResult<bool>.Ok(false);
        }

        var read = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<bool>.Fail(read.Error!, read.ExitCode);
        }

        var root = read.Value!;

        if (root["hooks"] is not JsonObject hooks
            || hooks[RefreshHook.EventName] is not JsonArray entries)
        {
            return OperationResult<bool>.Ok(false);
        }

        var removed = false;

        foreach (var entry in entries.OfType<JsonObject>().ToList())
        {
            if (entry["hooks"] is not JsonArray list)
            {
                continue;
            }

            foreach (var hook in list.OfType<JsonObject>().Where(IsOurs).ToList())
            {
                list.Remove(hook);
                removed = true;
            }

            // An entry left with no hooks is ours to tidy; one with somebody
            // else's hooks in it is left exactly as it was.
            if (list.Count == 0)
            {
                entries.Remove(entry);
            }
        }

        if (!removed)
        {
            return OperationResult<bool>.Ok(false);
        }

        if (entries.Count == 0)
        {
            hooks.Remove(RefreshHook.EventName);
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }

        var written = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<bool>.Fail(written.Error!, written.ExitCode)
            : OperationResult<bool>.Ok(true);
    }

    /// <summary>Whether the file carries the hook, whatever path it names.</summary>
    public static async Task<OperationResult<bool>> IsInstalledAsync(
        string settingsPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(settingsPath))
        {
            return OperationResult<bool>.Ok(false);
        }

        var read = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<bool>.Fail(read.Error!, read.ExitCode);
        }

        var present = read.Value!["hooks"] is JsonObject hooks
            && hooks[RefreshHook.EventName] is JsonArray entries
            && entries.OfType<JsonObject>().SelectMany(Hooks).Any(IsOurs);

        return OperationResult<bool>.Ok(present);
    }

    private static IEnumerable<JsonObject> Hooks(JsonObject entry) =>
        entry["hooks"] is JsonArray list ? list.OfType<JsonObject>() : [];

    private static bool IsOurs(JsonObject hook) =>
        CommandOf(hook) is { } command && RefreshHook.IsOwn(command);

    /// <summary>
    /// A hook's command when it is a string, and null for anything else.
    /// </summary>
    /// <remarks>
    /// Somebody else's entry may carry a command that is not a string — an
    /// object, a null — and asking for a string would throw out of the middle
    /// of an install. Not ours, and left alone, is the only answer.
    /// </remarks>
    private static string? CommandOf(JsonObject hook) =>
        hook["command"] is JsonValue value && value.TryGetValue<string>(out var command)
            ? command
            : null;

    private static async Task<OperationResult<JsonObject>> ReadAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
        {
            return OperationResult<JsonObject>.Ok(new JsonObject());
        }

        try
        {
            var text = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text))
            {
                return OperationResult<JsonObject>.Ok(new JsonObject());
            }

            return JsonNode.Parse(text, nodeOptions: null, Lenient) is JsonObject root
                ? OperationResult<JsonObject>.Ok(root)
                : OperationResult<JsonObject>.Fail(
                    $"{path} does not contain a JSON object, so it is not a settings file.");
        }
        catch (JsonException exception)
        {
            return OperationResult<JsonObject>.Fail(
                $"{path} is not valid JSON and was left alone: {exception.Message}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult<JsonObject>.Fail($"Could not read {path}: {exception.Message}");
        }
    }

    private static async Task<OperationResult> WriteAsync(string path, JsonObject root, CancellationToken ct)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, root.ToJsonString(Layout), ct).ConfigureAwait(false);

            return OperationResult.Ok();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write {path}: {exception.Message}");
        }
    }
}
