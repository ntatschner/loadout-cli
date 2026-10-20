using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// One task list across every project, narrowed by project and read a page
/// at a time, from outside the process: what Studio's board will read.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskEveryListContractTests
{
    [BuiltCliFact]
    public async Task Every_list_is_one_list_each_task_naming_its_project()
    {
        using var loadout = new LoadoutProcess();
        var website = await TaskTraceContractTests.Registered(loadout, "website");
        var homelab = await TaskTraceContractTests.Registered(loadout, "homelab");

        (await loadout.RunAsync("task", "declare", "fix-login", "open", "--title", "Fix the login", "--project", website)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "fix-login", "doing", "--title", "Fix the NAS login", "--project", homelab)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "loose-end", "open", "--title", "Belongs nowhere yet", "--global")).ExitCode.Should().Be(0);

        var every = await loadout.RunAsync("task", "list", "--every-list", "--json");

        every.ExitCode.Should().Be(0, every.StandardError);
        var read = every.Json();
        var rows = read.GetProperty("tasks").EnumerateArray()
            .Select(one => (one.GetProperty("id").GetString(), Project(one)))
            .ToList();

        rows.Should().Contain(("fix-login", website)).And.Contain(("fix-login", homelab)).And.Contain(("loose-end", null));
        read.GetProperty("movedAway").GetArrayLength().Should().Be(0);

        var one = (await loadout.RunAsync("task", "list", "--every-list", "--filter", $"project:{homelab}", "--json")).Json();

        one.GetProperty("tasks").EnumerateArray().Select(Project).Should().OnlyContain(slug => slug == homelab);
    }

    [BuiltCliFact]
    public async Task A_page_holds_its_limit_and_says_how_many_there_are_in_all()
    {
        using var loadout = new LoadoutProcess();
        var website = await TaskTraceContractTests.Registered(loadout, "website");

        foreach (var n in Enumerable.Range(1, 5))
        {
            (await loadout.RunAsync("task", "declare", $"task-{n}", "open", "--title", $"Task {n}", "--project", website))
                .ExitCode.Should().Be(0);
        }

        var first = (await loadout.RunAsync("task", "list", "--project", website, "--filter", "id:task", "--limit", "2", "--json")).Json();

        first.GetProperty("tasks").GetArrayLength().Should().Be(2);
        first.GetProperty("total").GetInt32().Should().Be(5);
        first.GetProperty("pages").GetInt32().Should().Be(3);

        var last = (await loadout.RunAsync("task", "list", "--project", website, "--filter", "id:task", "--limit", "2", "--page", "3", "--json")).Json();

        last.GetProperty("tasks").EnumerateArray().Single().GetProperty("id").GetString().Should().Be("task-5");

        var said = await loadout.RunAsync("task", "list", "--project", website, "--filter", "id:task", "--limit", "2");

        said.StandardOutput.Should().Contain("Page 1 of 3, 5 in all. --page 2 for the next.");
    }

    [BuiltCliFact]
    public async Task Every_list_with_one_project_named_is_refused()
    {
        using var loadout = new LoadoutProcess();
        var website = await TaskTraceContractTests.Registered(loadout, "website");

        var refused = await loadout.RunAsync("task", "list", "--every-list", "--project", website);

        refused.ExitCode.Should().NotBe(0);
        (refused.StandardOutput + refused.StandardError).Should().Contain("--every-list already covers every list");
    }

    private static string? Project(System.Text.Json.JsonElement task) =>
        task.TryGetProperty("project", out var project) && project.ValueKind == System.Text.Json.JsonValueKind.String
            ? project.GetString()
            : null;
}
