using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Telling a run that is still driven from one whose process has gone.
/// </summary>
public sealed class RunLivenessTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly RunOwner Gone = new(4242, Noon.AddDays(-1), "BOX", Noon.AddHours(-3));

    [Fact]
    public void Finished_run_is_finished_whatever_the_lock()
    {
        // A free lock on a finished run is just a runner that ended properly.
        RunLiveness.Of(Run(finished: true), new OwnerReading.Free(Gone), Noon.AddHours(-3), Noon)
            .Should().BeOfType<Liveness.Finished>();
    }

    [Fact]
    public void Held_lock_is_live()
    {
        // However quiet: a lead's turn can say nothing for a long while.
        RunLiveness.Of(Run(), new OwnerReading.Held(), Noon.AddHours(-5), Noon)
            .Should().BeOfType<Liveness.Live>();
    }

    [Fact]
    public void Free_lock_is_not_responding_and_names_the_last_owner()
    {
        // Not a guess, so no waiting: the process that held it has ended.
        RunLiveness.Of(Run(), new OwnerReading.Free(Gone), Noon.AddMinutes(-1), Noon)
            .Should().Be(new Liveness.NotResponding(Gone));
    }

    [Fact]
    public void Unknown_owner_is_unreadable_and_carries_why()
    {
        // Neither live nor gone, however quiet: an owner file that cannot be
        // read says nothing about whether its process still runs.
        RunLiveness.Of(Run(), new OwnerReading.Unknown("Access denied."), Noon.AddDays(-2), Noon)
            .Should().Be(new Liveness.Unreadable("Access denied."));
    }

    [Fact]
    public void No_record_and_quiet_past_the_threshold_is_possibly_not_responding()
    {
        var quiet = RunLiveness.OldRunQuiet + TimeSpan.FromMinutes(1);

        RunLiveness.Of(Run(started: Noon - quiet), new OwnerReading.NoRecord(), Noon - quiet, Noon)
            .Should().Be(new Liveness.PossiblyNotResponding(quiet));
    }

    [Fact]
    public void No_record_and_recent_writes_is_live()
    {
        // The journal was written a few minutes ago though no node has spoken
        // for hours: the run is still writing, so it is not judged quiet.
        RunLiveness.Of(Run(started: Noon.AddHours(-3)), new OwnerReading.NoRecord(), Noon.AddMinutes(-5), Noon)
            .Should().BeOfType<Liveness.Live>();
    }

    private static RunSummary Run(bool finished = false, DateTimeOffset? started = null) =>
        new(
            "r1",
            Directory: "d",
            Team: "docs",
            Goal: "write it",
            Autonomy: "supervised",
            Started: started ?? Noon.AddHours(-3),
            Finished: finished ? Noon.AddHours(-2) : null,
            Ended: finished ? "done" : null,
            CostUsd: 0m,
            Rounds: 1,
            Nodes: [],
            Merged: [],
            Branches: []);
}
