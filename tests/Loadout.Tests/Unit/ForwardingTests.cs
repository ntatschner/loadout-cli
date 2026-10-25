using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which commands typed in a terminal a running daemon may run instead, and
/// which have to stay in the terminal they were typed in.
/// </summary>
public sealed class ForwardingTests
{
    private static bool Forwardable(string line, bool inputRedirected = false, string? local = null)
    {
        Program.CommandNames().Should().NotBeEmpty("the catalogue is filled by registering the commands");

        return Forwarding.Forwardable(Program.Rewrite(line.Split(' ')), inputRedirected, local);
    }

    [Theory]
    [InlineData("project list")]
    [InlineData("list")]
    [InlineData("usage --days 30")]
    [InlineData("task list --project widget --json")]
    [InlineData("memory find release --project widget")]
    [InlineData("doctor")]
    public void A_command_that_only_reads_is_forwarded(string line) =>
        Forwardable(line).Should().BeTrue();

    [Theory]
    [InlineData("task declare fix-login doing --json")]
    [InlineData("memory write note --fact x --non-interactive")]
    [InlineData("task declare fix-login doing --dry-run")]
    public void A_change_told_not_to_ask_anything_is_forwarded(string line) =>
        Forwardable(line).Should().BeTrue();

    [Theory]
    [InlineData("task declare fix-login doing", "it may ask before it changes something")]
    [InlineData("launch widget", "it puts an agent in this window")]
    [InlineData("widget", "a project's name launches it")]
    [InlineData("statusline", "the agent runs it with its own input")]
    [InlineData("mcp serve", "it speaks a protocol on this terminal's streams")]
    [InlineData("config edit --json", "it opens an editor here")]
    [InlineData("code widget --json", "it opens a window from this terminal's environment")]
    [InlineData("team run iterating-project fix --json", "a run attached to this terminal must outlive a daemon restart")]
    [InlineData("update --check --json", "it replaces the daemon")]
    [InlineData("daemon stop --json", "it is the daemon")]
    [InlineData("dashboard", "it serves a page of its own")]
    public void A_command_that_needs_this_terminal_runs_here(string line, string because) =>
        Forwardable(line).Should().BeFalse(because);

    [Fact]
    public void A_command_that_reads_what_is_piped_in_runs_here_while_something_is() =>
        Forwardable("task apply --json", inputRedirected: true).Should().BeFalse("the daemon would hand it nothing to read");

    [Fact]
    public void A_command_that_reads_nothing_piped_in_is_forwarded_from_a_script() =>
        Forwardable("project list", inputRedirected: true).Should().BeTrue("a script's input is always redirected, and this reads none of it");

    [Fact]
    public void Nothing_is_forwarded_when_asked_to_run_here() =>
        Forwardable("project list", local: "1").Should().BeFalse();

    [Fact]
    public void The_screen_is_never_forwarded() =>
        Forwarding.Forwardable([], inputRedirected: false, local: null).Should().BeFalse();
}
