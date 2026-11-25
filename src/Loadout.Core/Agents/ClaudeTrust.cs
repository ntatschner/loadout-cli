using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Agents;

/// <summary>What <see cref="ClaudeTrust.Ensure"/> found and did.</summary>
public enum ClaudeTrustOutcome
{
    /// <summary>Claude Code already trusts the folder; nothing was written.</summary>
    AlreadyTrusted,

    /// <summary>The trust record was added to Claude Code's configuration file.</summary>
    Recorded,

    /// <summary>The record could not be added, so Claude Code will ask as before.</summary>
    NotRecorded,
}

/// <summary>
/// Records a folder Loadout owns as trusted in Claude Code's own configuration,
/// so a session it is added to with <c>--add-dir</c> does not stop to ask.
/// </summary>
/// <remarks>
/// <para>
/// Every interactive launch adds the project's workspace folder, and Claude
/// Code asks about each added folder it has no trust record for: "This session
/// hasn't worked here before." A yes there holds for one session only, so the
/// question came back on every launch. What makes it stop is the record Claude
/// Code writes when it is started in a folder and trusted there:
/// <c>projects[&lt;path&gt;].hasTrustDialogAccepted</c> in <c>.claude.json</c>.
/// Its check walks up from the added folder to the repository root, so one
/// record for the workspace covers every project folder in it.
/// </para>
/// <para>
/// Only for the workspace, which Loadout created and fills. Nothing else is
/// trusted on the person's behalf.
/// </para>
/// <para>
/// Claude Code keeps no lock on this file and rewrites it whole, often. So the
/// file is written only when the record is missing, which after the first
/// launch is never, and it is replaced in one step from a copy beside it, so
/// a reader never sees half a file. A session writing in the instant between
/// this read and that replace would lose its change, as it would to another
/// Claude Code process; the window is the time to rewrite one file.
/// </para>
/// </remarks>
public static class ClaudeTrust
{
    private const string Projects = "projects";
    private const string Accepted = "hasTrustDialogAccepted";

    /// <summary>How Claude Code lays the file out: two-space indent, LF, characters as they are.</summary>
    private static readonly JsonWriterOptions Layout = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The file Claude Code reads its trust records from: <c>.claude.json</c>
    /// in <c>CLAUDE_CONFIG_DIR</c> when that is set, otherwise in the home folder.
    /// </summary>
    public static string ConfigFile(IEnvironmentProvider environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        var directory = environment.GetVariable(AgentHome.Override) is { Length: > 0 } configured
            ? configured
            : environment.HomeDirectory;

        return Path.Combine(directory, ".claude.json");
    }

    /// <summary>The key Claude Code files a folder under: the full path, with forward slashes on Windows.</summary>
    public static string Key(string directory) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)).Replace('\\', '/');

    /// <summary>Makes sure Claude Code trusts <paramref name="directory"/>, writing the record only if it is missing.</summary>
    public static ClaudeTrustOutcome Ensure(IEnvironmentProvider environment, string directory)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var file = ConfigFile(environment);
        var key = Key(directory);

        // Claude Code reads a legacy .config.json in the same folder ahead of
        // this one when it holds anything. Writing here would change nothing.
        var legacy = Path.Combine(Path.GetDirectoryName(file)!, ".config.json");
        if (File.Exists(legacy) && new FileInfo(legacy).Length > 0)
        {
            return ClaudeTrustOutcome.NotRecorded;
        }

        try
        {
            // No file is a Claude Code that has never run here. Making one for
            // it could pass for its own and skip whatever it does the first time.
            if (!File.Exists(file)
                || JsonNode.Parse(File.ReadAllText(file)) is not JsonObject root)
            {
                return ClaudeTrustOutcome.NotRecorded;
            }

            if (root[Projects] is not JsonObject projects)
            {
                if (root[Projects] is not null)
                {
                    return ClaudeTrustOutcome.NotRecorded;
                }

                projects = [];
                root[Projects] = projects;
            }

            if (projects[key] is JsonObject existing)
            {
                if (existing[Accepted] is JsonValue value
                    && value.TryGetValue<bool>(out var trusted)
                    && trusted)
                {
                    return ClaudeTrustOutcome.AlreadyTrusted;
                }

                existing[Accepted] = true;
            }
            else if (projects[key] is null)
            {
                projects[key] = new JsonObject { [Accepted] = true };
            }
            else
            {
                return ClaudeTrustOutcome.NotRecorded;
            }

            Replace(file, root);
            return ClaudeTrustOutcome.Recorded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ClaudeTrustOutcome.NotRecorded;
        }
    }

    private static void Replace(string file, JsonObject root)
    {
        var copy = file + ".loadout-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            using (var stream = new FileStream(copy, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new Utf8JsonWriter(stream, Layout))
            {
                root.WriteTo(writer);
            }

            File.Move(copy, file, overwrite: true);
        }
        finally
        {
            if (File.Exists(copy))
            {
                File.Delete(copy);
            }
        }
    }
}
