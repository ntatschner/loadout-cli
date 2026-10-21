using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Events;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Turning reasons that are only ever true now into a history of when each
/// appeared and cleared.
/// </summary>
/// <remarks>
/// The property under all of these: <strong>one reason is one event</strong>.
/// It is written once when it appears, not again while it stays, cleared
/// once when it goes, and is news again only if it comes back. A history that
/// repeats itself every look is the channel somebody mutes.
/// </remarks>
public sealed class EventRecorderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private static Observed Asking(string key = "run/r1/asking/g1") =>
        new(key, EventLevels.Needs, EventSources.Run, EventKinds.Asking, "docs asks: may I?", Project: "p", Run: "r1");

    private static (IReadOnlyList<UserEvent> Events, RecorderState State) Look(RecorderState before, DateTimeOffset at, params Observed[] now) =>
        EventRecorder.Pass(before, now, [], before.Journals, before.Sessions, at);

    [Fact]
    public void A_reason_that_appears_is_written_once()
    {
        var (events, state) = Look(RecorderState.Empty, Noon, Asking());

        events.Should().ContainSingle().Which.Should().Match<UserEvent>(one =>
            one.Kind == EventKinds.Asking && one.Level == EventLevels.Needs && one.Direction == EventDirections.ToMe
            && one.Run == "r1" && one.Project == "p" && one.At == Noon && one.Clears == null);
        state.Outstanding.Should().ContainKey("run/r1/asking/g1");
    }

    [Fact]
    public void A_reason_that_stays_is_not_written_again()
    {
        var (_, first) = Look(RecorderState.Empty, Noon, Asking());

        var (events, second) = Look(first, Noon.AddMinutes(1), Asking());

        events.Should().BeEmpty();
        second.Outstanding["run/r1/asking/g1"].At.Should().Be(Noon, "it is still the event that recorded it appearing");
    }

    [Fact]
    public void A_reason_that_goes_is_cleared_once_pointing_at_the_event()
    {
        var (appeared, first) = Look(RecorderState.Empty, Noon, Asking());

        var (events, second) = Look(first, Noon.AddMinutes(4));
        var (again, _) = Look(second, Noon.AddMinutes(5));

        events.Should().ContainSingle().Which.Should().Match<UserEvent>(one =>
            one.Kind == EventKinds.Cleared && one.Clears == appeared[0].Id && one.At == Noon.AddMinutes(4) && one.Run == "r1");
        again.Should().BeEmpty();
        second.Outstanding.Should().BeEmpty();
    }

    [Fact]
    public void A_reason_that_comes_back_is_news_again_under_a_new_id()
    {
        var (appeared, first) = Look(RecorderState.Empty, Noon, Asking());
        var (_, cleared) = Look(first, Noon.AddMinutes(1));

        var (events, _) = Look(cleared, Noon.AddMinutes(2), Asking());

        events.Should().ContainSingle().Which.Id.Should().NotBe(appeared[0].Id);
    }

    [Fact]
    public void What_was_outstanding_survives_a_restart_through_the_file()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loadout-events-" + Guid.NewGuid().ToString("N"));

        try
        {
            var store = new EventStore(directory);
            var (_, first) = Look(RecorderState.Empty, Noon, Asking());

            store.Record(first);

            // A new daemon reads it back and sees the same reason still true.
            var (events, _) = Look(store.Recorded(), Noon.AddMinutes(30), Asking());

            events.Should().BeEmpty("a restart is not the reason appearing again");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_silence_is_one_event_however_long_it_lasts()
    {
        // The wording says how long the node has been quiet, so it changes
        // every minute. Keyed on the wording, each minute would clear one
        // event and raise another.
        var node = new RunNode("implementer/1", "role.implementer", RunAttention.Working, 2, 0.1m, Noon.AddMinutes(-20), Started: Noon.AddMinutes(-30));
        var run = Run(nodes: [node]);

        var (first, state) = Look(RecorderState.Empty, Noon, [.. EventRecorder.Of(run, Noon)]);
        var (later, _) = Look(state, Noon.AddMinutes(7), [.. EventRecorder.Of(run, Noon.AddMinutes(7))]);

        first.Should().ContainSingle().Which.Kind.Should().Be(EventKinds.Quiet);
        EventRecorder.Of(run, Noon).Single().Detail.Should().NotBe(EventRecorder.Of(run, Noon.AddMinutes(7)).Single().Detail);
        later.Should().BeEmpty();
    }

    [Fact]
    public void Each_question_a_run_asks_is_its_own_event()
    {
        var run = Run(gates:
        [
            new PendingAsk("g1", "implementer/1", "role.implementer", "Bash", "dotnet test", Noon),
            new PendingAsk("g2", "implementer/1", "role.implementer", "Bash", "git push", Noon),
        ]);

        var observed = EventRecorder.Of(run, Noon).ToList();

        observed.Should().HaveCount(2);
        observed.Select(one => one.Key).Should().OnlyHaveUniqueItems();
        observed.Should().OnlyContain(one => one.Level == EventLevels.Needs && one.Title.StartsWith("docs asks: ", StringComparison.Ordinal));
    }

    [Fact]
    public void A_finished_run_wants_nothing_so_its_reasons_clear()
    {
        var asking = Run(gates: [new PendingAsk("g1", "implementer/1", "role.implementer", "Bash", "dotnet test", Noon)]);
        var (_, state) = Look(RecorderState.Empty, Noon, [.. EventRecorder.Of(asking, Noon)]);

        var finished = asking with { Finished = Noon.AddMinutes(1), Ended = "done" };
        var (events, _) = Look(state, Noon.AddMinutes(1), [.. EventRecorder.Of(finished, Noon.AddMinutes(1))]);

        events.Should().ContainSingle().Which.Kind.Should().Be(EventKinds.Cleared);
    }

    [Theory]
    [InlineData("waiting", EventKinds.Waiting)]
    [InlineData("approval", EventKinds.Approval)]
    public void A_session_waiting_or_asking_approval_needs_you(string needs, string kind)
    {
        var seen = EventRecorder.Of(new SessionSeen("abcd1234", "fix the build", "p", needs));

        seen.Should().NotBeNull();
        seen!.Kind.Should().Be(kind);
        seen.Level.Should().Be(EventLevels.Needs);
        seen.Session.Should().Be("abcd1234");
    }

    [Fact]
    public void A_session_needing_nothing_is_not_a_reason()
    {
        EventRecorder.Of(new SessionSeen("abcd1234", "fix the build", "p", null)).Should().BeNull();
    }

    [Fact]
    public void The_journal_gives_milestones_and_your_answers_from_where_it_was_left()
    {
        var run = Run();
        IReadOnlyList<RunEvent> entries =
        [
            Entry("run.started", null, new { team = "docs" }),
            Entry("node.launched", "lead", new { role = "role.lead" }),
            Entry("node.answered", "implementer/1", new { tool = "Bash", allowed = true, chosen = "yes" }),
            Entry("node.answered", "implementer/1", new { tool = "Bash", allowed = false, by = NodePermissions.Ended }),
            Entry("gate.decided", "implementer/1", new { gate = "merge", branch = "teams/r1/implementer-1", allowed = true }),
            Entry("run.finished", null, new { ended = "done", outcome = "done" }),
        ];

        var all = EventRecorder.Copied(run, entries, 0);
        var later = EventRecorder.Copied(run, entries, 3);

        all.Select(one => one.Kind).Should().Equal(
            EventKinds.RunStarted, EventKinds.Answered, EventKinds.GateDecided, EventKinds.RunFinished);
        all.Single(one => one.Kind == EventKinds.Answered).Direction.Should().Be(EventDirections.ByMe);
        all.Single(one => one.Kind == EventKinds.RunFinished).Level.Should().Be(EventLevels.Done);
        later.Select(one => one.Kind).Should().Equal(EventKinds.GateDecided, EventKinds.RunFinished);
        EventRecorder.Copied(run, entries, 0).Select(one => one.Id).Should().Equal(all.Select(one => one.Id), "copying the same entry twice writes the same id");
    }

    [Fact]
    public void An_autonomous_run_deciding_its_own_merge_is_not_yours()
    {
        var run = Run() with { Autonomy = "autonomous" };

        EventRecorder.Copied(run, [Entry("gate.decided", "n", new { gate = "merge", branch = "b", allowed = true })], 0)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData("failed", EventLevels.Problem)]
    [InlineData("stopped", EventLevels.Info)]
    [InlineData("done", EventLevels.Done)]
    public void How_a_run_finished_sets_how_much_it_wants_you(string outcome, string level)
    {
        EventRecorder.Copied(Run(), [Entry("run.finished", null, new { ended = outcome, outcome })], 0)
            .Single().Level.Should().Be(level);
    }

    private static RunEvent Entry(string kind, string? node, object data) =>
        new(Noon, node, kind, JsonSerializer.SerializeToElement(data));

    private static RunSummary Run(IReadOnlyList<RunNode>? nodes = null, IReadOnlyList<PendingAsk>? gates = null) =>
        new(
            "r1",
            Directory: "d",
            Team: "docs",
            Goal: "write it",
            Autonomy: "supervised",
            Started: Noon.AddHours(-1),
            Finished: null,
            Ended: null,
            CostUsd: 0m,
            Rounds: 1,
            Nodes: nodes ?? [],
            Merged: [],
            Branches: [],
            RoundLimit: 5,
            Project: "p",
            Gates: gates);
}
