using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'loadout find' from outside the process: every kind searched at once,
/// grouped, with the command that shows each thing.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class FindContractTests
{
    [BuiltCliFact]
    public async Task It_finds_across_kinds_and_says_how_to_open_each()
    {
        using var loadout = new LoadoutProcess();

        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync("task", "declare", "fix-login", "open", "--title", "Fix the login page", "--project", project))
            .ExitCode.Should().Be(0);
        (await loadout.RunAsync("idea", "add", "--text", "Single sign-on for the login", "--project", project))
            .ExitCode.Should().Be(0);

        var found = await loadout.RunAsync("find", "login", "--json");

        found.ExitCode.Should().Be(0, found.StandardError);
        var groups = found.Json().GetProperty("groups").EnumerateArray().ToList();
        groups.Select(group => group.GetProperty("kind").GetString()).Should().Contain("task").And.Contain("idea");

        var task = groups.Single(group => group.GetProperty("kind").GetString() == "task")
            .GetProperty("hits").EnumerateArray().Single(hit => hit.GetProperty("id").GetString() == "fix-login");
        task.GetProperty("open").GetString().Should().Be($"loadout task list --project {project} --all");

        var narrowed = (await loadout.RunAsync("find", "--query=-kind:idea", "login", "--json"));
        narrowed.ExitCode.Should().NotBe(0, "the query is given once, after the command or as --query");

        var tasksOnly = (await loadout.RunAsync("find", "--query=-kind:idea,session,run,project login", "--json")).Json();
        tasksOnly.GetProperty("groups").EnumerateArray().Select(group => group.GetProperty("kind").GetString())
            .Should().Equal("task");

        var said = await loadout.RunAsync("find", "login");
        said.StandardOutput.Should().Contain("Tasks").And.Contain("Fix the login page");
    }

    [BuiltCliFact]
    public async Task A_field_search_does_not_have_is_refused()
    {
        using var loadout = new LoadoutProcess();

        var refused = await loadout.RunAsync("find", "colour:red");

        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain("'colour' is not something results can be filtered on");
    }
}
