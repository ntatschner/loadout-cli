using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models.Results;

namespace Loadout.Core.Statusline;

/// <summary>Where a status line was installed, for reporting back.</summary>
/// <param name="Path">The settings file that was written.</param>
/// <param name="Command">The command Claude will now run.</param>
/// <param name="Replaced">A status line that was already configured and has been overwritten.</param>
/// <param name="Refresh">The refresh interval written, in seconds, or null when none was.</param>
public sealed record StatuslineInstallation(string Path, string Command, string? Replaced, int? Refresh = null);

/// <summary>
/// Adds and removes the status line entry in a Claude settings file.
/// <para>
/// The file belongs to Claude, not to this launcher, and may hold anything —
/// permissions, hooks, environment, a theme somebody chose. So it is edited as
/// a JSON document rather than regenerated: every key other than
/// <c>statusLine</c> survives, and uninstalling puts the file back as it was
/// rather than emptying it. A settings file this tool corrupted would break the
/// agent entirely, which is a far worse outcome than having no status line.
/// </para>
/// </summary>
public static class StatuslineInstaller
{
    /// <summary>The key Claude reads, spelled exactly as the installed binary documents it.</summary>
    private const string Key = "statusLine";

    private static readonly JsonSerializerOptions Layout = new() { WriteIndented = true };

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// The command string to install: this launcher, invoked with the argument
    /// that renders a line.
    /// <para>
    /// Always quoted. Claude hands this string to a shell, and on Windows that
    /// shell is Git Bash, which reads a backslash as an escape character. An
    /// unquoted <c>C:\Users\me\AppData\Local\Programs\loadout\bin\loadout.exe</c>
    /// therefore arrives as <c>C:UsersmeAppDataLocalProgramsloadoutbinloadout.exe</c>
    /// and the status line dies with status 127, drawing nothing and reporting
    /// nothing to the person who installed it.
    /// </para>
    /// <para>
    /// Quoting used to be applied only when the path contained a space, on the
    /// reasoning that the default install path sits under Program Files. The
    /// per-user install path has no space in it, so the common case shipped
    /// broken — and silently, because the command works perfectly when run by
    /// hand and only fails through the shell.
    /// </para>
    /// <para>
    /// The backslashes are kept rather than swapped for forward slashes: inside
    /// double quotes bash leaves them alone, so the native path survives intact
    /// and stays correct if the string is ever run through cmd instead.
    /// </para>
    /// </summary>
    public static string CommandFor(string executablePath) =>
        "\"" + executablePath + "\" statusline";

    /// <summary>
    /// How often, in seconds, Claude is asked to redraw the line while nothing
    /// else is happening.
    /// <para>
    /// Without it the line is redrawn only on Claude's own events, so a time
    /// written as "3m ago" stops at whatever it said when the session went
    /// quiet, which is exactly when somebody glances at it. Thirty seconds is
    /// fine-grained enough for minutes, and it is a short process each time.
    /// </para>
    /// </summary>
    public const int RefreshSeconds = 30;

    /// <summary>
    /// The oldest Claude Code known to read <c>refreshInterval</c>.
    /// <para>
    /// The version it was confirmed in from the installed binary's own settings
    /// schema, not the one it was introduced in, which is not known. How an
    /// older Claude treats a key it does not recognise was not checked, and a
    /// settings file it refuses would break the agent rather than the line.
    /// </para>
    /// </summary>
    public static readonly Version RefreshFloor = new(2, 1, 308);

    /// <summary>
    /// Whether a <c>claude --version</c> line names a release at or past
    /// <see cref="RefreshFloor"/>. Compared as numbers: as strings, 10.0.0
    /// would sort before 2.1.308.
    /// </summary>
    public static bool SupportsRefresh(string? versionLine) =>
        ParseVersion(versionLine) is { } version && version >= RefreshFloor;

    /// <summary>
    /// The interval to install, or null for none: only for a Claude Code that
    /// reads it, and never under a profile that asks for nothing to redraw,
    /// because a line rewriting itself every half minute is noise to a screen
    /// reader.
    /// </summary>
    public static int? RefreshFor(string? versionLine, string? redraw) =>
        RedrawsNever(redraw) || !SupportsRefresh(versionLine) ? null : RefreshSeconds;

