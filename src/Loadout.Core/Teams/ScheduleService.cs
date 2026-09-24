using System.Globalization;
using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams;

/// <summary>What runs again and again on this machine, and when each is next due.</summary>
public interface IScheduleService
{
    /// <summary>Every schedule, in the order they were made.</summary>
    Task<OperationResult<IReadOnlyList<TeamSchedule>>> ListAsync(CancellationToken ct = default);

    /// <summary>Records one, replacing any of the same name.</summary>
    Task<OperationResult<TeamSchedule>> SaveAsync(TeamSchedule schedule, CancellationToken ct = default);

    /// <summary>Forgets one.</summary>
    Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// The schedules due now, and why each one is.
    /// </summary>
    Task<OperationResult<IReadOnlyList<TeamSchedule>>> DueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>Records that one has started, so it is not started again.</summary>
    Task<OperationResult> StartedAsync(string id, DateTimeOffset when, string runId, CancellationToken ct = default);

    /// <summary>Records the commit an event-watching schedule has now seen.</summary>
    Task<OperationResult> SawAsync(string id, string commit, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// One file on this machine rather than one per schedule: there are a handful
/// of these, and a single list is what makes "what is due" one read.
/// </para>
/// <para>
/// It holds the record and answers what is due. It never starts anything -
/// firing a run is the daemon's job, and it does it by running the same
/// command somebody would type, which is the rule the launcher already
/// follows.
/// </para>
/// </remarks>
public sealed class ScheduleService : IScheduleService
{
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;

    public ScheduleService(IPlatformPaths paths, YamlStore yaml)
    {
        _paths = paths;
        _yaml = yaml;
    }

    private string Path => System.IO.Path.Combine(_paths.Paths.State, "teams", "schedules.yaml");

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TeamSchedule>>> ListAsync(CancellationToken ct = default)
    {
        var loaded = await _yaml.LoadAsync(Path, () => new TeamScheduleList(), ct).ConfigureAwait(false);

        return loaded.Failed
            ? OperationResult<IReadOnlyList<TeamSchedule>>.Fail(loaded.Error!, loaded.ExitCode)
            : OperationResult<IReadOnlyList<TeamSchedule>>.Ok(loaded.Value!.Items);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TeamSchedule>> SaveAsync(TeamSchedule schedule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (Check(schedule) is { } wrong)
        {
            return OperationResult<TeamSchedule>.Fail(wrong, ExitCode.InvalidArguments);
        }

        TeamSchedule? saved = null;

        var written = await _yaml.UpdateAsync<TeamScheduleList>(
            Path,
            () => new TeamScheduleList(),
            list =>
            {
                // Replaced rather than added twice: somebody correcting a
                // schedule types the same name again, and two schedules with
                // one name is a run that happens twice and a removal that
                // only half works.
                list.Items.RemoveAll(item =>
                    string.Equals(item.Id, schedule.Id, StringComparison.OrdinalIgnoreCase));

                list.Items.Add(schedule);

                saved = schedule;
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded && saved is not null
            ? OperationResult<TeamSchedule>.Ok(saved)
            : OperationResult<TeamSchedule>.Fail(written.Error ?? "The schedule could not be recorded.", written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var gone = false;

        var written = await _yaml.UpdateAsync<TeamScheduleList>(
            Path,
            () => new TeamScheduleList(),
            list => gone = list.Items.RemoveAll(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0,
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult.Fail(written.Error!, written.ExitCode);
        }

        return gone
            ? OperationResult.Ok()
            : OperationResult.Fail($"There is no schedule called '{id}' on this machine.", ExitCode.ProjectNotFound);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TeamSchedule>>> DueAsync(
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var listed = await ListAsync(ct).ConfigureAwait(false);

        return listed.Failed
            ? listed
            : OperationResult<IReadOnlyList<TeamSchedule>>.Ok(
                [.. listed.Value!.Where(schedule => IsDue(schedule, now))]);
    }

    /// <inheritdoc />
    public async Task<OperationResult> StartedAsync(
        string id,
        DateTimeOffset when,
        string runId,
        CancellationToken ct = default)
    {
        var written = await _yaml.UpdateAsync<TeamScheduleList>(
            Path,
            () => new TeamScheduleList(),
            list =>
            {
                var schedule = list.Items.Find(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

                if (schedule is not null)
                {
                    schedule.LastRun = when;
                    schedule.LastRunId = runId;
                }
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(written.Error!, written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> SawAsync(string id, string commit, CancellationToken ct = default)
    {
        var written = await _yaml.UpdateAsync<TeamScheduleList>(
            Path,
            () => new TeamScheduleList(),
            list =>
            {
                var schedule = list.Items.Find(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

                if (schedule is not null)
                {
                    schedule.LastCommit = commit;
                }
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded
            ? OperationResult.Ok()
            : OperationResult.Fail(written.Error!, written.ExitCode);
    }

    /// <summary>
    /// Whether a schedule should start now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A daily schedule that was missed is not made up for. Waking a machine
    /// at noon and firing the nine o'clock run is a run nobody is expecting
    /// against a repository that has moved on, and doing it for every day the
    /// machine was off is worse.
    /// </para>
    /// <para>
    /// The window is an hour, which is what "it was due and the machine was
    /// awake" means in practice: a daemon that checks every minute meets it
    /// immediately, and one that was asleep through it does not.
    /// </para>
    /// </remarks>
    public static bool IsDue(TeamSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.Enabled)
        {
            return false;
        }

        if (schedule.Every is { } every && every > TimeSpan.Zero)
        {
            return schedule.LastRun is not { } last || now - last >= every;
        }

        if (schedule.At is not { } at)
        {
            // An event is not a clock. Whether one has happened is the
            // daemon's question, because answering it means asking git.
            return false;
        }

        var local = now.ToLocalTime();
        var today = new DateTimeOffset(local.Date.Add(at.ToTimeSpan()), local.Offset);

        if (local < today || local - today > TimeSpan.FromHours(1))
        {
            return false;
        }

        // Once a day, not once an hour: a schedule that already ran since the
        // moment it was due has had its turn.
        return schedule.LastRun is not { } ran || ran.ToLocalTime() < today;
    }

    /// <summary>
    /// Whether a schedule watching a repository should start, given where that
    /// repository is now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The first look never fires. Writing down a trigger and having it go off
    /// immediately, against whatever happened to be checked out, is not what
    /// anybody means by "when the repository moves" - and on a machine with
    /// several projects it would start every one of them at once the first
    /// time the daemon ran.
    /// </para>
    /// <para>
    /// Here rather than in the daemon because it is a rule, and the rules are
    /// here. What the daemon knows that this does not is where the repository
    /// actually is, which is why that arrives as an argument.
    /// </para>
    /// </remarks>
    public static bool Moved(TeamSchedule schedule, string? head)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.Enabled
            || schedule.On.Length == 0
            || head is not { Length: > 0 }
            || string.Equals(head, schedule.LastCommit, StringComparison.Ordinal))
        {
            return false;
        }

        return schedule.LastCommit.Length > 0;
    }

    /// <summary>When it next comes round, for a person reading the list.</summary>
    public static DateTimeOffset? Next(TeamSchedule schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.Enabled)
        {
            return null;
        }

        if (schedule.Every is { } every && every > TimeSpan.Zero)
        {
            return schedule.LastRun is { } last ? last + every : now;
        }

        if (schedule.At is not { } at)
        {
            return null;
        }

        var local = now.ToLocalTime();
        var today = new DateTimeOffset(local.Date.Add(at.ToTimeSpan()), local.Offset);

        return IsDue(schedule, now) ? today : today <= local ? today.AddDays(1) : today;
    }

    /// <summary>The events a schedule can wait for.</summary>
    /// <remarks>
    /// One, for now, and it is the one people ask for: the repository moved.
    /// Whether it has moved is a question for whoever holds a git manager, so
    /// the daemon answers it; this only says the word is one that means
    /// something.
    /// </remarks>
    public static IReadOnlyList<string> Events { get; } = ["commit", RunFinishedEvent];

    /// <summary>The event for another team's run reaching its end.</summary>
    public const string RunFinishedEvent = "run-finished";

    /// <summary>The standing team that looks after the tool catalogue.</summary>
    public const string ToolWorksTeam = "tool-works";

    /// <summary>The least time between two starts on run-finished.</summary>
    public static readonly TimeSpan RunFinishedQuiet = TimeSpan.FromHours(1);

    /// <summary>
    /// Whether a schedule waiting for a finished run should start, and the
    /// newest finished run to write down as seen, or null to write nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The watermark is the latest time a run was seen to finish, kept where a
    /// commit watcher keeps its commit. Not the run identifier: that starts
    /// with the time a run began, so a long run that began first and finished
    /// last sorts below a short one already seen, and would never fire. The
    /// first look writes it down and does not fire, for the reason
    /// <see cref="Moved" /> gives; so does a watermark that is not a time,
    /// which is one an older build wrote.
    /// </para>
    /// <para>
    /// Never on a tool-works run, nor on the schedule's own team: either would
    /// start a run whose finishing starts another. At most once an hour, and a
    /// run that finished inside the hour is not lost - the watermark is not
    /// moved until the schedule fires, so it is seen at the next look after.
    /// </para>
    /// </remarks>
    public static (bool Fire, string? Seen) RunFinished(
        TeamSchedule schedule,
        IEnumerable<RunSummary> runs,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(runs);

        if (!schedule.Enabled || !string.Equals(schedule.On, RunFinishedEvent, StringComparison.OrdinalIgnoreCase))
        {
            return (false, null);
        }

        var newest = runs
            .Where(one => one.Finished is not null
                && !string.Equals(one.Team, ToolWorksTeam, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(one.Team, schedule.Team, StringComparison.OrdinalIgnoreCase))
            .Select(one => one.Finished)
            .Max();

        if (!DateTimeOffset.TryParseExact(
                schedule.LastCommit, "o", CultureInfo.InvariantCulture, DateTimeStyles.None, out var watermark))
        {
            // Before every finish, so a baseline taken before any run has
            // finished still lets the first one fire.
            return (false, Watermark(newest ?? DateTimeOffset.MinValue));
        }

        if (newest is not { } latest
            || latest <= watermark
            || (schedule.LastRun is { } last && now - last < RunFinishedQuiet))
        {
            return (false, null);
        }

        return (true, Watermark(latest));
    }

    private static string Watermark(DateTimeOffset finished) =>
        finished.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>What is wrong with a schedule, or null when nothing is.</summary>
    private static string? Check(TeamSchedule schedule)
    {
        if (Tasks.TaskIds.Rejection(schedule.Id) is { } rejected)
        {
            // The same shape a task or a checkpoint has, and for the same
            // reason: these are quoted on a command line far more often than
            // they are read from a list.
            return rejected.Replace("task id", "schedule name", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(schedule.Project))
        {
            return "A schedule needs a project to run against.";
        }

        if (string.IsNullOrWhiteSpace(schedule.Team))
        {
            return "A schedule needs a team to run.";
        }

        if (string.IsNullOrWhiteSpace(schedule.Goal))
        {
            return "A schedule needs a goal, in your own words, for the lead to work from.";
        }

        if (string.Equals(schedule.Autonomy, "manual", StringComparison.OrdinalIgnoreCase))
        {
            return "A schedule cannot be manual: it fires when nobody is watching, and a run that "
                + "stops at the first question has spent a session to ask something nobody will "
                + "read until morning. Use supervised or autonomous.";
        }

        if (schedule.On is { Length: > 0 } on && !Events.Contains(on, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{on}' is not something this can watch for. It knows: {string.Join(", ", Events)}.";
        }

        if (schedule.Every is null && schedule.At is null && schedule.On.Length == 0)
        {
            return "A schedule needs how often it runs, the time of day it runs at, or something to "
                + "watch for.";
        }

        if (schedule.Every is { } every && every < TimeSpan.FromMinutes(5))
        {
            // Not a limit for its own sake: a team run takes minutes and costs
            // money, and one started every thirty seconds would overlap itself
            // until somebody noticed the bill.
            return "A schedule cannot run more often than every five minutes. A team run takes "
                + "minutes and costs money, and one that overlaps itself spends twice for one answer.";
        }

        return null;
    }
}
