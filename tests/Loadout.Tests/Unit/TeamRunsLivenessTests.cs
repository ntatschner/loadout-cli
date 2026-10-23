using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>A team run nothing drives any more, as the commands that show runs say it.</summary>
/// <remarks>
/// <para>
/// Against a real journal on disk, since liveness reads the journal file's own
/// write time, and a fake owner, since the alternative is killing a process to
/// arrange a dead one.
/// </para>
/// <para>
/// JSON is written straight to <see cref="Console.Out"/>, past the test console,
/// so the cases that read it swap that out, and run alone so nothing else's
/// output lands in what they read.
/// </para>
/// </remarks>
[Collection(ConsoleOutCollection.Name)]
public sealed class TeamRunsLivenessTests : IDisposable
{
    private const string Run = "20260916-1200-aaaa";

    private readonly LivenessFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Free_lock_shows_not_responding_with_pid()
    {
        var directory = _fixture.Write(Run, LivenessFixture.Started, LivenessFixture.Launched);
        var ownership = new FakeRunOwnership().Script(directory, new OwnerReading.Free(LivenessFixture.Dead));

        var said = await _fixture.RunsAsync(ownership);

        said.Should().Contain("not responding (pid 9876, last seen");
        said.Should().Contain($"End it with: loadout team runs close {Run}");

        var json = await _fixture.RunsJsonAsync(ownership);
        var run = json.GetProperty("runs")[0];

        run.GetProperty("liveness").GetString().Should().Be("not-responding");
        run.GetProperty("running").GetBoolean().Should().BeTrue("running keeps its meaning");
        run.GetProperty("owner").GetProperty("pid").GetInt32().Should().Be(9876);
        run.GetProperty("owner").GetProperty("machine").GetString().Should().Be("BUILDBOX");
    }

    [Fact]
    public async Task Old_quiet_run_shows_possibly_not_responding()
    {
        var directory = _fixture.Write(Run, LivenessFixture.Started, LivenessFixture.Launched);
        File.SetLastWriteTimeUtc(Path.Combine(directory, "journal.jsonl"), DateTime.UtcNow.AddHours(-3));

        // No owner recorded at all: a run from a build before owners were kept.
        var ownership = new FakeRunOwnership();

        var said = await _fixture.RunsAsync(ownership);

        said.Should().Contain("possibly not responding (quiet for");
        said.Should().Contain($"End it with: loadout team runs close {Run}");

        var run = (await _fixture.RunsJsonAsync(ownership)).GetProperty("runs")[0];

        run.GetProperty("liveness").GetString().Should().Be("possibly-not-responding");
        run.TryGetProperty("owner", out _).Should().BeFalse("an owner nobody recorded is left out");
    }

    [Fact]
    public async Task Held_lock_shows_running()
    {
        var directory = _fixture.Write(Run, LivenessFixture.Started, LivenessFixture.Launched);
        var ownership = new FakeRunOwnership().Script(directory, new OwnerReading.Held());

        var said = await _fixture.RunsAsync(ownership);

        said.Should().Contain("running");
        said.Should().NotContain("not responding");
        said.Should().NotContain("team runs close");

        var run = (await _fixture.RunsJsonAsync(ownership)).GetProperty("runs")[0];

        run.GetProperty("liveness").GetString().Should().Be("live");
    }
}

/// <summary>Where 'team status' says a run's driver has gone.</summary>
public sealed class TeamStatusLivenessTests : IDisposable
{
    private const string Run = "20260916-1200-bbbb";

    private readonly LivenessFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Not_responding_points_at_close()
    {
        var directory = _fixture.Write(Run, LivenessFixture.Started, LivenessFixture.Launched);
        var ownership = new FakeRunOwnership().Script(directory, new OwnerReading.Free(LivenessFixture.Dead));
        var console = new TestConsole().Width(400);

        var command = new TeamStatusCommand(_fixture.Journal, console, TimeProvider.System, new FakeGit(_fixture.Root), ownership);
        var exit = await command.ExecuteAsync(null!,new TeamStatusCommand.Settings { Run = Run }, CancellationToken.None);

        exit.Should().Be(0);
        console.Output.Should().Contain("not responding (pid 9876, last seen");
        console.Output.Should().Contain($"End it with: loadout team runs close {Run}");
        console.Output.Should().NotContain("since it last said anything", "it is not running in any sense that helps");
    }
}

