using System.Globalization;
using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>What sort of thing is in the bin.</summary>
public enum BinKind
{
    /// <summary>A run's directory: its journal, briefs, reports and streams.</summary>
    Run,

    /// <summary>A team of your own: the file that defined it.</summary>
    Team,
}

/// <summary>One thing in the bin, as its record describes it.</summary>
/// <param name="Kind">A run or a team.</param>
/// <param name="Name">The run's identifier, or the team's name.</param>
/// <param name="Directory">Where it sits in the bin.</param>
/// <param name="Removed">When it was put there.</param>
/// <param name="Team">The team: the one that ran, for a run, or the team itself.</param>
/// <param name="From">
/// Where a team's file was, which is where restoring it puts it back. Null for
/// a run, whose place is worked out from its identifier.
/// </param>
/// <param name="Unmerged">Branches the run left that nothing merged, as they stood when it went.</param>
/// <param name="Bytes">How much disk it is holding.</param>
public sealed record BinEntry(
    BinKind Kind,
    string Name,
    string Directory,
    DateTimeOffset Removed,
    string Team,
    string? From,
    IReadOnlyList<string> Unmerged,
    long Bytes)
{
    /// <summary>The kind as a person says it.</summary>
    public string KindWord => Kind == BinKind.Run ? "run" : "team";

    /// <summary>When it goes for good, or null when nothing in the bin expires.</summary>
    public DateTimeOffset? Expires(int days) => days > 0 ? Removed.AddDays(days) : null;

    /// <summary>Whole days before it goes for good, or null when nothing expires.</summary>
    /// <remarks>
    /// Rounded up, so something with an hour left says one day rather than
    /// none: "0 days left" beside something still there reads as a fault.
    /// </remarks>
    public int? DaysLeft(DateTimeOffset now, int days) =>
        Expires(days) is { } expires
            ? Math.Max(0, (int)Math.Ceiling((expires - now).TotalDays))
            : null;

    /// <summary>Whether it has been in the bin longer than it is kept.</summary>
    public bool Expired(DateTimeOffset now, int days) =>
        Expires(days) is { } expires && now >= expires;
}

/// <summary>
/// Where removed runs and teams wait before they go for good.
/// </summary>
/// <remarks>
/// <para>
/// <c>team runs remove</c>, <c>team runs prune</c> and <c>team remove</c> all
/// deleted outright, and each of them is a command somebody types while
/// clearing up — which is when the wrong identifier gets pasted. A run's
/// journal is the only record of what it did, and a team of your own is a file
/// you wrote; neither had any way back. So removing one moves it here, and it
/// is deleted for good only once it has been here longer than the machine keeps
/// things (<see cref="DefaultDays"/> unless <c>team-bin-days</c> says
/// otherwise), or when somebody empties the bin on purpose.
/// </para>
/// <para>
/// A record, <see cref="RecordName"/>, sits inside each entry and says what it
/// is, when it went, and where it came from. The directory's own name is not
/// enough: a team called <c>bug-hunt-2</c> removed twice and a team called
/// <c>bug-hunt</c> are not reliably told apart by splitting a name on hyphens,
/// and a date read back from a file's timestamps is whatever the file system
/// last decided it was.
/// </para>
/// <para>
/// Everything here is under the state directory, beside the runs, so moving a
/// run in and out is a rename on one volume rather than a copy. A team's file
/// may live on another volume, because the workspace can be put anywhere, so it
/// is moved as a file, which the runtime copies across volumes when it has to.
/// </para>
/// </remarks>
public sealed class TeamBin
{
    /// <summary>How many days something stays in the bin when nothing says otherwise.</summary>
    public const int DefaultDays = 30;

    /// <summary>The record inside each entry.</summary>
    public const string RecordName = "bin.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly Platform.Abstractions.IPlatformPaths _paths;

    public TeamBin(Platform.Abstractions.IPlatformPaths paths) => _paths = paths;

    /// <summary>The bin itself.</summary>
    public string Root => Path.Combine(_paths.Paths.State, "teams", "bin");

    private string RunsRoot => Path.Combine(Root, "runs");

    private string TeamsRoot => Path.Combine(Root, "teams");

    /// <summary>
    /// The number of days to keep things, from the configured value.
    /// </summary>
    /// <remarks>
    /// Null means nobody set one, which is the default. Zero means keep until
    /// emptied by hand: of the two ways to read a zero on a setting that
    /// deletes things, "delete at once" is the one nobody can take back, so it
    /// is not the one chosen.
    /// </remarks>
    public static int Days(int? configured) => configured ?? DefaultDays;

