using System.Text;
using System.Text.RegularExpressions;

namespace Loadout.Core.Instructions;

/// <summary>
/// Fits the memory index a session is given into a fixed size, keeping the
/// descriptions of the most recently written topics and naming the rest.
/// </summary>
/// <remarks>
/// <para>
/// The index is pasted into every compiled context, so its size is paid on every
/// launch. One project's three indexes grew to 45KB across 159 topics, about
/// 11,500 tokens a session, and the audit's answer of an 8KB file could only be
/// met by deleting the descriptions that make the index worth carrying.
/// </para>
/// <para>
/// Every topic stays listed, because a session that does not know a topic exists
/// will not search for it: across 281 sessions of real work, with the whole
/// index in front of them, about one in five looked anything up. What
/// is dropped is an older topic's description, which <c>loadout_recall</c> and
/// <c>loadout memory show</c> still give on request.
/// </para>
/// <para>
/// Applied when the index is read for a session, not when MEMORY.md is written.
/// The workspace syncs between machines and each machine has its own file times,
/// so an index written to a budget would be rewritten differently on each one.
/// </para>
/// </remarks>
public static partial class MemoryIndexBudget
{
    /// <summary>
    /// What a session's memory index may cost, in UTF-8 bytes: about 6,000
    /// tokens. On 159 topics that described the newest 66, about ten days'
    /// worth, and named the rest; 16KB described 36 and 32KB 98.
    /// </summary>
    public const int DefaultBytes = 24 * 1024;

    /// <summary>One scope's index as written, and when each of its topics was last written.</summary>
    /// <param name="Text">The index text, headings included.</param>
    /// <param name="WrittenUtc">Given a topic's file name, when that file was last written.</param>
    public sealed record Section(string Text, Func<string, DateTime> WrittenUtc);

    /// <summary>The index as a session will see it.</summary>
    /// <param name="Text">The fitted text.</param>
    /// <param name="Described">Topics listed with their description.</param>
    /// <param name="Total">Topics listed at all, which is every topic in the indexes.</param>
    /// <param name="Bytes">The fitted text's size in UTF-8 bytes.</param>
    public sealed record Fitted(string Text, int Described, int Total, int Bytes)
    {
        /// <summary>Topics listed by name only.</summary>
        public int NamedOnly => Total - Described;
    }

    /// <summary>
    /// Fits the sections into <paramref name="budgetBytes"/>, describing topics
    /// newest first until the next one would not fit.
    /// </summary>
    /// <remarks>
    /// Stops at the first topic that does not fit rather than skipping to smaller
    /// ones, so what a session sees described is exactly the newest topics and
    /// not whichever happened to have short descriptions. When even names alone
    /// exceed the budget, every topic is named and the result is over budget;
    /// the audit reports that case.
    /// </remarks>
    public static Fitted Fit(IReadOnlyList<Section> sections, int budgetBytes, string separator)
    {
        var parsed = sections.Select(Parse).ToList();
        var entries = parsed
            .SelectMany((lines, s) => lines
                .Select((line, i) => (Section: s, Line: i, line.File))
                .Where(e => e.File is not null))
            .ToList();

        var everything = entries.Select(e => (e.Section, e.Line)).ToHashSet();
        var whole = Render(parsed, everything, separator);

        if (Size(whole) <= budgetBytes)
        {
            return new Fitted(whole, entries.Count, entries.Count, Size(whole));
        }

        var newestFirst = entries
            .OrderByDescending(e => sections[e.Section].WrittenUtc(e.File!))
            .ThenBy(e => e.File, StringComparer.Ordinal)
            .ToList();

        var described = new HashSet<(int, int)>();
        var text = Render(parsed, described, separator);

        foreach (var entry in newestFirst)
        {
            described.Add((entry.Section, entry.Line));
            var attempt = Render(parsed, described, separator);

            if (Size(attempt) > budgetBytes)
            {
                described.Remove((entry.Section, entry.Line));
                break;
            }

            text = attempt;
        }

        return new Fitted(text, described.Count, entries.Count, Size(text));
    }

    private static int Size(string text) => Encoding.UTF8.GetByteCount(text);

    private static List<(string Text, string? File)> Parse(Section section) =>
        section.Text
            .Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Select(line => (line, Entry().Match(line) is { Success: true } m ? m.Groups["file"].Value : null))
            .ToList();

    private static string Render(
        List<List<(string Text, string? File)>> parsed,
        HashSet<(int, int)> described,
        string separator)
    {
        var rendered = new List<string>();

        for (var s = 0; s < parsed.Count; s++)
        {
            var kept = new List<string>();
            var named = new List<string>();

            for (var i = 0; i < parsed[s].Count; i++)
            {
                var (text, file) = parsed[s][i];

                if (file is not null && !described.Contains((s, i)))
                {
                    named.Add(Path.GetFileNameWithoutExtension(file));
                    continue;
                }

                kept.Add(text);
            }

            var body = string.Join(Environment.NewLine, kept).TrimEnd();

            if (named.Count > 0)
            {
                body += (body.Length == 0 ? string.Empty : Environment.NewLine + Environment.NewLine)
                    + "By name only, to keep this list short; `loadout_recall` or "
                    + "`loadout memory show <name>` gives what each one holds: "
                    + string.Join(", ", named) + ".";
            }

            rendered.Add(body);
        }

        return string.Join(separator, rendered);
    }

    /// <summary>An index line: a bullet whose first thing is a link to the topic's file.</summary>
    [GeneratedRegex(@"^- \[[^\]\n]+\]\((?<file>[^)\s]+\.md)\)")]
    private static partial Regex Entry();
}
