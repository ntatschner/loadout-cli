using System.Globalization;
using System.Text;
using System.Text.Json;
using Loadout.Core.Security;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions;

/// <summary>What a launch was given, supplied by the launcher at the moment it knows.</summary>
/// <param name="ProjectSlug">The project, by the name the registry knows it by.</param>
/// <param name="ProjectName">The project as a person calls it.</param>
/// <param name="Agent">Adapter about to run.</param>
/// <param name="Task">What the user said they were doing, or null when they said nothing.</param>
/// <param name="Profile">Context profile applied, or null for the base context.</param>
/// <param name="Worktree">Working tree being launched into, or null for the main one.</param>
/// <param name="Instructions">
/// What the resolver chose for this launch, or null when the specialist layer
/// was not in play. Passed whole rather than as four separate fields so that the
/// mapping from a resolution to a record lives in one place and cannot disagree
/// with itself.
/// </param>
public sealed record NewLaunch(
    string ProjectSlug,
    string ProjectName,
    string Agent,
    string? Task,
    string? Profile,
    string? Worktree,
    EffectiveInstructions? Instructions);

/// <summary>A record of every launch this machine started.</summary>
public interface ILaunchLedger
{
    /// <summary>Where the record is kept, so a person can go and look.</summary>
    string Path { get; }

    /// <summary>
    /// Writes down a launch that is about to start, and returns the identifier
    /// its ending will be filed under.
    /// </summary>
    Task<string> RecordStartAsync(NewLaunch launch, CancellationToken ct = default);

    /// <summary>
    /// Closes the launch with that identifier.
    /// </summary>
    /// <param name="launchId">What <see cref="RecordStartAsync"/> returned.</param>
    /// <param name="exitCode">
    /// The agent's own exit status, or null when the agent never ran.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordEndAsync(string launchId, int? exitCode, CancellationToken ct = default);

    /// <summary>Launches started on or after a moment, oldest first.</summary>
    Task<OperationResult<IReadOnlyList<LaunchRecord>>> ReadAsync(
        DateTimeOffset since,
        CancellationToken ct = default);
}

/// <summary>
/// The launch ledger: one JSON object per line, two lines per launch.
/// </summary>
/// <remarks>
/// <para>
/// Append-only, and two lines rather than one rewritten in place, because a
/// launch can end in ways that never come back through this code — the machine
/// is shut down, the terminal is closed, the process is killed. A file that is
/// only ever appended to survives all of those with the loss of one ending
/// rather than the loss of the file, and the reader joins a start to its end by
/// identifier. It is the same shape the agents' own transcripts take, and the
/// same reasoning as <c>usage/reported.jsonl</c> next door.
/// </para>
/// <para>
/// Machine-local, under the state directory. What this machine launched joins to
/// the transcripts and usage figures that are also machine-local, and a file
/// appended to on every launch does not belong in a workspace repository that
/// several machines push to.
/// </para>
/// <para>
/// Nothing here fails a launch. A ledger that cannot be written is a report that
/// will be missing a line; a ledger that throws is a session that did not start
/// because of bookkeeping. Every write swallows what it cannot do, and the
/// reader is the only place that reports a problem, where somebody asked.
/// </para>
/// </remarks>
internal sealed class LaunchLedger : ILaunchLedger
{
    private readonly IFilePermissions _permissions;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LaunchLedger(IPlatformPaths paths, IFilePermissions permissions, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(paths);

        _permissions = permissions;
        _time = time;

        Path = System.IO.Path.Combine(paths.Paths.State, "launches", "ledger.jsonl");
    }

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public async Task<string> RecordStartAsync(NewLaunch launch, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(launch);

        var id = Guid.NewGuid().ToString("N");

        // The task is the user's own words and reaches a file that is kept.
        // Memory, compression and import all refuse a credential before writing
        // rather than redacting one afterwards, and a record of what somebody
        // typed is no different. The pattern name is kept so the gap in the
        // record explains itself; the text is not.
        var matched = SecretScanner.Match(launch.Task);

        await AppendAsync(
            new Row(
                Kind: StartKind,
                Id: id,
                When: Moment(_time.GetUtcNow()),
                Slug: launch.ProjectSlug,
                Project: launch.ProjectName,
                Agent: launch.Agent,
                Mode: launch.Instructions?.Mode,
                Task: matched.Count > 0 ? null : launch.Task,
                Withheld: matched.Count > 0 ? matched[0] : null,
                Profile: launch.Profile,
                Worktree: launch.Worktree,
                Specialists: launch.Instructions?.Selected
                    .Select(selection => selection.Specialist.Id)
                    .ToList(),
                Tokens: launch.Instructions?.Budget.EstimatedTokens ?? 0,
                Budget: launch.Instructions?.Budget.TokenBudget ?? 0),
            ct).ConfigureAwait(false);

        return id;
    }

