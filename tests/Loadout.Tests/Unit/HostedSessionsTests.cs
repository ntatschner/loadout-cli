using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The daemon's hosted sessions: what each one runs, and what happens to them
/// when one is removed or the daemon stops.
/// </summary>
public sealed class HostedSessionsTests
{
    private static readonly (string, IReadOnlyList<string>)? Launcher = ("loadout-host", ["loadout.dll"]);

    [Fact]
    public void A_session_runs_the_launch_command_with_each_option_joined_to_its_value()
    {
        var arguments = HostedSessions.Arguments(
            ["loadout.dll"],
            new HostedLaunch("starstats", Agent: "codex", Task: "-fix the flaky test", TaskId: "t1", Mode: "implement"));

        arguments.Value.Should().Equal(
            "loadout.dll", "launch", "starstats",
            "--agent=codex", "--task=-fix the flaky test", "--task-id=t1", "--mode=implement");
    }

    [Fact]
    public void Options_left_empty_are_left_out()
    {
        HostedSessions.Arguments([], new HostedLaunch("starstats", Agent: " ", Task: ""))
            .Value.Should().Equal("launch", "starstats");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--help")]
    [InlineData("star\u001bstats")]
    public void A_project_the_command_would_not_read_as_one_is_refused(string project)
    {
        HostedSessions.Arguments([], new HostedLaunch(project)).Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_task_carrying_a_control_character_is_refused()
    {
        HostedSessions.Arguments([], new HostedLaunch("starstats", Task: "one\rloadout config set"))
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Starting_one_hands_the_terminal_the_launcher_and_lists_the_session()
    {
        var terminal = new ScriptedTerminal();
        await using var sessions = new HostedSessions(() => terminal, () => Launcher);

        var started = await sessions.StartAsync(new HostedLaunch("starstats", Agent: "codex"));

        started.Succeeded.Should().BeTrue(started.Error);
        terminal.Request!.Executable.Should().Be("loadout-host");
        terminal.Request.Arguments.Should().Equal("loadout.dll", "launch", "starstats", "--agent=codex");
        sessions.All.Should().ContainSingle().Which.Title.Should().Be("starstats (codex)");
        sessions.Find(started.Value!.Id).Should().BeSameAs(started.Value);
    }

    [Fact]
    public async Task A_launcher_that_cannot_say_how_it_was_started_starts_nothing()
    {
        var terminal = new ScriptedTerminal();
        await using var sessions = new HostedSessions(() => terminal, () => null);

        var started = await sessions.StartAsync(new HostedLaunch("starstats"));

        started.Succeeded.Should().BeFalse();
        terminal.Request.Should().BeNull();
        sessions.All.Should().BeEmpty();
    }

    [Fact]
    public async Task Removing_one_ends_it_and_forgets_it()
    {
        var terminal = new ScriptedTerminal();
        await using var sessions = new HostedSessions(() => terminal, () => Launcher);
        var started = await sessions.StartAsync(new HostedLaunch("starstats"));

        (await sessions.RemoveAsync(started.Value!.Id)).Should().BeTrue();

        terminal.Disposed.Should().BeTrue();
        sessions.All.Should().BeEmpty();
        (await sessions.RemoveAsync(started.Value.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task A_session_that_ends_by_itself_stays_listed_with_how_it_ended()
    {
        var terminal = new ScriptedTerminal();
        await using var sessions = new HostedSessions(() => terminal, () => Launcher);
        var session = (await sessions.StartAsync(new HostedLaunch("starstats"))).Value!;

        terminal.Exit(3);

        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!session.HasEnded)
        {
            await Task.Delay(20, patience.Token);
        }

        sessions.All.Should().ContainSingle().Which.ExitCode.Should().Be(3);
    }

    [Fact]
    public async Task Stopping_the_daemon_ends_every_session_and_starts_no_more()
    {
        var terminals = new List<ScriptedTerminal>();
        var sessions = new HostedSessions(() => Made(terminals), () => Launcher);
        await sessions.StartAsync(new HostedLaunch("one"));
        await sessions.StartAsync(new HostedLaunch("two"));

        await sessions.DisposeAsync();

        terminals.Should().HaveCount(2).And.OnlyContain(one => one.Disposed);
        sessions.All.Should().BeEmpty();

        var late = await sessions.StartAsync(new HostedLaunch("three"));

        late.Succeeded.Should().BeFalse();
        terminals.Should().HaveCount(3).And.OnlyContain(one => one.Disposed);
    }

    private static ScriptedTerminal Made(List<ScriptedTerminal> terminals)
    {
        var terminal = new ScriptedTerminal();
        terminals.Add(terminal);

        return terminal;
    }
}
