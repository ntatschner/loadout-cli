using System.Text.Json;
using FluentAssertions;
using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Changing what a run may spend while it runs, and being asked rather than
/// stopped when it gets there.
/// </summary>
/// <remarks>
/// The budget came from the team file, was read once when the run started, and
/// ended the run when it was reached. There was nowhere to raise it: editing
/// the file mid-run changed nothing, and a run that stopped with the goal half
/// done could only be started over.
/// </remarks>
public sealed class RunBudgetTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-budget-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task A_budget_set_while_it_runs_is_read_back()
    {
        RunControl.Budget(_directory).Should().BeNull("nobody has changed it, so the team's own applies");

        await RunControl.SetBudgetAsync(_directory, 42.5m);

        RunControl.Budget(_directory).Should().Be(42.5m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("0")]
    [InlineData("-5")]
    public async Task A_budget_nobody_could_mean_is_the_team_s_own(string written)
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(Path.Combine(_directory, RunControl.BudgetFile), written);

        RunControl.Budget(_directory).Should().BeNull();
    }

    [Fact]
    public void The_run_is_measured_against_the_latest_budget()
    {
        var summary = RunJournal.Fold("r", _directory,
        [
            Event("run.started", new { team = "t", goal = "g", autonomy = "supervised", budget = 25 }),
            Event("run.budget", new { budget = 40, by = "you" }),
        ]);

        summary.BudgetUsd.Should().Be(40m);

        RunJournal.Wording(Event("run.budget", new { budget = 40, by = "you" }))
            .Should().Be("budget set to $40.00 by you");
    }

    /// <summary>
    /// Picked up with more money, the budget goes into the reopening rather than
    /// a <c>run.budget</c> of its own, because the runner starts out held to it.
    /// Folded without it, the page and <c>team status</c> went on measuring the
    /// run against the team's first figure: 14.46 "of its 12.00" on a run given
    /// 50, flagged as needing somebody for a limit it no longer had.
    /// </summary>
    [Fact]
    public void A_run_picked_up_with_more_money_is_measured_against_it()
    {
        var summary = RunJournal.Fold("r", _directory,
        [
            Event("run.started", new { team = "t", goal = "g", autonomy = "supervised", budget = 12 }),
            Event("run.finished", new { ended = "budget spent: 13.70 of 12.00 USD", outcome = "limited", cost = 13.70 }),
            Event("run.reopened", new { was = "budget spent: 13.70 of 12.00 USD", autonomy = "supervised", rounds = 0, budget = 50 }),
        ]);

        summary.BudgetUsd.Should().Be(50m);
    }

    [Theory]
    [InlineData("Raise it to $38", 38)]
    [InlineData("Raise it to $50", 50)]
    [InlineData("60", 60)]
    [InlineData("$60", 60)]
    public async Task A_run_at_its_budget_asks_how_much_more(string answer, int raised)
    {
        var console = new Answering(answer);

        (await TeamRunner.RaiseAsync(console, 25.10m, 25m, CancellationToken.None))
            .Usd.Should().Be(raised);

        console.Asked!.Options.Should().Equal("Raise it to $38", "Raise it to $50", TeamRunner.TakeTheCapOff);
        console.Asked.Question.Should().Contain("$25.10").And.Contain("$25.00");
    }

    [Theory]
    [InlineData(TeamRunner.TakeTheCapOff)]
    [InlineData("none")]
    public async Task A_run_at_its_budget_may_be_told_to_carry_on_with_no_cap(string answer)
    {
        (await TeamRunner.RaiseAsync(new Answering(answer), 25.10m, 25m, CancellationToken.None))
            .Should().Be(UsdCap.NoCap);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("20")]
    [InlineData("0")]
    [InlineData("keep going")]
    public async Task Stopping_or_an_answer_that_is_not_more_money_ends_the_run(string? answer)
    {
        (await TeamRunner.RaiseAsync(new Answering(answer), 25.10m, 25m, CancellationToken.None))
            .IsSet.Should().BeFalse();
    }

    [Fact]
    public async Task A_run_told_no_cap_while_it_runs_is_read_back_as_no_cap()
    {
        await RunControl.SetCapAsync(_directory, UsdCap.NoCap);

        RunControl.Cap(_directory).Should().Be(UsdCap.NoCap);
        RunControl.Budget(_directory).Should().BeNull("there is no figure");
    }

    [Fact]
    public void A_cap_taken_off_clears_the_figure_the_run_was_measured_against()
    {
        // A null figure used to keep the earlier one, and the page went on
        // measuring the run against a limit it no longer had.
        var summary = RunJournal.Fold("r", _directory,
        [
            Event("run.started", new { team = "t", goal = "g", autonomy = "supervised", budget = 25 }),
            Event("run.budget", new { budget = (decimal?)null, uncapped = true, by = "you" }),
        ]);

        summary.BudgetUsd.Should().BeNull();
        summary.Uncapped.Should().BeTrue();
        RunJournal.Wording(Event("run.budget", new { budget = (decimal?)null, uncapped = true, by = "you" }))
            .Should().Be("budget cap taken off by you");
    }

    [Fact]
    public void A_figure_after_no_cap_puts_a_cap_back()
    {
        var summary = RunJournal.Fold("r", _directory,
        [
            Event("run.started", new { team = "t", goal = "g", autonomy = "supervised", budget = (decimal?)null, uncapped = true }),
            Event("run.budget", new { budget = 40, uncapped = false, by = "you" }),
        ]);

        summary.BudgetUsd.Should().Be(40m);
        summary.Uncapped.Should().BeFalse();
    }

    private static RunEvent Event(string kind, object data) =>
        new(DateTimeOffset.UtcNow, null, kind, JsonSerializer.SerializeToElement(data));

    private sealed class Answering(string? answer) : ITeamConsole
    {
        public ReportQuestion? Asked { get; private set; }

        public bool CanAsk => true;

        public Task<bool> ConfirmAsync(string what, CancellationToken ct = default) => Task.FromResult(true);

        public Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default)
        {
            Asked = question;

            return Task.FromResult(answer);
        }

        public void Note(string line)
        {
        }
    }
}
