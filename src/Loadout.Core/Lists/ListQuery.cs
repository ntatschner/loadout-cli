using System.Text;
using Loadout.Models.Results;

namespace Loadout.Core.Lists;

/// <summary>One field term: <c>state:doing,open</c>, or <c>-project:x</c> to leave those out.</summary>
/// <param name="Field">The field's name, in lower case.</param>
/// <param name="Values">What it must be, any one of them.</param>
/// <param name="Negated">True to leave out what matches instead.</param>
public sealed record ListTerm(string Field, IReadOnlyList<string> Values, bool Negated);

/// <summary>One field to sort by: <c>sort:updated</c>, or <c>sort:-updated</c> for the newest first.</summary>
public sealed record ListSort(string Field, bool Descending);

/// <summary>A word or quoted phrase looked for in every text field, or left out with a leading minus.</summary>
public sealed record ListWord(string Text, bool Negated);

/// <summary>
/// A list query as a person writes it: field terms, words to look for, and
/// what to sort by, in one line such as
/// <c>state:doing,open -project:x sort:-updated fix login</c>.
/// </summary>
/// <remarks>
/// <para>
/// One grammar for every list on every surface. Studio's chips write it, its
/// search box takes it typed, the TUI's filter field takes it, and the CLI
/// takes it as <c>--filter</c>, so a chip and the text beside it cannot
/// disagree: they are the same string.
/// </para>
/// <para>
/// Parsing knows only the grammar. Whether a field exists, and what values it
/// takes, is for the kind being listed to say (<see cref="ListFields{T}"/>),
/// so global search can parse one query and put it to every kind.
/// </para>
/// </remarks>
public sealed class ListQuery
{
    /// <summary>The key that sorts rather than filters.</summary>
    public const string SortKey = "sort";

    /// <summary>A query that keeps everything in the default order.</summary>
    public static readonly ListQuery Empty = new([], [], []);

    public ListQuery(IReadOnlyList<ListTerm> terms, IReadOnlyList<ListWord> words, IReadOnlyList<ListSort> sorts)
    {
        Terms = terms;
        Words = words;
        Sorts = sorts;
    }

    /// <summary>The field terms, every one of which must hold.</summary>
    public IReadOnlyList<ListTerm> Terms { get; }

    /// <summary>The words, every one of which must be found (or, negated, not found).</summary>
    public IReadOnlyList<ListWord> Words { get; }

    /// <summary>What to sort by, the first deciding and the rest breaking ties.</summary>
    public IReadOnlyList<ListSort> Sorts { get; }

    /// <summary>True when the query neither filters nor sorts.</summary>
    public bool IsEmpty => Terms.Count == 0 && Words.Count == 0 && Sorts.Count == 0;

    /// <summary>
    /// Reads a query. Null or blank is the empty query. A word holding a colon
    /// is a field term; quote it to look for it as text instead.
    /// </summary>
    public static OperationResult<ListQuery> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult<ListQuery>.Ok(Empty);
        }

        var tokens = Tokenise(text);

        if (tokens.Failed)
        {
            return OperationResult<ListQuery>.Fail(tokens.Error!, tokens.ExitCode);
        }

        var terms = new List<ListTerm>();
        var words = new List<ListWord>();
        var sorts = new List<ListSort>();

        foreach (var (raw, quoted) in tokens.Value!)
        {
            var negated = raw.Length > 1 && raw[0] == '-';
            var body = negated ? raw[1..] : raw;
            var colon = quoted ? -1 : body.IndexOf(':', StringComparison.Ordinal);

            if (colon < 0)
            {
                words.Add(new ListWord(body, negated));

                continue;
            }

            if (colon == 0)
            {
                return Refuse($"'{raw}' has no field before its colon. Quote it to look for it as text: \"{raw}\"");
            }

            var field = body[..colon].ToLowerInvariant();
            var values = body[(colon + 1)..]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (values.Length == 0)
            {
                return Refuse($"'{raw}' says no value. Write it as {field}:<value>, with commas between several.");
            }

            if (field == SortKey)
            {
                if (negated)
                {
                    return Refuse($"'{raw}' cannot be left out. Sort the other way with sort:-<field>.");
                }

                foreach (var value in values)
                {
                    var descending = value.Length > 1 && value[0] == '-';
                    sorts.Add(new ListSort((descending ? value[1..] : value).ToLowerInvariant(), descending));
                }

                continue;
            }

            terms.Add(new ListTerm(field, values, negated));
        }

        return OperationResult<ListQuery>.Ok(new ListQuery(terms, words, sorts));
    }

    /// <summary>This query with one term taken out: how a chip is removed.</summary>
    public ListQuery Without(ListTerm term) =>
        new([.. Terms.Where(one => !ReferenceEquals(one, term) && one != term)], Words, Sorts);

    /// <summary>This query with another put after it: a default with what was asked for added.</summary>
    public ListQuery And(ListQuery other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return new ListQuery([.. Terms, .. other.Terms], [.. Words, .. other.Words], [.. Sorts, .. other.Sorts]);
    }

    /// <summary>
    /// The query written out again, terms, then words, then the sort: what is
    /// stored, and what a chip's removal leaves in the search box.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        parts.AddRange(Terms.Select(term => $"{(term.Negated ? "-" : string.Empty)}{term.Field}:{string.Join(',', term.Values)}"));
        parts.AddRange(Words.Select(word => (word.Negated ? "-" : string.Empty) + Quote(word.Text)));

        if (Sorts.Count > 0)
        {
            parts.Add($"{SortKey}:{string.Join(',', Sorts.Select(sort => (sort.Descending ? "-" : string.Empty) + sort.Field))}");
        }

        return string.Join(' ', parts);
    }

    private static string Quote(string text) =>
        text.Length == 0 || text.Any(c => char.IsWhiteSpace(c) || c is ':' or '"') || text[0] == '-'
            ? $"\"{text.Replace("\"", string.Empty, StringComparison.Ordinal)}\""
            : text;

    private static OperationResult<ListQuery> Refuse(string error) =>
        OperationResult<ListQuery>.Fail(error, Models.ExitCode.InvalidArguments);

    /// <summary>Splits on white space, keeping a double-quoted phrase whole, minus sign and all.</summary>
    private static OperationResult<List<(string Text, bool Quoted)>> Tokenise(string text)
    {
        var tokens = new List<(string, bool)>();
        var current = new StringBuilder();
        var quoted = false;
        var inQuotes = false;

        void Flush()
        {
            if (current.Length > 0 || quoted)
            {
                tokens.Add((current.ToString(), quoted));
            }

            current.Clear();
            quoted = false;
        }

        foreach (var c in text)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                quoted = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        if (inQuotes)
        {
            return OperationResult<List<(string, bool)>>.Fail(
                "A quote is opened and never closed.", Models.ExitCode.InvalidArguments);
        }

        Flush();

        // A quoted phrase that came to nothing, such as "", looks for nothing.
        return OperationResult<List<(string, bool)>>.Ok([.. tokens.Where(token => token.Item1.Length > 0 && token.Item1 != "-")]);
    }
}
