using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Updates;
using Loadout.Models.Platform;
using Loadout.Models.Results;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// After an update, a running daemon is still the old version, and
/// restarting it ends the sessions it hosts for Studio; both are said.
/// </summary>
public sealed class UpdateDaemonNoteTests : IDisposable
{
    private static readonly DateTimeOffset Started = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-updnote-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;

    public UpdateDaemonNoteTests()
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
    [InlineData(0, "still running the previous version. Restart it with")]
    [InlineData(1, "with 1 session for Studio that ends when it restarts")]
    [InlineData(2, "with 2 sessions for Studio that end when it restarts")]
    public async Task An_update_says_the_daemon_is_still_the_old_one_and_what_a_restart_ends(int hosted, string expected)
    {
        Note(hosted);
        var console = new TestConsole();

        var exit = await Command(console, new FakeProcessInspector().MarkLive(7070, Started))
            .ExecuteAsync(null!, new UpdateCommand.Settings { Yes = true }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.ReplaceLineEndings(" ").Should().Contain(expected);
    }

    [Fact]
    public async Task With_no_daemon_running_it_says_nothing_about_one()
    {
        Note(2);
        var console = new TestConsole();

        await Command(console, new FakeProcessInspector())
            .ExecuteAsync(null!, new UpdateCommand.Settings { Yes = true }, CancellationToken.None);

        console.Output.Should().NotContain("daemon");
    }

    private UpdateCommand Command(TestConsole console, FakeProcessInspector processes)
    {
        console.Profile.Width = 400;

        return new UpdateCommand(
            new Newer(),
            console,
            _paths,
            processes,
            new UpdateFinisher(new StubProcessLauncher(string.Empty), processes, new NoOpFilePermissions(), _paths, TimeProvider.System),
            TimeProvider.System);
    }

    private void Note(int hosted)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DaemonNote.PathFor(_paths))!);
        File.WriteAllText(
            DaemonNote.PathFor(_paths),
            System.Text.Json.JsonSerializer.Serialize(new DaemonState(7070, Started, null, Started, hosted)));
    }

    /// <summary>A release source with a newer version, which installs without touching anything.</summary>
    private sealed class Newer : IUpdateService
    {
        public Task<OperationResult<UpdateCheck>> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<UpdateCheck>.Ok(new UpdateCheck("0.66.1", "0.67.0", true, null, null)));

        public Task<OperationResult<string>> ApplyAsync(UpdateCheck check, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Ok("loadout.old"));

        public string Executable => string.Empty;

        public InstallChannel Channel => InstallChannel.Archive;

        public Task<OperationResult<string>> StageAsync(UpdateCheck check, InstallChannel channel, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Fail("not staged here"));
    }
}
