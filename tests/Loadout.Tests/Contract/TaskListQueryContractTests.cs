using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'task list --filter' and '--sort', from outside the process: the same
/// query syntax Studio and the TUI take, so a script gets the list the
/// screens show.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskListQueryContractTests
{
    [BuiltCliFact]
    public async Task A_filter_and_a_sort_choose_and_order_the_tasks()
    {
        using var loadout = await WithWorkspace();

        await Declare(loadout, "b-login", "open", "Fix the login page");
        await Declare(loadout, "a-release", "doing", "Ship the release");
        await Declare(loadout, "c-copy", "done", "Login copy");

        // The default still hides finished work, as a filter --all clears.
        Ids(await List(loadout)).Should().Equal("b-login", "a-release");
        Ids(await List(loadout, "--all")).Should().Equal("b-login", "a-release", "c-copy");

        // A filter replaces the default, so done work it names is shown.
        Ids(await List(loadout, "--filter", "login")).Should().Equal("b-login", "c-copy");
        Ids(await List(loadout, "--filter", "state:doing,done")).Should().Equal("a-release", "c-copy");
        Ids(await List(loadout, "--filter=-state:open")).Should().Equal("a-release", "c-copy");

        Ids(await List(loadout, "--all", "--sort", "id")).Should().Equal("a-release", "b-login", "c-copy");
        Ids(await List(loadout, "--all", "--sort=-id")).Should().Equal("c-copy", "b-login", "a-release");
    }

    [BuiltCliFact]
    public async Task A_field_tasks_do_not_have_is_refused_naming_the_ones_they_do()
    {
        using var loadout = await WithWorkspace();

        var refused = await loadout.RunAsync("task", "list", "--global", "--filter", "colour:red");

        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain("'colour' is not something tasks can be filtered on")
            .And.Contain("state");
    }

    [BuiltCliFact]
    public async Task A_filter_that_keeps_nothing_says_so_and_how_to_see_the_rest()
    {
        using var loadout = await WithWorkspace();

        await Declare(loadout, "b-login", "open", "Fix the login page");

        var said = await loadout.RunAsync("task", "list", "--global", "--filter", "nothing-like-this");

        said.ExitCode.Should().Be(0, said.StandardError);
        said.StandardOutput.Should().Contain("Nothing matches nothing-like-this").And.Contain("without --filter");
    }

    /// <summary>A home with a workspace in it, which registering a project makes; the tasks go on the workspace-wide list.</summary>
    private static async Task<LoadoutProcess> WithWorkspace()
    {
        var loadout = new LoadoutProcess();

        await MemoryWriteContractTests.Registered(loadout);

        return loadout;
    }

    private static async Task Declare(LoadoutProcess loadout, string id, string state, string title)
    {
        var declared = await loadout.RunAsync("task", "declare", id, state, "--title", title, "--global");

        declared.ExitCode.Should().Be(0, declared.StandardError);
    }

    private static async Task<System.Text.Json.JsonElement> List(LoadoutProcess loadout, params string[] words)
    {
        var listed = await loadout.RunAsync(["task", "list", "--global", "--json", .. words]);

        listed.ExitCode.Should().Be(0, listed.StandardError);

        return listed.Json();
    }

    private static List<string?> Ids(System.Text.Json.JsonElement listed) =>
        [.. listed.GetProperty("tasks").EnumerateArray().Select(one => one.GetProperty("id").GetString())];
}
