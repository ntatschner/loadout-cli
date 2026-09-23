using FluentAssertions;
using Loadout.Platform.Abstractions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// A child process the launcher talks to while it runs, against the real
/// operating system.
/// </summary>
/// <remarks>
/// <para>
/// The existing process paths were built for a question and for a person:
/// one closes stdin the moment it has written its string, the other hands
/// the child the terminal and reads nothing. An agent driven headlessly needs
/// a third shape, a pipe held open at both ends, and the only way to know
/// the pipe really stays open is to hold one to a real process and write to
/// it twice.
/// </para>
/// <para>
/// The child is the shell's own line echo, because it is on every machine
/// the suite runs on and reads stdin until it is closed, which is exactly the
/// behaviour an agent has. Nothing here depends on an agent being installed.
/// </para>
/// </remarks>
public sealed class PipedProcessTests
{
    private readonly ThrottledProcessLauncher _launcher = new();

    /// <summary>A process that echoes each line of its input until the input closes.</summary>
    /// <remarks>
    /// On Windows this is a PowerShell loop that flushes after every line.
    /// <c>findstr .</c> was the obvious choice and does the same job, except
    /// that its C runtime block-buffers stdout when it is a pipe, so nothing
    /// came back until the input was closed and the first test timed out
    /// waiting for an answer the child had already produced. The instrument
    /// was wrong, not the seam: the kill and missing-executable cases passed
    /// on the same run.
    /// </remarks>
    private static ProcessRequest LineEcho() =>
        OperatingSystem.IsWindows()
            ? new ProcessRequest(
                "powershell.exe",
                ["-NoProfile", "-NonInteractive", "-Command",
                 "while (($line = [Console]::In.ReadLine()) -ne $null) { [Console]::Out.WriteLine($line); [Console]::Out.Flush() }"])
            : new ProcessRequest("/bin/sh", ["-c", "cat"]);

    /// <summary>A process that sits there until it is killed.</summary>
    private static ProcessRequest Waiter() =>
        OperatingSystem.IsWindows()
            ? new ProcessRequest("cmd.exe", ["/d", "/c", "ping", "-n", "60", "127.0.0.1"])
            : new ProcessRequest("/bin/sh", ["-c", "sleep 60"]);

    [Fact]
    public async Task Two_messages_written_apart_are_both_answered_before_the_input_is_closed()
    {
        var started = await _launcher.StartPipedAsync(LineEcho());

        started.Succeeded.Should().BeTrue(started.Error);
        await using var child = started.Value!;

        await child.Input.WriteLineAsync("first");
        await child.Input.FlushAsync();
        (await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be("first");

        // The second write is the whole point: after the first answer the
        // child is still there, still reading, and the pipe is still open.
        await child.Input.WriteLineAsync("second");
        await child.Input.FlushAsync();
        (await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be("second");

        child.Exited.IsCompleted.Should().BeFalse("the child reads until its input is closed");

        await child.CloseInputAsync();

        (await child.Exited.WaitAsync(TimeSpan.FromSeconds(20))).Should().Be(0);
    }

    [Fact]
    public async Task Killing_the_child_completes_its_exit_with_a_failure_code()
    {
        var started = await _launcher.StartPipedAsync(Waiter());

        started.Succeeded.Should().BeTrue(started.Error);
        await using var child = started.Value!;

        child.Exited.IsCompleted.Should().BeFalse();

        child.Kill();

        var code = await child.Exited.WaitAsync(TimeSpan.FromSeconds(20));

        code.Should().NotBe(0, "a killed process does not get to report success");
    }

    [Fact]
    public async Task The_request_input_is_written_first_and_the_pipe_stays_open()
    {
        var started = await _launcher.StartPipedAsync(LineEcho() with { StandardInput = "seeded\n" });

        started.Succeeded.Should().BeTrue(started.Error);
        await using var child = started.Value!;

        (await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be("seeded");

        await child.Input.WriteLineAsync("after");
        await child.Input.FlushAsync();
        (await child.Output.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))).Should().Be("after");

        await child.CloseInputAsync();
        await child.Exited.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Fact]
    public async Task A_missing_executable_is_reported_rather_than_thrown()
    {
        var started = await _launcher.StartPipedAsync(
            new ProcessRequest(Path.Combine(Path.GetTempPath(), "no-such-agent-" + Guid.NewGuid().ToString("N")), []));

        started.Failed.Should().BeTrue();
        started.Error.Should().Contain("Could not start");
    }
}
