using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// One word for one idea, on every command that can change something.
/// <para>
/// Preview-before-mutate was a rule each command expressed in its own
/// vocabulary: <c>--apply</c> on six, <c>--dry-run</c> on one, <c>--fix</c>
/// with <c>--yes</c> on two. All of them were safe — none changed anything
/// without being told to — but learning one taught nothing about the others,
/// and a script wanting "show me and touch nothing" had to know which of three
/// spellings each command wanted.
/// </para>
/// <para>
/// <c>--dry-run</c> is now accepted everywhere and always means the same thing.
/// This asserts that, because a global option is exactly the kind of thing a
/// new command forgets to honour.
/// </para>
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class DryRunContractTests
{
    /// <summary>
    /// Commands that change files or configuration when told to.
    /// </summary>
    public static TheoryData<string> Mutating =>
    [
        "doctor",
        "drift",
        "migrate",
        "memory compress",
        "rules split",
        "project survey",
        "backup restore",
        "memory import",
    ];

    [Theory]
    [MemberData(nameof(Mutating))]
    public async Task Every_mutating_command_accepts_dry_run(string command)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), "--dry-run"]);

        // Whether it succeeds depends on there being something to act on, which
        // a throwaway home has not got. What must never happen is the option
        // being rejected: that is the failure mode a new command introduces by
        // declaring its own settings and forgetting the shared ones.
        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain(
            "Unknown option",
            $"'{command}' must accept --dry-run like every other command that can change something");

        everything.Should().NotContain("Unexpected option", $"'{command}' must accept --dry-run");
    }

    [BuiltCliFact]
    public async Task Dry_run_wins_when_both_are_asked_for()
    {
        using var loadout = new LoadoutProcess();

        // Asking for both is not a contradiction needing resolution: the more
        // cautious of the two is what was meant, and the alternative is a
        // script that thought it was previewing and was not.
        var run = await loadout.RunAsync("doctor", "--fix", "--dry-run", "--json");

        var json = run.Json();

        // Nothing was applied, so nothing is reported as having been.
        json.TryGetProperty("applied", out var applied).Should().BeFalse(
            "a dry run must not report applying anything");

        _ = applied;
    }

    [BuiltCliFact]
    public async Task Asking_to_fix_during_a_dry_run_does_nothing_at_all()
    {
        using var loadout = new LoadoutProcess();

        // Compared against the same command without --fix rather than against a
        // side effect. A throwaway home has little for a fix to change, so
        // watching for a changed file proved nothing: this asserts the stronger
        // property, that --fix under --dry-run is indistinguishable from not
        // having asked to fix anything.
        var reportOnly = await loadout.RunAsync("doctor", "--dry-run");
        var askedToFix = await loadout.RunAsync("doctor", "--fix", "--dry-run");

        askedToFix.ExitCode.Should().Be(reportOnly.ExitCode);

        askedToFix.StandardOutput.Should().Be(
            reportOnly.StandardOutput,
            "--fix under --dry-run must behave exactly as though --fix were absent");
    }

    [BuiltCliFact]
    public async Task Dry_run_changes_no_configuration()
    {
        using var loadout = new LoadoutProcess();

        var before = (await loadout.RunAsync("config", "list", "--json")).StandardOutput;

        await loadout.RunAsync("doctor", "--fix", "--dry-run");
        await loadout.RunAsync("migrate", "--dry-run");

        var after = (await loadout.RunAsync("config", "list", "--json")).StandardOutput;

        after.Should().Be(before, "a dry run is the one promise that must never be broken");
    }
}
