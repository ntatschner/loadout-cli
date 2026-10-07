using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Loadout.Tests.Platform;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Moving a finished headless conversation out of Claude Code's projects
/// folder, and back before it is resumed.
/// </summary>
/// <remarks>
/// Against a Claude folder made in a temporary directory and named through
/// <c>CLAUDE_CONFIG_DIR</c>, the way the agent itself is told where it lives,
/// so nothing here can touch the transcripts of the machine running it.
/// </remarks>
public sealed class HeadlessTranscriptsTests : IDisposable
{
    private const string Id = "0f5b3c1e-node";

    private readonly string _root;
    private readonly string _projects;
    private readonly string _store;
    private readonly HeadlessTranscripts _transcripts;

    public HeadlessTranscriptsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-transcripts-" + Guid.NewGuid().ToString("N"));

        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["CLAUDE_CONFIG_DIR"] = Path.Combine(_root, "claude"),
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        var paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(
                HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST-MACHINE"));

        paths.EnsureDirectoriesExist();

        _projects = Path.Combine(_root, "claude", "projects");
        _store = Path.Combine(paths.Paths.State, "headless", "transcripts");
        _transcripts = new HeadlessTranscripts(environment, paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    /// <summary>A transcript and its folder of subagent files, as Claude leaves them.</summary>
    private string Plant(string project = "D--git-repo", string text = "the node's conversation")
    {
        var folder = Path.Combine(_projects, project);

        Directory.CreateDirectory(Path.Combine(folder, Id, "subagents"));
        File.WriteAllText(Path.Combine(folder, Id + ".jsonl"), text);
        File.WriteAllText(Path.Combine(folder, Id, "subagents", "agent-1.jsonl"), "a subagent");

        return folder;
    }

    [Fact]
    public async Task A_finished_transcript_and_its_folder_leave_claudes_projects_folder()
    {
        var folder = Plant();

        (await _transcripts.PutAwayAsync("claude", Id)).Should().BeTrue();

        File.Exists(Path.Combine(folder, Id + ".jsonl")).Should().BeFalse("Claude's picker reads this folder");
        Directory.Exists(Path.Combine(folder, Id)).Should().BeFalse();

        File.ReadAllText(Path.Combine(_store, "D--git-repo", Id + ".jsonl")).Should().Be("the node's conversation");
        File.Exists(Path.Combine(_store, "D--git-repo", Id, "subagents", "agent-1.jsonl")).Should().BeTrue();
    }

    [Fact]
    public async Task A_restored_transcript_goes_back_to_the_folder_it_came_from()
    {
        var folder = Plant();

        await _transcripts.PutAwayAsync("claude", Id);

        _transcripts.Restore("claude", Id).Should().BeTrue();

        File.ReadAllText(Path.Combine(folder, Id + ".jsonl")).Should().Be("the node's conversation");
        File.Exists(Path.Combine(folder, Id, "subagents", "agent-1.jsonl")).Should().BeTrue();
        Directory.EnumerateFileSystemEntries(Path.Combine(_store, "D--git-repo")).Should().BeEmpty(
            "a conversation is in one place at a time");
    }

    [Fact]
    public async Task Putting_away_a_resumed_conversation_replaces_the_older_copy()
    {
        // Resumed, written to, ended again. What the agent last wrote is the
        // conversation; what was put away last time is not.
        Plant(text: "first");
        await _transcripts.PutAwayAsync("claude", Id);
        _transcripts.Restore("claude", Id);

        Plant(text: "first, then more");
        File.WriteAllText(Path.Combine(_store, "D--git-repo", Id + ".jsonl"), "stale");
        Directory.CreateDirectory(Path.Combine(_store, "D--git-repo", Id));

        (await _transcripts.PutAwayAsync("claude", Id)).Should().BeTrue();

        File.ReadAllText(Path.Combine(_store, "D--git-repo", Id + ".jsonl")).Should().Be("first, then more");
        File.Exists(Path.Combine(_store, "D--git-repo", Id, "subagents", "agent-1.jsonl")).Should().BeTrue();
    }

    [Fact]
    public async Task Nothing_to_move_is_reported_as_nothing_moved()
    {
        (await _transcripts.PutAwayAsync("claude", Id)).Should().BeFalse();
        _transcripts.Restore("claude", Id).Should().BeFalse();
    }

    [Fact]
    public async Task Another_agent_s_sessions_are_left_alone()
    {
        var folder = Plant();

        (await _transcripts.PutAwayAsync("codex", Id)).Should().BeFalse();

        File.Exists(Path.Combine(folder, Id + ".jsonl")).Should().BeTrue();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../D--git-repo/" + Id)]
    [InlineData("a\\b")]
    public async Task An_identifier_that_names_a_path_is_refused(string id)
    {
        Plant();

        (await _transcripts.PutAwayAsync("claude", id)).Should().BeFalse();
        _transcripts.Restore("claude", id).Should().BeFalse();
    }

    [WindowsFact]
    public async Task A_transcript_still_held_open_is_left_where_it_is()
    {
        var folder = Plant();
        var file = Path.Combine(folder, Id + ".jsonl");

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            (await _transcripts.PutAwayAsync("claude", Id)).Should().BeFalse();
        }

        File.Exists(file).Should().BeTrue("a transcript the agent still holds stays where the agent can find it");

        // And the next attempt, once let go, takes the rest of it.
        (await _transcripts.PutAwayAsync("claude", Id)).Should().BeTrue();
        File.Exists(Path.Combine(_store, "D--git-repo", Id, "subagents", "agent-1.jsonl")).Should().BeTrue();
    }
}
