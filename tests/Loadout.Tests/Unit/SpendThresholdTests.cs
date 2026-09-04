using FluentAssertions;
using Loadout.Core.Usage;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Thresholds that say where you stand and stop nothing.
/// </summary>
/// <remarks>
/// Loadout starts an agent and is then out of the loop. A limit enforced at the
/// door would be crossed by the session it let in, and nothing here would see
/// it — so there is deliberately no way to make any of this refuse.
/// </remarks>
public sealed class SpendThresholdTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static SpendSettings Settings(
        long daily = 0,
        double planWarnAt = 0,
        params (string Slug, long Tokens)[] projects)
    {
        var settings = new SpendSettings { DailyTokens = daily, PlanWarnAt = planWarnAt };

        foreach (var (slug, tokens) in projects)
        {
            settings.ProjectDailyTokens[slug] = tokens;
        }

        return settings;
    }

    [Fact]
    public void Nothing_set_means_nothing_is_read()
    {
        // The whole reason this check exists: working out what was spent means
        // reading the agents' transcripts, about two seconds of it, and nobody
        // who never asked for a threshold should pay that on every launch.
        SpendThresholds.AnySet(new SpendSettings(), "demo").Should().BeFalse();
        SpendThresholds.AnySet(null, "demo").Should().BeFalse();

        SpendThresholds.AnySet(Settings(daily: 1), "demo").Should().BeTrue();
        SpendThresholds.AnySet(Settings(planWarnAt: 0.8), "demo").Should().BeTrue();
        SpendThresholds.AnySet(Settings(projects: ("demo", 100)), "demo").Should().BeTrue();
    }

    [Fact]
    public void A_threshold_for_another_project_is_not_this_project_s_business()
    {
        SpendThresholds.AnySet(Settings(projects: ("other", 100)), "demo").Should().BeFalse();
    }

    [Fact]
    public void A_zero_threshold_is_off_rather_than_crossed_by_everything()
    {
        // Zero has to mean "not set". Read as a number it is crossed by the
        // first token of the day, every day.
        SpendThresholds.AnySet(Settings(projects: ("demo", 0)), "demo").Should().BeFalse();

        SpendThresholds.Crossed(Settings(projects: ("demo", 0)), "demo", 5_000, 5_000)
            .Should().BeEmpty();
    }

    [Fact]
    public void Spending_under_the_line_says_nothing()
    {
        SpendThresholds.Crossed(Settings(daily: 10_000), "demo", 9_999, 0)
            .Should().BeEmpty();
    }

    [Fact]
    public void Reaching_the_line_is_enough_to_be_told()
    {
        SpendThresholds.Crossed(Settings(daily: 10_000), "demo", 10_000, 0)
            .Should().ContainSingle().Which.Subject.Should().Be("today");
    }

    [Fact]
    public void The_project_is_named_before_the_day()
    {
        var crossed = SpendThresholds.Crossed(
            Settings(daily: 10_000, projects: ("demo", 4_000)), "demo", 12_000, 5_000);

        // Somebody who set both wants to know which they are near, and the
        // narrower answer is the more actionable of the two.
        crossed.Select(c => c.Subject).Should().Equal("demo", "today");
        crossed[0].Spent.Should().Be(5_000);
        crossed[0].Share.Should().BeApproximately(1.25, 0.001);
    }

    [Fact]
    public void No_plan_reading_is_not_the_same_as_plenty_left()
    {
        // Only one of the agents records this and only sometimes. Treating an
        // absent reading as room to spare would be the one wrong answer.
        SpendThresholds.Plan(Settings(planWarnAt: 0.5), null).Should().BeNull();
    }

    [Fact]
    public void A_plan_window_past_the_level_is_reported_with_what_it_was()
    {
        var reading = new PlanHeadroom(
            "codex", UsedFraction: 0.91, TimeSpan.FromDays(7), null, Now.AddHours(-3), "Pro");

        var warning = SpendThresholds.Plan(Settings(planWarnAt: 0.8), reading);

        warning.Should().NotBeNull();
        warning!.Reading.WindowName.Should().Be("week");
        warning.Reading.Age(Now).Should().Be(TimeSpan.FromHours(3));
    }

    [Fact]
    public void A_plan_window_below_the_level_says_nothing()
    {
        var reading = new PlanHeadroom(
            "codex", UsedFraction: 0.79, TimeSpan.FromDays(7), null, Now, "Pro");

        SpendThresholds.Plan(Settings(planWarnAt: 0.8), reading).Should().BeNull();
    }

    [Fact]
    public void Nothing_here_can_refuse_anything()
    {
        // Guarded as a property rather than trusted as a habit. Every answer
        // this type gives is a description of where things stand; there is no
        // shape it can return that a caller could read as "do not start".
        var crossed = SpendThresholds.Crossed(
            Settings(daily: 1, projects: ("demo", 1)), "demo", long.MaxValue, long.MaxValue);

        crossed.Should().HaveCount(2);
        crossed.Should().AllSatisfy(c => c.Should().BeOfType<SpendWarning>());
    }
}
