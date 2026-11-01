using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Core.Triggers;
using Loadout.Models.Delivery;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>Branches landing and releases being cut, as trigger events.</summary>
public sealed class LandingSourceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly LandingService _landings;
    private readonly LandingSource _source;

    public LandingSourceTests()
    {
        _landings = new LandingService(_space.Paths, _space.Yaml, _clock);
        _source = new LandingSource(_space.Paths.Paths.State, _landings);
    }

    public void Dispose() => _space.Dispose();

    private string State => _space.Paths.Paths.State;

    private Task RecordAsync(string branch, LandingState state) =>
        _landings.RecordAsync("website", [new BranchLanding { Branch = branch, Team = "builders", RunId = "r1", Task = "login", State = state }]);

    [Fact]
    public async Task The_first_look_posts_nothing_so_turning_triggers_on_answers_nothing_old()
    {
        await RecordAsync("queue/old", LandingState.Merged);
        ReleaseLog.Append(State, new ReleaseRecord("website", "v1.0.0", "1.0.0", "abc", "tag-push", _clock.Now));

        (await _source.LookAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_branch_merged_after_the_first_look_is_one_event_with_its_fields_and_only_once()
    {
        await _source.LookAsync();
        await RecordAsync("queue/login", LandingState.Pending);
        _clock.Now += TimeSpan.FromMinutes(1);

        (await _source.LookAsync()).Should().BeEmpty("pending is not landed");

        await RecordAsync("queue/login", LandingState.Merged);

        var landed = (await _source.LookAsync()).Should().ContainSingle().Subject;
        landed.Kind.Should().Be(TriggerEventKinds.BranchLanded);
        landed.Project.Should().Be("website");
        landed.Fields["branch"].Should().Be("queue/login");
        landed.Fields["task"].Should().Be("login");
        TriggerEventKinds.Fields[landed.Kind].Should().BeEquivalentTo(landed.Fields.Keys, "a rule may ask about every field it carries");

        (await _source.LookAsync()).Should().BeEmpty("it was posted already");
    }

    [Fact]
    public async Task A_release_cut_after_the_first_look_is_an_event()
    {
        await _source.LookAsync();
        _clock.Now += TimeSpan.FromMinutes(1);
        ReleaseLog.Append(State, new ReleaseRecord("website", "v1.3.0", "1.3.0", "abc123", "tag-push", _clock.Now));

        var released = (await _source.LookAsync()).Should().ContainSingle().Subject;

        released.Kind.Should().Be(TriggerEventKinds.Released);
        released.Fields["version"].Should().Be("1.3.0");
        TriggerEventKinds.Fields[released.Kind].Should().BeEquivalentTo(released.Fields.Keys);
    }

    [Fact]
    public void A_line_in_the_release_log_that_cannot_be_read_is_passed_over()
    {
        ReleaseLog.Append(State, new ReleaseRecord("website", "v1.3.0", "1.3.0", "abc123", "tag-push", _clock.Now));
        File.AppendAllText(ReleaseLog.PathIn(State), "{ torn\n");
        ReleaseLog.Append(State, new ReleaseRecord("website", "v1.4.0", "1.4.0", "def456", "tag-push", _clock.Now));

        ReleaseLog.Read(State).Select(one => one.Tag).Should().Equal("v1.3.0", "v1.4.0");
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
