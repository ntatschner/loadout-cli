using System.Runtime.Versioning;
using System.Text;
using AgentWorkspace.Platform.Abstractions;
using AgentWorkspace.Platform.Windows;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Platform;

/// <summary>
/// Exercises a real pseudo-terminal with a real child process.
/// <para>
/// A PTY is not something that can be usefully mocked. What matters is whether
/// the child believes it is attached to a terminal and whether its output comes
/// back through the launcher's own handles, and neither of those is observable
/// without spawning something.
/// </para>
/// </summary>
public sealed class PseudoTerminalTests
{
    /// <summary>
    /// Long enough for a cold process start on a loaded machine, short enough
    /// that a hang fails the run rather than stalling it.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task A_child_runs_in_the_pseudo_console_and_its_output_comes_back()
    {
        using var terminal = new WindowsPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest(
                "cmd.exe",
                ["/c", "echo", "PTY-MARKER-6f2a"]),
            columns: 120,
            rows: 30);

        started.Succeeded.Should().BeTrue(started.Error ?? string.Empty);

        var output = await DrainAsync(terminal);
        var exit = await terminal.WaitForExitAsync();

        output.Should().Contain("PTY-MARKER-6f2a");
        exit.Value.Should().Be(0);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task The_child_exit_code_is_reported()
    {
        using var terminal = new WindowsPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("cmd.exe", ["/c", "exit", "3"]),
            columns: 80,
            rows: 24);

        await DrainAsync(terminal);

        // A launcher that lost the child's exit status would report every
        // failed agent run as a success.
        (await terminal.WaitForExitAsync()).Value.Should().Be(3);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task The_child_sees_the_size_it_was_given()
    {
        using var terminal = new WindowsPseudoTerminal();

        // The whole point of an owned PTY over a pair of pipes: the child can
        // ask how wide its terminal is and gets an answer.
        await terminal.StartAsync(
            new ProcessRequest(
                "powershell.exe",
                ["-NoProfile", "-Command", "[Console]::WindowWidth"]),
            columns: 132,
            rows: 43);

        var output = await DrainAsync(terminal);
        (await terminal.WaitForExitAsync()).Succeeded.Should().BeTrue();

        output.Should().Contain("132");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Input_written_by_the_launcher_reaches_the_child()
    {
        using var terminal = new WindowsPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest(
                "powershell.exe",
                ["-NoProfile", "-Command", "$line = [Console]::ReadLine(); Write-Host \"GOT:$line\""]),
            columns: 100,
            rows: 30);

        await terminal.WriteAsync(Encoding.UTF8.GetBytes("hello-pty\r"));

        var output = await DrainAsync(terminal);
        (await terminal.WaitForExitAsync()).Succeeded.Should().BeTrue();

        output.Should().Contain("GOT:hello-pty");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task An_environment_variable_reaches_the_child()
    {
        using var terminal = new WindowsPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest(
                "cmd.exe",
                ["/c", "echo", "%AGENTCTL_PTY_TEST%"],
                Environment: new Dictionary<string, string>
                {
                    ["AGENTCTL_PTY_TEST"] = "value-9c3f",
                }),
            columns: 80,
            rows: 24);

        var output = await DrainAsync(terminal);
        (await terminal.WaitForExitAsync()).Succeeded.Should().BeTrue();

        // Secrets reach an agent this way, so a lost variable is not cosmetic.
        output.Should().Contain("value-9c3f");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Resizing_a_live_console_succeeds()
    {
        using var terminal = new WindowsPseudoTerminal();

        await terminal.StartAsync(
            // A child that is genuinely still running, without depending on how
            // the console translates a keystroke: what is under test is the
            // resize, not "pause".
            new ProcessRequest("cmd.exe", ["/c", "ping -n 2 127.0.0.1 > nul"]),
            columns: 80,
            rows: 24);

        terminal.Resize(140, 50).Succeeded.Should().BeTrue();

        await DrainAsync(terminal);
        (await terminal.WaitForExitAsync()).Succeeded.Should().BeTrue();
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task Starting_something_that_does_not_exist_fails_with_a_reason()
    {
        using var terminal = new WindowsPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest("agentctl-no-such-executable-4b7d.exe", []),
            columns: 80,
            rows: 24);

        started.Failed.Should().BeTrue();
        started.Error.Should().NotBeNullOrWhiteSpace();
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Disposing_a_terminal_that_never_started_is_harmless()
    {
        var terminal = new WindowsPseudoTerminal();

        // Cleanup runs on the failure path too, so it has to tolerate handles
        // that were never allocated.
        terminal.Dispose();
        terminal.Dispose();
    }

    [WindowsTheory]
    [SupportedOSPlatform("windows")]
    [InlineData(new[] { "plain" }, "exe plain")]
    [InlineData(new[] { "has space" }, "exe \"has space\"")]
    [InlineData(new[] { "quote\"inside" }, "exe \"quote\\\"inside\"")]
    [InlineData(new[] { "trailing\\" }, "exe trailing\\")]
    [InlineData(new[] { "with space\\" }, "exe \"with space\\\\\"")]
    public void Arguments_are_quoted_the_way_the_runtime_will_read_them_back(
        string[] arguments,
        string expected) =>
        // Windows passes a command line, not an argument vector, so a path with
        // a space in it arrives as two arguments unless this is exactly right.
        WindowsPseudoTerminal.BuildCommandLine(new ProcessRequest("exe", arguments))
            .Should().Be(expected);

    
/// <summary>Reads until the child closes its end of the console.</summary>
    private static async Task<string> DrainAsync(IPseudoTerminal terminal)
    {
        using var cancellation = new CancellationTokenSource(Patience);

        var buffer = new byte[4096];
        var builder = new StringBuilder();

        while (true)
        {
            var read = await terminal.ReadAsync(buffer, cancellation.Token);

            if (read.Failed || read.Value == 0)
            {
                return builder.ToString();
            }

            builder.Append(Encoding.UTF8.GetString(buffer, 0, read.Value));
        }
    }
}