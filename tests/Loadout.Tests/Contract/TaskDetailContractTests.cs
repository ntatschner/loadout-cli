using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A long title kept to a line, said so where it is written, and the whole of
/// it read back with task show, from outside the process.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class TaskDetailContractTests
{
    private const string Long =
        "Make idea and task lists in Studio and the CLI quick to scan, because today a "
        + "card shows the whole description and nothing else fits beside it";

    [BuiltCliFact]
    public async Task A_long_title_is_shortened_said_so_and_shown_whole()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        var declared = await loadout.RunAsync("task", "declare", "scan", "open", "--title", Long, "--project", project);

        declared.ExitCode.Should().Be(0, declared.StandardError);
        declared.StandardOutput.Should().Contain("The title was over 72 characters");

        (await loadout.RunAsync("task", "declare", "scan", "doing", "--project", project, "--by", "agent"))
            .ExitCode.Should().Be(0);

        var shown = await loadout.RunAsync("task", "show", "scan", "--project", project, "--json");

        shown.ExitCode.Should().Be(0, shown.StandardError);
        var task = shown.Json();
        task.GetProperty("title").GetString()!.Length.Should().BeLessThanOrEqualTo(72);
        task.GetProperty("detail").GetString().Should().Be(Long);
        task.GetProperty("history").EnumerateArray()
            .Select(one => one.GetProperty("state").GetString())
            .Should().Equal("open", "doing");

        var said = await loadout.RunAsync("task", "show", "scan", "--project", project);

        said.StandardOutput.Should().Contain("nothing else fits beside it").And.Contain("History");
    }

    [BuiltCliFact]
    public async Task A_task_accepted_from_an_idea_names_it_and_one_that_was_not_names_none()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        (await loadout.RunAsync("task", "declare", "status-page", "open", "--title", "Status page", "--project", project)).ExitCode.Should().Be(0);
        (await loadout.RunAsync("task", "declare", "plain", "open", "--title", "Plain work", "--project", project)).ExitCode.Should().Be(0);

        // The record an accepted idea leaves beside the list, written by hand
        // because accepting one for real needs an agent's round first.
        var list = Directory.EnumerateFiles(loadout.Home, "tasks.yaml", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == project);
        var ideas = Path.Combine(Path.GetDirectoryName(list)!, "ideas");
        Directory.CreateDirectory(ideas);
        await File.WriteAllTextAsync(Path.Combine(ideas, "status-page.yaml"), $"""
            id: status-page
            ask: A status page for the home lab.
            accepted:
              accepted_utc: 2026-10-19T10:00:00.0000000+00:00
              accepted_by: nigel
              project: {project}
              plan_path: projects/{project}/ideas/status-page.md
            """);

        var linked = (await loadout.RunAsync("task", "show", "status-page", "--project", project, "--json")).Json();

        linked.GetProperty("idea").GetProperty("plan").GetString().Should().Be($"projects/{project}/ideas/status-page.md");

        var said = await loadout.RunAsync("task", "show", "status-page", "--project", project);

        // Spectre wraps redirected output at 80 columns, so the words are
        // compared with the wrapping taken out.
        string.Join(' ', said.StandardOutput.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Should().Contain("From the idea status-page")
            .And.Contain($"loadout idea show status-page --project {project} --full");

        var plain = (await loadout.RunAsync("task", "show", "plain", "--project", project, "--json")).Json();

        (plain.TryGetProperty("idea", out var none) && none.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().BeFalse("a task that never was an idea names none");
    }

    [BuiltCliFact]
    public async Task A_task_that_is_not_there_is_refused_by_name()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        var shown = await loadout.RunAsync("task", "show", "nothing-here", "--project", project);

        shown.ExitCode.Should().NotBe(0);
        (shown.StandardOutput + shown.StandardError).Should().Contain("'nothing-here' is not a task");
    }

    [BuiltCliFact]
    public async Task Shortening_with_dry_run_names_what_it_would_shorten_and_writes_nothing()
    {
        using var loadout = new LoadoutProcess();
        var project = await TaskTraceContractTests.Registered(loadout, "repository");

        // Written over as an older launcher would have left it, with no limit
        // on the title, wherever this platform keeps the workspace.
        var list = Directory.EnumerateFiles(loadout.Home, "tasks.yaml", SearchOption.AllDirectories)
            .Single(path => Path.GetFileName(Path.GetDirectoryName(path)) == project);
        await File.WriteAllTextAsync(list, $"""
            schema_version: 1
            items:
            - id: scan
              title: {Long}
              state: Open
              kind: Task
              declared_by: someone
              declared_utc: 2026-09-01T10:00:00.0000000+00:00
            """);
        var before = await File.ReadAllTextAsync(list);

        var tried = await loadout.RunAsync("task", "shorten", "--every-list", "--dry-run", "--json");

        tried.ExitCode.Should().Be(0, tried.StandardError);
        var found = tried.Json();
        found.GetProperty("written").GetBoolean().Should().BeFalse();
        found.GetProperty("shortened").EnumerateArray().Single().GetProperty("id").GetString().Should().Be("scan");
        (await File.ReadAllTextAsync(list)).Should().Be(before);

        var done = await loadout.RunAsync("task", "shorten", "--every-list", "--json");

        done.Json().GetProperty("written").GetBoolean().Should().BeTrue();
        (await File.ReadAllTextAsync(list)).Should().Contain("detail:").And.Contain("title shortened");
    }
}
