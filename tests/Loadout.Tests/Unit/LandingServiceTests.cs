using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Models.Delivery;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The landings a project keeps: one per branch, moving only forwards except
/// for the two ways back, and never undoing a merge.
/// </summary>
public sealed class LandingServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly LandingService _landings;

    public LandingServiceTests() => _landings = new LandingService(_space.Paths, _space.Yaml, _clock);

    public void Dispose() => _space.Dispose();

    private static BranchLanding One(string branch, LandingState state, string pr = "") =>
        new() { Branch = branch, Team = "builders", RunId = "r1", State = state, PullRequest = pr, Why = state.ToString() };

    [Fact]
    public async Task A_branch_recorded_twice_is_one_landing_that_keeps_when_it_was_first_seen()
    {
        (await _landings.RecordAsync("website", [One("queue/login", LandingState.Pending)])).Value.Should().BeEmpty();

        _clock.Now = _clock.Now.AddHours(3);
        (await _landings.RecordAsync("website", [One("queue/login", LandingState.Ready, "https://x/pull/1")])).Value.Should().BeEmpty();

        var kept = (await _landings.LoadAsync("website")).Value!.Landings.Should().ContainSingle().Subject;
        kept.State.Should().Be(LandingState.Ready);
        kept.PullRequest.Should().Be("https://x/pull/1");
        kept.CreatedUtc.Should().Be(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
        kept.UpdatedUtc.Should().Be(_clock.Now);
        _landings.Projects().Should().Equal("website");
    }

    [Fact]
    public async Task A_merged_landing_is_never_sent_back_and_says_why_it_was_not()
    {
        await _landings.RecordAsync("website", [One("queue/login", LandingState.Merged, "https://x/pull/1")]);

        var skipped = (await _landings.RecordAsync("website", [One("queue/login", LandingState.Pending)])).Value!;

        skipped.Should().ContainSingle().Which.Should().Be("queue/login is merged, and can't go back to pending");
        (await _landings.LoadAsync("website")).Value!.Landings.Single().State.Should().Be(LandingState.Merged);
    }

    [Fact]
    public async Task Being_told_again_that_it_merged_leaves_it_as_it_was()
    {
        await _landings.RecordAsync("website", [One("queue/login", LandingState.Merged)]);
        var first = (await _landings.LoadAsync("website")).Value!.Landings.Single().UpdatedUtc;

        _clock.Now = _clock.Now.AddMinutes(15);
        await _landings.RecordAsync("website", [One("queue/login", LandingState.Merged)]);

        (await _landings.LoadAsync("website")).Value!.Landings.Single().UpdatedUtc.Should().Be(first);
    }

    [Fact]
    public async Task Old_finished_landings_are_trimmed_and_waiting_ones_never_are()
    {
        var many = Enumerable.Range(0, LandingService.KeptFinished + 5)
            .Select(index => One($"done/{index}", LandingState.Merged))
            .Append(One("waiting/one", LandingState.Pending))
            .ToList();

        await _landings.RecordAsync("website", many);

        var kept = (await _landings.LoadAsync("website")).Value!.Landings;
        kept.Count(one => one.State == LandingState.Merged).Should().Be(LandingService.KeptFinished);
        kept.Should().Contain(one => one.Branch == "waiting/one");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData(".hidden")]
    [InlineData("")]
    public async Task A_name_that_could_not_name_a_file_of_its_own_is_refused(string project)
    {
        (await _landings.LoadAsync(project)).Failed.Should().BeTrue();
        (await _landings.RecordAsync(project, [One("b", LandingState.Pending)])).Failed.Should().BeTrue();
    }

    [Theory]
    [InlineData(LandingState.Pending, LandingState.Ready, true)]
    [InlineData(LandingState.Draft, LandingState.Ready, true)]
    [InlineData(LandingState.Ready, LandingState.Pending, true)]
    [InlineData(LandingState.Abandoned, LandingState.Pending, true)]
    [InlineData(LandingState.Ready, LandingState.Draft, false)]
    [InlineData(LandingState.Merged, LandingState.Pending, false)]
    [InlineData(LandingState.Merged, LandingState.Abandoned, false)]
    [InlineData(LandingState.Abandoned, LandingState.Merged, false)]
    public void Only_the_moves_the_plan_names_are_allowed(LandingState from, LandingState to, bool allowed) =>
        LandingMoves.Allowed(from, to).Should().Be(allowed);

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
