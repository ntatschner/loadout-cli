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
    public void A_picked_task_reaches_the_command_by_its_id_alone()
    {
        var arguments = TerminalLauncher.LaunchArguments(
            Project(),
            "claude",
            new LaunchOptions(Task: "Ship the thing", TaskId: "ship-it"));

        // The launch reads the title from the record. Sending it as well would
        // be a second answer that could only agree or be wrong.
        arguments.Should().Equal("alpha", "--agent", "claude", "--task-id", "ship-it");
    }

    [Fact]
    public void The_launch_command_is_one_the_parser_has()
    {
        // The seam test checks every entry in LauncherCommands.All against the
        // registered commands; this pins that the new one is in that list at
        // all, so it cannot be added and left unchecked.
        LauncherCommands.All.Should().Contain(LauncherCommands.Launch);
    }

    private static SessionLink Link(string launchId, int hour, string agent = "claude") =>
        new("abc123", agent, launchId, At(hour));

    [Fact]
    public void Resuming_carries_the_launch_the_conversation_is_linked_to()
    {
        var found = Resumption.LaunchesByConversation(
            [Link("1", 9)],
            [
                Launch("1", At(9), task: "first thing", mode: "advise"),

                // Newer, same project and agent, and before the session was
                // last active: what the old inference would have chosen.
                Launch("2", At(10), task: "second thing", mode: "implement"),
            ]);

        // Known, not guessed: the link names launch 1, so its task is the
        // one carried, however tempting the newer launch looks.
        found[Resumption.Key("claude", "abc123")].Task.Should().Be("first thing");
    }

    [Fact]
    public void A_conversation_resumed_again_carries_its_newest_launch()
    {
        var found = Resumption.LaunchesByConversation(
            [Link("2", 10), Link("1", 9)],
            [Launch("1", At(9), task: "old"), Launch("2", At(10), task: "picked up again")]);

        // Resumed with a new task, the conversation was last running under
        // that one, and the next resume should carry it.
        found[Resumption.Key("claude", "abc123")].Task.Should().Be("picked up again");
    }

    [Fact]
    public void A_conversation_with_no_link_carries_nothing()
    {
        // The old rule would have carried launch 1 over. Known links only:
        // a confident wrong guess is worse than nothing.
        Resumption.LaunchesByConversation([], [Launch("1", At(9), task: "something else")])
            .Should().BeEmpty();
    }

    [Fact]
    public void A_link_to_a_launch_the_ledger_does_not_have_carries_nothing()
    {
        Resumption.LaunchesByConversation([Link("gone", 9)], [Launch("1", At(9))])
            .Should().BeEmpty();
    }

    [Fact]
    public void A_link_is_for_its_own_agent_only()
    {
        var found = Resumption.LaunchesByConversation(
            [Link("1", 9, agent: "codex")],
            [Launch("1", At(9), task: "codex work")]);

        found.Should().NotContainKey(Resumption.Key("claude", "abc123"));
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

    [Fact]
    public void The_launch_sheet_previews_the_onboarding_a_launch_with_no_task_will_become()
    {
        // The sheet exists to say what the session about to start will load.
        // Previewing an ordinary implement session while the launch runs the
        // onboarding would be the sheet contradicting the launch.
        var (task, mode, named) = TerminalLauncher.PreviewedSession(
            new LaunchPreviewRequest(Project(), "claude", null, null, null), onboardingPending: true);

        task.Should().Be(Loadout.Core.Projects.ProjectOnboardingTask.Title);
        mode.Should().Be("investigate");
        named.Should().Contain(Loadout.Core.Projects.ProjectOnboardingTask.Skill);
    }

    [Fact]
    public void The_launch_sheet_previews_a_task_of_its_own_as_it_was_typed()
    {
        var (task, mode, named) = TerminalLauncher.PreviewedSession(
            new LaunchPreviewRequest(Project(), "claude", null, "fix the upload retry", "review"),
            onboardingPending: true);

        task.Should().Be("fix the upload retry");
        mode.Should().Be("review");
        named.Should().BeNull();
    }
}
