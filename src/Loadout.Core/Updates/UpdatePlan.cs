using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Teams.Daemon;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Updates;

/// <summary>What an update or a roll back asked from Studio is to do.</summary>
public enum UpdateKind
{
    /// <summary>Put a newer release in place.</summary>
    Install,

    /// <summary>Put back the binary an archive update kept.</summary>
    RollBack,
}

/// <summary>
/// An update waiting for the daemon to restart, and then for the helper that
/// carries it out.
/// </summary>
/// <param name="Kind">Installing a release, or rolling one back.</param>
/// <param name="Channel">How this copy was installed, which decides how it is replaced.</param>
/// <param name="From">The version running when it was asked for.</param>
/// <param name="Version">The version being installed, or null for a roll back.</param>
/// <param name="Payload">The verified binary or MSI, or null for a roll back.</param>
/// <param name="Executable">The installed executable being replaced.</param>
/// <param name="AskedAt">When it was asked for.</param>
/// <param name="Daemon">
/// How to start the daemon again afterwards, command first. Written by the
/// daemon as it hands over, since it is the only thing that knows how it was
/// started; null until then.
/// </param>
/// <param name="DaemonPid">The daemon handing over, for the helper to wait for it to go.</param>
/// <param name="DaemonStartedAt">When that process started, because identifiers are reused.</param>
/// <remarks>
/// <para>
/// A file beside the daemon's note, like its other controls, because three
/// processes need it in turn: the command that verified the download, the
/// daemon that has to finish its runs first, and the helper that replaces the
/// daemon once it has gone. None of them can reach into another.
/// </para>
/// <para>
/// Nothing in it is secret, and nothing in it is trusted further than the
/// payload's own path: the helper installs only what is in the staging folder
/// this launcher verified.
/// </para>
/// </remarks>
public sealed record UpdatePlan(
    UpdateKind Kind,
    InstallChannel Channel,
    string From,
    string? Version,
    string? Payload,
    string Executable,
    DateTimeOffset AskedAt,
    IReadOnlyList<string>? Daemon = null,
    int? DaemonPid = null,
    DateTimeOffset? DaemonStartedAt = null)
{
    /// <summary>The plan's file name, beside the daemon's note.</summary>
    public const string FileName = "daemon-update.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    /// <summary>Where the plan is kept.</summary>
    public static string PathFor(IPlatformPaths paths) =>
        Path.Combine(DaemonControl.DirectoryFor(paths), FileName);

    /// <summary>The plan waiting, or null when there is none or it cannot be read.</summary>
    public static UpdatePlan? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(path), Json)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A plan nobody can read is no plan: the daemon restarts as it
            // would have, on the version it is, which is the safe way to be wrong.
            return null;
        }
    }

    /// <summary>The plan waiting for this machine's daemon.</summary>
    public static UpdatePlan? Read(IPlatformPaths paths) => Read(PathFor(paths));

    /// <summary>Writes the plan, replacing any before it.</summary>
    public async Task WriteAsync(string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Through a temporary file, so the daemon reading it mid-write sees
        // the old plan or the new one and never half of one.
        var temporary = path + ".tmp";

        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(this, Json), ct).ConfigureAwait(false);

        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Takes the plan back, saying whether there was one.</summary>
    public static bool Clear(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>The plan as the daemon hands it over: how to start it again, and which process to wait for.</summary>
    public UpdatePlan WithDaemon(IReadOnlyList<string> daemon, int pid, DateTimeOffset startedAt) =>
        this with { Daemon = daemon, DaemonPid = pid, DaemonStartedAt = startedAt };

    /// <summary>A few words for what it does, for the daemon and the page to say.</summary>
    [JsonIgnore]
    public string Described => Kind == UpdateKind.RollBack
        ? $"rolling back from {From}"
        : $"updating to {Version}";
}
