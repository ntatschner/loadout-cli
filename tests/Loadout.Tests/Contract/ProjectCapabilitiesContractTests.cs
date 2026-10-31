using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// loadout project capabilities, from outside the process: what the files say,
/// every state in its fixed spelling, nothing kept without --refresh, and a
/// done-when only CI could meet named on a project with none.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ProjectCapabilitiesContractTests
{
    [BuiltCliFact]
    public async Task A_project_with_no_workflow_shows_ci_as_a_gap_and_the_work_that_needs_it()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        await File.WriteAllTextAsync(Path.Combine(loadout.Home, "repository", "package.json"), "{}");

        (await loadout.RunAsync(
                "task", "declare", "ship", "open", "--title", "Ship it", "--done-when", "CI passes on main", "--project", project))
            .ExitCode.Should().Be(0);

        var shown = await loadout.RunAsync("project", "capabilities", project, "--json");

        shown.ExitCode.Should().Be(0, shown.StandardError);
        var read = shown.Json();
        read.GetProperty("kept").GetBoolean().Should().BeFalse("nothing is kept without --refresh");
        read.GetProperty("github").GetString().Should().Be("not asked");

        var states = read.GetProperty("checks").EnumerateArray()
            .ToDictionary(one => one.GetProperty("id").GetString()!, one => one.GetProperty("state").GetString());

        states.Should().Equal(new Dictionary<string, string?>
        {
            ["ci"] = "gap",
            ["release"] = "unknown",
            ["environments"] = "unknown",
            ["branch-protection"] = "unknown",
        });
        read.GetProperty("stacks").EnumerateArray().Select(one => one.GetProperty("name").GetString()).Should().Equal("node");

        var needs = read.GetProperty("needsCi").EnumerateArray().Single();
        needs.GetProperty("id").GetString().Should().Be("ship");
        needs.GetProperty("criterion").GetString().Should().Be("CI passes on main");
    }

    [BuiltCliFact]
    public async Task Classifying_names_the_gaps_each_type_would_meet_and_creates_nothing()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var list = Directory.EnumerateFiles(loadout.Home, "tasks.yaml", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == project);

        // As a launcher from before types wrote it.
        await File.WriteAllTextAsync(list, """
            schema_version: 1
            items:
            - id: notes
              title: Publish the release notes
              state: Open
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
              note: ''
            - id: words
              title: Update the README
              state: Open
              kind: Task
              declared_by: someone-earlier
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
              note: ''
            """);
        var before = await File.ReadAllTextAsync(list);

        var classified = await loadout.RunAsync("task", "classify", "--project", project, "--dry-run", "--json");

        classified.ExitCode.Should().Be(0, classified.StandardError);
        var rows = classified.Json().GetProperty("classified").EnumerateArray()
            .ToDictionary(one => one.GetProperty("id").GetString()!);

        rows["notes"].GetProperty("type").GetString().Should().Be("release");
        rows["notes"].GetProperty("gaps").EnumerateArray().Select(one => one.GetString())
            .Should().Equal(["ci"], "a release depends on CI, and no workflow runs on push or pull request");
        rows["words"].GetProperty("gaps").GetArrayLength().Should().Be(0, "docs depend on nothing the profile checks");
        (await File.ReadAllTextAsync(list)).Should().Be(before);
        Directory.EnumerateFiles(loadout.Home, "capabilities.yaml", SearchOption.AllDirectories).Should().BeEmpty("a report keeps nothing");
    }

    [BuiltCliFact]
    public async Task Refreshing_keeps_the_profile_and_a_workflow_is_seen()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");
        var workflows = Path.Combine(loadout.Home, "repository", ".github", "workflows");
        Directory.CreateDirectory(workflows);
        await File.WriteAllTextAsync(Path.Combine(workflows, "ci.yml"), "on: pull_request\njobs:\n  b:\n    steps: []\n");

        var refreshed = await loadout.RunAsync("project", "capabilities", project, "--refresh", "--json");

        refreshed.ExitCode.Should().Be(0, refreshed.StandardError);
        refreshed.Json().GetProperty("kept").GetBoolean().Should().BeTrue();
        refreshed.Json().GetProperty("github").GetString().Should().Be("not on github.com", "the repository has no remote");

        var again = (await loadout.RunAsync("project", "capabilities", project, "--json")).Json();

        again.GetProperty("kept").GetBoolean().Should().BeTrue("the kept profile is what is shown");
        again.GetProperty("checks").EnumerateArray().First().GetProperty("state").GetString().Should().Be("present");
        again.GetProperty("needsCi").GetArrayLength().Should().Be(0);
    }
}
