using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Loadout.Tests.Unit;

/// <summary>
/// Takes out of a replay's record what differs between two runs of the same
/// script and nothing else: the run's id, the temporary root, the stepping
/// clock's times and random ids.
/// </summary>
/// <remarks>
/// Everything else is kept as written, data fields included, because the
/// point of a replay is that a changed field shows. A time outside the
/// stepping clock's window was written by the script, not the clock, and is
/// kept too, so a run that started dating folded events at the fold shows.
/// </remarks>
internal static partial class ReplayNormaliser
{
    public static string Normalise(string text, string runId, string root)
    {
        text = Roots(text, root);

        if (runId.Length > 0)
        {
            text = text.Replace(runId, "<run>", StringComparison.Ordinal);
        }

        text = Times().Replace(text, match =>
            DateTimeOffset.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            && at >= SteppingClock.Start
            && at <= SteppingClock.Start + SteppingClock.Window
                ? $"<t+{(long)(at - SteppingClock.Start).TotalMilliseconds}>"
                : match.Value);

        return Ids().Replace(text, "<id>");
    }

    /// <summary>
    /// The root as written raw, as JSON escapes it and with forward slashes,
    /// each with the rest of its path given forward slashes too, so a record
    /// made on Windows reads the same as one made anywhere else.
    /// </summary>
    private static string Roots(string text, string root)
    {
        if (root.Length == 0)
        {
            return text;
        }

        var forms = new[] { root.Replace("\\", "\\\\", StringComparison.Ordinal), root, root.Replace('\\', '/') }
            .Distinct(StringComparer.Ordinal);

        foreach (var form in forms)
        {
            var built = new StringBuilder();
            var from = 0;

            while (true)
            {
                var found = text.IndexOf(form, from, StringComparison.Ordinal);

                if (found < 0)
                {
                    built.Append(text, from, text.Length - from);
                    break;
                }

                var end = found + form.Length;

                while (end < text.Length && !EndsAPath(text[end]))
                {
                    end++;
                }

                var tail = text[(found + form.Length)..end]
                    .Replace("\\\\", "/", StringComparison.Ordinal)
                    .Replace('\\', '/');

                built.Append(text, from, found - from).Append("<root>").Append(tail);
                from = end;
            }

            text = built.ToString();
        }

        return text;
    }

    private static bool EndsAPath(char c) =>
        char.IsWhiteSpace(c) || c is '"' or '\'' or '`' or ',' or ')' or ']' or '}' or '>';

    [GeneratedRegex(@"[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]+)?(?:Z|[+-][0-9]{2}:[0-9]{2})")]
    private static partial Regex Times();

    /// <summary>Eight hex digits after a word and a dash, as a gate's or an ask's id is made.</summary>
    [GeneratedRegex(@"(?<=[a-z]-)[0-9a-f]{8}(?![0-9A-Za-z])")]
    private static partial Regex Ids();
}
