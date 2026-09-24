using System.Globalization;

namespace Loadout.Models.Teams;

/// <summary>What a team run may spend: a figure, no cap on purpose, or not said.</summary>
/// <remarks>
/// <para>
/// Three states, because two were not enough. A null budget meant "not set"
/// everywhere, and not set falls back to something else - the run's figure
/// to the team's, the team's to the machine's - and, where nothing sets one and
/// no round limit was given, the run is refused. Taking the cap off on purpose
/// has to be something a person says, not the absence of a figure, or a team
/// file that forgot its budget would be indistinguishable from one that meant
/// to have none.
/// </para>
/// <para>
/// Written as a figure in US dollars ("25", "$25", "12.50") or as
/// <see cref="NoneWord" />.
/// </para>
/// </remarks>
public readonly record struct UsdCap
{
    /// <summary>How no cap is written, wherever a budget is typed.</summary>
    public const string NoneWord = "none";

    private UsdCap(decimal? usd, bool none)
    {
        Usd = usd;
        None = none;
    }

    /// <summary>The figure, or null for no cap or not said.</summary>
    public decimal? Usd { get; }

    /// <summary>Whether somebody said there is no cap.</summary>
    public bool None { get; }

    /// <summary>Whether anything was said at all.</summary>
    public bool IsSet => Usd is not null || None;

    /// <summary>Nothing said, so whatever comes next decides.</summary>
    public static UsdCap Unset => default;

    /// <summary>No cap, on purpose.</summary>
    public static UsdCap NoCap => new(null, true);

    /// <summary>A figure, which must be above zero.</summary>
    public static UsdCap Of(decimal usd) =>
        usd > 0m ? new(usd, false) : throw new ArgumentOutOfRangeException(nameof(usd), usd, "A budget is a figure above zero.");

    /// <summary>This, where it says anything, else <paramref name="fallback" />.</summary>
    public UsdCap Or(UsdCap fallback) => IsSet ? this : fallback;

    /// <summary>Reads what somebody typed: a figure above zero, or <see cref="NoneWord" />.</summary>
    /// <remarks>Blank reads as not said. Zero, a negative figure or anything else is refused rather than taken as no cap.</remarks>
    public static bool TryParse(string? text, out UsdCap cap)
    {
        cap = Unset;
        var said = text?.Trim() ?? string.Empty;

        if (said.Length == 0)
        {
            return true;
        }

        if (string.Equals(said, NoneWord, StringComparison.OrdinalIgnoreCase))
        {
            cap = NoCap;

            return true;
        }

        if (decimal.TryParse(said.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var usd) && usd > 0m)
        {
            cap = Of(usd);

            return true;
        }

        return false;
    }

    /// <summary>What to say about something typed as a budget that is not one.</summary>
    public static string Refusal(string said) =>
        $"'{said}' is not a budget. Give a figure in US dollars above zero, or '{NoneWord}' for no cap.";

    /// <summary>As it would be typed: the figure, <see cref="NoneWord" />, or empty.</summary>
    public override string ToString() =>
        None ? NoneWord : Usd?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>For a person: "$25.00", "no cap", or "not set".</summary>
    public string Describe() =>
        None ? "no cap" : Usd is { } usd ? $"${usd:0.00}" : "not set";
}
