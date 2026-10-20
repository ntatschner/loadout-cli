using System.Text;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>How long a task's title may be, and what happens to one that is longer.</summary>
/// <remarks>
/// One rule, used wherever a title is written and by the pass that tidies the
/// ones written before it, so a title shortened today and one shortened by the
/// pass read the same way.
/// </remarks>
public static class TaskTitles
{
    /// <summary>The longest a title may be: the convention for a git subject line.</summary>
    public const int Length = 72;

    /// <summary>What <see cref="TaskEvent"/> records when a title is shortened.</summary>
    public const string ShortenedReason =
        "title shortened to 72 characters; the full text was kept at the start of the detail";

    /// <summary>Whether this title is too long and would be shortened.</summary>
    public static bool Shortens(string title) => Flatten(title).Length > Length;

    /// <summary>
    /// The title as it would be kept: on one line, and cut at a word when it
    /// is longer than <see cref="Length"/>.
    /// </summary>
    /// <remarks>
    /// Cut at the last space that leaves at least half the room used, so a
    /// title is never cut to a couple of words because one long word sat near
    /// the end. Failing that it is cut mid-word, since a title has to fit.
    /// </remarks>
    public static string Shorten(string title)
    {
        var single = Flatten(title);

        if (single.Length <= Length)
        {
            return single;
        }

        var cut = single[..(Length - 1)];
        var space = cut.LastIndexOf(' ');

        if (space >= Length / 2)
        {
            cut = cut[..space];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '-', '.') + "…";
    }

    /// <summary>
    /// The detail a shortened title leaves behind: the original word for word,
    /// then whatever detail there was.
    /// </summary>
    /// <remarks>
    /// The original goes first because it was the description somebody wrote
    /// first. It is not added twice when the detail already starts with it, so
    /// a title shortened again by the tidying pass does not repeat itself.
    /// </remarks>
    public static string Keep(string original, string detail)
    {
        var whole = original.Trim();
        var rest = detail.Trim();

        if (rest.StartsWith(whole, StringComparison.Ordinal))
        {
            return rest;
        }

        return rest.Length == 0 ? whole : whole + "\n\n" + rest;
    }

    private static string Flatten(string text)
    {
        var flat = new StringBuilder(text.Length);
        var gap = false;

        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                gap = true;
                continue;
            }

            if (gap)
            {
                flat.Append(' ');
                gap = false;
            }

            flat.Append(c);
        }

        return flat.ToString();
    }
}
