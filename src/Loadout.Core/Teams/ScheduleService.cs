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

        if (schedule.Every is null && schedule.At is null)
        {
            return "A schedule needs either how often it runs or the time of day it runs at.";
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
