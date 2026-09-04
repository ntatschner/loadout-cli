using FluentAssertions;
using Loadout.Core.Sessions;
using Loadout.Models.Agents;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the ledger adds up to, and what it declines to claim.
/// </summary>
/// <remarks>
/// The report exists for one question the library cannot answer about itself:
/// which specialists does anybody's work actually reach. Everything asserted
/// here comes from launches that happened; nothing is modelled from what a
/// launch would compose today.
/// </remarks>
public sealed class LaunchStatisticsTests
{
    private static readonly Dictionary<string, int> Library = new(StringComparer.OrdinalIgnoreCase)
    {
        ["foundation.change-safety"] = 640,
        ["language.csharp"] = 500,
        ["cloud.aws"] = 420,
        ["database.mysql"] = 380,
    };

    [Fact]
    public void Specialists_are_counted_by_the_launches_that_reached_them()
    {
        var statistics = LaunchStatistics.From(
            [
                Record("a", ["foundation.change-safety", "language.csharp"]),
                Record("b", ["foundation.change-safety"]),
            ],
            Library);

        statistics.Launches.Should().Be(2);

        statistics.Loaded.Should().SatisfyRespectively(
            first =>
            {
                first.Id.Should().Be("foundation.change-safety");
                first.Launches.Should().Be(2);
                first.TokensNow.Should().Be(640);
            },
            second =>
            {
                second.Id.Should().Be("language.csharp");
                second.Launches.Should().Be(1);
            });
    }

    [Fact]
    public void A_specialist_no_launch_reached_is_named()
    {
        var statistics = LaunchStatistics.From(
            [Record("a", ["language.csharp"])],
            Library);

        // The finding the whole report exists for: something ships, and nothing
        // anybody does has ever brought it into a session.
        statistics.NeverLoaded.Should().Equal(
            "cloud.aws", "database.mysql", "foundation.change-safety");

        statistics.LibrarySize.Should().Be(4);
    }

    [Fact]
    public void A_specialist_named_twice_by_one_launch_counts_once()
    {
        var statistics = LaunchStatistics.From(
            [Record("a", ["language.csharp", "language.csharp"])],
            Library);

        // Counting compositions rather than launches would let one launch make a
        // specialist look twice as reached as it is.
        statistics.Loaded.Should().ContainSingle()
            .Which.Launches.Should().Be(1);
    }

    [Fact]
    public void Launches_with_no_ending_are_counted_apart()
    {
        var statistics = LaunchStatistics.From(
            [
                Record("a", ["language.csharp"], exitCode: 0),
                Record("b", ["language.csharp"]),
            ],
            Library);

        statistics.Launches.Should().Be(2);
        statistics.NeverClosed.Should().Be(1);
    }

    [Fact]
    public void Estimated_tokens_are_added_across_launches()
    {
        var statistics = LaunchStatistics.From(
            [
                Record("a", ["language.csharp"], tokens: 2400),
                Record("b", ["language.csharp"], tokens: 1800),
            ],
            Library);

        statistics.EstimatedTokens.Should().Be(4200);
    }

    [Fact]
    public void A_specialist_the_library_no_longer_holds_is_still_counted()
    {
        var statistics = LaunchStatistics.From(
            [Record("a", ["framework.retired"])],
            Library);

        // It was composed. Dropping it because the library has moved on would
        // make the history disagree with itself, and hide that sessions were
        // being given something nobody can now read.
        var usage = statistics.Loaded.Should().ContainSingle().Subject;

        usage.Id.Should().Be("framework.retired");
        usage.Launches.Should().Be(1);
        usage.TokensNow.Should().Be(0, "the library cannot price what it no longer holds");
    }

    [Fact]
    public void The_most_reached_come_first_and_ties_are_broken_by_name()
    {
        var statistics = LaunchStatistics.From(
            [
                Record("a", ["database.mysql", "cloud.aws", "language.csharp"]),
                Record("b", ["language.csharp"]),
            ],
            Library);

        statistics.Loaded.Select(usage => usage.Id)
            .Should().Equal("language.csharp", "cloud.aws", "database.mysql");
    }

    [Fact]
    public void Nothing_recorded_reports_nothing_rather_than_failing()
    {
        var statistics = LaunchStatistics.From([], Library);

        statistics.Launches.Should().Be(0);
        statistics.Loaded.Should().BeEmpty();
        statistics.NeverLoaded.Should().HaveCount(4);
    }

    private static LaunchRecord Record(
        string id,
        string[] specialists,
        int tokens = 1000,
        int? exitCode = null) =>
        new(
            id,
            new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.Zero),
            "starstats",
            "StarStats",
            "claude",
            "implement",
            "a task",
            null,
            null,
            null,
            specialists,
            tokens,
            12000,
            exitCode is null ? null : new DateTimeOffset(2026, 2, 1, 10, 0, 0, TimeSpan.Zero),
            exitCode);
}
