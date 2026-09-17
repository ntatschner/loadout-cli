using FluentAssertions;
using Loadout.Agents.Claude;
using Loadout.Cli.Commands;
using Loadout.Core.Sessions;
using Loadout.Models.Agents;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The seams between the launcher's entry points, which used to disagree:
/// the screen built its own request, a resumed session lost its task, and
/// every launch detected the agent twice.
/// </summary>
public sealed class LaunchPathTests
{
    private static ProjectResolution Project(string slug = "alpha", string agent = "claude") =>
        new(new ProjectRegistryEntry { Slug = slug, Name = slug, DefaultAgent = agent },
            "/repos/" + slug, null, 0, false);

    private static AgentSession Session(
        string agent = "claude",
        string slug = "alpha",
        DateTimeOffset? lastActive = null) =>
        new(agent, "abc123", "Some conversation", "/repos/" + slug, "main",
            lastActive ?? new DateTimeOffset(2026, 9, 7, 12, 0, 0, TimeSpan.Zero),
            "/transcripts/abc123", slug);

    private static LaunchRecord Launch(
        string id,
        DateTimeOffset startedAt,
        string slug = "alpha",
        string agent = "claude",
        string? task = null,
        string? mode = null) =>
        new(id, startedAt, slug, slug, agent, mode, task, null, null, null, [], 0, 0);

    private static DateTimeOffset At(int hour) =>
        new(2026, 9, 7, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_screen_launch_with_the_defaults_is_the_bare_command()
    {
        var arguments = TerminalLauncher.LaunchArguments(Project(), "claude", new LaunchOptions());

        // No "--profile default", no "--worktree main": the launch spells the
        // default as the absence of the option, and naming it would fail.
        arguments.Should().Equal("alpha", "--agent", "claude");
    }

    [Fact]
    public void Everything_the_sheet_collects_reaches_the_command_as_its_flags()
    {
        var arguments = TerminalLauncher.LaunchArguments(
            Project(),
            "claude",
            new LaunchOptions(
                Task: "slow query",
                Mode: "investigate",
                Offline: true,
                NoSync: true,
                Agent: "codex",
                Profile: "database",
                Worktree: "feature-x",
                IncludeHandoff: true));

        // The sheet's agent wins over the project's, because it is the one
        // somebody just chose.
        arguments.Should().Equal(
            "alpha",
            "--agent", "codex",
            "--task", "slow query",
            "--mode", "investigate",
            "--profile", "database",
            "--worktree", "feature-x",
            "--handoff",
            "--offline",
            "--no-sync");
    }

    [Fact]
    public void The_launch_command_is_one_the_parser_has()
    {
        // The seam test checks every entry in LauncherCommands.All against the
        // registered commands; this pins that the new one is in that list at
        // all, so it cannot be added and left unchecked.
        LauncherCommands.All.Should().Contain(LauncherCommands.Launch);
    }

    [Fact]
    public void Resuming_carries_the_most_recent_launch_of_that_project_with_that_agent()
    {
        var found = ResumeCommand.LaunchOf(
            [
                Launch("1", At(9), task: "first thing", mode: "advise"),
                Launch("2", At(10), task: "second thing", mode: "implement"),
                Launch("3", At(10), slug: "beta", task: "another project"),
                Launch("4", At(11), agent: "codex", task: "another agent"),
            ],
            Session(lastActive: At(12)),
            "alpha");

        found.Should().NotBeNull();
        found!.Task.Should().Be("second thing");
        found.Mode.Should().Be("implement");
    }

    [Fact]
    public void A_launch_that_began_after_the_session_was_last_active_is_not_its_launch()
    {
        var found = ResumeCommand.LaunchOf(
            [Launch("1", At(9), task: "old"), Launch("2", At(14), task: "later")],
            Session(lastActive: At(12)),
            "alpha");

        // The later launch cannot be where this session came from, and
        // carrying its task over would be guessing dressed as a record.
        found!.Task.Should().Be("old");
    }

    [Fact]
    public void A_session_with_no_recorded_launch_carries_nothing()
    {
        ResumeCommand.LaunchOf([Launch("1", At(14))], Session(lastActive: At(12)), "alpha")
            .Should().BeNull();

        ResumeCommand.LaunchOf([], Session(), "alpha").Should().BeNull();
    }

    [Fact]
    public async Task An_agent_is_probed_once_however_often_it_is_asked_about()
    {
        var processes = new StubProcessLauncher("--append-system-prompt-file <path>");

        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            processes,
            []);

        var first = await adapter.DetectAsync();
        var second = await adapter.DetectAsync();

        // Two probes — version and help — for the first answer, and none for
        // the second. A launch asked twice and started the agent four times.
        processes.Requests.Should().HaveCount(2);
        second.Should().BeSameAs(first);
    }
}
