namespace Loadout.Core.Triggers;

/// <summary>
/// What a run's journal says started it, where a trigger did.
/// </summary>
/// <param name="Rule">The rule that started it.</param>
/// <param name="Version">That rule's version when it did.</param>
/// <param name="Reservation">The money set aside for it, which its landing closes.</param>
/// <param name="Chain">The rules whose actions led to the event that matched, oldest first.</param>
/// <remarks>
/// <para>
/// One line of text, because it travels as a command-line option to
/// <c>team run --origin</c> and lands in the run's journal as a string. The
/// rule names it carries cannot hold a colon or a comma, so neither needs
/// escaping.
/// </para>
/// <para>
/// The reservation is what lets the daemon find the run again. Taking the
/// newest run on the machine as its own would be right until two runs started
/// in the same minute.
/// </para>
/// </remarks>
public sealed record RunOrigin(string Rule, int Version, string Reservation, IReadOnlyList<string> Chain)
{
    private const string Prefix = "trigger:";

    /// <summary>The rules that led to the run's own events: the chain, then the rule that started it.</summary>
    public IReadOnlyList<string> Origins => [.. Chain, Rule];

    /// <summary>The text written to the run's journal.</summary>
    public override string ToString() =>
        $"{Prefix}{Rule}:{Version}:{Reservation}:{string.Join(',', Chain)}";

    /// <summary>The origin a journal recorded, or null for a run no trigger started.</summary>
    public static RunOrigin? Parse(string? text)
    {
        if (text is null || !text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = text[Prefix.Length..].Split(':');

        if (parts.Length != 4 || parts[0].Length == 0 || parts[2].Length == 0
            || !int.TryParse(parts[1], System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var version))
        {
            return null;
        }

        return new RunOrigin(
            parts[0], version, parts[2], parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries));
    }
}
