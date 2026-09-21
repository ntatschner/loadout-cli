using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;

namespace Loadout.Core.Teams;

/// <summary>What kind of thing is waiting.</summary>
public enum WaitingKind
{
    /// <summary>A schedule that has not fired yet.</summary>
    Schedule,

    /// <summary>A task nobody has finished.</summary>
    Task,
}

/// <summary>
/// One thing that has not started.
/// </summary>
/// <param name="Kind">Which sort it is.</param>
/// <param name="Id">The schedule's name or the task's id.</param>
/// <param name="Title">What it is for, in the person's own words.</param>
/// <param name="Team">The team that would run, for a schedule.</param>
/// <param name="Project">Which project, where one is known.</param>
/// <param name="Due">When it next comes round. Null for anything a clock does not decide.</param>
/// <param name="Because">Why it is waiting, said plainly.</param>
/// <param name="Held">
/// Whether something is stopping it rather than merely not being its turn: a
/// disabled schedule, a blocked task. Drawn apart, because "not yet" and "not
/// going to, until somebody does something" are different rooms.
/// </param>
public sealed record Waiting(
    WaitingKind Kind,
    string Id,
    string Title,
    string? Team,
    string? Project,
    DateTimeOffset? Due,
    string Because,
    bool Held);

/// <summary>
/// Everything on this machine that is queued rather than going.
/// </summary>
/// <remarks>
/// <para>
/// The office shows what is being worked on and the timeline shows what was.
/// Neither has ever shown what is <em>about</em> to be, and that is the
/// question somebody actually asks before they go to bed: is anything going to
/// start without me, and is anything sitting here that I said I would do.
/// </para>
/// <para>
/// Two sources because there are two answers, and they are not the same shape.
/// A schedule is the machine's own intention - it will fire whether or not
/// anybody remembers. A task is a person's, recorded and dated, and nothing
/// will ever fire it. Showing one without the other answers half the question.
/// </para>
/// <para>
/// Read rather than computed: neither source is touched here, and nothing in
/// this file can start anything.
/// </para>
/// </remarks>
public static class WaitingRoom
{
    /// <summary>
    /// The most tasks to read from any one project.
    /// </summary>
    /// <remarks>
    /// A waiting area is a glance, not a backlog tool. A project with four
    /// hundred open tasks would otherwise bury every schedule on the machine
    /// under it, and the person who wanted to know what fires overnight would
    /// scroll past all of them.
    /// </remarks>
    public const int MostPerProject = 12;

    /// <summary>
    /// What is waiting, for every project this machine knows about.
    /// </summary>
    /// <remarks>
    /// The registry is asked for its slugs and the rest is the method below.
    /// Kept as a thin overload so the part with the rules in it depends on a
    /// list of names rather than on the whole project service, which is ten
    /// members wide and all but one of them irrelevant here.
    /// </remarks>
    public static async Task<IReadOnlyList<Waiting>> ReadAsync(
        IScheduleService? schedules,
        ITaskService? tasks,
        IProjectService? projects,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        IReadOnlyList<string>? slugs = null;

        if (projects is not null)
        {
            var listed = await projects.ListAsync(ct).ConfigureAwait(false);

            slugs =
            [
                .. (listed.Value ?? [])
                    .Select(one => one.Entry.Slug)
                    .Where(slug => slug.Length > 0),
            ];
        }

        return await ReadAsync(schedules, tasks, slugs, now, ct).ConfigureAwait(false);
    }

    /// <summary>What is waiting, soonest first.</summary>
    /// <param name="schedules">The machine's schedules, or null to read none.</param>
    /// <param name="tasks">The task store, or null to read none.</param>
    /// <param name="projects">The projects to read tasks for, or null for none.</param>
    /// <param name="now">The clock, so this can be argued with in a test.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<IReadOnlyList<Waiting>> ReadAsync(
        IScheduleService? schedules,
        ITaskService? tasks,
        IReadOnlyList<string>? projects,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var waiting = new List<Waiting>();

        if (schedules is not null)
        {
            var listed = await schedules.ListAsync(ct).ConfigureAwait(false);

            foreach (var schedule in listed.Value ?? [])
            {
                waiting.Add(new Waiting(
                    WaitingKind.Schedule,
                    schedule.Id,
                    schedule.Goal,
                    schedule.Team,
                    schedule.Project,

                    // A watcher has no next time: what starts it is somebody
                    // else committing, and saying "due at" about that would be
                    // a guess dressed as a fact.
                    schedule.Enabled ? ScheduleService.Next(schedule, now) : null,
                    Wording(schedule),
                    !schedule.Enabled));
            }
        }

        if (tasks is not null && projects is not null)
        {
            foreach (var slug in projects)
            {
                if (slug.Length == 0)
                {
                    continue;
                }

                var read = await tasks.ListAsync(slug, ct).ConfigureAwait(false);

                if (read.Failed)
                {
                    continue;
                }

                var queued = (read.Value ?? [])
                    .Where(one => one.State is TaskState.Open or TaskState.Blocked)
                    .OrderBy(one => one.State == TaskState.Blocked)
                    .ThenBy(one => one.DeclaredUtc)
                    .Take(MostPerProject);

                foreach (var task in queued)
                {
                    waiting.Add(new Waiting(
                        WaitingKind.Task,
                        task.Id,
                        task.Title.Length > 0 ? task.Title : task.Id,
                        Team: null,
                        slug,
                        Due: null,
                        task.State == TaskState.Blocked
                            ? Blocked(task)
                            : $"open since {task.DeclaredUtc.ToLocalTime():d MMMM}",
                        task.State == TaskState.Blocked));
                }
            }
        }

        // Soonest first, because the question is what happens next. Anything
        // a clock does not decide comes after everything a clock does, and
        // anything held comes last of all - it is not going to happen until
        // somebody does something, so it is not part of "what happens next".
        return
        [
            .. waiting
                .OrderBy(one => one.Held)
                .ThenBy(one => one.Due is null)
                .ThenBy(one => one.Due ?? DateTimeOffset.MaxValue)
                .ThenBy(one => one.Id, StringComparer.Ordinal),
        ];
    }

    /// <summary>Why a schedule is waiting, as somebody would say it.</summary>
    private static string Wording(TeamSchedule schedule)
    {
        if (!schedule.Enabled)
        {
            return "paused";
        }

        if (schedule.On is { Length: > 0 } on)
        {
            return on == "commit" ? "when the repository moves" : $"on {on}";
        }

        if (schedule.Every is { } every && every > TimeSpan.Zero)
        {
            return $"every {Said(every)}";
        }

        return schedule.At is { } at ? $"daily at {at:HH\\:mm}" : "nothing starts it";
    }

    /// <summary>What a blocked task says it is waiting on.</summary>
    private static string Blocked(TaskItem task) =>
        task.Note is { Length: > 0 } note ? $"blocked: {note}" : "blocked";

    private static string Said(TimeSpan span) =>
        span < TimeSpan.FromHours(1)
            ? $"{span.TotalMinutes:F0} minutes"
            : span < TimeSpan.FromDays(1)
                ? $"{span.TotalHours:F0} hours"
                : $"{span.TotalDays:F0} days";
}
