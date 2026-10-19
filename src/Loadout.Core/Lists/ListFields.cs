using Loadout.Models.Results;

namespace Loadout.Core.Lists;

/// <summary>How a field's values are compared, filtered and sorted.</summary>
public enum ListFieldType
{
    /// <summary>Free text: a term matches when the field contains any of its values.</summary>
    Text,

    /// <summary>One of a fixed set: a term matches the values exactly, and sorts in the set's order.</summary>
    Choice,

    /// <summary>A moment: <c>&lt;7d</c> is under seven days old, <c>&gt;2026-10-01</c> after that day.</summary>
    Date,

    /// <summary>A count: <c>&gt;2</c>, <c>&lt;=5</c>, or a bare number for exactly that.</summary>
    Number,
}

/// <summary>One field a kind of item can be filtered and sorted on.</summary>
/// <typeparam name="T">The item listed.</typeparam>
public sealed class ListField<T>
{
    private ListField(string name, ListFieldType type, string description, IReadOnlyList<string> choices, Func<T, object?> read)
    {
        Name = name;
        Type = type;
        Description = description;
        Choices = choices;
        Read = read;
    }

    /// <summary>The name a query uses, in lower case.</summary>
    public string Name { get; }

    public ListFieldType Type { get; }

    /// <summary>What the field is, said for the help and for an error naming it.</summary>
    public string Description { get; }

    /// <summary>For a choice, its values in the order they sort; empty otherwise.</summary>
    public IReadOnlyList<string> Choices { get; }

    internal Func<T, object?> Read { get; }

    public static ListField<T> Text(string name, string description, Func<T, string?> read) =>
        new(name, ListFieldType.Text, description, [], item => read(item));

    public static ListField<T> Choice(string name, string description, IReadOnlyList<string> choices, Func<T, string?> read) =>
        new(name, ListFieldType.Choice, description, choices, item => read(item));

    public static ListField<T> Date(string name, string description, Func<T, DateTimeOffset?> read) =>
        new(name, ListFieldType.Date, description, [], item => read(item));

    public static ListField<T> Number(string name, string description, Func<T, double?> read) =>
        new(name, ListFieldType.Number, description, [], item => read(item));
}

/// <summary>
/// What one kind of item can be filtered and sorted on, and how it is listed
/// when nobody has said: the registry a <see cref="ListQuery"/> is checked
/// against and applied with.
/// </summary>
/// <typeparam name="T">The item listed.</typeparam>
/// <remarks>
/// One per kind, in Core, so Studio, the TUI and the CLI filter a list in the
/// same way and refuse an unknown field with the same words. Free words are
/// looked for in every text and choice field.
/// </remarks>
public sealed class ListFields<T>
{
    private readonly Dictionary<string, ListField<T>> _byName;

    /// <param name="kind">What the items are, plural, as an error says it: "tasks".</param>
    /// <param name="fields">Every field, in the order the help lists them.</param>
    /// <param name="defaultSort">The order when a query names none, and the tie-breaker when it does.</param>
    /// <param name="defaultQuery">The query a view starts with when nothing was saved for it.</param>
    public ListFields(string kind, IReadOnlyList<ListField<T>> fields, IReadOnlyList<ListSort> defaultSort, string defaultQuery = "")
    {
        Kind = kind;
        Fields = fields;
        DefaultSort = defaultSort;
        DefaultQuery = defaultQuery;
        _byName = fields.ToDictionary(field => field.Name, StringComparer.OrdinalIgnoreCase);
    }

    public string Kind { get; }

    public IReadOnlyList<ListField<T>> Fields { get; }

    public IReadOnlyList<ListSort> DefaultSort { get; }

    /// <summary>The built-in query for this kind's view, such as hiding finished tasks.</summary>
    public string DefaultQuery { get; }

    /// <summary>The field with this name, or null.</summary>
    public ListField<T>? Find(string name) => _byName.GetValueOrDefault(name);

