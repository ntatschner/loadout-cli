using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which category a run's ending falls into.
/// </summary>
/// <remarks>
/// <para>
/// This decides what <c>team runs prune --failed</c> deletes, so the endings
/// below are not invented for the test: every one of them is a sentence
/// <c>TeamRunner</c> writes, taken from the assignments to <c>ended</c> in
/// that file. A sentence that stops being written is a case that stops
/// mattering; one that is added and not covered here is read back as a run
/// whose category cannot be said, which is the safe direction.
/// </para>
/// <para>
/// The run also records its category as it ends, so a later rewording cannot
/// re-file a run that has already finished. Both paths are covered.
/// </para>
/// </remarks>
public sealed class RunOutcomesTests
{
    [Theory]
    // The four the lead reports as its own status.
    [InlineData("done", RunOutcome.Done)]
    [InlineData("blocked", RunOutcome.Blocked)]
    [InlineData("failed", RunOutcome.Failed)]
    [InlineData("needs-decision", RunOutcome.NeedsDecision)]

    // Ran out of what it was given.
    [InlineData("round limit: 6 rounds", RunOutcome.Limited)]
    [InlineData("round limit: 1 round", RunOutcome.Limited)]
    [InlineData("budget spent: 4.80 of 5.00 USD", RunOutcome.Limited)]

    // Somebody stopped it.
    [InlineData("stopped by request", RunOutcome.Stopped)]
    [InlineData("stopped by the person", RunOutcome.Stopped)]
    [InlineData("stopped at a decision", RunOutcome.Stopped)]
    [InlineData("stopped before the lead was briefed", RunOutcome.Stopped)]

    // Did not get there.
    [InlineData("the lead could not be started", RunOutcome.Failed)]
    [InlineData("the lead ended without a report", RunOutcome.Failed)]
    [InlineData("the lead ended without a report. It said: I am done here", RunOutcome.Failed)]
    [InlineData("no progress: two rounds without a request or a finish", RunOutcome.Failed)]
    [InlineData("halted: the lead took an outward action its brief did not allow", RunOutcome.Failed)]
    [InlineData("halted: implementer/1 took an outward action its brief did not allow", RunOutcome.Failed)]

    // A done the lead could not account for is not a done. Asking it to
    // account for the goal is pointless if the answer is filed as success.
    [InlineData("the lead reported done without accounting for the goal", RunOutcome.Failed)]
    [InlineData("the lead reported done with 2 of 3 criteria unmet", RunOutcome.Failed)]
    public void Each_ending_a_run_can_write_is_filed(string ended, RunOutcome expected)
    {
        RunOutcomes.From(ended).Should().Be(expected);
    }

    [Fact]
    public void A_finish_with_no_word_about_how_it_went_is_unrecorded()
    {
        // Not a killed run: that one never writes a finish at all, so it
        // still reads as running and no prune takes it.
        RunOutcomes.From(null).Should().Be(RunOutcome.Unrecorded);
        RunOutcomes.From("   ").Should().Be(RunOutcome.Unrecorded);
    }

    [Fact]
    public void An_ending_nothing_here_knows_is_not_guessed_at()
    {
        // The property that keeps a filter honest. Answering "failed" to a
        // sentence nobody has seen before would delete runs on a coincidence,
        // and the caller asked for a category rather than a best effort.
        RunOutcomes.From("something a later version writes").Should().BeNull();
    }

    [Fact]
    public void A_live_run_is_running_whatever_it_last_wrote()
    {
        RunOutcomes.Of(Run(ended: null, running: true)).Should().Be(RunOutcome.Running);
    }

    [Fact]
    public void What_the_run_recorded_beats_what_its_sentence_says_now()
    {
        // The reason the category is written down at all: rewording an
        // ending must not re-file runs that have already finished.
        var run = Run(ended: "an ending whose wording has since changed", recorded: "stopped");

        RunOutcomes.Of(run).Should().Be(RunOutcome.Stopped);
    }

    [Fact]
    public void A_recorded_category_nothing_understands_falls_back_to_the_sentence()
    {
        var run = Run(ended: "done", recorded: "gubbins");

        RunOutcomes.Of(run).Should().Be(RunOutcome.Done);
    }

    [Theory]
    [InlineData("failed", RunOutcome.Failed)]
    [InlineData("FAILED", RunOutcome.Failed)]
    [InlineData(" needs-decision ", RunOutcome.NeedsDecision)]
    public void The_word_somebody_types_reads_back(string typed, RunOutcome expected)
    {
        RunOutcomes.TryParse(typed, out var outcome).Should().BeTrue();
        outcome.Should().Be(expected);
    }

    [Theory]
    [InlineData("fail")]
    [InlineData("")]
    [InlineData(null)]
    public void A_word_that_is_not_an_ending_is_refused(string? typed)
    {
        RunOutcomes.TryParse(typed, out _).Should().BeFalse();
    }

    [Fact]
    public void Every_category_has_a_word_that_reads_back_as_itself()
    {
        // The round trip the command's error message promises: everything
        // Names lists is something TryParse accepts.
        foreach (var name in RunOutcomes.Names)
        {
            RunOutcomes.TryParse(name, out var read).Should().BeTrue($"'{name}' is offered");
            RunOutcomes.Spell(read).Should().Be(name);
        }
    }

    private static RunSummary Run(string? ended, bool running = false, string? recorded = null) =>
        new(
            "r-1",
            "C:/runs/r-1",
            "bug-hunt",
            "a goal",
            "supervised",
            DateTimeOffset.UtcNow.AddHours(-1),
            running ? null : DateTimeOffset.UtcNow,
            ended,
            0.1m,
            1,
            [],
            [],
            [],
            Outcome: recorded);
}
