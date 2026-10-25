using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Closing a run whose driver has gone, from the built command line, against
/// a lock held by a real process.
/// </summary>
/// <remarks>
/// <para>
/// <c>RunCloserTests</c> covers the rules against a fake owner. What only two
/// processes can show is that the lock the runner takes is one the built
/// binary's reader really sees: on Windows that is an unshared open, on Unix
/// the exclusive flock .NET turns it into, and a mistake in either reads as a
/// free run and ends one that is still going.
/// </para>
/// <para>
/// The test host holds the lock, through the same <see cref="FileRunOwnership"/>
/// the runner uses, so the holder is a different process from
/// <c>loadout</c>. The run is written by hand, as
/// <see cref="TeamBinContractTests"/> writes one, rather than started.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class TeamRunsCloseContractTests
{
    private const string Run = "20261023-1800-c1c1";

    [BuiltCliFact]
    public async Task Close_refused_while_owner_lives_and_accepted_after()
    {
        using var loadout = new LoadoutProcess();

        var directory = await WriteRunAsync(loadout);
        var journal = Path.Combine(directory, "journal.jsonl");
        var ownership = new FileRunOwnership(new ProcessInspector(), TimeProvider.System);

        var held = ownership.Take(directory);

        held.Failed.Should().BeFalse(held.Error);

        using (held.Value!)
        {
            var refused = await loadout.RunAsync("team", "runs", "close", Run, "--json");

            refused.ExitCode.Should().Be(
                (int)ExitCode.PolicyViolation,
                "a process holds the run: " + refused.StandardOutput + refused.StandardError);

            // Exit 9 alone is shared with the take's own refusal, which a close
            // that misread the lock as free would reach; only the reading's
            // refusal says this. Whitespace is folded because redirected
            // output wraps.
            Regex.Replace(refused.StandardOutput + refused.StandardError, @"\s+", " ")
                .Should().Contain(
                    "is still going: a process on this machine is driving it",
                    "the close must be refused on reading the lock as held, not later by failing to take it");
            Finishes(journal).Should().BeEmpty("a refused close writes nothing");
        }

        var closed = await loadout.RunAsync("team", "runs", "close", Run, "--json");

        closed.ExitCode.Should().Be(0, closed.StandardOutput + closed.StandardError);

        var finished = Finishes(journal).Should().ContainSingle().Subject;

        finished.GetProperty("data").GetProperty("outcome").GetString().Should().Be("abandoned");

        var removed = await loadout.RunAsync("team", "runs", "remove", Run);

        removed.ExitCode.Should().Be(0, "a closed run needs no --force: " + removed.StandardOutput + removed.StandardError);
        Directory.Exists(directory).Should().BeFalse();
    }

    private static List<JsonElement> Finishes(string journal) =>
        [.. File.ReadAllLines(journal)
            .Where(line => line.Length > 0)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .Where(entry => entry.GetProperty("kind").GetString() == "run.finished")];

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

    /// <summary>A run that started, launched its lead, and never wrote its finish.</summary>
    private static async Task<string> WriteRunAsync(LoadoutProcess loadout)
    {
        var directory = Path.Combine(await StateAsync(loadout), "teams", "runs", Run);

        Directory.CreateDirectory(directory);

        await File.WriteAllLinesAsync(
            Path.Combine(directory, "journal.jsonl"),
            [
                """{"at":"2026-10-23T18:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""",
                """{"at":"2026-10-23T18:00:02+00:00","run":"r","node":"lead","kind":"node.launched","data":{"role":"role.project-lead"}}""",
            ]);

        return directory;
    }
}
