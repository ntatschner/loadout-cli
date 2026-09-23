using System.Text.Json;
using Loadout.Core.Diagnostics;
using Loadout.Models.Diagnostics;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>Where the daemon says it is, while it is there.</summary>
/// <param name="Pid">The process, for asking whether it is still running.</param>
/// <param name="StartedAt">When that process started, because identifiers are reused.</param>
/// <param name="Address">The dashboard, token and all, or null when it serves none.</param>
/// <param name="Since">When the daemon began watching.</param>
public sealed record DaemonState(
    int Pid,
    DateTimeOffset StartedAt,
    string? Address,
    DateTimeOffset Since);

/// <summary>Reading and writing the daemon's own note about itself.</summary>
/// <remarks>
/// A file rather than a lock or a port probe: a person can read it, whatever
/// wants to find the dashboard can read it, and it survives the thing that
/// wrote it going away - which is exactly the case the reader has to handle.
/// </remarks>
public static class DaemonNote
{
    /// <summary>Where the note lives.</summary>
    public static string PathFor(IPlatformPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Combine(paths.Paths.State, "teams", "daemon.json");
    }

    /// <summary>What the note says, or null when there is none to read.</summary>
    public static DaemonState? Read(IPlatformPaths paths)
    {
        var path = PathFor(paths);

        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<DaemonState>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A note nobody can read is the same as no note: whatever it says,
            // it cannot be acted on.
            return null;
        }
    }

    /// <summary>
    /// What the note says, when the daemon that wrote it is still there.
    /// </summary>
    /// <remarks>
    /// The note alone is not enough and never was: a daemon that was killed
    /// rather than stopped leaves one behind, identifiers are reused, and a
    /// machine that has restarted has a note describing somebody else's
    /// process. Asking whether that process is still the one that wrote it is
    /// the difference between pointing somebody at a dashboard and pointing
    /// them at a closed port.
    /// </remarks>
    public static DaemonState? Live(IPlatformPaths paths, IProcessInspector processes)
    {
        ArgumentNullException.ThrowIfNull(processes);

        return Read(paths) is { } note && processes.IsRunning(note.Pid, note.StartedAt)
            ? note
            : null;
    }
}

/// <summary>
/// Says whether anything is firing the schedules.
/// </summary>
/// <remarks>
/// <para>
/// The finding worth having is not "the daemon is not running". It is "you
/// have schedules and nothing is running them", which is a machine where
/// somebody wrote down what they wanted and it has been quietly not
/// happening. A person with no schedules is told nothing, because there is
/// nothing to tell them.
/// </para>
/// <para>
/// A note left behind by a daemon that has gone is reported in the same line
/// rather than as a finding of its own. It is not a fault - a machine that
/// restarted has one - and two lines about one absence is one line too many.
/// </para>
/// </remarks>
internal sealed class DaemonDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Teams";

    private readonly IScheduleService _schedules;
    private readonly IPlatformPaths _paths;
    private readonly IProcessInspector _processes;

    public DaemonDiagnosticContributor(
        IScheduleService schedules,
        IPlatformPaths paths,
        IProcessInspector processes)
    {
        _schedules = schedules;
        _paths = paths;
        _processes = processes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var note = DaemonNote.Read(_paths);
        var running = note is not null && _processes.IsRunning(note.Pid, note.StartedAt);

        if (running)
        {
            return
            [
                DiagnosticCheck.Ok(
                    Category,
                    "Daemon",
                    $"Running since {note!.Since.ToLocalTime():yyyy-MM-dd HH:mm}"
                    + (note.Address is { Length: > 0 } address
                        ? $", serving the dashboard at {address}"
                        : ", serving no dashboard")

                    // Said here because a held daemon looks exactly like a
                    // working one from outside: it is running, it serves the
                    // page, and nothing fires.
                    + (DaemonControl.Paused(_paths)
                        ? ". Paused: no schedule fires until 'loadout team daemon resume'"
                        : string.Empty)),
            ];
        }

        var listed = await _schedules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return
            [
                DiagnosticCheck.Warn(
                    Category,
                    "Schedules",
                    $"The schedules could not be read: {listed.Error}"),
            ];
        }

        var waiting = listed.Value!.Count(schedule => schedule.Enabled);

        if (waiting == 0)
        {
            return [];
        }

        return
        [
            DiagnosticCheck.Warn(
                Category,
                "Daemon",
                $"{waiting} schedule(s) are written down and nothing is firing them"
                + (note is null ? string.Empty : "; a daemon was running and is not any more")
                + ". Start one with: loadout team daemon"),
        ];
    }
}