    /// <summary>
    /// Whether every field the query names is one of these, with values it
    /// can take. The first problem is the one said.
    /// </summary>
    public OperationResult Check(ListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        foreach (var term in query.Terms)
        {
            if (Find(term.Field) is not { } field)
            {
                return Unknown(term.Field, "filtered");
            }

            foreach (var value in term.Values)
            {
                var problem = field.Type switch
                {
                    ListFieldType.Choice when !field.Choices.Contains(value, StringComparer.OrdinalIgnoreCase) =>
                        $"'{value}' is not a {field.Name}. It is one of: {string.Join(", ", field.Choices)}.",
                    ListFieldType.Date when !ListValues.TryDate(value, out _) =>
                        $"'{field.Name}:{value}' is not a date. Write an age such as <7d or >12h, or a day such as >=2026-10-01.",
                    ListFieldType.Number when !ListValues.TryNumber(value, out _) =>
                        $"'{field.Name}:{value}' is not a number. Write one such as 3, >2 or <=5.",
                    _ => null,
                };

                if (problem is not null)
                {
                    return OperationResult.Fail(problem, Models.ExitCode.InvalidArguments);
                }
            }
        }

        foreach (var sort in query.Sorts)
        {
            if (Find(sort.Field) is null)
            {
                return Unknown(sort.Field, "sorted");
            }
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// The items the query keeps, in its order: its own sorts first, then the
    /// default, then the order they came in.
    /// </summary>
    public OperationResult<IReadOnlyList<T>> Apply(ListQuery query, IEnumerable<T> items, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(items);

        var checkedQuery = Check(query);

        if (checkedQuery.Failed)
        {
            return OperationResult<IReadOnlyList<T>>.Fail(checkedQuery.Error!, checkedQuery.ExitCode);
        }

        var kept = items.Where(item => Keeps(query, item, now));
        var sorts = query.Sorts.Concat(DefaultSort).ToList();

        if (sorts.Count == 0)
        {
            return OperationResult<IReadOnlyList<T>>.Ok([.. kept]);
        }

        IOrderedEnumerable<T> ordered = Order(kept, sorts[0], first: true, null);

        foreach (var sort in sorts.Skip(1))
        {
            ordered = Order(kept, sort, first: false, ordered);
        }

        return OperationResult<IReadOnlyList<T>>.Ok([.. ordered]);
    }

    /// <summary>The fields, one line each, for help text.</summary>
    public IEnumerable<string> Describe() =>
        Fields.Select(field => field.Type == ListFieldType.Choice
            ? $"{field.Name}: {field.Description} ({string.Join(", ", field.Choices)})"
            : $"{field.Name}: {field.Description} ({field.Type.ToString().ToLowerInvariant()})");

    private OperationResult Unknown(string name, string how) =>
        OperationResult.Fail(
            $"'{name}' is not something {Kind} can be {how} on. They can be: {string.Join(", ", Fields.Select(f => f.Name))}.",
            Models.ExitCode.InvalidArguments);

    private bool Keeps(ListQuery query, T item, DateTimeOffset now)
    {
        foreach (var term in query.Terms)
        {
            var field = Find(term.Field)!;
            var value = field.Read(item);
            var matches = term.Values.Any(wanted => Matches(field, value, wanted, now));

            if (matches == term.Negated)
            {
                return false;
            }
        }

        foreach (var word in query.Words)
        {
            var found = Fields
                .Where(field => field.Type is ListFieldType.Text or ListFieldType.Choice)
                .Any(field => field.Read(item) is string text && text.Contains(word.Text, StringComparison.OrdinalIgnoreCase));

            if (found == word.Negated)
            {
                return false;
            }
        }

        return true;
    }

    private static bool Matches(ListField<T> field, object? value, string wanted, DateTimeOffset now) => field.Type switch
    {
        ListFieldType.Text => value is string text && text.Contains(wanted, StringComparison.OrdinalIgnoreCase),
        ListFieldType.Choice => value is string text && string.Equals(text, wanted, StringComparison.OrdinalIgnoreCase),
        ListFieldType.Date => value is DateTimeOffset at && ListValues.TryDate(wanted, out var date) && date.Holds(at, now),
        ListFieldType.Number => value is double count && ListValues.TryNumber(wanted, out var number) && number.Holds(count),
        _ => false,
    };

    private IOrderedEnumerable<T> Order(IEnumerable<T> items, ListSort sort, bool first, IOrderedEnumerable<T>? after)
    {
        var field = Find(sort.Field)!;

        // Anything without a value goes last whichever way round, so a
        // descending sort does not lead with blanks.
        Func<T, bool> missing = item => field.Read(item) is null or "";
        Func<T, IComparable> key = item => field.Read(item) switch
        {
            string text when field.Type == ListFieldType.Choice => IndexOf(field, text),
            string text => text.ToLowerInvariant(),
            DateTimeOffset at => at,
            double count => count,
            _ => 0,
        };

        var byMissing = first ? items.OrderBy(missing) : after!.ThenBy(missing);

        return sort.Descending
            ? byMissing.ThenByDescending(key, Comparer<IComparable>.Create(Compare))
            : byMissing.ThenBy(key, Comparer<IComparable>.Create(Compare));
    }

    private static int IndexOf(ListField<T> field, string text)
    {
        for (var i = 0; i < field.Choices.Count; i++)
        {
            if (string.Equals(field.Choices[i], text, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return field.Choices.Count;
    }

    private static int Compare(IComparable? a, IComparable? b) =>
        a is string x && b is string y ? string.CompareOrdinal(x, y) : Comparer<IComparable>.Default.Compare(a!, b!);
}
