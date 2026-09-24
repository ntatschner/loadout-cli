using FluentAssertions;
using Loadout.Core.Tools;
using Loadout.Models.Tools;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the Refiner measures beyond correctness and generality: one test per
/// dimension, and the thresholds that bring a tool back to it.
/// </summary>
public sealed class ToolHealthTests : IDisposable
{
    private const string Script = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private readonly ToolStoreFixture _store = new();

    public void Dispose() => _store.Dispose();

    private static ToolRecord Head() => new()
    {
        Name = "free-cache",
        Lifecycle = ToolLifecycle.Active,
        Active = "1.1",
        KnownGood = ["1.0", "1.1"],
    };

    private static ToolVersion Active(Dictionary<string, double>? seconds = null)
    {
        var version = ToolStoreFixture.Manifest("free-cache", "1.1");
        version.Tests = new ToolTestRecord { Seconds = seconds ?? [] };

        return version;
    }

    private static ToolUsage Use(string outcome, string team, int daysAgo = 1) =>
        new() { Tool = "free-cache", Version = "1.1", Outcome = outcome, Team = team, At = Now.AddDays(-daysAgo) };

    private static ToolAuditEntry Promoted(int daysAgo) =>
        new(Now.AddDays(-daysAgo), "promote", "free-cache", "1.0", null, null, null);

    [Fact]
    public void Performance_is_the_median_and_worst_case_time_recorded_at_verify()
    {
        var health = ToolHealth.Measure(
            Head(), Active(new() { ["success"] = 1.0, ["failure"] = 3.0, ["edge"] = 2.0, ["invalid"] = 31.5 }),
            Script, [], [], null, Now);

        health.Performance.Should().Be(new ToolPerformance(4, 2.5, 31.5, "invalid"));
        health.Crossed.Should().ContainSingle().Which.Should().Contain("invalid");
    }

    [Fact]
    public async Task Verify_records_a_time_for_every_case_it_ran()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        var health = registry.Health("free-cache").Should().ContainSingle().Subject;

        health.Performance.Cases.Should().Be(ToolCaseClass.All.Count);
        health.Performance.WorstSeconds.Should().NotBeNull();
    }

    [Fact]
    public void Usability_is_the_recent_failed_and_workaround_rates_and_how_many_teams()
    {
        // Twenty-two uses: the two oldest failed, and fall outside the window.
        var usage = new List<ToolUsage> { Use(ToolOutcome.Failed, "a"), Use(ToolOutcome.Failed, "a") };
        usage.AddRange(Enumerable.Range(0, 15).Select(_ => Use(ToolOutcome.Ok, "a")));
        usage.AddRange(Enumerable.Range(0, 5).Select(i => Use(ToolOutcome.Workaround, "team-" + i)));

        var health = ToolHealth.Measure(Head(), Active(), Script, usage, [], null, Now);

        health.Usability.Uses.Should().Be(ToolHealth.UsageWindow);
        health.Usability.FailedRate.Should().Be(0);
        health.Usability.WorkaroundRate.Should().Be(0.25);
        health.Usability.Teams.Should().Be(6);
        health.Usability.TeamShare.Should().Be(0.3);
        health.Crossed.Should().ContainSingle().Which.Should().Contain("workaround");
    }

    [Fact]
    public void A_rate_on_fewer_than_the_minimum_uses_crosses_nothing()
    {
        var usage = Enumerable.Range(0, ToolHealth.MinimumUses - 1).Select(_ => Use(ToolOutcome.Failed, "a")).ToList();

        ToolHealth.Measure(Head(), Active(), Script, usage, [], null, Now).Crossed.Should().BeEmpty();
    }

    [Fact]
    public void Maintainability_is_script_size_inputs_dependencies_and_churn()
    {
        var audit = new[] { Promoted(200), Promoted(40), Promoted(3) };

        var health = ToolHealth.Measure(Head(), Active(), Script + "\n\n# note\n", [], audit, null, Now);

        health.Maintainability.Should().Be(new ToolMaintainability(3, 1, 1, 2, 2));
    }

    [Fact]
    public void Relevance_is_days_idle_and_a_newer_overlapping_tool()
    {
        var idle = ToolHealth.Measure(
            Head(), Active(), Script, [Use(ToolOutcome.Ok, "a", daysAgo: ToolHealth.IdleDays)], [Promoted(100)], "clear-cache", Now);

        idle.Relevance.DaysIdle.Should().Be(ToolHealth.IdleDays);
        idle.Relevance.SupersededBy.Should().Be("clear-cache");
        idle.Crossed.Should().ContainSingle().Which.Should().Contain("60 days");

        // Never used: idle since it was promoted.
        ToolHealth.Measure(Head(), Active(), Script, [], [Promoted(10)], null, Now)
            .Relevance.DaysIdle.Should().Be(10);
    }

    [Fact]
    public async Task A_crossing_the_stand_downs_already_saw_does_not_override_them()
    {
        var (registry, _) = _store.Registry();
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        // Two failures in eight: a quarter, on exactly the minimum sample.
        foreach (var outcome in new[] { "failed", "failed", "ok", "ok", "ok", "ok", "ok", "ok" })
        {
            registry.RecordUsage(new ToolUsage { Tool = "free-cache", Version = "1.0", Outcome = outcome, Team = "a" })
                .Succeeded.Should().BeTrue();
        }

        registry.StandDown("free-cache", "nothing worth changing").Succeeded.Should().BeTrue();
        registry.StandDown("free-cache", "still nothing").Succeeded.Should().BeTrue();

        registry.NeedsRefining("free-cache").Should().BeFalse();
    }

    [Fact]
    public async Task An_unchanged_crossing_after_two_stand_downs_is_not_refined_again()
    {
        var clock = new Clock(Now);
        var (registry, _) = _store.Registry(clock: clock);
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        clock.Advance(TimeSpan.FromDays(ToolHealth.IdleDays + 1));
        registry.NeedsRefining("free-cache").Should().BeTrue();
        registry.StandDown("free-cache", "idle, but nothing to change").Succeeded.Should().BeTrue();
        registry.StandDown("free-cache", "still idle, still nothing").Succeeded.Should().BeTrue();

        // A day more idle is the same crossing, not a worse one.
        clock.Advance(TimeSpan.FromDays(1));

        registry.NeedsRefining("free-cache").Should().BeFalse();
    }

    [Fact]
    public async Task A_new_crossing_after_stand_downs_is_refined()
    {
        var clock = new Clock(Now);
        var (registry, _) = _store.Registry(clock: clock);
        await _store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), Script, ToolStoreFixture.Cases());

        registry.StandDown("free-cache", "nothing worth changing").Succeeded.Should().BeTrue();
        registry.StandDown("free-cache", "still nothing").Succeeded.Should().BeTrue();
        registry.NeedsRefining("free-cache").Should().BeFalse();

        clock.Advance(TimeSpan.FromDays(ToolHealth.IdleDays + 1));

        registry.NeedsRefining("free-cache").Should().BeTrue();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
