using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Types and labels on tasks and ideas, from outside the process: guessed when
/// nobody gives one, chosen with --type or idea type, and labels changed on
/// every list only after somebody agrees.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskTypeContractTests
{
    [BuiltCliFact]
    public async Task A_task_declared_without_a_type_has_a_guess_and_one_given_is_kept_as_chosen()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "login", "open", "--title", "Fix the broken login", "--project", project))
            .ExitCode.Should().Be(0);
        var declared = await loadout.RunAsync(
            "task", "declare", "notes", "open", "--title", "Write it up", "--type", "release", "--project", project);

        declared.ExitCode.Should().Be(0, declared.StandardError);

        var tasks = (await loadout.RunAsync("task", "list", "--project", project, "--json")).Json()
            .GetProperty("tasks").EnumerateArray()
            .ToDictionary(one => one.GetProperty("id").GetString()!);

        tasks["login"].GetProperty("type").GetString().Should().Be("bug-fix");
        tasks["login"].GetProperty("typeBy").GetString().Should().Be("rule");
        tasks["notes"].GetProperty("type").GetString().Should().Be("release");
        tasks["notes"].GetProperty("typeBy").GetString().Should().Be("person");

        var refused = await loadout.RunAsync("task", "declare", "x", "open", "--type", "chore", "--project", project);

        refused.ExitCode.Should().NotBe(0);
        refused.StandardError.Should().Contain("'chore' is not a type");
    }

    [BuiltCliFact]
    public async Task An_idea_type_is_chosen_and_handed_back()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("idea", "add", "--text", "The login is broken on Safari", "--id", "safari", "--project", project))
            .ExitCode.Should().Be(0);

        var chosen = await loadout.RunAsync("idea", "type", "safari", "docs", "--project", project);

        chosen.ExitCode.Should().Be(0, chosen.StandardError);

        var shown = (await loadout.RunAsync("idea", "show", "safari", "--project", project, "--json")).Json();
        shown.GetProperty("type").GetString().Should().Be("docs");
        shown.GetProperty("typeBy").GetString().Should().Be("person");

        (await loadout.RunAsync("idea", "type", "safari", "agent", "--project", project)).ExitCode.Should().Be(0);

        var handed = (await loadout.RunAsync("idea", "show", "safari", "--project", project, "--json")).Json();
        handed.GetProperty("type").GetString().Should().Be("bug-fix", "the guess reads what was asked");
        handed.GetProperty("typeBy").GetString().Should().Be("rule");
    }

    [BuiltCliFact]
    public async Task A_label_is_renamed_everywhere_only_once_somebody_agrees()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "a", "open", "--title", "One", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "b", "open", "--title", "Two", "--global")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "label", "a", "homelab", "urgent", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "label", "b", "Homelab", "--global")).ExitCode.Should().Be(0);

        var counted = (await loadout.RunAsync("task", "labels", "list", "--json")).Json()
            .GetProperty("labels").EnumerateArray()
            .ToDictionary(one => one.GetProperty("name").GetString()!.ToLowerInvariant(), one => one.GetProperty("count").GetInt32());

        counted.Should().Equal(new Dictionary<string, int> { ["homelab"] = 2, ["urgent"] = 1 }, "case does not make two labels");

        var unasked = await loadout.RunAsync("task", "labels", "rename", "homelab", "home-lab");

        unasked.ExitCode.Should().NotBe(0, "nobody is there to agree to it");
        // Compared with the wrapping taken out, since redirected output wraps at 80 columns.
        string.Join(' ', unasked.StandardError.Split((char[])[' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("rename 'homelab' to 'home-lab' on 2 task(s) and idea(s)").And.Contain("--yes");
        Labels(await loadout.RunAsync("task", "show", "a", "--project", project, "--json")).Should().Equal("homelab", "urgent");

        var renamed = await loadout.RunAsync("task", "labels", "rename", "homelab", "home-lab", "--yes");

        renamed.ExitCode.Should().Be(0, renamed.StandardError);
        Labels(await loadout.RunAsync("task", "show", "a", "--project", project, "--json")).Should().Equal("home-lab", "urgent");
        Labels(await loadout.RunAsync("task", "show", "b", "--global", "--json")).Should().Equal("home-lab");
    }

    private static IEnumerable<string?> Labels(LoadoutRun shown) =>
        shown.Json().GetProperty("task").GetProperty("labels").EnumerateArray().Select(one => one.GetString());
}
