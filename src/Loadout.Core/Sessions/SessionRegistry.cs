using System.Globalization;
using System.Text;
using System.Text.Json;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>A session that was running when somebody last looked.</summary>
/// <param name="LaunchId">The launch this belongs to, as the ledger files it.</param>
/// <param name="ProjectSlug">The project, by the name the registry knows it by.</param>
/// <param name="ProjectName">The project as a person calls it.</param>
/// <param name="Agent">Adapter that is running.</param>
/// <param name="Worktree">Working tree it was launched into, or null for the main one.</param>
/// <param name="WorkingDirectory">Where the agent was started.</param>
/// <param name="ProcessId">The launcher process holding this session open.</param>
/// <param name="ProcessStartedAt">
/// When that process started, which is what tells a live session from a
/// recycled identifier wearing its number.
/// </param>
/// <param name="StartedAt">When the session began.</param>
public sealed record RunningSession(
    string LaunchId,
    string ProjectSlug,
    string ProjectName,
    string Agent,
    string? Worktree,
    string WorkingDirectory,
    int ProcessId,
    DateTimeOffset ProcessStartedAt,
    DateTimeOffset StartedAt);

/// <summary>What the launcher knows about a session it is about to start.</summary>
/// <remarks>
/// Which process is holding the session open, and when, are not asked of the
/// caller. Every caller would answer them the same way, and a caller that
/// answered differently would be recording something untrue about this machine.
/// </remarks>
/// <param name="LaunchId">The launch this belongs to, as the ledger files it.</param>
/// <param name="ProjectSlug">The project, by the name the registry knows it by.</param>
/// <param name="ProjectName">The project as a person calls it.</param>
/// <param name="Agent">Adapter about to run.</param>
/// <param name="Worktree">Working tree being launched into, or null for the main one.</param>
/// <param name="WorkingDirectory">Where the agent is being started.</param>
public sealed record NewSession(
    string LaunchId,
    string ProjectSlug,
    string ProjectName,
    string Agent,
    string? Worktree,
    string WorkingDirectory);

/// <summary>Which sessions are running on this machine right now.</summary>
public interface ISessionRegistry
{
    /// <summary>Where the entries are kept, so a person can go and look.</summary>
    string Path { get; }

    /// <summary>Claims an entry for a session that is about to run.</summary>
    Task RegisterAsync(NewSession session, CancellationToken ct = default);

    /// <summary>Gives up the entry for a session that has finished.</summary>
    Task ReleaseAsync(string launchId, CancellationToken ct = default);

    /// <summary>
    /// The sessions still running, oldest first.
    /// </summary>
    /// <remarks>
    /// Entries whose process is gone are left out. They are not deleted here:
    /// reading is a question, and a question that quietly tidies up is a
    /// question that behaves differently depending on who asked it first.
    /// </remarks>
    Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// One small file per running session, in a directory of them.
/// </summary>
/// <remarks>
/// <para>
/// A file each rather than one list, because several launchers can be running at
/// once — that is the point of launching into worktrees — and two processes
/// rewriting one list will eventually lose an entry. Creating and deleting a
/// named file is the smallest operation the filesystem offers that two
/// processes can do at the same time without arranging it between themselves.
/// </para>
/// <para>
/// An entry is a claim, not a fact. A session that is killed, or whose machine
/// is turned off, never deletes its file, so every entry is checked against the
/// process that wrote it before it is reported. Without that the record would be
/// wrong in exactly the circumstances somebody consults it — after something went
/// wrong — which is the sort of instrument that has to be validated against a
/// known answer before it is trusted.
/// </para>
/// <para>
/// Stale entries are cleared when the next session registers. That is a moment
/// something is already being written, it needs no separate schedule, and it
/// keeps the reading path free of surprises.
/// </para>
/// </remarks>
internal sealed class SessionRegistry : ISessionRegistry
{
    private readonly IFilePermissions _permissions;
    private readonly IProcessInspector _processes;
    private readonly TimeProvider _time;

