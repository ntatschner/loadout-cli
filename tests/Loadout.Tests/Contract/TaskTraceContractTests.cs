using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What a list says about a task moved off it, and what the list it went to
/// says about where it came from, from outside the process.
/// </summary>
/// <remarks>
/// The trace is kept apart from the list's tasks in the JSON, because what
/// reads the tasks counts them, suggests what to do next, and hands them to
/// sessions, and none of those should meet a task that is somewhere else now.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TaskTraceContractTests
{
    [BuiltCliFact]
    public async Task The_list_a_task_left_says_where_it_went_without_counting_it_as_its_own()
    {
        using var loadout = new LoadoutProcess();

        var from = await Registered(loadout, "first-repository");
        var to = await Registered(loadout, "second-repository");

        (await loadout.RunAsync("task", "declare", "fix-login", "open", "--title", "Fix the login", "--project", from))
            .ExitCode.Should().Be(0);

        var moved = await loadout.RunAsync("task", "move", "fix-login", "--project", from, "--to", to);

        moved.ExitCode.Should().Be(0, moved.StandardError);
        moved.StandardOutput.Should().Contain($"from {from} to {to}");

        var left = (await loadout.RunAsync("task", "list", "--project", from, "--json")).Json();

        // Registering a project gives it a task of its own, so the question is
        // whether this one is among them, not whether the list is empty.
        Ids(left.GetProperty("tasks")).Should().NotContain("fix-login", "it is not this list's work any more");
        var away = left.GetProperty("movedAway").EnumerateArray().Single();
        away.GetProperty("id").GetString().Should().Be("fix-login");
        away.GetProperty("to").GetString().Should().Be(to);

        var landed = (await loadout.RunAsync("task", "list", "--project", to, "--json")).Json();

        landed.GetProperty("movedAway").GetArrayLength().Should().Be(0);
        landed.GetProperty("tasks").EnumerateArray().Single(one => one.GetProperty("id").GetString() == "fix-login")
            .GetProperty("moved").GetProperty("from").GetString().Should().Be(from);
        var stayed = landed.GetProperty("tasks").EnumerateArray().Single(one => one.GetProperty("id").GetString() == "onboard-project");
        (stayed.TryGetProperty("moved", out var none) && none.ValueKind != System.Text.Json.JsonValueKind.Null)
            .Should().BeFalse("a task that never moved says nothing about it");

        var said = await loadout.RunAsync("task", "list", "--project", from);

        said.StandardOutput.Should().Contain($"moved to {to}").And.Contain($"loadout task list --project {to}");
    }

    private static List<string?> Ids(System.Text.Json.JsonElement tasks) =>
        [.. tasks.EnumerateArray().Select(one => one.GetProperty("id").GetString())];

    internal static async Task<string> Registered(LoadoutProcess loadout, string name)
    {
        var repository = Path.Combine(loadout.Home, name);

        Directory.CreateDirectory(repository);

        await Git(repository, "init");
        await File.WriteAllTextAsync(Path.Combine(repository, "readme.md"), name);
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--json");

        added.ExitCode.Should().Be(0, "the rest of this test is about projects that exist: " + added.StandardError);

        return added.Json().GetProperty("id").GetString()!;
    }

    private static async Task Git(string directory, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var git = System.Diagnostics.Process.Start(start)!;

        await git.WaitForExitAsync();

        git.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}");
    }
}
