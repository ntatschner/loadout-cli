namespace Loadout.Core.Usage;

/// <summary>One model's published API prices, in US dollars per million tokens.</summary>
/// <param name="Input">An ordinary input token.</param>
/// <param name="Output">A token the model produced, thinking included.</param>
/// <param name="CacheRead">
/// A token served from cache. Given per model rather than as a share of the
/// input price, because the newest models broke the old ratio: Opus 5.5 reads
/// cache at a twentieth of its input price and Fable 5.1 at a fortieth, where
/// the older models charge a tenth.
/// </param>
public sealed record ListRate(decimal Input, decimal Output, decimal CacheRead);

/// <summary>What some tokens would have cost at list rates, and what could not be priced.</summary>
/// <param name="Usd">The estimate for everything that could be priced.</param>
/// <param name="UnpricedTokens">Tokens from models this table has no price for, left out of the figure.</param>
/// <param name="UnpricedModels">Which models those were.</param>
public sealed record SpendEstimate(decimal Usd, long UnpricedTokens, IReadOnlyList<string> UnpricedModels)
{
    /// <summary>Nothing spent.</summary>
    public static readonly SpendEstimate Zero = new(0m, 0, []);
}

/// <summary>
/// Turns token counts into money at Anthropic's published API prices.
/// </summary>
/// <remarks>
/// <para>
/// An estimate, and every figure it produces has to be shown as one. A
/// subscription does not charge these rates, so for most people this is what
/// the same work would have cost through the API, not what they paid.
/// <see cref="UsageTotals"/> keeps to ratios for exactly that reason; this
/// exists because "how much did I spend" is a question asked in money, and
/// tokens alone do not answer it.
/// </para>
/// <para>
/// The table goes out of date when a model launches, and it fails safe when it
/// does: a model it does not know is left out of the figure and named, rather
/// than priced at a guess. Prices are from the API reference as cached on 6
/// October 2026. Cache writes keep the published multipliers on the input
/// price, which have held across every model so far.
/// </para>
/// </remarks>
public static class PriceTable
{
    /// <summary>When the prices were last checked against the published table.</summary>
    public const string CheckedOn = "2026-10-06";

    // Longest names first, so a dated or suffixed model id finds its own
    // family before a shorter one it happens to start with.
    private static readonly (string Model, ListRate Rate)[] Rates =
    [
        ("claude-fable-5-1", new ListRate(10.00m, 50.00m, 0.25m)),
        ("claude-mythos-5-1", new ListRate(10.00m, 50.00m, 0.25m)),
        ("claude-fable-5", new ListRate(10.00m, 50.00m, 1.00m)),
        ("claude-opus-5-5", new ListRate(4.00m, 20.00m, 0.20m)),
        ("claude-opus-5", new ListRate(5.00m, 25.00m, 0.50m)),
        ("claude-opus-4-8", new ListRate(5.00m, 25.00m, 0.50m)),
        ("claude-opus-4-7", new ListRate(5.00m, 25.00m, 0.50m)),
        ("claude-opus-4-6", new ListRate(5.00m, 25.00m, 0.50m)),
        ("claude-sonnet-5-5", new ListRate(2.00m, 10.00m, 0.20m)),
        ("claude-sonnet-5", new ListRate(2.00m, 10.00m, 0.20m)),
        ("claude-sonnet-4-6", new ListRate(3.00m, 15.00m, 0.30m)),
        ("claude-haiku-5-5", new ListRate(0.10m, 0.50m, 0.01m)),
        ("claude-haiku-4-5", new ListRate(1.00m, 5.00m, 0.10m)),
    ];

    /// <summary>The prices for a model as an agent recorded its name, or null when there are none.</summary>
    public static ListRate? Find(string model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var name = model.Trim();

        // Bedrock and Vertex spell the same model with a prefix or an '@'
        // version; the price table is the first-party one either way.
        if (name.StartsWith("anthropic.", StringComparison.OrdinalIgnoreCase))
        {
            name = name["anthropic.".Length..];
        }

        return Rates
            .Where(one => name.StartsWith(one.Model, StringComparison.OrdinalIgnoreCase)
                && (name.Length == one.Model.Length || name[one.Model.Length] is '-' or '@' or '['))
            .OrderByDescending(one => one.Model.Length)
            .Select(one => (ListRate?)one.Rate)
            .FirstOrDefault();
    }

    /// <summary>What one model's tokens would have cost, or null when the model has no price here.</summary>
    public static decimal? Price(string model, UsageTotals totals)
    {
        if (Find(model) is not { } rate)
        {
            return null;
        }

        const decimal PerToken = 1_000_000m;

        // Thinking is part of the output already, so it is not added again.
        return ((totals.Input * rate.Input)
            + (totals.CacheWrite5m * rate.Input * (decimal)UsageTotals.CacheWrite5mRate)
            + (totals.CacheWrite1h * rate.Input * (decimal)UsageTotals.CacheWrite1hRate)
            + (totals.CacheRead * rate.CacheRead)
            + (totals.Output * rate.Output)) / PerToken;
    }

    /// <summary>What a set of per-model totals would have cost together.</summary>
    public static SpendEstimate Estimate(IEnumerable<UsageGroup> byModel)
    {
        ArgumentNullException.ThrowIfNull(byModel);

        var usd = 0m;
        var unpriced = 0L;
        var unknown = new List<string>();

        foreach (var model in byModel)
        {
            if (Price(model.Name, model.Totals) is { } priced)
            {
                usd += priced;
            }
            else if (model.Totals.Total > 0)
            {
                unpriced += model.Totals.Total;
                unknown.Add(model.Name);
            }
        }

        return new SpendEstimate(usd, unpriced, unknown);
    }
}
