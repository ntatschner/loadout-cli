using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams;
using Loadout.Core.Triggers;
using Loadout.Models.Platform;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Where trigger events come from: finished runs, and the origin a triggered run carries.</summary>
public sealed class TriggerSourceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-trigsrc-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly RunJournal _runs;

    public TriggerSourceTests()
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
        _runs = new RunJournal(_paths);
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

    [Fact]
    public void An_origin_reads_back_as_it_was_written_and_anything_else_reads_as_none()
    {
        var origin = new RunOrigin("review-failures", 3, "abc123", ["nightly"]);

        RunOrigin.Parse(origin.ToString()).Should().BeEquivalentTo(origin);
        origin.Origins.Should().Equal("nightly", "review-failures");

        RunOrigin.Parse(null).Should().BeNull();
        RunOrigin.Parse("studio").Should().BeNull();
        RunOrigin.Parse("trigger:rule:x:abc:").Should().BeNull("the version is not a number");
    }

    [Fact]
    public void The_first_look_posts_nothing_and_later_finishes_arrive_once_with_their_origin()
    {
        Run("20261022-1000-a001", "alpha", finished: Now.AddHours(-2));

        var source = new RunFinishedSource(_paths.Paths.State, _runs);

        source.Look().Should().BeEmpty("turning triggers on does not answer every run that ever finished");

        var origin = new RunOrigin("review-failures", 2, "res-1", []);
        Run("20261022-1100-b001", "reviewer", finished: Now, origin: origin.ToString(), project: "loadout-cli");

        var events = source.Look();

        var one = events.Should().ContainSingle().Which;
        one.Kind.Should().Be(TriggerEventKinds.RunFinished);
        one.Subject.Should().Be("run:20261022-1100-b001");
        one.Project.Should().Be("loadout-cli");
        one.Field("team").Should().Be("reviewer");
        one.Field("outcome").Should().Be("failed");
        one.Origins.Should().Equal("review-failures");

        source.Look().Should().BeEmpty("that run has been seen");
    }

    [Fact]
    public async Task The_daemon_finds_a_triggered_run_by_its_reservation_and_settles_it_once_it_ends()
    {
        var actions = new DaemonTriggerActions(null!, _runs, null!, null!, null!);
        var origin = new RunOrigin("review-failures", 1, "res-9", []).ToString();

        (await actions.SettleAsync("res-9", CancellationToken.None)).State.Should().Be(SettleState.Unknown);

        Run("20261022-1100-c001", "reviewer", finished: null, origin: origin);
        (await actions.SettleAsync("res-9", CancellationToken.None)).State.Should().Be(SettleState.Going);

        Finish("20261022-1100-c001", Now, cost: 2.5m);
        var settled = await actions.SettleAsync("res-9", CancellationToken.None);

        settled.State.Should().Be(SettleState.Ended);
        settled.Outcome.Should().Be(new ActionOutcome(2.5m, "20261022-1100-c001"));
    }

    [Fact]
    public void A_goal_names_what_the_event_carries_and_leaves_what_it_does_not()
    {
        var happened = RunFinishedSource.Event(new RunSummary(
            "20261022-1100-d001", string.Empty, "alpha", "goal", "autonomous", Now, Now, "failed", 0m, 1, [], [], [],
            Outcome: "failed"));

        DaemonTriggerActions.Fill("Find out why {team} {outcome} in {run}; see {branch}.", happened)
            .Should().Be("Find out why alpha failed in 20261022-1100-d001; see {branch}.");
    }

    private void Run(string id, string team, DateTimeOffset? finished, string? origin = null, string? project = null)
    {
        Line(id, Now.AddHours(-3), "run.started", new { team, goal = "work", autonomy = "autonomous", project, origin });

        if (finished is { } at)
        {
            Finish(id, at, 1m);
        }
    }

    private void Finish(string id, DateTimeOffset at, decimal cost) =>
        Line(id, at, "run.finished", new { ended = "failed", outcome = "failed", cost });

    private void Line(string id, DateTimeOffset at, string kind, object data)
    {
        var directory = Path.Combine(_paths.Paths.State, "teams", "runs", id);
        Directory.CreateDirectory(directory);
        File.AppendAllText(
            Path.Combine(directory, "journal.jsonl"),
            JsonSerializer.Serialize(new { at, run = id, node = (string?)null, kind, data }) + "\n");
    }
}