    public SessionRegistry(
        IPlatformPaths paths,
        IFilePermissions permissions,
        IProcessInspector processes,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _permissions = permissions;
        _processes = processes;
        _time = time;

        Path = System.IO.Path.Combine(paths.Paths.State, "launches", "running");
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public async Task RegisterAsync(NewSession session, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(session.LaunchId))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path);
            _permissions.RestrictDirectoryToCurrentUser(Path);

            await ClearAbandonedAsync(ct).ConfigureAwait(false);

            var file = EntryPath(session.LaunchId);

            await File.WriteAllTextAsync(
                file,
                JsonSerializer.Serialize(Entry.From(new RunningSession(
                    session.LaunchId,
                    session.ProjectSlug,
                    session.ProjectName,
                    session.Agent,
                    session.Worktree,
                    session.WorkingDirectory,
                    _processes.CurrentProcessId,
                    _processes.CurrentProcessStartedAt,
                    _time.GetUtcNow()))),
                Encoding.UTF8,
                ct).ConfigureAwait(false);

            // It says what somebody is working on and where the code lives.
            _permissions.RestrictToCurrentUser(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Bookkeeping does not get to stop a session starting. The cost of
            // failing here is a session missing from a list, not a session that
            // did not run.
        }
    }

    /// <inheritdoc />
    public Task ReleaseAsync(string launchId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(EntryPath(launchId));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind, and the process check will see it for what it is.
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunningSession>> ListAsync(CancellationToken ct = default)
    {
        var running = new List<RunningSession>();

        foreach (var (session, _) in await ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (_processes.IsRunning(session.ProcessId, session.ProcessStartedAt))
            {
                running.Add(session);
            }
        }

        return running.OrderBy(session => session.StartedAt).ToList();
    }

    /// <summary>Deletes the entries whose processes are gone.</summary>
    private async Task ClearAbandonedAsync(CancellationToken ct)
    {
        foreach (var (session, file) in await ReadAllAsync(ct).ConfigureAwait(false))
        {
            if (_processes.IsRunning(session.ProcessId, session.ProcessStartedAt))
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Another launcher may be tidying the same entry. Whoever wins,
                // the entry goes, and neither of them needs to hear about it.
            }
        }
    }

    /// <summary>Every entry on disk, live or not, with the file it came from.</summary>
    private async Task<IReadOnlyList<(RunningSession Session, string File)>> ReadAllAsync(
        CancellationToken ct)
    {
        if (!Directory.Exists(Path))
        {
            return [];
        }

        var found = new List<(RunningSession, string)>();

        IEnumerable<string> files;

        try
        {
            files = Directory.EnumerateFiles(Path, "*.json");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (var file in files)
        {
            Entry? entry;

            try
            {
                entry = JsonSerializer.Deserialize<Entry>(
                    await File.ReadAllTextAsync(file, ct).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Half written, or being written now. It will be readable next
                // time or it will be cleared as abandoned; either way one
                // unreadable entry costs that entry.
                continue;
            }

            if (entry?.ToSession() is { } session)
            {
                found.Add((session, file));
            }
        }

        return found;
    }

    private string EntryPath(string launchId) =>
        System.IO.Path.Combine(Path, launchId + ".json");

    /// <summary>One entry file.</summary>
    private sealed record Entry(
        string LaunchId,
        string Slug,
        string Project,
        string Agent,
        string? Worktree,
        string Directory,
        int Pid,
        string ProcessStarted,
        string Started)
    {
        public static Entry From(RunningSession session) => new(
            session.LaunchId,
            session.ProjectSlug,
            session.ProjectName,
            session.Agent,
            session.Worktree,
            session.WorkingDirectory,
            session.ProcessId,
            Moment(session.ProcessStartedAt),
            Moment(session.StartedAt));

        public RunningSession? ToSession() =>
            string.IsNullOrWhiteSpace(LaunchId)
            || !TryMoment(ProcessStarted, out var processStarted)
            || !TryMoment(Started, out var started)
                ? null
                : new RunningSession(
                    LaunchId,
                    Slug,
                    Project,
                    Agent,
                    Worktree,
                    Directory,
                    Pid,
                    processStarted,
                    started);

        private static string Moment(DateTimeOffset when) =>
            when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static bool TryMoment(string? text, out DateTimeOffset when) =>
            DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out when);
    }
}
