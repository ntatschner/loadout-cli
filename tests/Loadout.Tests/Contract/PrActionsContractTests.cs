using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'pr open', 'pr ready' and 'pr merge' from the built command line, through
/// their dry runs: what each would do, what each refuses, and that a dry run
/// leaves the landings as they were. Nothing here reaches gh.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class PrActionsContractTests
{
    private const string Landings = """
        landings:
        - branch: queue/login-20261028
          team: builders
          run_id: r1
          task: login
          state: Ready
          pull_request: https://github.com/example/widget/pull/42
          created_utc: 2026-10-27T09:00:00.0000000+00:00
          updated_utc: 2026-10-27T09:00:00.0000000+00:00
        - branch: teams/r1/implementer-2
          team: builders
          run_id: r1
          node: implementer-2
          state: Pending
          created_utc: 2026-10-27T09:00:00.0000000+00:00
          updated_utc: 2026-10-27T09:00:00.0000000+00:00
        """;

    [BuiltCliFact]
    public async Task Dry_runs_say_what_would_happen_refuse_what_cannot_and_change_nothing()
    {
        using var loadout = new LoadoutProcess();
        var file = await WaitingAsync(loadout);

        var unsaid = await loadout.RunAsync("pr", "merge", "queue/login-20261028", "--dry-run");
        unsaid.ExitCode.Should().NotBe(0);
        (unsaid.StandardOutput + unsaid.StandardError).Should().Contain("--subject");

        var merge = await loadout.RunAsync(
            "pr", "merge", "queue/login-20261028", "--subject", "Fix the login", "--dry-run");
        merge.ExitCode.Should().Be(0, merge.StandardError);
        merge.StandardOutput.Should().Contain("Dry run").And.Contain("Fix the login").And.Contain("pull/42");

        var pending = await loadout.RunAsync(
            "pr", "merge", "teams/r1/implementer-2", "--subject", "Fix the login", "--dry-run");
        pending.ExitCode.Should().NotBe(0);
        (pending.StandardOutput + pending.StandardError).Should().Contain("loadout pr open");

        var ready = await loadout.RunAsync("pr", "ready", "queue/login-20261028", "--dry-run");
        ready.ExitCode.Should().NotBe(0, "it is ready already");

        var open = await loadout.RunAsync("pr", "open", "teams/r1/implementer-2", "--draft", "--dry-run", "--json");
        open.ExitCode.Should().Be(0, open.StandardError);
        open.Json().GetProperty("draft").GetBoolean().Should().BeTrue();
        open.Json().GetProperty("title").GetString().Should().Be("teams/r1/implementer-2", "it was made for no task");

        (await File.ReadAllTextAsync(file)).Should().Be(Landings, "a dry run writes nothing");
    }

    private static async Task<string> WaitingAsync(LoadoutProcess loadout)
    {
        var repository = Path.Combine(loadout.Home, "widget");
        Directory.CreateDirectory(repository);
        await File.WriteAllTextAsync(Path.Combine(repository, "README.md"), "# Widget\n");
        await Git(repository, "init");
        await Git(repository, "remote", "add", "origin", "https://github.com/example/widget.git");
        await Git(repository, "add", "-A");
        await Git(repository, "-c", "user.email=t@example.com", "-c", "user.name=T", "commit", "-m", "first");

        var added = await loadout.RunAsync("project", "add", repository, "--non-interactive");
        added.ExitCode.Should().Be(0, added.StandardError);

        var directory = Path.Combine(State(loadout.Home), "delivery");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "widget.yaml");
        await File.WriteAllTextAsync(file, Landings);

        return file;
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

        using var process = System.Diagnostics.Process.Start(start)!;

        await process.WaitForExitAsync();
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
