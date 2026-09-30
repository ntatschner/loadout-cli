using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Removing a run, finding it in the bin, getting it back and emptying the
/// bin, from the built command line.
/// </summary>
/// <remarks>
/// <para>
/// The bin's own rules are covered against a directory by
/// <c>TeamBinTests</c>. What only a process can show is that the commands are
/// wired to them: that <c>remove</c> really moves rather than deletes, that the
/// refusals come back as the exit codes a script reads, and that emptying the
/// bin will not go ahead unagreed when nobody is at a terminal.
/// </para>
/// <para>
/// The run is written by hand, as <see cref="AnsweringContractTests"/> writes
/// one, because a real run would mean starting agents and spending money to
/// assert something about a directory.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TeamBinContractTests
{
    private const string Run = "20260920-0900-b1b1";

    [BuiltCliFact]
    public async Task A_removed_run_waits_in_the_bin_and_comes_back_whole()
    {
        using var loadout = new LoadoutProcess();

        var directory = await WriteRunAsync(loadout);

        var removed = await loadout.RunAsync("team", "runs", "remove", Run, "--json");

        removed.ExitCode.Should().Be(0, removed.StandardError);
        Directory.Exists(directory).Should().BeFalse();

        var bin = removed.Json().GetProperty("forgotten")[0].GetProperty("bin").GetString();

        File.Exists(Path.Combine(bin!, "journal.jsonl")).Should().BeTrue("it was moved, not deleted");

        var listed = (await loadout.RunAsync("team", "bin", "--json")).Json();

        listed.GetProperty("keptDays").GetInt32().Should().Be(30);
        var entry = listed.GetProperty("entries")[0];
        entry.GetProperty("kind").GetString().Should().Be("run");
        entry.GetProperty("name").GetString().Should().Be(Run);
        entry.GetProperty("daysLeft").GetInt32().Should().Be(30);

        // A run of the same name put back in the meantime is not overwritten.
        Directory.CreateDirectory(directory);

        var refused = await loadout.RunAsync("team", "runs", "restore", Run);

        refused.ExitCode.Should().Be(2, "a run with that identifier is already there");
        Directory.Delete(directory);

        var restored = await loadout.RunAsync("team", "runs", "restore", Run);

        restored.ExitCode.Should().Be(0, restored.StandardOutput + restored.StandardError);
        File.Exists(Path.Combine(directory, "journal.jsonl")).Should().BeTrue();

        (await loadout.RunAsync("team", "bin", "--json")).Json()
            .GetProperty("entries").GetArrayLength().Should().Be(0);
    }

    [BuiltCliFact]
    public async Task Emptying_the_bin_lists_under_dry_run_and_will_not_go_ahead_unagreed()
    {
        using var loadout = new LoadoutProcess();

        await WriteRunAsync(loadout);

        (await loadout.RunAsync("team", "runs", "remove", Run)).ExitCode.Should().Be(0);

        var dry = await loadout.RunAsync("team", "bin", "empty", "--dry-run");

        dry.ExitCode.Should().Be(0);
        dry.StandardOutput.Should().Contain(Run).And.Contain("Nothing was deleted");

        var unagreed = await loadout.RunAsync("team", "bin", "empty", "--non-interactive");

        unagreed.ExitCode.Should().Be(2, "deleting for good with nobody to agree needs --yes");

        Count(await loadout.RunAsync("team", "bin", "--json")).Should().Be(1, "neither of those deleted anything");

        (await loadout.RunAsync("team", "bin", "empty", "--yes")).ExitCode.Should().Be(0);

        Count(await loadout.RunAsync("team", "bin", "--json")).Should().Be(0);
    }

    [BuiltCliFact]
    public async Task Zero_days_is_kept_until_emptied_rather_than_deleted_at_once()
    {
        using var loadout = new LoadoutProcess();

        await WriteRunAsync(loadout);

        (await loadout.RunAsync("config", "set", "team-bin-days", "0")).ExitCode.Should().Be(0);
        (await loadout.RunAsync("team", "runs", "remove", Run)).ExitCode.Should().Be(0);

        var entry = (await loadout.RunAsync("team", "bin", "--json")).Json().GetProperty("entries")[0];

        // Absent rather than null: the command line's JSON leaves nulls out.
        // Either way, what must not be there is a count that runs down.
        (entry.TryGetProperty("daysLeft", out var left) && left.ValueKind != JsonValueKind.Null)
            .Should().BeFalse("nothing in the bin expires when it keeps things until emptied");
        entry.GetProperty("name").GetString().Should().Be(Run);
    }

    private static int Count(LoadoutRun listing) =>
        listing.Json().GetProperty("entries").GetArrayLength();

    /// <summary>Where this run's state lives, according to the run itself.</summary>
    private static async Task<string> StateAsync(LoadoutProcess loadout)
    {
        var doctor = await loadout.RunAsync("doctor", "--json");

        return doctor.Json()
            .GetProperty("checks")
            .EnumerateArray()
            .First(check => check.GetProperty("name").GetString() == "State")
            .GetProperty("detail")
            .GetString()!;
    }

    /// <summary>A finished run's journal, and nothing else.</summary>
    private static async Task<string> WriteRunAsync(LoadoutProcess loadout)
    {
        var directory = Path.Combine(await StateAsync(loadout), "teams", "runs", Run);

        Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(
            Path.Combine(directory, "journal.jsonl"),
            [
                """{"at":"2026-09-20T09:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""",
                """{"at":"2026-09-20T09:05:00+00:00","run":"r","node":null,"kind":"run.finished","data":{"ended":"done","cost":0.2,"rounds":1,"merged":[]}}""",
            ]);

        return directory;
    }
}
