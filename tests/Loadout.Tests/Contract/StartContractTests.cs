using FluentAssertions;
using Loadout.Core.Starters;
using Loadout.Models.Platform;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// <c>loadout start</c>, driven by flags up to the point where an agent would
/// start - which it never does without a person, so these tests reach the
/// handover and stop there by design rather than by stubbing.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class StartContractTests
{
    [BuiltCliFact]
    public async Task Without_a_name_in_git_it_stops_and_prints_the_commands_for_this_machine()
    {
        using var loadout = new LoadoutProcess();

        await ReadyAsync(loadout, withIdentity: false);

        var project = Path.Combine(loadout.Home, "first");
        var run = await loadout.RunAsync("start", "--yes", "--non-interactive", "--path", project);

        run.ExitCode.Should().Be(7, run.StandardOutput + run.StandardError);

        var said = Unwrapped(run.StandardOutput);

        foreach (var command in Prerequisites.GitIdentity.CommandsFor(ThisMachine))
        {
            said.Should().Contain(command, "it is the one thing missing, so it is what the guide shows");
        }

        Directory.Exists(project).Should().BeFalse("nothing is made until everything needed is there");
    }

    [BuiltCliFact]
    public async Task It_makes_the_project_protects_it_and_waits_for_a_person_before_an_agent()
    {
        using var loadout = new LoadoutProcess();

        await ReadyAsync(loadout, withIdentity: true);

        var project = Path.Combine(loadout.Home, "first");
        var run = await loadout.RunAsync(
            "start", "--starter", "web-page", "--name", "My page", "--yes", "--json", "--path", project);

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);

        var json = run.Json();

        json.GetProperty("stoppedAt").GetString().Should().Be("waiting-for-a-person");
        json.GetProperty("next").GetString().Should().Be("plan");
        json.GetProperty("project").GetProperty("id").GetString().Should().Be("my-page");

        File.Exists(Path.Combine(project, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(project, "style.css")).Should().BeTrue();
        File.ReadAllText(Path.Combine(project, "README.md")).Should().StartWith("# My page");

        (await GitAsync(project, "status", "--porcelain")).Should().BeEmpty("the starter is saved as a version");
        (await GitAsync(project, "log", "--format=%s")).Should().Contain("web-page starter");

        File.Exists(Path.Combine(project, ".git", "hooks", "pre-commit")).Should().BeTrue("it is protected");

        (await ListedAsync(loadout)).Should().Contain("my-page");

        File.Exists(Path.Combine(project, FirstProjectBriefs.PlanFile))
            .Should().BeFalse("no agent was started, so there is no plan");
    }

    [BuiltCliFact]
    public async Task Running_it_again_carries_on_rather_than_making_a_second_project()
    {
        using var loadout = new LoadoutProcess();

        await ReadyAsync(loadout, withIdentity: true);

        var project = Path.Combine(loadout.Home, "first");

        (await loadout.RunAsync("start", "--yes", "--json", "--path", project)).ExitCode.Should().Be(0);

        var again = await loadout.RunAsync("start", "--yes", "--json", "--path", Path.Combine(loadout.Home, "second"));

        again.ExitCode.Should().Be(0, again.StandardOutput + again.StandardError);
        again.Json().GetProperty("project").GetProperty("path").GetString().Should().Be(project);

        Directory.Exists(Path.Combine(loadout.Home, "second")).Should().BeFalse();
        (await ListedAsync(loadout)).Should().ContainSingle();
    }

    [BuiltCliFact]
    public async Task A_dry_run_makes_nothing()
    {
        using var loadout = new LoadoutProcess();

        await ReadyAsync(loadout, withIdentity: true);

        var project = Path.Combine(loadout.Home, "first");
        var run = await loadout.RunAsync("start", "--yes", "--non-interactive", "--dry-run", "--path", project);

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);
        Unwrapped(run.StandardOutput).Should().Contain("Nothing was changed");

        Directory.Exists(project).Should().BeFalse();
        (await ListedAsync(loadout)).Should().BeEmpty();
    }

    [BuiltCliFact]
    public async Task An_unknown_starter_is_refused_with_the_ones_there_are()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("start", "--starter", "game", "--non-interactive");

        run.ExitCode.Should().Be(2);
        Unwrapped(run.StandardOutput + run.StandardError).Should().Contain("web-page, checklist, own-idea");
    }

    private static HostOperatingSystem ThisMachine =>
        OperatingSystem.IsWindows() ? HostOperatingSystem.Windows
        : OperatingSystem.IsMacOS() ? HostOperatingSystem.MacOS
        : HostOperatingSystem.Linux;

    /// <summary>
    /// Sets up Loadout in the throwaway home, with a stand-in agent so the
    /// result does not depend on whether this machine has one installed.
    /// </summary>
    /// <remarks>
    /// Git itself stands in, being the one executable every leg has. It is only
    /// ever detected here, never launched.
    /// </remarks>
    private static async Task ReadyAsync(LoadoutProcess loadout, bool withIdentity)
    {
        var gitConfig = Path.Combine(loadout.Home, "test.gitconfig");

        await File.WriteAllTextAsync(
            gitConfig,
            withIdentity ? "[user]\n\tname = Test Person\n\temail = test@example.invalid\n" : string.Empty);

        // Explicit rather than left to HOME, and with the system file off, so
        // an identity configured anywhere on the machine cannot answer for the
        // throwaway one.
        loadout.Environment["GIT_CONFIG_GLOBAL"] = gitConfig;
        loadout.Environment["GIT_CONFIG_NOSYSTEM"] = "1";

        var setup = await loadout.RunAsync("setup", "--local-only", "--no-update-feed", "--non-interactive");

        setup.ExitCode.Should().Be(0, setup.StandardOutput + setup.StandardError);

        var config = Directory
            .EnumerateFiles(loadout.Home, "config.yaml", SearchOption.AllDirectories)
            .Single(path => !path.Contains("workspace", StringComparison.OrdinalIgnoreCase));

        var text = await File.ReadAllTextAsync(config);

        text.Should().Contain("custom_agents: {}", "the stand-in replaces the empty list setup writes");

        await File.WriteAllTextAsync(
            config,
            text.Replace(
                "custom_agents: {}",
                "custom_agents:\n  stand-in:\n    display_name: Stand-in agent\n    executable: git",
                StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<string>> ListedAsync(LoadoutProcess loadout)
    {
        var list = await loadout.RunAsync("project", "list", "--json");

        list.ExitCode.Should().Be(0, list.StandardError);

        return [.. list.Json().GetProperty("projects").EnumerateArray()
            .Select(p => p.GetProperty("id").GetString()!)];
    }

    /// <summary>The output with the console's line wrapping taken back out.</summary>
    private static string Unwrapped(string output) =>
        System.Text.RegularExpressions.Regex.Replace(output, @"\s+", " ");

    private static async Task<string> GitAsync(string directory, params string[] arguments)
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

        using var process = System.Diagnostics.Process.Start(start)!;

        var output = await process.StandardOutput.ReadToEndAsync();

        await process.WaitForExitAsync();

        return output.Trim();
    }
}
