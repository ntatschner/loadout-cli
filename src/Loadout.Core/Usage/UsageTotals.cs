namespace Loadout.Core.Usage;

/// <summary>
/// Token counts for one message, one session, one project or one day —
/// whatever they were added up over.
/// </summary>
/// <remarks>
/// <para>
/// Cache writes are held apart by their lifetime because they are not billed
/// alike, and a single "cache write" figure cannot be turned back into the two.
/// The agents record the split, so keeping it costs nothing and throwing it
/// away would quietly overstate how much caching saved: on this machine the
/// difference between assuming every write was the cheaper kind and reading
/// which kind it was is a percentage point.
/// </para>
/// <para>
/// A struct, and immutable, because these are added up in tight loops over
/// hundreds of thousands of transcript lines.
/// </para>
/// </remarks>
/// <param name="Input">Ordinary input tokens, neither written to nor read from cache.</param>
/// <param name="CacheWrite5m">Input tokens written to the five-minute cache.</param>
/// <param name="CacheWrite1h">Input tokens written to the one-hour cache.</param>
/// <param name="CacheRead">Input tokens served from cache rather than re-read.</param>
/// <param name="Output">Tokens the model produced.</param>
/// <param name="Thinking">
/// The part of <paramref name="Output"/> spent on extended thinking. A subset,
/// not an addition — adding it to the output would count it twice.
/// </param>
public readonly record struct UsageTotals(
    long Input,
    long CacheWrite5m,
    long CacheWrite1h,
    long CacheRead,
    long Output,
    long Thinking)
{
    /// <summary>Nothing counted yet.</summary>
    public static readonly UsageTotals Zero = default;

    /// <summary>
    /// What a cache read costs against an ordinary input token.
    /// </summary>
    /// <remarks>
    /// These are Anthropic's published multipliers, not prices. Deliberately
    /// ratios: they hold across models and across the plan somebody is on,
    /// whereas a table of prices per model would be wrong the week a model
    /// launched and nobody would notice until the numbers had been believed
    /// for a while.
    /// </remarks>
    public const double CacheReadRate = 0.10;

    /// <summary>What writing to the five-minute cache costs against an ordinary input token.</summary>
    public const double CacheWrite5mRate = 1.25;

    /// <summary>What writing to the one-hour cache costs against an ordinary input token.</summary>
    public const double CacheWrite1hRate = 2.00;

    /// <summary>Every cache write, whatever its lifetime.</summary>
    public long CacheWrite => CacheWrite5m + CacheWrite1h;

    /// <summary>Every token that went in, cached or not.</summary>
    public long TotalInput => Input + CacheWrite + CacheRead;

    /// <summary>Everything, in both directions.</summary>
    public long Total => TotalInput + Output;

    /// <summary>
    /// Input priced in ordinary-input-token equivalents, so cached and uncached
    /// work can be compared on one scale.
    /// </summary>
    public double BilledInputEquivalent =>
        Input
        + (CacheWrite5m * CacheWrite5mRate)
        + (CacheWrite1h * CacheWrite1hRate)
        + (CacheRead * CacheReadRate);

    /// <summary>
    /// What the same conversation would have cost with no cache at all: every
    /// token sent afresh, every time.
    /// </summary>
    public double UncachedInputEquivalent => TotalInput;

    /// <summary>
    /// The share of input the cache avoided paying for, or null when nothing
    /// went in at all.
    /// </summary>
    /// <remarks>
    /// Reported rather than celebrated. The agents earn this, not the launcher;
    /// it is here because a number that drops is worth seeing, not because a
    /// high one is an achievement.
    /// </remarks>
    public double? SavedFraction =>
        UncachedInputEquivalent > 0
            ? 1 - (BilledInputEquivalent / UncachedInputEquivalent)
            : null;

    /// <summary>How much of the input arrived from cache, or null when none did.</summary>
    public double? CacheHitFraction =>
        TotalInput > 0 ? (double)CacheRead / TotalInput : null;

    public static UsageTotals operator +(UsageTotals a, UsageTotals b) => new(
        a.Input + b.Input,
        a.CacheWrite5m + b.CacheWrite5m,
        a.CacheWrite1h + b.CacheWrite1h,
        a.CacheRead + b.CacheRead,
        a.Output + b.Output,
        a.Thinking + b.Thinking);

    /// <summary>Named alternative to the operator, which analysers ask for.</summary>
    public static UsageTotals Add(UsageTotals left, UsageTotals right) => left + right;
}
