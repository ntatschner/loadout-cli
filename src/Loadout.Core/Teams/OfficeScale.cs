using System.Globalization;

namespace Loadout.Core.Teams;

/// <summary>
/// How large the office may be drawn: the smallest and largest number of
/// screen pixels per pixel of art, as the page's own CSS pixels.
/// </summary>
/// <param name="Min">Never smaller than this; below it the grid goes to one room a row.</param>
/// <param name="Max">Never larger than this, however wide the window.</param>
/// <remarks>
/// <para>
/// Between the two the page picks the largest whole step of device pixels that
/// fits, so the art stays crisp: at 125% display scaling the steps are 0.8, 1.6,
/// 2.4... in CSS pixels, and a range of 1 to 2 allows 1.6.
/// </para>
/// <para>
/// One setting written as <c>min-max</c> rather than two, because it is one
/// decision and has to be carried as one field from the page to the command:
/// a pair of fields is twice the chance of one of them being dropped on the way.
/// </para>
/// </remarks>
public sealed record OfficeScale(double Min, double Max)
{
    /// <summary>What an unset setting means: the art at one to two CSS pixels per art pixel.</summary>
    public static OfficeScale Default { get; } = new(1, 2);

    /// <summary>The setting's value read, or null when it is not a range the page can use.</summary>
    public static OfficeScale? Parse(string? value)
    {
        if (value is not { Length: > 0 } text)
        {
            return null;
        }

        var parts = text.Trim().Split('-');

        if (parts is not [var low, var high]
            || !double.TryParse(low, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            || !double.TryParse(high, NumberStyles.Float, CultureInfo.InvariantCulture, out var max)
            || min is < 0.5 or > 8 || max is < 0.5 or > 8 || max < min)
        {
            return null;
        }

        return new OfficeScale(min, max);
    }

    /// <summary>The setting as it is written.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Min}-{Max}");
}