    /// <summary>
    /// Moves a run's directory into the bin, with a record of what it was.
    /// </summary>
    /// <param name="runDirectory">Where the run is now.</param>
    /// <param name="runId">Its identifier, which is also its name in the bin.</param>
    /// <param name="team">The team that ran.</param>
    /// <param name="unmerged">The branches it left that nothing merged.</param>
    /// <param name="now">When, as the caller's clock says.</param>
    /// <returns>Where it now is.</returns>
    public OperationResult<string> PutRun(
        string runDirectory,
        string runId,
        string team,
        IReadOnlyList<string> unmerged,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(unmerged);

        now = now.ToUniversalTime();

        if (!RunJournal.Names(runId))
        {
            return OperationResult<string>.Fail(
                $"'{runId}' is not a run identifier.", ExitCode.InvalidArguments);
        }

        var destination = Path.Combine(RunsRoot, runId);

        // Refused rather than replaced. Replacing would delete the copy
        // already here, which is the one thing the bin exists to stop.
        if (System.IO.Directory.Exists(destination))
        {
            return OperationResult<string>.Fail(
                $"A run called '{runId}' is already in the bin. Empty it first with: "
                + "loadout team bin empty",
                ExitCode.InvalidArguments);
        }

        var record = Path.Combine(runDirectory, RecordName);

        try
        {
            // Written before the move rather than after, so the entry appears
            // in the bin already carrying its record. The other order leaves a
            // window where a crash files a run with no date on it, and a date
            // is what decides when it goes.
            Write(record, new BinRecord("run", now, runId, team, null, [.. unmerged]));

            System.IO.Directory.CreateDirectory(RunsRoot);
            System.IO.Directory.Move(runDirectory, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(record);

            return OperationResult<string>.Fail(
                $"'{runId}' could not be moved to the bin: {ex.Message}");
        }

        return OperationResult<string>.Ok(destination);
    }

    /// <summary>
    /// Moves a team's file into the bin, under the team's name and the time it went.
    /// </summary>
    /// <remarks>
    /// Timestamped, because the same name can be removed more than once — made,
    /// removed, made again differently and removed again — and each of those is
    /// a different file somebody may want back.
    /// </remarks>
    /// <param name="name">The team.</param>
    /// <param name="file">Its definition.</param>
    /// <param name="now">When, as the caller's clock says.</param>
    public OperationResult<BinEntry> PutTeam(string name, string file, DateTimeOffset now)
    {
        now = now.ToUniversalTime();

        if (!TeamFiles.Names(name))
        {
            return OperationResult<BinEntry>.Fail(
                $"'{name}' cannot be a team's name.", ExitCode.InvalidArguments);
        }

        if (!File.Exists(file))
        {
            return OperationResult<BinEntry>.Fail(
                $"'{file}' is not there to move.", ExitCode.ProjectNotFound);
        }

        var stamp = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var directory = Path.Combine(TeamsRoot, $"{name}-{stamp}");

        // Two removals of one name inside a second are not something a person
        // does, but a script can, and the second must not land on the first.
        for (var suffix = 2; System.IO.Directory.Exists(directory); suffix++)
        {
            directory = Path.Combine(TeamsRoot, $"{name}-{stamp}-{suffix}");
        }

        long bytes;

        try
        {
            bytes = new FileInfo(file).Length;

            System.IO.Directory.CreateDirectory(directory);

            Write(
                Path.Combine(directory, RecordName),
                new BinRecord("team", now, null, name, Path.GetFullPath(file), []));

            File.Move(file, Path.Combine(directory, Path.GetFileName(file)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDeleteDirectory(directory);

            return OperationResult<BinEntry>.Fail(
                $"'{file}' could not be moved to the bin: {ex.Message}");
        }

        return OperationResult<BinEntry>.Ok(new BinEntry(
            BinKind.Team, name, directory, now, name, Path.GetFullPath(file), [], bytes));
    }

    /// <summary>Everything in the bin, most recently removed first.</summary>
    public IReadOnlyList<BinEntry> List()
    {
        var entries = new List<BinEntry>();

        foreach (var (root, kind) in new[] { (RunsRoot, BinKind.Run), (TeamsRoot, BinKind.Team) })
        {
            if (!System.IO.Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in System.IO.Directory.EnumerateDirectories(root))
            {
                entries.Add(Read(directory, kind));
            }
        }

        return
        [
            .. entries
                .OrderByDescending(entry => entry.Removed)
                .ThenBy(entry => entry.Name, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// What emptying the bin would take: everything, or only what has been in
    /// it longer than an age.
    /// </summary>
    public IReadOnlyList<BinEntry> Choose(DateTimeOffset now, TimeSpan? olderThan) =>
        [.. List().Where(entry => olderThan is not { } age || now - entry.Removed >= age)];

    /// <summary>What has been in the bin longer than it is kept.</summary>
    public IReadOnlyList<BinEntry> Expired(DateTimeOffset now, int days) =>
        [.. List().Where(entry => entry.Expired(now, days))];

    /// <summary>
    /// Deletes everything that has been in the bin longer than it is kept.
    /// </summary>
    /// <remarks>
    /// Quiet about anything it cannot delete: a file held open by a scanner is
    /// still there next time, and this runs again. Failing loudly from
    /// housekeeping would stop the thing that called it for no gain.
    /// </remarks>
    /// <returns>What went.</returns>
    public IReadOnlyList<BinEntry> Sweep(DateTimeOffset now, int days)
    {
        var gone = new List<BinEntry>();

        foreach (var entry in Expired(now, days))
        {
            if (Delete(entry).Succeeded)
            {
                gone.Add(entry);
            }
        }

        return gone;
    }

    /// <summary>Deletes one entry for good.</summary>
    public OperationResult Delete(BinEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Only ever something under the bin. The entry came from a listing
        // of it, but a caller holding an entry is not proof of where it points.
        if (!Inside(entry.Directory))
        {
            return OperationResult.Fail(
                $"'{entry.Directory}' is not in the bin.", ExitCode.InvalidArguments);
        }

        try
        {
            System.IO.Directory.Delete(entry.Directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone is what was asked for.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"'{entry.Name}' could not be deleted: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Puts a run back where it was.
    /// </summary>
    /// <param name="runId">The run.</param>
    /// <param name="destination">
    /// Where runs live, for this one: the journal's <see cref="IRunJournal.DirectoryOf"/>.
    /// </param>
    /// <param name="dryRun">
    /// Make every check and move nothing, so a dry run refuses exactly what the
    /// real one would.
    /// </param>
    public OperationResult<BinEntry> RestoreRun(string runId, string destination, bool dryRun = false)
    {
        if (!RunJournal.Names(runId))
        {
            return OperationResult<BinEntry>.Fail(
                $"'{runId}' is not a run identifier.", ExitCode.InvalidArguments);
        }

        var directory = Path.Combine(RunsRoot, runId);

        if (!System.IO.Directory.Exists(directory))
        {
            return OperationResult<BinEntry>.Fail(
                $"No run called '{runId}' in the bin. See what is there with: loadout team bin",
                ExitCode.ProjectNotFound);
        }

        // Refused rather than merged. Two directories of the same run are two
        // accounts of it, and folding one into the other would silently choose
        // which of them wins file by file.
        if (System.IO.Directory.Exists(destination))
        {
            return OperationResult<BinEntry>.Fail(
                $"A run called '{runId}' is already there. Nothing was restored.",
                ExitCode.InvalidArguments);
        }

        var entry = Read(directory, BinKind.Run);

        if (dryRun)
        {
            return OperationResult<BinEntry>.Ok(entry);
        }

        var record = Path.Combine(directory, RecordName);
        var kept = File.Exists(record) ? File.ReadAllText(record) : null;

        try
        {
            // The record belongs to the bin. Left in the run's directory it
            // would come back with it and say it was binned on a date it no
            // longer is.
            TryDelete(record);

            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            System.IO.Directory.Move(directory, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (kept is not null && System.IO.Directory.Exists(directory))
            {
                File.WriteAllText(record, kept);
            }

            return OperationResult<BinEntry>.Fail(
                $"'{runId}' could not be restored: {ex.Message}");
        }

        return OperationResult<BinEntry>.Ok(entry with { Directory = destination });
    }

    /// <summary>The most recently binned copy of a team, or a sentence saying there is none.</summary>
    public OperationResult<BinEntry> LatestTeam(string name)
    {
        var entry = List()
            .Where(one => one.Kind == BinKind.Team && string.Equals(one.Name, name, StringComparison.Ordinal))
            .MaxBy(one => one.Removed);

        return entry is null
            ? OperationResult<BinEntry>.Fail(
                $"No team called '{name}' in the bin. See what is there with: loadout team bin",
                ExitCode.ProjectNotFound)
            : OperationResult<BinEntry>.Ok(entry);
    }

    /// <summary>
    /// Puts the most recently removed copy of a team back where it was.
    /// </summary>
    /// <remarks>
    /// Where it was is the record's <see cref="BinEntry.From"/>, not the
    /// workspace's default, because a team written for one project lives under
    /// that project, and putting it back somewhere every project reads would be
    /// a different team from the one that was removed.
    /// </remarks>
    /// <param name="name">The team.</param>
    /// <param name="dryRun">Make every check and move nothing.</param>
    public OperationResult<BinEntry> RestoreTeam(string name, bool dryRun = false)
    {
        if (!TeamFiles.Names(name))
        {
            return OperationResult<BinEntry>.Fail(
                $"'{name}' cannot be a team's name.", ExitCode.InvalidArguments);
        }

        var found = LatestTeam(name);

        if (found.Failed)
        {
            return found;
        }

        var entry = found.Value!;

        if (entry.From is not { Length: > 0 } from)
        {
            return OperationResult<BinEntry>.Fail(
                $"The bin does not say where '{name}' came from, so there is nowhere to put it back. "
                + $"Its file is in {entry.Directory}.");
        }

        if (File.Exists(from))
        {
            return OperationResult<BinEntry>.Fail(
                $"'{from}' is already there. Nothing was restored.", ExitCode.InvalidArguments);
        }

        var file = System.IO.Directory.EnumerateFiles(entry.Directory)
            .FirstOrDefault(one => !string.Equals(Path.GetFileName(one), RecordName, StringComparison.Ordinal));

        if (file is null)
        {
            return OperationResult<BinEntry>.Fail(
                $"The bin's copy of '{name}' has no file in it. Nothing was restored.");
        }

        if (dryRun)
        {
            return OperationResult<BinEntry>.Ok(entry);
        }

        try
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(from)!);
            File.Move(file, from);
            System.IO.Directory.Delete(entry.Directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<BinEntry>.Fail($"'{name}' could not be restored: {ex.Message}");
        }

        return OperationResult<BinEntry>.Ok(entry);
    }

    /// <summary>Reads one entry, falling back to what the directory says when its record will not.</summary>
    /// <remarks>
    /// An entry with no readable record is still listed, dated by the
    /// directory. Hiding it would leave something in the bin that no listing
    /// shows and no sweep takes, which is how a bin fills up for ever.
    /// </remarks>
    private static BinEntry Read(string directory, BinKind kind)
    {
        var name = Path.GetFileName(directory);
        BinRecord? record = null;

        try
        {
            var path = Path.Combine(directory, RecordName);

            if (File.Exists(path))
            {
                record = JsonSerializer.Deserialize<BinRecord>(File.ReadAllText(path), Json);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }

        long bytes = 0;

        try
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetFileName(file), RecordName, StringComparison.Ordinal))
                {
                    bytes += new FileInfo(file).Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Measuring is a courtesy, as it is when a run is removed.
        }

        if (record is null)
        {
            return new BinEntry(
                kind,
                name,
                directory,
                new DateTimeOffset(System.IO.Directory.GetLastWriteTimeUtc(directory), TimeSpan.Zero),
                string.Empty,
                null,
                [],
                bytes);
        }

        return new BinEntry(
            kind,
            kind == BinKind.Run ? record.Run ?? name : record.Team,
            directory,
            record.Removed,
            record.Team,
            record.From,
            record.Unmerged ?? [],
            bytes);
    }

    private bool Inside(string path)
    {
        var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;

        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static void Write(string path, BinRecord record) =>
        File.WriteAllText(path, JsonSerializer.Serialize(record, Json));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            System.IO.Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>What <see cref="RecordName"/> holds.</summary>
    /// <param name="Kind">run or team.</param>
    /// <param name="Removed">When, in UTC.</param>
    /// <param name="Run">The run's identifier, for a run.</param>
    /// <param name="Team">The team that ran, or the team itself.</param>
    /// <param name="From">Where a team's file was.</param>
    /// <param name="Unmerged">The branches a run left that nothing merged.</param>
    private sealed record BinRecord(
        string Kind,
        DateTimeOffset Removed,
        string? Run,
        string Team,
        string? From,
        List<string>? Unmerged);
}
