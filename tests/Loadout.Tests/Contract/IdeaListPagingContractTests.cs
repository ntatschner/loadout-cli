using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>The idea list read a page at a time, from outside the process.</summary>
[Collection(ContractCollection.Name)]
public sealed class IdeaListPagingContractTests
{
    [BuiltCliFact]
    public async Task A_page_holds_its_limit_and_says_how_many_there_are_in_all()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        foreach (var n in Enumerable.Range(1, 3))
        {
            (await loadout.RunAsync("idea", "add", $"Idea number {n}", "--project", project)).ExitCode.Should().Be(0);
        }

        var first = (await loadout.RunAsync("idea", "list", "--project", project, "--limit", "2", "--json")).Json();

        first.GetProperty("ideas").GetArrayLength().Should().Be(2);
        first.GetProperty("total").GetInt32().Should().Be(3);
        first.GetProperty("pages").GetInt32().Should().Be(2);

        var second = (await loadout.RunAsync("idea", "list", "--project", project, "--limit", "2", "--page", "2", "--json")).Json();

        second.GetProperty("ideas").GetArrayLength().Should().Be(1);

        var said = await loadout.RunAsync("idea", "list", "--project", project, "--limit", "2");

        said.StandardOutput.Should().Contain("Page 1 of 2, 3 in all. --page 2 for the next.");
    }
}
