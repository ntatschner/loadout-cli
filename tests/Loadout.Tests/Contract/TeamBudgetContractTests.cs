using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// "none" as a budget from the built command line: accepted where it is a
/// budget, and a figure that is neither a figure nor none refused before
/// anything is looked up or written.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TeamBudgetContractTests
{
    [BuiltCliTheory]
    [InlineData("team", "run", "iterating-project", "Do a thing.", "--usd", "plenty")]
    [InlineData("team", "schedule", "add", "nightly", "iterating-project", "Do a thing.", "--every", "1d", "--usd", "plenty")]
    public async Task A_budget_that_is_not_one_is_refused_before_anything_starts(params string[] asked)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(asked);

        run.ExitCode.Should().NotBe(0);
        (run.StandardOutput + run.StandardError).Should().Contain("not a budget").And.Contain("none");
    }

    [BuiltCliFact]
    public async Task The_machines_default_team_budget_takes_a_figure_or_none_and_refuses_anything_else()
    {
        using var loadout = new LoadoutProcess();

        (await loadout.RunAsync("config", "set", "team-budget", "none")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("config", "get", "team-budget")).StandardOutput.Should().Contain("none");

        (await loadout.RunAsync("config", "set", "team-budget", "$25")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("config", "get", "team-budget")).StandardOutput.Should().Contain("25");

        var refused = await loadout.RunAsync("config", "set", "team-budget", "0");

        refused.ExitCode.Should().NotBe(0, "zero is a mistake, not a way of saying no cap");
        (await loadout.RunAsync("config", "get", "team-budget")).StandardOutput.Should().Contain("25", "nothing was written");
    }
}
