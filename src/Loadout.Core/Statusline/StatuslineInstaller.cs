using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Models.Results;

namespace Loadout.Core.Statusline;

/// <summary>Where a status line was installed, for reporting back.</summary>
/// <param name="Path">The settings file that was written.</param>
/// <param name="Command">The command Claude will now run.</param>
/// <param name="Replaced">A status line that was already configured and has been overwritten.</param>
public sealed record StatuslineInstallation(string Path, string Command, string? Replaced);

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
    /// that renders a line. Quoted because the default install path on Windows
    /// sits under a directory with a space in it, and Claude hands the string
    /// to a shell.
    /// </summary>
    public static string CommandFor(string executablePath) =>
        executablePath.Contains(' ', StringComparison.Ordinal)
            ? "\"" + executablePath + "\" statusline"
            : executablePath + " statusline";

    /// <summary>
    /// Writes the status line entry, creating the file and its directory when
    /// they do not exist yet.
    /// </summary>
    public static async Task<OperationResult<StatuslineInstallation>> InstallAsync(
        string settingsPath,
        string executablePath,
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

        root[Key] = new JsonObject
        {
            ["type"] = "command",
            ["command"] = command,
        };

        var writeResult = await WriteAsync(settingsPath, root, ct).ConfigureAwait(false);

        return writeResult.Failed
            ? OperationResult<StatuslineInstallation>.Fail(writeResult.Error!, writeResult.ExitCode)
            : OperationResult<StatuslineInstallation>.Ok(
                new StatuslineInstallation(settingsPath, command, previous));
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
