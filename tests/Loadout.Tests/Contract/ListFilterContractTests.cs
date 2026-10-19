using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// --filter and --sort on the list commands beyond tasks, from outside the
/// process: the same query everywhere, and a field a kind does not have
/// refused in that kind's words.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ListFilterContractTests
{
    [BuiltCliFact]
    public async Task Ideas_and_projects_take_the_query()
    {
        using var loadout = new LoadoutProcess();

        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync("idea", "add", "--text", "Dark mode for the dashboard", "--global")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("idea", "add", "--text", "Single sign-on for the login", "--project", project)).ExitCode.Should().Be(0);

        var ideas = await Ids(loadout, "ideas", "idea", "list", "--filter", "login");
        ideas.Should().ContainSingle().Which.Should().Contain("sign-on");

        var global = await Ids(loadout, "ideas", "idea", "list", "--filter=-project:" + project);
        global.Should().ContainSingle().Which.Should().Contain("dark-mode");

        (await Ids(loadout, "projects", "list", "--filter", "where:here")).Should().Equal(project);
        (await Ids(loadout, "projects", "list", "--filter", "where:elsewhere")).Should().BeEmpty();
    }

    [BuiltCliFact]
    public async Task A_saved_project_query_narrows_what_a_person_reads_and_a_script_only_when_it_asks()
    {
        using var loadout = new LoadoutProcess();

        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync("config", "set", "studio-query-projects", "where:elsewhere")).ExitCode.Should().Be(0);

        var read = await loadout.RunAsync("list");
        read.StandardOutput.Should().Contain("Filtered by the saved query where:elsewhere").And.Contain("Nothing matches the filter");

        (await Ids(loadout, "projects", "list")).Should().Equal(project);
        (await Ids(loadout, "projects", "list", "--saved")).Should().BeEmpty();
        (await Ids(loadout, "projects", "list", "--saved", "--filter", "where:here")).Should().Equal(project);
    }

    [BuiltCliTheory]
    [InlineData("ideas", new[] { "idea", "list" })]
    [InlineData("projects", new[] { "list" })]
    [InlineData("sessions", new[] { "sessions", "--all" })]
    [InlineData("runs", new[] { "team", "runs" })]
    public async Task A_field_the_kind_does_not_have_is_refused_in_its_words(string kind, string[] command)
    {
        using var loadout = new LoadoutProcess();

        // A home with a workspace, so each command reaches its filter.
        await MemoryWriteContractTests.Registered(loadout);

        var refused = await loadout.RunAsync([.. command, "--filter", "colour:red"]);

        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain($"'colour' is not something {kind} can be filtered on");
    }

    private static async Task<List<string?>> Ids(LoadoutProcess loadout, string array, params string[] words)
    {
        var listed = await loadout.RunAsync([.. words, "--json"]);

        listed.ExitCode.Should().Be(0, listed.StandardError);

        return [.. listed.Json().GetProperty(array).EnumerateArray().Select(one => one.GetProperty("id").GetString())];
    }
}