    /// <inheritdoc />
    public async Task RecordEndAsync(string launchId, int? exitCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(launchId))
        {
            return;
        }

        await AppendAsync(
            new Row(
                Kind: EndKind,
                Id: launchId,
                When: Moment(_time.GetUtcNow()),
                Exit: exitCode),
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<LaunchRecord>>> ReadAsync(
        DateTimeOffset since,
        CancellationToken ct = default)
    {
        if (!File.Exists(Path))
        {
            return OperationResult<IReadOnlyList<LaunchRecord>>.Ok([]);
        }

        // Starts in the order they were written, so the report reads
        // chronologically without sorting a second time, and endings applied to
        // them as they are met. An ending whose start fell before the window, or
        // whose start was never written, has nothing to attach to and is
        // dropped rather than invented.
        var started = new List<LaunchRecord>();
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);

        try
        {
            using var reader = new StreamReader(File.OpenRead(Path));

            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Row? row;

                try
                {
                    row = JsonSerializer.Deserialize<Row>(line);
                }
                catch (JsonException)
                {
                    // A line half-written when something was killed mid-append.
                    continue;
                }

                if (row is null || !TryMoment(row.When, out var when))
                {
                    continue;
                }

                if (string.Equals(row.Kind, StartKind, StringComparison.Ordinal))
                {
                    if (when < since)
                    {
                        continue;
                    }

                    byId[row.Id] = started.Count;

                    started.Add(new LaunchRecord(
                        row.Id,
                        when,
                        row.Slug ?? string.Empty,
                        row.Project ?? string.Empty,
                        row.Agent ?? string.Empty,
                        row.Mode,
                        row.Task,
                        row.Withheld,
                        row.Profile,
                        row.Worktree,
                        row.Specialists ?? [],
                        row.Tokens,
                        row.Budget));

                    continue;
                }

                if (string.Equals(row.Kind, EndKind, StringComparison.Ordinal)
                    && byId.TryGetValue(row.Id, out var index))
                {
                    started[index] = started[index] with { EndedAt = when, ExitCode = row.Exit };
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IReadOnlyList<LaunchRecord>>.Fail(
                $"Could not read the launch ledger at {Path}: {ex.Message}");
        }

        return OperationResult<IReadOnlyList<LaunchRecord>>.Ok(started);
    }

    private async Task AppendAsync(Row row, CancellationToken ct)
    {
        var line = JsonSerializer.Serialize(row) + Environment.NewLine;

        await _lock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (directory is { Length: > 0 })
            {
                Directory.CreateDirectory(directory);
                _permissions.RestrictDirectoryToCurrentUser(directory);
            }

            await File.AppendAllTextAsync(Path, line, Encoding.UTF8, ct).ConfigureAwait(false);

            // It records when somebody was working, on what, and in their own
            // words. That is theirs.
            _permissions.RestrictToCurrentUser(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deliberately silent. See the note on the class: bookkeeping does
            // not get to stop a session starting or finishing.
        }
        finally
        {
            _lock.Release();
        }
    }

    private const string StartKind = "start";
    private const string EndKind = "end";

    private static string Moment(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool TryMoment(string? text, out DateTimeOffset when) =>
        DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out when);

    /// <summary>One line of the file.</summary>
    private sealed record Row(
        string Kind,
        string Id,
        string When,
        string? Slug = null,
        string? Project = null,
        string? Agent = null,
        string? Mode = null,
        string? Task = null,
        string? Withheld = null,
        string? Profile = null,
        string? Worktree = null,
        IReadOnlyList<string>? Specialists = null,
        int Tokens = 0,
        int Budget = 0,
        int? Exit = null);
}
