using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A project with no remote lands its branches with local git: 'pr merge'
/// from the built command line shows what goes in, merges nothing without a
/// say-so, and squash-merges into main once given one.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class LocalLandingContractTests
{
    [BuiltCliFact]
    public async Task Nothing_merges_without_a_say_so_and_one_commit_lands_with_it()
    {
        using var loadout = new LoadoutProcess();
        var repository = await RepositoryAsync(loadout);
        var main = await Git(repository, "rev-parse", "main");
        var branch = await Git(repository, "rev-parse", "teams/r1/implementer-2");

        var open = await loadout.RunAsync("pr", "open", "teams/r1/implementer-2", "--dry-run");
        open.ExitCode.Should().NotBe(0, "local git has no pull requests");
        (open.StandardOutput + open.StandardError).Should().Contain("local git");

        var dry = await loadout.RunAsync(
            "pr", "merge", "teams/r1/implementer-2", "--subject", "Add the feature", "--dry-run", "--json");
        dry.ExitCode.Should().Be(0, dry.StandardError);
        dry.Json().GetProperty("into").GetString().Should().Be("main");
        dry.Json().GetProperty("diffStat").GetString().Should().Contain("feature.txt");

        // Nothing can ask here, so without --yes it is refused.
        var unasked = await loadout.RunAsync("pr", "merge", "teams/r1/implementer-2", "--subject", "Add the feature");
        unasked.ExitCode.Should().NotBe(0);
        (unasked.StandardOutput + unasked.StandardError).Should().Contain("--yes");

        (await Git(repository, "rev-parse", "main")).Should().Be(main, "declining leaves main where it was");
        (await Git(repository, "rev-parse", "teams/r1/implementer-2")).Should().Be(branch, "and the branch");

        var merged = await loadout.RunAsync(
            "pr", "merge", "teams/r1/implementer-2", "--subject", "Add the feature", "--yes", "--json");
        merged.ExitCode.Should().Be(0, merged.StandardError);
        merged.Json().GetProperty("state").GetString().Should().Be("merged");

        (await Git(repository, "log", "-1", "--format=%s", "main")).Should().Be("Add the feature");
        (await Git(repository, "rev-list", "--count", $"{main}..main")).Should().Be("1");

        var listed = await loadout.RunAsync("pr", "list", "--all", "--json");
        listed.Json()[0].GetProperty("state").GetString().Should().Be("merged");
    }

    private static async Task<string> RepositoryAsync(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, "widget");
        Directory.CreateDirectory(repository);
        await Git(repository, "init", "-q", "-b", "main");
        await Git(repository, "config", "user.email", "t@example.com");
        await Git(repository, "config", "user.name", "T");
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Widget\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "first");
        await Git(repository, "checkout", "-q", "-b", "teams/r1/implementer-2");
        await File.WriteAllTextAsync(Path.Combine(repository, "feature.txt"), "one\n");
        await Git(repository, "add", "-A");
        await Git(repository, "commit", "-q", "-m", "one");
        await Git(repository, "checkout", "-q", "--detach");

        var added = await loadout.RunAsync("project", "add", repository, "--non-interactive");
        added.ExitCode.Should().Be(0, added.StandardError);

        var directory = Path.Combine(State(loadout.Home), "delivery");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "widget.yaml"), """
            landings:
            - branch: teams/r1/implementer-2
              team: builders
              run_id: r1
              node: implementer-2
              state: Pending
              created_utc: 2026-10-27T09:00:00.0000000+00:00
              updated_utc: 2026-10-27T09:00:00.0000000+00:00
            """);

        return repository;
    }

    private static async Task<string> Git(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return output.Trim();
    }

    private static string State(string home)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(home, "Local", "Loadout");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(home, "Library", "Application Support", "Loadout", "state");
        }

        return Path.Combine(home, "data", "loadout");
    }
}
