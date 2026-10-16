using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Stopping or restarting a daemon that is hosting sessions for Studio says
/// that they end, before it does anything.
/// </summary>
/// <remarks>
/// The question it asks when somebody is there to answer cannot be reached
/// here: a test host's console is redirected, which is exactly the case where
/// the command does not ask.
/// </remarks>
public sealed class DaemonStopHostedTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-stophost-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly FakeProcessInspector _processes = new FakeProcessInspector().MarkLive(7070, Started);

    public DaemonStopHostedTests()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

        _paths = new LinuxPaths(
            environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        _paths.EnsureDirectoriesExist();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Theory]
    [InlineData(1, "hosting 1 session for Studio")]
    [InlineData(3, "hosting 3 sessions for Studio")]
    public async Task A_stop_says_the_sessions_it_hosts_will_end(int hosted, string expected)
    {
        Note(hosted);
        var console = new TestConsole();

        var exit = await new TeamDaemonStopCommand(_paths, _processes, console, TimeProvider.System)
            .ExecuteAsync(null!, new DaemonStopSettings { DryRun = true }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.Should().Contain(expected).And.Contain("Would stop");
    }

    [Fact]
    public async Task A_restart_says_so_too()
    {
        Note(2);
        var console = new TestConsole();

        await new TeamDaemonRestartCommand(_paths, _processes, console, TimeProvider.System)
            .ExecuteAsync(null!, new DaemonStopSettings { DryRun = true }, CancellationToken.None);

        console.Output.Should().Contain("hosting 2 sessions for Studio").And.Contain("Would restart");
    }

    [Fact]
    public async Task With_none_hosted_it_says_nothing_about_them()
    {
        Note(0);
        var console = new TestConsole();

        await new TeamDaemonStopCommand(_paths, _processes, console, TimeProvider.System)
            .ExecuteAsync(null!, new DaemonStopSettings { DryRun = true }, CancellationToken.None);

        console.Output.Should().NotContain("Studio").And.Contain("Would stop");
    }

    [Fact]
    public void A_note_written_before_sessions_were_counted_reads_as_none()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(_paths))!);
        File.WriteAllText(
            DaemonNote.PathFor(_paths),
            """{"Pid":7070,"StartedAt":"2026-10-16T09:00:00+00:00","Address":null,"Since":"2026-10-16T09:00:00+00:00"}""");

        DaemonNote.Read(_paths)!.Hosted.Should().Be(0);
    }

    private void Note(int hosted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(_paths))!);
        File.WriteAllText(
            DaemonNote.PathFor(_paths),
            System.Text.Json.JsonSerializer.Serialize(new DaemonState(7070, Started, null, Started, hosted)));
    }
}
