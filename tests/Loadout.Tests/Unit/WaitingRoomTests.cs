using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What is queued rather than going.
/// </summary>
/// <remarks>
/// <para>
/// The office shows what is being worked on and the timeline shows what was.
/// This is the third question, which neither answered: is anything going to
/// start without me, and is anything sitting here I said I would do.
/// </para>
/// <para>
/// Two sources on purpose. A schedule is the machine's own intention and fires
/// whether or not anybody remembers; a task is a person's, and nothing will
/// ever fire it. What has to hold is that both arrive, that the order is the
/// order somebody reads - what happens next, then next - and that anything
/// held is not mixed in with what is merely not its turn yet.
/// </para>
/// </remarks>
public sealed class WaitingRoomTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static TeamSchedule Daily(string id, string at, bool enabled = true) =>
        new()
        {
            Id = id,
            Team = "docs-crew",
            Project = "loadout-cli",
            Goal = "check the docs against the code",
            At = TimeOnly.Parse(at, System.Globalization.CultureInfo.InvariantCulture),
            Enabled = enabled,
        };

    private static TeamSchedule Watching(string id) =>
        new()
        {
            Id = id,
            Team = "bug-hunt",
            Project = "loadout-cli",
            Goal = "look at what changed",
            On = "commit",
            Enabled = true,
        };

    private static TaskItem Item(string id, TaskState state, string title = "") =>
        new()
        {
            Id = id,
            Title = title.Length > 0 ? title : id,
            State = state,
            DeclaredBy = "nigel",
            DeclaredUtc = Noon.AddDays(-2),
        };

    private static Task<IReadOnlyList<Waiting>> ReadAsync(
        IEnumerable<TeamSchedule>? schedules = null,
        IEnumerable<TaskItem>? tasks = null) =>
        WaitingRoom.ReadAsync(
            schedules is null ? null : new Schedules(schedules),
            tasks is null ? null : new Tasks(tasks),
            tasks is null ? null : (IReadOnlyList<string>)["loadout-cli"],
            Noon);

    [Fact]
    public async Task Both_sources_arrive_in_one_room()
    {
        var waiting = await ReadAsync(
            [Daily("nightly", "23:00")],
            [Item("office-art", TaskState.Open, "Put art in the office")]);

        // Showing one without the other answers half the question.
        waiting.Select(one => one.Kind)
            .Should().Contain(WaitingKind.Schedule).And.Contain(WaitingKind.Task);
    }

    [Fact]
    public async Task A_machine_with_neither_has_an_empty_room_rather_than_a_failure()
    {
        (await WaitingRoom.ReadAsync(null, null, (IReadOnlyList<string>?)null, Noon)).Should().BeEmpty();
    }

    [Fact]
    public async Task What_comes_round_soonest_is_first()
    {
        var waiting = await ReadAsync([Daily("late", "23:00"), Daily("soon", "13:00")]);

        // The question this view answers is "what happens next", so the order
        // is the answer rather than a detail of it.
        waiting.Select(one => one.Id).Should().Equal("soon", "late");
    }

    [Fact]
    public async Task Anything_a_clock_does_not_decide_comes_after_everything_it_does()
    {
        var waiting = await ReadAsync(
            [Watching("on-commit"), Daily("nightly", "23:00")],
            [Item("something", TaskState.Open)]);

        // A watcher has no next time - what starts it is somebody else
        // committing - and neither has a task. Saying "due at" about either
        // would be a guess dressed as a fact.
        waiting[0].Id.Should().Be("nightly");
        waiting[0].Due.Should().NotBeNull();

        waiting.Skip(1).Should().OnlyContain(one => one.Due == null);
    }

    [Fact]
    public async Task Held_things_come_last_and_say_what_is_holding_them()
    {
        var waiting = await ReadAsync(
            [Daily("paused", "13:00", enabled: false), Daily("nightly", "23:00")],
            [Item("blocked-one", TaskState.Blocked), Item("open-one", TaskState.Open)]);

        // Not going to happen until somebody does something, so not part of
        // "what happens next" - but still in the room, because forgetting it
        // is how a paused schedule stays paused for a month.
        waiting.Where(one => one.Held).Select(one => one.Id)
            .Should().BeEquivalentTo(["paused", "blocked-one"]);

        waiting.TakeLast(2).Should().OnlyContain(one => one.Held);

        waiting.Single(one => one.Id == "paused").Because.Should().Be("paused");
        waiting.Single(one => one.Id == "blocked-one").Because.Should().Contain("blocked");
    }

    [Fact]
    public async Task A_paused_schedule_is_not_given_a_next_time()
    {
        var waiting = await ReadAsync([Daily("paused", "13:00", enabled: false)]);

        // It has an "at" and it is not coming round at it. A time here would
        // be a promise the schedule is not making.
        waiting.Single().Due.Should().BeNull();
    }

    [Fact]
    public async Task Why_each_one_is_waiting_is_said_in_words()
    {
        var waiting = await ReadAsync([Daily("nightly", "23:00"), Watching("on-commit")]);

        waiting.Single(one => one.Id == "nightly").Because.Should().Be("daily at 23:00");
        waiting.Single(one => one.Id == "on-commit").Because.Should().Be("when the repository moves");
    }

    [Fact]
    public async Task Only_what_has_not_been_finished_is_waiting()
    {
        var waiting = await ReadAsync(tasks:
        [
            Item("open-one", TaskState.Open),
            Item("doing-one", TaskState.Doing),
            Item("done-one", TaskState.Done),
            Item("dropped-one", TaskState.Dropped),
            Item("blocked-one", TaskState.Blocked),
        ]);

        // Doing is not waiting - it is in the office. Done and dropped are
        // not waiting either, and a waiting area that filled up with them
        // would be a list nobody reads.
        waiting.Select(one => one.Id).Should().BeEquivalentTo(["open-one", "blocked-one"]);
    }

    [Fact]
    public async Task One_project_cannot_bury_every_schedule_on_the_machine()
    {
        var many = Enumerable.Range(1, WaitingRoom.MostPerProject + 20)
            .Select(i => Item("task-" + i.ToString("000"), TaskState.Open));

        var waiting = await ReadAsync([Daily("nightly", "23:00")], many);

        waiting.Count(one => one.Kind == WaitingKind.Task)
            .Should().Be(WaitingRoom.MostPerProject, "a waiting area is a glance, not a backlog tool");

        // And the schedule is still the first thing somebody sees.
        waiting[0].Id.Should().Be("nightly");
    }

    // ------------------------------------------------------------- fakes

    private sealed class Schedules : IScheduleService
    {
        private readonly IReadOnlyList<TeamSchedule> _items;

        public Schedules(IEnumerable<TeamSchedule> items) => _items = [.. items];

        public Task<OperationResult<IReadOnlyList<TeamSchedule>>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TeamSchedule>>.Ok(_items));

        public Task<OperationResult<TeamSchedule>> SaveAsync(TeamSchedule schedule, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");

        public Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");

        public Task<OperationResult<IReadOnlyList<TeamSchedule>>> DueAsync(
            DateTimeOffset now, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here fires anything.");

        public Task<OperationResult> StartedAsync(
            string id, DateTimeOffset when, string runId, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");

        public Task<OperationResult> SawAsync(string id, string commit, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");
    }

    private sealed class Tasks : ITaskService
    {
        private readonly IReadOnlyList<TaskItem> _items;

        public Tasks(IEnumerable<TaskItem> items) => _items = [.. items];

        public Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
            string projectSlug, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<TaskItem>>.Ok(_items));

        public Task<OperationResult<TaskItem>> DeclareAsync(
            string projectSlug, string id, TaskState state, string declaredBy,
            string? title = null, string? note = null, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");

        public Task<OperationResult> RemoveAsync(
            string projectSlug, string id, CancellationToken ct = default) =>
            throw new NotSupportedException("Nothing here writes.");
    }

}
