using System.Text;

namespace Loadout.Core.Instructions;

/// <summary>
/// Turns a Markdown heading into a name that is also a filename.
/// <para>
/// One implementation because two tools need the same answer. The instruction
/// splitter names rule files after headings and the memory compressor names
/// topics after them, and when each did its own the two disagreed: a heading
/// carrying source paths produced a readable name in one place and a
/// hundred-character one in the other, for the same document.
/// </para>
/// </summary>
public static class HeadingName
{
    /// <summary>
    /// How long a derived name may get. It becomes a filename and an index
    /// entry, and a heading that names six source files would otherwise
    /// produce one unreadable in both places.
    /// </summary>
    public const int MaximumLength = 48;

    /// <summary>
    /// Derives the name.
    /// <para>
    /// Backticked spans and parentheses are dropped first. In a real
    /// instruction file those hold the source paths and dates the section
    /// concerns — useful in a heading, meaningless in a filename, and the
    /// reason an unfiltered name runs past a hundred characters.
    /// </para>
    /// </summary>
    /// <param name="heading">Heading text, without its leading hashes.</param>
    /// <param name="fallback">Name to use when the heading reduces to nothing.</param>
    public static string From(string? heading, string fallback = "notes")
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return fallback;
        }

        var stripped = Strip(Strip(heading, '`', '`'), '(', ')');

        var builder = new StringBuilder(stripped.Length);

        foreach (var c in stripped.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var name = builder.ToString().Trim('-');

        if (name.Length > MaximumLength)
        {
            // Cut at a word boundary so the name still reads as words rather
            // than stopping mid-syllable.
            var cut = name.LastIndexOf('-', MaximumLength);

            name = (cut > MaximumLength / 2 ? name[..cut] : name[..MaximumLength]).Trim('-');
        }

        return name.Length == 0 ? fallback : name;
    }

    /// <summary>
    /// Makes a name unique within a set, so two headings that shorten alike do
    /// not silently overwrite one another.
    /// </summary>
    public static string Unique(string name, HashSet<string> used)
    {
        ArgumentNullException.ThrowIfNull(used);

        if (used.Add(name))
        {
            return name;
        }

        for (var n = 2; ; n++)
        {
            var candidate = $"{name}-{n}";

            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// The paths a heading names in backticks, which are the globs the section
    /// is about.
    /// <para>
    /// Read rather than guessed: a heading that says
    /// <c>(`crates/core/src/modules`)</c> has already stated which paths its
    /// rule concerns, and re-typing that by hand is the step that makes people
    /// abandon scoping halfway.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> PathsIn(string? heading)
    {
        if (string.IsNullOrWhiteSpace(heading))
        {
            return [];
        }

        var found = new List<string>();
        var span = heading.AsSpan();
        var start = -1;

        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != '`')
            {
                continue;
            }

            if (start < 0)
            {
                start = i + 1;
                continue;
            }

            var text = span[start..i].ToString().Trim();
            start = -1;

            // A backticked span is only a path if it looks like one. Headings
            // also backtick type names, flags and commands, and turning those
            // into globs would scope a rule to files that do not exist.
            if (LooksLikePath(text))
            {
                found.Add(text);
            }
        }

        return found;
    }

    private static bool LooksLikePath(string text) =>
        text.Length > 0
        && !text.Contains(' ', StringComparison.Ordinal)
        && (text.Contains('/', StringComparison.Ordinal)
            || text.Contains('.', StringComparison.Ordinal));

    /// <summary>Removes every span between two delimiters, inclusive.</summary>
    private static string Strip(string text, char open, char close)
    {
        var builder = new StringBuilder(text.Length);
        var depth = 0;

        foreach (var c in text)
        {
            if (c == open && (open != close || depth == 0))
            {
                depth++;
                continue;
            }

            if (c == close && depth > 0)
            {
                depth--;
                continue;
            }

            if (depth == 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
