using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// 'loadout session' from the built command line, where no daemon is running:
/// it says so and fails, rather than reporting an empty list or a change that
/// never happened.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class HostedSessionContractTests
{
    [BuiltCliTheory]
    [InlineData("session", "list")]
    [InlineData("session", "rename", "ab12", "docs")]
    [InlineData("session", "end", "ab12", "--yes")]
    public async Task With_no_daemon_running_it_says_so_and_fails(params string[] words)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(words);

        run.ExitCode.Should().NotBe(0);
        (run.StandardOutput + run.StandardError).Should().Contain("No daemon is running");
    }

    [BuiltCliTheory]
    [InlineData("session", "rename", "ab12")]
    [InlineData("session", "rename", "ab12", "docs", "--reset")]
    public async Task A_rename_needs_a_name_or_reset_and_not_both(params string[] words)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync(words);

        run.ExitCode.Should().NotBe(0);
        (run.StandardOutput + run.StandardError).Should().Contain("--reset");
    }
}
