using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'pr list' from the built command line: what is waiting by default, the
/// merged added with --all, and saying so when nothing is waiting.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class PrListContractTests
{
    private const string Landings = """
        landings:
        - branch: queue/login-20261028
          team: builders
          run_id: r1
          task: login
          state: Ready
          pull_request: https://github.com/o/website/pull/42
          why: Met its done-when.
          created_utc: 2026-10-27T09:00:00.0000000+00:00
          updated_utc: 2026-10-27T09:00:00.0000000+00:00
        - branch: teams/r1/implementer-2
          team: builders
          run_id: r1
          node: implementer-2
          task: login
          state: Pending
          why: Not merged into queue/login-20261028, so not part of the run's pull request.
          created_utc: 2026-10-27T09:00:00.0000000+00:00
          updated_utc: 2026-10-27T09:00:00.0000000+00:00
        - branch: queue/old-20261001
          team: builders
          run_id: r0
          state: Merged
          created_utc: 2026-10-01T09:00:00.0000000+00:00
          updated_utc: 2026-10-02T09:00:00.0000000+00:00
        """;

    [BuiltCliFact]
    public async Task What_is_waiting_is_listed_and_the_merged_only_with_all()
    {
        using var loadout = new LoadoutProcess();
        var directory = Path.Combine(State(loadout.Home), "delivery");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "website.yaml"), Landings);

        var waiting = await loadout.RunAsync("pr", "list", "--json");
        waiting.ExitCode.Should().Be(0, waiting.StandardError);

        var rows = waiting.Json().EnumerateArray().ToList();
        rows.Select(row => row.GetProperty("branch").GetString())
            .Should().Equal("queue/login-20261028", "teams/r1/implementer-2");
        rows[0].GetProperty("state").GetString().Should().Be("ready");
        rows[0].GetProperty("pullRequest").GetString().Should().Be("https://github.com/o/website/pull/42");
        rows[1].GetProperty("state").GetString().Should().Be("pending");
        rows[1].GetProperty("node").GetString().Should().Be("implementer-2");
        rows[1].GetProperty("project").GetString().Should().Be("website");

        var all = await loadout.RunAsync("pr", "list", "--all", "--json");
        all.ExitCode.Should().Be(0, all.StandardError);
        all.Json().EnumerateArray().Select(row => row.GetProperty("state").GetString())
            .Should().Contain("merged");

        var text = await loadout.RunAsync("pr", "list");
        text.ExitCode.Should().Be(0, text.StandardError);
        text.StandardOutput.Should().Contain("teams/r1/implementer-2").And.Contain("implementer-2 in run r1");
    }

    [BuiltCliFact]
    public async Task With_nothing_waiting_it_says_so()
    {
        using var loadout = new LoadoutProcess();

        var empty = await loadout.RunAsync("pr", "list");

        empty.ExitCode.Should().Be(0, empty.StandardError);
        empty.StandardOutput.Should().Contain("Nothing is waiting to land.");
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
