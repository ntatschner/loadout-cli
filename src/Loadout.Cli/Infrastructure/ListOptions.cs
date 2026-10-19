using Loadout.Core.Lists;
using Loadout.Models.Results;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// A list command's --filter and --sort, put through the kind's fields: the
/// query Studio and the TUI take, so a script gets the list the screens show.
/// </summary>
internal static class ListOptions
{
    /// <summary>
    /// The items the options keep, in their order. Without either option the
    /// list is returned as it came, so a command that gains them prints
    /// exactly what it did for anyone who does not ask.
    /// </summary>
    internal static OperationResult<IReadOnlyList<T>> Apply<T>(
        ListFields<T> fields,
        string? filter,
        string? sort,
        IReadOnlyList<T> items,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(items);

        if (filter is not { Length: > 0 } && sort is not { Length: > 0 })
        {
            return OperationResult<IReadOnlyList<T>>.Ok(items);
        }

        var parsed = ListQuery.Parse(Text(filter, sort));

        return parsed.Failed
            ? OperationResult<IReadOnlyList<T>>.Fail(parsed.Error!, parsed.ExitCode)
            : fields.Apply(parsed.Value!, items, now);
    }

    /// <summary>
    /// The query as applied, for --json: what a screen draws its chips and
    /// menus from. Only called once <see cref="Apply"/> has accepted it.
    /// </summary>
    internal static ListShape Shape<T>(ListFields<T> fields, string? filter, string? sort)
    {
        ArgumentNullException.ThrowIfNull(fields);

        return fields.Shape(ListQuery.Parse(Text(filter, sort)).Value ?? ListQuery.Empty);
    }

    /// <summary>
    /// The filter in force: what --filter says, or else the query saved for
    /// the view when it applies, or none.
    /// </summary>
    /// <param name="filter">What --filter said.</param>
    /// <param name="saved">The query saved for this list's view, or null.</param>
    /// <param name="asked">Whether --saved was given.</param>
    /// <param name="json">Whether the output is JSON.</param>
    /// <remarks>
    /// The rule 'task list' keeps: a saved query narrows what a person reads,
    /// and what a script reads only when it asks, so a script's list does not
    /// change because somebody used a screen.
    /// </remarks>
    internal static (string? Filter, bool FromSaved) Filter(string? filter, string? saved, bool asked, bool json) =>
        filter is { Length: > 0 }
            ? (filter, false)
            : saved is { Length: > 0 } && (asked || !json) ? (saved, true) : (null, false);

    private static string Text(string? filter, string? sort) =>
        (filter ?? string.Empty) + (sort is { Length: > 0 } by ? $" {ListQuery.SortKey}:{by}" : string.Empty);
}
