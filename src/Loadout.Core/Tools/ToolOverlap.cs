using System.Text.RegularExpressions;
using Loadout.Core.Teams;

namespace Loadout.Core.Tools;

/// <summary>What overlap is measured on: what a tool says it is, and what it does.</summary>
/// <param name="Capabilities">Its search terms.</param>
/// <param name="Summary">Its one-line summary.</param>
/// <param name="Script">Its script text.</param>
public sealed record ToolShape(IReadOnlyList<string> Capabilities, string Summary, string Script);

/// <summary>How much two tools overlap.</summary>
/// <param name="Score">0 to 1; 1 for a duplicate.</param>
/// <param name="Duplicate">The same script once comments and spacing are taken out.</param>
public sealed record ToolOverlapScore(double Score, bool Duplicate)
{
    /// <summary>Whether this is enough to ask the submitter to extend rather than add.</summary>
    public bool Overlaps => Duplicate || Score >= ToolOverlap.Threshold;
}

/// <summary>
/// Whether a new tool is one the catalogue already has.
/// </summary>
/// <remarks>
/// Deterministic on purpose: the same two tools always score the same, so a
/// refusal can be argued with. Half on what the tools say they are, half on
/// what their scripts do, because either alone is fooled by a rename.
/// </remarks>
public static partial class ToolOverlap
{
    /// <summary>The score at which two tools are flagged. A starting value, to be judged on real tools.</summary>
    public const double Threshold = 0.6;

    private const int Shingle = 5;

    /// <summary>Scores two tools against each other.</summary>
    public static ToolOverlapScore Score(ToolShape a, ToolShape b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var left = Normalise(a.Script);
        var right = Normalise(b.Script);

        if (left.Length > 0
            && string.Equals(RemedyCeiling.Fingerprint(left), RemedyCeiling.Fingerprint(right), StringComparison.Ordinal))
        {
            return new ToolOverlapScore(1, true);
        }

        var described = Jaccard(Words(a), Words(b));
        var scripted = Jaccard(Shingles(left), Shingles(right));

        return new ToolOverlapScore(0.5 * described + 0.5 * scripted, false);
    }

    /// <summary>A script with comments and spacing taken out, so layout is not difference.</summary>
    public static string Normalise(string? script)
    {
        if (string.IsNullOrEmpty(script))
        {
            return string.Empty;
        }

        var text = script.Replace("\r\n", "\n", StringComparison.Ordinal);
        text = BlockComment().Replace(text, " ");
        text = LineComment().Replace(text, string.Empty);

        return Space().Replace(text, " ").Trim();
    }

    private static HashSet<string> Words(ToolShape shape) =>
    [
        .. shape.Capabilities
            .Concat(Word().Matches(shape.Summary ?? string.Empty).Select(one => one.Value))
            .Select(one => one.Trim().ToLowerInvariant())
            .Where(one => one.Length > 2),
    ];

    private static HashSet<string> Shingles(string normalised)
    {
        var tokens = Word().Matches(normalised).Select(one => one.Value.ToLowerInvariant()).ToList();

        var set = new HashSet<string>(StringComparer.Ordinal);

        if (tokens.Count < Shingle)
        {
            if (tokens.Count > 0)
            {
                set.Add(string.Join(' ', tokens));
            }

            return set;
        }

        for (var i = 0; i + Shingle <= tokens.Count; i++)
        {
            set.Add(string.Join(' ', tokens.Skip(i).Take(Shingle)));
        }

        return set;
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
        {
            return 0;
        }

        var union = new HashSet<string>(a, a.Comparer);
        union.UnionWith(b);

        return (double)a.Count(b.Contains) / union.Count;
    }

    [GeneratedRegex(@"<#.*?#>", RegexOptions.Singleline, 1000)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"#[^\n]*", RegexOptions.None, 1000)]
    private static partial Regex LineComment();

    [GeneratedRegex(@"\s+", RegexOptions.None, 1000)]
    private static partial Regex Space();

    [GeneratedRegex(@"[A-Za-z0-9_$-]+", RegexOptions.None, 1000)]
    private static partial Regex Word();
}
