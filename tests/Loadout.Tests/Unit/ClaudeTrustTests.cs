using System.Text.Json.Nodes;
using FluentAssertions;
using Loadout.Core.Agents;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The workspace is recorded as trusted in Claude Code's own file, once, so a
/// launch that adds a project's workspace folder stops being asked about it,
/// and everything else in that file comes through untouched.
/// </summary>
public sealed class ClaudeTrustTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "loadout-claude-trust-" + Guid.NewGuid().ToString("N"));
    private readonly string _workspace;

    public ClaudeTrustTests()
    {
        Directory.CreateDirectory(_home);
        _workspace = Path.Combine(_home, "Loadout", "workspace");
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private string ConfigFile => Path.Combine(_home, ".claude.json");

    private FakeEnvironmentProvider Environment(Dictionary<string, string>? variables = null) => new(_home, variables);

    // Shaped like the real file: other settings, another project, a character
    // outside ASCII, and no newline at the end.
    private const string Existing =
        "{\n  \"numStartups\": 491,\n  \"theme\": \"dark\",\n  \"name\": \"Zoë — café\",\n"
        + "  \"projects\": {\n    \"D:/git/other\": {\n      \"allowedTools\": [],\n"
        + "      \"hasTrustDialogAccepted\": true\n    }\n  }\n}";

    [Fact]
    public void A_missing_record_is_added_and_nothing_else_changes()
    {
        File.WriteAllText(ConfigFile, Existing);

        ClaudeTrust.Ensure(Environment(), _workspace).Should().Be(ClaudeTrustOutcome.Recorded);

        var text = File.ReadAllText(ConfigFile);
        var written = JsonNode.Parse(text)!.AsObject();

        written["projects"]![ClaudeTrust.Key(_workspace)]!["hasTrustDialogAccepted"]!.GetValue<bool>()
            .Should().BeTrue();

        // Take the new record out and what is left is the file as it was.
        written["projects"]!.AsObject().Remove(ClaudeTrust.Key(_workspace));
        JsonNode.DeepEquals(written, JsonNode.Parse(Existing)).Should().BeTrue();

        text.Should().NotContain("\r\n").And.Contain("Zoë — café").And.Contain("\n  \"numStartups\": 491");
    }

    [Fact]
    public void The_key_is_the_full_path_in_forward_slashes()
    {
        var key = ClaudeTrust.Key(_workspace + Path.DirectorySeparatorChar);

        key.Should().NotContain("\\").And.NotEndWith("/");
        key.Should().Be(Path.GetFullPath(_workspace).Replace('\\', '/'));
    }

    [Fact]
    public void A_folder_already_trusted_is_not_written_again()
    {
        var trusted = Existing.Replace("D:/git/other", ClaudeTrust.Key(_workspace));
        File.WriteAllText(ConfigFile, trusted);
        var before = File.GetLastWriteTimeUtc(ConfigFile);

        ClaudeTrust.Ensure(Environment(), _workspace).Should().Be(ClaudeTrustOutcome.AlreadyTrusted);

        File.ReadAllText(ConfigFile).Should().Be(trusted);
        File.GetLastWriteTimeUtc(ConfigFile).Should().Be(before);
    }

    [Fact]
    public void A_record_that_says_no_is_turned_to_yes_and_keeps_its_other_fields()
    {
        var untrusted = Existing
            .Replace("D:/git/other", ClaudeTrust.Key(_workspace))
            .Replace("\"hasTrustDialogAccepted\": true", "\"hasTrustDialogAccepted\": false");
        File.WriteAllText(ConfigFile, untrusted);

        ClaudeTrust.Ensure(Environment(), _workspace).Should().Be(ClaudeTrustOutcome.Recorded);

        var record = JsonNode.Parse(File.ReadAllText(ConfigFile))!["projects"]![ClaudeTrust.Key(_workspace)]!;
        record["hasTrustDialogAccepted"]!.GetValue<bool>().Should().BeTrue();
        record["allowedTools"].Should().NotBeNull();
    }

    [Fact]
    public void No_file_is_made_for_a_claude_code_that_has_never_run()
    {
        ClaudeTrust.Ensure(Environment(), _workspace).Should().Be(ClaudeTrustOutcome.NotRecorded);

        File.Exists(ConfigFile).Should().BeFalse();
    }

    [Fact]
    public void A_file_that_is_not_json_is_left_alone()
    {
        File.WriteAllText(ConfigFile, "{ not json");

        ClaudeTrust.Ensure(Environment(), _workspace).Should().Be(ClaudeTrustOutcome.NotRecorded);

        File.ReadAllText(ConfigFile).Should().Be("{ not json");
        Directory.GetFiles(_home).Should().ContainSingle("no copy is left behind");
    }

    [Fact]
    public void Only_a_launch_that_adds_a_folder_inside_the_workspace_asks_for_trust()
    {
        var project = Path.Combine(_workspace, "projects", "starstats");
        var sibling = _workspace + "-other";

        Agents.AgentLauncher.AddsFolderInside(["--model", "opus", "--add-dir", project], _workspace)
            .Should().BeTrue();

        // A folder whose name only starts the same way is not inside it, nor is
        // the path given to some other option, nor the workspace with nothing after.
        Agents.AgentLauncher.AddsFolderInside(["--add-dir", sibling], _workspace).Should().BeFalse();
        Agents.AgentLauncher.AddsFolderInside(["--plugin-dir", project], _workspace).Should().BeFalse();
        Agents.AgentLauncher.AddsFolderInside(["--add-dir"], _workspace).Should().BeFalse();
    }

    [Fact]
    public void A_moved_configuration_directory_is_written_where_claude_code_reads()
    {
        var moved = Path.Combine(_home, "elsewhere");
        Directory.CreateDirectory(moved);
        File.WriteAllText(Path.Combine(moved, ".claude.json"), Existing);
        File.WriteAllText(ConfigFile, Existing);

        var environment = Environment(new() { [AgentHome.Override] = moved });

        ClaudeTrust.ConfigFile(environment).Should().Be(Path.Combine(moved, ".claude.json"));
        ClaudeTrust.Ensure(environment, _workspace).Should().Be(ClaudeTrustOutcome.Recorded);

        File.ReadAllText(Path.Combine(moved, ".claude.json")).Should().Contain(ClaudeTrust.Key(_workspace));
        File.ReadAllText(ConfigFile).Should().Be(Existing);
    }
}