/// <summary>Stopping a run nothing drives.</summary>
public sealed class TeamHaltTests : IDisposable
{
    private const string Run = "20260916-1200-cccc";

    private readonly LivenessFixture _fixture = new();

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task Halt_on_unowned_run_says_nothing_will_read_it()
    {
        var directory = _fixture.Write(Run, LivenessFixture.Started, LivenessFixture.Launched);
        var ownership = new FakeRunOwnership().Script(directory, new OwnerReading.Free(LivenessFixture.Dead));
        var console = new TestConsole().Width(400);

        var command = new TeamHaltCommand(_fixture.Journal, console, ownership, TimeProvider.System);
        var exit = await command.ExecuteAsync(null!,new TeamHaltCommand.Settings { Run = Run }, CancellationToken.None);

        exit.Should().Be(0);
        RunControl.Stopped(directory).Should().BeTrue("the stop is still written; a reopened run clears it");
        console.Output.Should().Contain("No process is driving this run, so nothing will read the stop.");
        console.Output.Should().Contain($"End it with: loadout team runs close {Run}");
        console.Output.Should().NotContain("ends when the round it is in comes back");
    }
}

/// <summary>Runs alone, because its tests swap <see cref="Console.Out"/>.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleOutCollection
{
    public const string Name = "Console.Out";
}

/// <summary>A journal in a temporary home, and the commands that read it.</summary>
internal sealed class LivenessFixture : IDisposable
{
    public const string Started =
        """{"at":"2026-09-16T12:00:00+00:00","run":"r","node":null,"kind":"run.started","data":{"team":"bug-hunt","goal":"a goal","autonomy":"supervised"}}""";

    public const string Launched =
        """{"at":"2026-09-16T12:00:02+00:00","run":"r","node":"implementer/1","kind":"node.launched","data":{"role":"role.implementer","worktree":"teams/r/implementer-1"}}""";

    public static readonly RunOwner Dead =
        new(9876, new DateTimeOffset(2026, 9, 16, 11, 59, 0, TimeSpan.Zero), "BUILDBOX", new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));

    private readonly LinuxPaths _paths;
    private readonly FakeEnvironmentProvider _environment;

    public LivenessFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "loadout-liveness-" + Guid.NewGuid().ToString("N")[..8]);

        _environment = new FakeEnvironmentProvider(
            Path.Combine(Root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(Root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(Root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(Root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(Root, "cache"),
            });

        _paths = new LinuxPaths(
            _environment,
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        _paths.EnsureDirectoriesExist();
        Journal = new RunJournal(_paths);
    }

    public string Root { get; }

    public RunJournal Journal { get; }

    public string Write(string runId, params string[] lines)
    {
        var directory = Journal.DirectoryOf(runId);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "journal.jsonl"), lines);

        return directory;
    }

    /// <summary>What 'team runs' says, with the markup taken off.</summary>
    public async Task<string> RunsAsync(IRunOwnership ownership)
    {
        var console = new TestConsole().Width(400);

        (await Runs(console, ownership).ExecuteAsync(null!,new TeamRunsCommand.Settings(), CancellationToken.None))
            .Should().Be(0);

        return console.Output;
    }

    /// <summary>What 'team runs --json' writes.</summary>
    public async Task<JsonElement> RunsJsonAsync(IRunOwnership ownership)
    {
        var was = Console.Out;
        var captured = new StringWriter();

        Console.SetOut(captured);

        try
        {
            (await Runs(new TestConsole(), ownership).ExecuteAsync(null!,new TeamRunsCommand.Settings { Json = true }, CancellationToken.None))
                .Should().Be(0);
        }
        finally
        {
            Console.SetOut(was);
        }

        return JsonDocument.Parse(captured.ToString()).RootElement.Clone();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or DirectoryNotFoundException or UnauthorizedAccessException)
        {
        }
    }

    private TeamRunsCommand Runs(IAnsiConsole console, IRunOwnership ownership) =>
        new(Journal, console, TimeProvider.System, new ConfigurationService(_paths, _environment, new YamlStore(new NoOpFilePermissions())), ownership);
}
