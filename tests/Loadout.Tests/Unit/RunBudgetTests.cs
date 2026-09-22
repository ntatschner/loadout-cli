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

    [Theory]
    [InlineData("Raise it to $38", 38)]
    [InlineData("Raise it to $50", 50)]
    [InlineData("60", 60)]
    [InlineData("$60", 60)]
    public async Task A_run_at_its_budget_asks_how_much_more(string answer, int raised)
    {
        var console = new Answering(answer);

        (await TeamRunner.RaiseAsync(console, 25.10m, 25m, CancellationToken.None))
            .Should().Be(raised);

        console.Asked!.Options.Should().Equal("Raise it to $38", "Raise it to $50");
        console.Asked.Question.Should().Contain("$25.10").And.Contain("$25.00");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("20")]
    [InlineData("keep going")]
    public async Task Stopping_or_an_answer_that_is_not_more_money_ends_the_run(string? answer)
    {
        (await TeamRunner.RaiseAsync(new Answering(answer), 25.10m, 25m, CancellationToken.None))
            .Should().BeNull();
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
