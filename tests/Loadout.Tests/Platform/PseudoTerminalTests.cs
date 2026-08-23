using System.Runtime.Versioning;
using System.Text;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Unix;
using Loadout.Platform.Windows;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Platform;

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
                ["/c", "echo", "%LOADOUT_PTY_TEST%"],
                Environment: new Dictionary<string, string>
                {
                    ["LOADOUT_PTY_TEST"] = "value-9c3f",
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
            new ProcessRequest("loadout-no-such-executable-4b7d.exe", []),
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


    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_child_runs_in_the_pty_and_its_output_comes_back()
    {
        using var terminal = new UnixPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "echo PTY-MARKER-6f2a"]),
            columns: 120,
            rows: 30);

        started.Succeeded.Should().BeTrue(started.Error ?? string.Empty);

        var output = await DrainAsync(terminal);
        var exit = await terminal.WaitForExitAsync();

        output.Should().Contain("PTY-MARKER-6f2a");
        exit.Value.Should().Be(0);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_child_exit_code_is_reported_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "exit 3"]),
            columns: 80,
            rows: 24);

        await DrainAsync(terminal);

        (await terminal.WaitForExitAsync()).Value.Should().Be(3);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_child_killed_by_a_signal_is_not_reported_as_success()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "kill -TERM $$"]),
            columns: 80,
            rows: 24);

        await DrainAsync(terminal);

        // A process that died from a signal has no exit code of its own, and
        // reporting the zero that leaves would turn a killed agent into a
        // successful run. 128 plus the signal number is the shell convention.
        (await terminal.WaitForExitAsync()).Value.Should().Be(143);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_child_is_attached_to_a_real_terminal()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "tty"]),
            columns: 80,
            rows: 24);

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        // The whole point of forkpty over a pair of pipes. A child on a pipe
        // reports "not a tty" and disables colour, paging and prompting.
        output.Should().Contain("/dev/pts/").And.NotContain("not a tty");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_child_sees_the_size_it_was_given_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "stty size"]),
            columns: 132,
            rows: 43);

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        // stty prints rows then columns.
        output.Should().Contain("43 132");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_resize_reaches_the_child_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest(
                "/bin/sh",
                ["-c", "read line; stty size"]),
            columns: 80,
            rows: 24);

        terminal.Resize(140, 50).Succeeded.Should().BeTrue();

        // Told to measure only after the resize, so this asserts the new size
        // actually reached the terminal rather than that the call returned.
        await terminal.WriteAsync(Encoding.UTF8.GetBytes("go\n"));

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        output.Should().Contain("50 140");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task Input_written_by_the_launcher_reaches_the_child_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "read line; echo GOT:$line"]),
            columns: 100,
            rows: 30);

        await terminal.WriteAsync(Encoding.UTF8.GetBytes("hello-pty\n"));

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        output.Should().Contain("GOT:hello-pty");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task An_environment_variable_reaches_the_child_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest(
                "/bin/sh",
                ["-c", "echo $LOADOUT_PTY_TEST"],
                Environment: new Dictionary<string, string>
                {
                    ["LOADOUT_PTY_TEST"] = "value-9c3f",
                }),
            columns: 80,
            rows: 24);

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        // Secrets reach an agent this way, so a lost variable is not cosmetic.
        output.Should().Contain("value-9c3f");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_working_directory_is_honoured_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "pwd"], WorkingDirectory: "/tmp"),
            columns: 80,
            rows: 24);

        var output = await DrainAsync(terminal);
        await terminal.WaitForExitAsync();

        output.Should().Contain("/tmp");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task A_relative_executable_is_refused_rather_than_searched_for()
    {
        using var terminal = new UnixPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest("sh", ["-c", "true"]),
            columns: 80,
            rows: 24);

        // execve does not search PATH, and searching it in the forked child is
        // not an option: nothing that allocates may run between fork and exec.
        started.Failed.Should().BeTrue();
        started.ExitCode.Should().Be(ExitCode.InvalidArguments);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task Starting_something_that_does_not_exist_fails_with_a_reason_on_unix()
    {
        using var terminal = new UnixPseudoTerminal();

        var started = await terminal.StartAsync(
            new ProcessRequest("/usr/bin/loadout-no-such-executable-4b7d", []),
            columns: 80,
            rows: 24);

        // Reported here rather than as a child that exited 127, and the same
        // way on every architecture. posix_spawn itself is not consistent about
        // this: on x64 it hands the exec failure back to the caller, and on
        // arm64 it succeeds and lets the child carry the news, so the launcher
        // checks before it spawns rather than depending on which.
        started.Failed.Should().BeTrue();
        started.ExitCode.Should().Be(ExitCode.AgentUnavailable);
        started.Error.Should().Contain("loadout-no-such-executable-4b7d");
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public async Task The_exit_code_can_be_asked_for_twice()
    {
        using var terminal = new UnixPseudoTerminal();

        await terminal.StartAsync(
            new ProcessRequest("/bin/sh", ["-c", "exit 7"]),
            columns: 80,
            rows: 24);

        await DrainAsync(terminal);

        // A child can only be reaped once. Asking again must give the same
        // answer rather than the error a second waitpid would return.
        (await terminal.WaitForExitAsync()).Value.Should().Be(7);
        (await terminal.WaitForExitAsync()).Value.Should().Be(7);
    }

    [UnixFact]
    [UnsupportedOSPlatform("windows")]
    public void Disposing_a_unix_terminal_that_never_started_is_harmless()
    {
        var terminal = new UnixPseudoTerminal();

        terminal.Dispose();
        terminal.Dispose();
    }

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