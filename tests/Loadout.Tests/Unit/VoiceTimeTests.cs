using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Voice;
using Loadout.Models.Voice;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The time guide: which layer's setting is in force on each surface, what the
/// two checks find, and how a setting reads once rendered.
/// </summary>
/// <remarks>
/// The estimate cases are written the way people and agents actually phrase
/// estimates, including the ones it must leave alone: a measured duration, a
/// timeout, an interval. Those negatives matter more than the positives,
/// because a check that fires on "the suite took 8 minutes" gets turned off.
/// </remarks>
public sealed class VoiceTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly VoiceGuide Guide = VoiceGuides.Time();

    private static VoiceLayer Layer(StyleLayer layer, params VoiceRule[] rules) =>
        new(layer, layer.ToString().ToLowerInvariant(), "unused", new VoiceProfile { Guide = Guide.Reference, Rules = [.. rules] });

    private static TimeSurface On(IReadOnlyList<TimeSurface> surfaces, string surface) =>
        surfaces.Single(s => s.Surface == surface);

    private static IReadOnlyList<VoiceFinding> Check(string text, string surface, params VoiceRule[] rules) =>
        VoiceChecker.Check(text, surface, rules).Findings;

    // ---------------------------------------------------------- which layer

    [Fact]
    public void The_codebase_wins_on_its_own_surfaces_and_the_person_wins_on_chat_and_interfaces()
    {
        var surfaces = VoiceTime.Effective(Guide,
        [
            Layer(StyleLayer.Personal, VoiceTime.EstimatesSetting(VoiceTime.Never, Now)),
            Layer(StyleLayer.Codebase, VoiceTime.EstimatesSetting(VoiceTime.RangeWithOwner, Now)),
        ]);

        On(surfaces, VoiceGuides.PullRequest).Estimates.Should().Be(VoiceTime.RangeWithOwner);
        On(surfaces, VoiceGuides.PullRequest).EstimatesFrom.Should().Be(StyleLayer.Codebase);

        On(surfaces, VoiceGuides.Chat).Estimates.Should().Be(VoiceTime.Never);
        On(surfaces, VoiceGuides.Chat).EstimatesFrom.Should().Be(StyleLayer.Personal);
        On(surfaces, VoiceGuides.Ui).EstimatesFrom.Should().Be(StyleLayer.Personal);
    }

    [Fact]
    public void Each_surface_falls_back_to_the_other_layer_when_its_own_sets_nothing()
    {
        var surfaces = VoiceTime.Effective(Guide,
        [
            Layer(StyleLayer.Personal, VoiceTime.FormatSetting(VoiceGuides.Commit, "long-british", Now)),
            Layer(StyleLayer.Codebase, VoiceTime.FormatSetting(VoiceGuides.Chat, "iso", Now)),
        ]);

        On(surfaces, VoiceGuides.Commit).Format.Should().Be("long-british");
        On(surfaces, VoiceGuides.Commit).FormatFrom.Should().Be(StyleLayer.Personal);

        On(surfaces, VoiceGuides.Chat).Format.Should().Be("iso");
        On(surfaces, VoiceGuides.Chat).FormatFrom.Should().Be(StyleLayer.Codebase);

        On(surfaces, VoiceGuides.Document).Format.Should().BeNull("a format applies only to the surface it names");
    }

    [Fact]
    public void With_nothing_set_estimates_default_to_a_range_with_an_owner()
    {
        var surfaces = VoiceTime.Effective(Guide, [Layer(StyleLayer.Personal)]);

        surfaces.Should().OnlyContain(s => s.Estimates == VoiceTime.RangeWithOwner && s.EstimatesFrom == null);
        surfaces.Select(s => s.Surface).Should().Equal(Guide.Surfaces);
    }

    // ------------------------------------------------------------ estimates

    [Theory]
    [InlineData("This will take two days.")]
    [InlineData("It should take about 10 minutes.")]
    [InlineData("The migration takes roughly an hour.")]
    [InlineData("I estimate three weeks for the rewrite.")]
    [InlineData("Expect a couple of hours for review.")]
    public void An_estimate_is_recognised(string sentence) =>
        VoiceTime.Estimate(sentence).Should().NotBeNull();

    [Theory]
    [InlineData("The suite took 8 minutes.")]
    [InlineData("Set the timeout to 30 seconds.")]
    [InlineData("The job runs every 5 minutes.")]
    [InlineData("It took two days to find the cause.")]
    [InlineData("The token expires after an hour.")]
    public void A_duration_that_is_not_an_estimate_is_left_alone(string sentence) =>
        VoiceTime.Estimate(sentence).Should().BeNull();

    [Fact]
    public void An_estimate_needs_a_range_and_somebody_whose_time_it_is()
    {
        var rule = VoiceTime.EstimatesSetting(VoiceTime.RangeWithOwner, Now);

        Check("This will take two days.", VoiceGuides.PullRequest, rule).Single().Detail
            .Should().Contain("a range").And.Contain("whose time it is");

        Check("It should take about an hour of your time.", VoiceGuides.PullRequest, rule).Single().Detail
            .Should().Contain("a range").And.NotContain("whose time");

        Check("It should take ten to twenty minutes of agent time.", VoiceGuides.PullRequest, rule)
            .Should().BeEmpty();
    }

    [Fact]
    public void Under_never_every_estimate_is_reported_however_well_it_is_put()
    {
        var rule = VoiceTime.EstimatesSetting(VoiceTime.Never, Now);

        Check("It should take ten to twenty minutes of agent time.", VoiceGuides.Document, rule)
            .Should().ContainSingle().Which.Gates.Should().BeFalse();
    }

    // ---------------------------------------------------------------- dates

    [Fact]
    public void A_date_in_another_shape_is_reported_and_one_in_the_format_is_not()
    {
        var rule = VoiceTime.FormatSetting(VoiceGuides.Commit, "iso", Now);

        var findings = Check(
            "Released 19/10/2026, planned for 19 October 2026, shipped 2026-10-19 as 0.75.0.",
            VoiceGuides.Commit,
            rule);

        findings.Select(f => f.Detail).Should().HaveCount(2)
            .And.Contain(d => d.StartsWith("'19/10/2026'", StringComparison.Ordinal))
            .And.Contain(d => d.StartsWith("'19 October 2026'", StringComparison.Ordinal));
    }

    [Fact]
    public void A_format_applies_only_to_its_own_surface_and_never_to_code()
    {
        var rule = VoiceTime.FormatSetting(VoiceGuides.Commit, "iso", Now);

        Check("Released 19/10/2026.", VoiceGuides.Document, rule).Should().BeEmpty();
        Check("Run `touch -d 19/10/2026 file` first.", VoiceGuides.Commit, rule).Should().BeEmpty();
    }

    [Fact]
    public void A_pattern_format_is_named_as_unchecked_rather_than_guessed_at()
    {
        var rule = VoiceTime.FormatSetting(VoiceGuides.Document, "pattern:dd MMM yyyy", Now);

        var result = VoiceChecker.Check("Released 19/10/2026.", VoiceGuides.Document, [rule]);

        result.Findings.Should().BeEmpty();
        result.Unchecked.Should().ContainSingle(u => u.Rule == rule.Id);
    }

    [Theory]
    [InlineData("iso", null)]
    [InlineData("LONG-BRITISH", null)]
    [InlineData("pattern:yyyy MM", null)]
    [InlineData("pattern:", "needs the pattern")]
    [InlineData("british", "is not a format")]
    public void Only_a_preset_or_a_pattern_is_a_format(string value, string? problem)
    {
        if (problem is null)
        {
            VoiceTime.FormatProblem(value).Should().BeNull();
        }
        else
        {
            VoiceTime.FormatProblem(value).Should().Contain(problem);
        }
    }

    [Fact]
    public void A_range_can_follow_the_unit()
    {
        VoiceTime.Estimate("It should take an hour or two of your review.")!.Value.Ranged.Should().BeTrue();
    }

    // ------------------------------------------------------------ rendering

    [Fact]
    public void A_codebase_setting_says_it_gives_way_to_yours_on_chat_and_interfaces()
    {
        var profile = new VoiceProfile { Guide = Guide.Reference, Rules = [VoiceTime.EstimatesSetting(VoiceTime.Never, Now)] };

        var text = VoiceRendering.Render([(Guide, profile)], StyleLayer.Codebase, CodingStyles.Codebase);

        text.Should().Contain(
            "*(docs, commit messages and pull requests; chat and interfaces only where your own voice sets nothing)*");
    }

    [Fact]
    public void Your_own_setting_renders_without_giving_way_and_a_format_names_its_surface()
    {
        var profile = new VoiceProfile
        {
            Guide = Guide.Reference,
            Rules =
            [
                VoiceTime.EstimatesSetting(VoiceTime.RangeWithOwner, Now),
                VoiceTime.FormatSetting(VoiceGuides.Commit, "iso", Now),
            ],
        };

        var text = VoiceRendering.Render([(Guide, profile)], StyleLayer.Personal, CodingStyles.Personal);

        text.Should().NotContain("only where your own voice sets nothing");
        text.Should().Contain("Write dates in ISO 8601, such as 2026-10-19, and 14:05 UTC for a time. *(commit messages only)*");
    }

    // --------------------------------------------------------------- guide

    [Fact]
    public void The_time_guide_is_set_rather_than_learned_and_keeps_its_checks_to_itself()
    {
        VoiceGuides.IsSet(Guide).Should().BeTrue();
        VoiceGuides.IsSet(VoiceGuides.Prose()).Should().BeFalse();

        VoiceGuides.Prose().CheckTypes.Should().NotContain(VoiceChecker.TimeTypes,
            "a learn run must never propose a setting as if it had been learned");
    }
}