    /// <summary>Whether an accessibility redraw setting asks for nothing to redraw.</summary>
    public static bool RedrawsNever(string? redraw) =>
        string.Equals(redraw, "never", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The leading major.minor.patch of a version line such as
    /// <c>2.1.308 (Claude Code)</c>, or null when it does not start with one.
    /// </summary>
    private static Version? ParseVersion(string? versionLine)
    {
        if (string.IsNullOrWhiteSpace(versionLine))
        {
            return null;
        }

        var match = System.Text.RegularExpressions.Regex.Match(
            versionLine, @"^\s*v?(\d+)\.(\d+)\.(\d+)");

        return match.Success
            && int.TryParse(match.Groups[1].Value, out var major)
            && int.TryParse(match.Groups[2].Value, out var minor)
            && int.TryParse(match.Groups[3].Value, out var patch)
            ? new Version(major, minor, patch)
            : null;
    }

    /// <summary>
    /// Writes the status line entry, creating the file and its directory when
    /// they do not exist yet.
    /// <para>
    /// <paramref name="refreshSeconds"/> is written only when given. Left out,
    /// the key is left out too, so reinstalling after switching to a
    /// screen-reader profile removes an interval an earlier install wrote.
    /// </para>
    /// </summary>
    public static async Task<OperationResult<StatuslineInstallation>> InstallAsync(
        string settingsPath,
        string executablePath,
        int? refreshSeconds = null,
        CancellationToken ct = default)
    {
        var readResult = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (readResult.Failed)
        {
            return OperationResult<StatuslineInstallation>.Fail(
                readResult.Error!, readResult.ExitCode);
        }

        var root = readResult.Value!;

        var previous = root[Key] is JsonObject existing
            ? existing["command"]?.GetValue<string>()
            : null;

        var command = CommandFor(executablePath);

        var entry = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
        };

        if (refreshSeconds is { } seconds)
        {
            entry["refreshInterval"] = seconds;
        }

        root[Key] = entry;

        var writeResult = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

        return writeResult.Failed
            ? OperationResult<StatuslineInstallation>.Fail(writeResult.Error!, writeResult.ExitCode)
            : OperationResult<StatuslineInstallation>.Ok(
                new StatuslineInstallation(settingsPath, command, previous, refreshSeconds));
    }

    /// <summary>
    /// The refresh interval currently configured, in seconds, or null when
    /// there is none or the file cannot be read: a question about the entry,
    /// asked by a command that only reports, so a bad file is not its failure.
    /// </summary>
    public static async Task<int?> ReadRefreshAsync(
        string settingsPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        var readResult = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (readResult.Failed
            || readResult.Value![Key] is not JsonObject entry
            || entry["refreshInterval"] is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<int>(out var seconds) ? seconds
            : value.TryGetValue<double>(out var fractional) ? (int)fractional
            : null;
    }

    /// <summary>
    /// Removes the status line entry. A file without one is not an error: the
    /// caller asked for it gone, and it is gone.
    /// </summary>
    public static async Task<OperationResult<bool>> UninstallAsync(
        string settingsPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(settingsPath))
        {
            return OperationResult<bool>.Ok(false);
        }

        var readResult = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        if (readResult.Failed)
        {
            return OperationResult<bool>.Fail(readResult.Error!, readResult.ExitCode);
        }

        var root = readResult.Value!;

        if (!root.Remove(Key))
        {
            return OperationResult<bool>.Ok(false);
        }

        var writeResult = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

        return writeResult.Failed
            ? OperationResult<bool>.Fail(writeResult.Error!, writeResult.ExitCode)
            : OperationResult<bool>.Ok(true);
    }

    /// <summary>The command currently configured, or null when there is none.</summary>
    public static async Task<OperationResult<string?>> ReadCommandAsync(
        string settingsPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(settingsPath))
        {
            return OperationResult<string?>.Ok(null);
        }

        var readResult = await ReadAsync(settingsPath, ct).ConfigureAwait(false);

        return readResult.Failed
            ? OperationResult<string?>.Fail(readResult.Error!, readResult.ExitCode)
            : OperationResult<string?>.Ok(
                readResult.Value![Key] is JsonObject entry
                    ? entry["command"]?.GetValue<string>()
                    : null);
    }

    /// <summary>
    /// Reads the settings file into a mutable object, treating a missing or
    /// empty file as an empty one. Malformed JSON fails loudly rather than
    /// being silently replaced, because overwriting somebody's settings with a
    /// fresh document would lose whatever they had configured.
    /// </summary>
    private static async Task<OperationResult<JsonObject>> ReadAsync(
        string path,
        CancellationToken ct)
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
        catch (JsonException ex)
        {
            return OperationResult<JsonObject>.Fail(
                $"{path} is not valid JSON and was left alone: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<JsonObject>.Fail($"Could not read {path}: {ex.Message}");
        }
    }

    private static async Task<OperationResult> WriteAsync(
        string path,
        JsonObject root,
        CancellationToken ct)
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not write {path}: {ex.Message}");
        }
    }
}
