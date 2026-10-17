using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// <c>memory show</c> and <c>memory replace</c> through the real binary: the
/// version one prints is the one the other checks, and an edit made against
/// an old version writes nothing and says so in its exit code.
/// </summary>
/// <remarks>
/// The exit code is what Studio's save reads, so it is only worth checking
/// from outside the process.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class MemoryTextContractTests
{
    private const string Description = "why installers fail with 1603 over a running app";

    [BuiltCliFact]
    public async Task An_edit_from_the_version_shown_is_saved_and_one_from_an_older_version_is_not()
    {
        using var loadout = new LoadoutProcess();
        var project = await MemoryWriteContractTests.Registered(loadout);

        (await loadout.RunAsync(
            "memory", "write", "restart-manager", "--project", project,
            "--fact", "Restart Manager is disabled by policy on this machine.", "--description", Description))
            .ExitCode.Should().Be(0);

        var shown = await loadout.RunAsync("memory", "show", "restart-manager", "--project", project, "--json");
        shown.ExitCode.Should().Be(0, shown.StandardError);
        var first = shown.Json().GetProperty("version").GetString()!;
        var text = shown.Json().GetProperty("text").GetString()!;

        var edited = Path.Combine(loadout.Home, "edited.md");
        await File.WriteAllTextAsync(edited, text + "- Close the launcher before an upgrade.\n");

        var saved = await loadout.RunAsync(
            "memory", "replace", "restart-manager", "--project", project, "--file", edited, "--expect", first);

        saved.ExitCode.Should().Be(0, saved.StandardError);

        var again = await loadout.RunAsync(
            "memory", "replace", "restart-manager", "--project", project, "--file", edited, "--expect", first, "--json");

        again.ExitCode.Should().Be(12, "the topic changed since that version, so nothing is written");

        var now = await loadout.RunAsync("memory", "show", "restart-manager", "--project", project, "--json");
        now.Json().GetProperty("text").GetString().Should().Contain("Close the launcher before an upgrade.");
        now.Json().GetProperty("version").GetString().Should().NotBe(first);
    }

    [BuiltCliFact]
    public async Task A_dry_run_checks_everything_and_writes_nothing()
    {
        using var loadout = new LoadoutProcess();
        var project = await MemoryWriteContractTests.Registered(loadout);

        await loadout.RunAsync(
            "memory", "write", "restart-manager", "--project", project,
            "--fact", "Restart Manager is disabled by policy on this machine.", "--description", Description);

        var shown = await loadout.RunAsync("memory", "show", "restart-manager", "--project", project, "--json");
        var version = shown.Json().GetProperty("version").GetString()!;
        var text = shown.Json().GetProperty("text").GetString()!;

        var edited = Path.Combine(loadout.Home, "edited.md");
        await File.WriteAllTextAsync(edited, text + "- A fact a dry run must not write.\n");

        var previewed = await loadout.RunAsync(
            "memory", "replace", "restart-manager", "--project", project, "--file", edited, "--expect", version, "--dry-run");

        previewed.ExitCode.Should().Be(0, previewed.StandardError);

        var after = await loadout.RunAsync("memory", "show", "restart-manager", "--project", project, "--json");
        after.Json().GetProperty("version").GetString().Should().Be(version);
    }

    [BuiltCliFact]
    public async Task Without_the_version_it_refuses_rather_than_guess()
    {
        using var loadout = new LoadoutProcess();
        var project = await MemoryWriteContractTests.Registered(loadout);

        var edited = Path.Combine(loadout.Home, "edited.md");
        await File.WriteAllTextAsync(edited, "---\nname: restart-manager\ndescription: " + Description + "\n---\n");

        var run = await loadout.RunAsync("memory", "replace", "restart-manager", "--project", project, "--file", edited);

        run.ExitCode.Should().Be(2);
        (run.StandardOutput + run.StandardError).Should().Contain("--expect");
    }
}
