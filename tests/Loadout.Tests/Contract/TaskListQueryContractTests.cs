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

        // What a screen draws chips from: the query as applied, in pieces,
        // with how much was recorded before it, and the fields it can name.
        var shaped = await List(loadout, "--filter", "state:doing,done login", "--sort=-updated");
        var query = shaped.GetProperty("query");
        query.GetProperty("text").GetString().Should().Be("state:doing,done login sort:-updated");
        query.GetProperty("parts").EnumerateArray().Select(one => one.GetString()).Should().Equal("state:doing,done", "login", "sort:-updated");
        query.GetProperty("defaultQuery").GetString().Should().Be("-state:done,dropped");
        query.GetProperty("fields").EnumerateArray().Single(one => one.GetProperty("name").GetString() == "state")
            .GetProperty("choices").GetArrayLength().Should().Be(5);
        shaped.GetProperty("recorded").GetInt32().Should().Be(3);

        Ids(await List(loadout, "--all", "--sort", "id")).Should().Equal("a-release", "b-login", "c-copy");
        Ids(await List(loadout, "--all", "--sort=-id")).Should().Equal("c-copy", "b-login", "a-release");
    }

    [BuiltCliFact]
    public async Task The_list_carries_the_revision_a_change_from_it_names_and_a_change_moves_it()
    {
        using var loadout = await WithWorkspace();
        await Declare(loadout, "a", "open", "A");

        var listed = (await List(loadout)).GetProperty("revision").GetString();
        var shown = await loadout.RunAsync("task", "show", "a", "--global", "--json");

        listed.Should().NotBeNullOrEmpty();
        shown.Json().GetProperty("revision").GetString().Should().Be(listed, "one list, one revision, however it is read");

        await Declare(loadout, "a", "doing", "A");
        (await List(loadout)).GetProperty("revision").GetString().Should().NotBe(listed);
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

    [BuiltCliFact]
    public async Task A_saved_query_narrows_what_a_person_reads_and_a_script_only_when_it_asks()
    {
        using var loadout = await WithWorkspace();

        await Declare(loadout, "b-login", "open", "Fix the login page");
        await Declare(loadout, "a-release", "doing", "Ship the release");

        // Starting with a minus, so it goes as --value=, as Studio sends it.
        var set = await loadout.RunAsync("config", "set", "studio-query-tasks", "--value=-state:open");
        set.ExitCode.Should().Be(0, set.StandardError);

        var read = await loadout.RunAsync("task", "list", "--global");
        read.StandardOutput.Should().Contain("Filtered by the saved query -state:open").And.Contain("a-release").And.NotContain("b-login");

        Ids(await List(loadout)).Should().Equal("b-login", "a-release");
        Ids(await List(loadout, "--saved")).Should().Equal("a-release");
        Ids(await List(loadout, "--saved", "--filter", "login")).Should().Equal("b-login");

        var refused = await loadout.RunAsync("config", "set", "studio-query-tasks", "colour:red");
        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain("'colour' is not something tasks can be filtered on");
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
