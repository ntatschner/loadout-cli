using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a session costs before it starts, across every layer at once.
/// </summary>
/// <remarks>
/// The layers were counted separately and in different units — tokens for
/// specialists, bytes for rules, nothing at all for the memory index — so there
/// was no answer to what a session here costs, and the one enforced ceiling
/// governed the layer that was already the most disciplined.
/// </remarks>
public sealed class ContextBudgetTests
{
    [Fact]
    public void Every_layer_paid_for_on_a_launch_is_added_into_one_figure()
    {
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 2400),
            alwaysLoadedRuleBytes: 4000,
            scopedRuleBytes: 0,
            memoryIndexBytes: 400);

        // 2,400 + 1,000 + 100. One unit, so the total means something.
        budget.EveryLaunchTokens.Should().Be(3500);
    }

    [Fact]
    public void What_loads_only_on_demand_is_kept_out_of_that_figure()
    {
        // The whole reason the layers exist is that their prices differ. A
        // figure that added a scoped rule to the always-loaded total would hide
        // the thing it was meant to show.
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 1000),
            alwaysLoadedRuleBytes: 0,
            scopedRuleBytes: 8000,
            memoryIndexBytes: 0);

        budget.EveryLaunchTokens.Should().Be(1000);
        budget.OnDemandTokens.Should().Be(2000);
    }

    [Fact]
    public void A_layer_a_project_does_not_have_costs_nothing_rather_than_being_unknown()
    {
        // A project with no memory really is paying nothing for it, and saying
        // "unknown" would put a caveat on every report from every project that
        // has not written a note yet.
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 1000),
            alwaysLoadedRuleBytes: 0,
            scopedRuleBytes: 0,
            memoryIndexBytes: 0);

        budget.EveryLaunchTokens.Should().Be(1000);
        budget.Layers.Should().Contain(layer => layer.Name == "Memory index" && layer.EstimatedTokens == 0);
    }

    [Fact]
    public void The_ceiling_is_reported_against_what_every_launch_pays()
    {
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 8000, tokenBudget: 10000),
            alwaysLoadedRuleBytes: 12000,
            scopedRuleBytes: 0,
            memoryIndexBytes: 0);

        // 8,000 specialists plus 3,000 of rules is over ten thousand, even
        // though the specialist layer alone is comfortably under it. That gap is
        // the reason for counting all three.
        budget.EveryLaunchTokens.Should().Be(11000);
        budget.IsOverBudget.Should().BeTrue();
        budget.UsedFraction.Should().BeApproximately(1.1, 0.001);
    }

    [Fact]
    public void Without_a_ceiling_nothing_is_over_it()
    {
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 90000, tokenBudget: 0),
            alwaysLoadedRuleBytes: 0,
            scopedRuleBytes: 0,
            memoryIndexBytes: 0);

        budget.IsOverBudget.Should().BeFalse();
        budget.UsedFraction.Should().BeNull();
    }

    [Fact]
    public void Bytes_are_kept_alongside_the_estimate()
    {
        // Bytes are what can actually be known; tokens are an approximation
        // that no tokeniser here matches. Losing the measurable one would leave
        // only the guess.
        var budget = ContextBudget.From(
            Instructions(estimatedTokens: 1000),
            alwaysLoadedRuleBytes: 4001,
            scopedRuleBytes: 0,
            memoryIndexBytes: 0);

        var rules = budget.Layers.Single(layer => layer.Name == "Instructions and rules");

        rules.Bytes.Should().Be(4001);
        rules.EstimatedTokens.Should().Be(1001, "a part-used token is still a token");
    }

    private static EffectiveInstructions Instructions(int estimatedTokens, int tokenBudget = 12000) =>
        new(
            "implement",
            [],
            [],
            [],
            new InstructionContextBudget(estimatedTokens * 4, estimatedTokens, tokenBudget, 80));
}
