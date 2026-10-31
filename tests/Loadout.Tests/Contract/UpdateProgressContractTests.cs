using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The hidden <c>update --progress</c>, which the launcher's About window reads
/// line by line from a child process.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class UpdateProgressContractTests
{
    [BuiltCliFact]
    public async Task Progress_option_writes_json_lines()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_LOCAL"] = "1";

        // A local feed offering a version no build will reach, with a build for
        // every platform the suite runs on. --dry-run, so nothing is fetched.
        var feed = Path.Combine(loadout.Home, "feed.json");
        var artifact = new { url = Path.Combine(loadout.Home, "loadout.zip"), sha256 = "00" };
        await File.WriteAllTextAsync(feed, JsonSerializer.Serialize(new
        {
            version = "999.0.0",
            artifacts = new Dictionary<string, object>
            {
                ["win-x64"] = artifact,
                ["win-arm64"] = artifact,
                ["linux-x64"] = artifact,
                ["linux-arm64"] = artifact,
                ["osx-x64"] = artifact,
                ["osx-arm64"] = artifact,
            },
        }));

        var set = await loadout.RunAsync("config", "set", "updates-source", feed);
        set.ExitCode.Should().Be(0, set.StandardError);

        var run = await loadout.RunAsync("update", "--yes", "--progress", "--dry-run");
        run.ExitCode.Should().Be(0, run.StandardError);

        var lines = Lines(run.StandardOutput);

        lines.Select(line => line.GetProperty("stage").GetString()).Should().Equal("Checking", "Done");
        lines[^1].GetProperty("message").GetString().Should().Contain("999.0.0");
    }

    [BuiltCliFact]
    public async Task Check_with_progress_installs_nothing()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment["LOADOUT_LOCAL"] = "1";

        // --yes and no --dry-run, so only --check stands between this and an
        // install. The artifact does not exist, so a missed --check fails the
        // download rather than replacing anything.
        var feed = Path.Combine(loadout.Home, "feed.json");
        var artifact = new { url = Path.Combine(loadout.Home, "loadout.zip"), sha256 = "00" };
        await File.WriteAllTextAsync(feed, JsonSerializer.Serialize(new
        {
            version = "999.0.0",
            artifacts = new Dictionary<string, object>
            {
                ["win-x64"] = artifact,
                ["win-arm64"] = artifact,
                ["linux-x64"] = artifact,
                ["linux-arm64"] = artifact,
                ["osx-x64"] = artifact,
                ["osx-arm64"] = artifact,
            },
        }));

        var set = await loadout.RunAsync("config", "set", "updates-source", feed);
        set.ExitCode.Should().Be(0, set.StandardError);

        var run = await loadout.RunAsync("update", "--check", "--yes", "--progress");
        run.ExitCode.Should().Be(0, run.StandardError);

        var lines = Lines(run.StandardOutput);

        lines.Select(line => line.GetProperty("stage").GetString()).Should().Equal("Checking", "Done");
        lines[^1].GetProperty("message").GetString().Should().Contain("999.0.0");
        Directory.EnumerateDirectories(loadout.Home, "update-*", SearchOption.AllDirectories)
            .Should().BeEmpty("checking stages nothing");
    }

    private static List<JsonElement> Lines(string output) => output
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(line => JsonDocument.Parse(line).RootElement)
        .ToList();
}
