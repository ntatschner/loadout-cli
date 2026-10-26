using FluentAssertions;
using Loadout.Core.Usage;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Turning token counts into a list-rate estimate.
/// </summary>
public sealed class PriceTableTests
{
    [Fact]
    public void Each_kind_of_token_is_priced_at_its_own_rate()
    {
        // A million of each, on Opus 5.5: $4 in, $20 out, $0.20 cache read,
        // and cache writes at 1.25x and 2x the input price.
        var totals = new UsageTotals(1_000_000, 1_000_000, 1_000_000, 1_000_000, 1_000_000, 500_000);

        PriceTable.Price("claude-opus-5-5", totals).Should().Be(4m + 5m + 8m + 0.20m + 20m);
    }

    [Fact]
    public void Thinking_is_not_charged_twice()
    {
        var withThinking = new UsageTotals(0, 0, 0, 0, 1_000_000, 1_000_000);
        var without = new UsageTotals(0, 0, 0, 0, 1_000_000, 0);

        PriceTable.Price("claude-opus-5-5", withThinking).Should().Be(PriceTable.Price("claude-opus-5-5", without));
    }

    [Theory]
    [InlineData("claude-opus-5-5", 4.00)]
    [InlineData("claude-opus-5", 5.00)]
    [InlineData("claude-opus-5-20260101", 5.00)]
    [InlineData("anthropic.claude-sonnet-5-5", 2.00)]
    [InlineData("claude-sonnet-4-6@20260101", 3.00)]
    [InlineData("claude-fable-5-1", 10.00)]
    [InlineData("claude-haiku-5-5", 0.10)]
    public void A_model_finds_its_own_family_and_not_a_shorter_one(string model, double input)
    {
        PriceTable.Find(model)!.Input.Should().Be((decimal)input);
    }

    [Theory]
    [InlineData("gpt-5-codex")]
    [InlineData("unknown")]
    [InlineData("claude-opus-55")]
    public void A_model_with_no_price_is_not_guessed(string model)
    {
        PriceTable.Find(model).Should().BeNull();
    }

    [Fact]
    public void An_estimate_leaves_out_and_names_what_it_could_not_price()
    {
        var estimate = PriceTable.Estimate(
        [
            new UsageGroup("claude-haiku-5-5", new UsageTotals(1_000_000, 0, 0, 0, 0, 0)),
            new UsageGroup("gpt-5-codex", new UsageTotals(300, 0, 0, 0, 200, 0)),
        ]);

        estimate.Usd.Should().Be(0.10m);
        estimate.UnpricedTokens.Should().Be(500);
        estimate.UnpricedModels.Should().Equal("gpt-5-codex");
    }
}
