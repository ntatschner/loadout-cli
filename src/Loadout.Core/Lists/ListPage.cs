using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Lists;

/// <summary>One page of a list the query has already kept and ordered.</summary>
/// <param name="Items">What is on this page.</param>
/// <param name="Total">How many the query kept, on every page.</param>
/// <param name="Page">This page, counted from 1.</param>
/// <param name="Pages">How many pages there are; 1 for an empty list, so page 1 always exists.</param>
/// <param name="Limit">How many a page holds, or null for one page holding everything.</param>
public sealed record ListPage<T>(IReadOnlyList<T> Items, int Total, int Page, int Pages, int? Limit)
{
    /// <summary>Whether there is a page after this one.</summary>
    public bool More => Page < Pages;
}

/// <summary>Cuts a kept list into pages.</summary>
/// <remarks>
/// After the query, never before: a page of a list filtered afterwards would
/// hold fewer than it says, and the next page would skip what this one
/// dropped.
/// </remarks>
public static class ListPages
{
    /// <summary>
    /// The page asked for, or the whole list as one page when no limit is
    /// given, so a command that gains paging prints what it did for anyone
    /// who does not ask.
    /// </summary>
    /// <param name="items">The list, already filtered and sorted.</param>
    /// <param name="page">The page, counted from 1.</param>
    /// <param name="limit">How many a page holds, or null for no paging.</param>
    public static OperationResult<ListPage<T>> Of<T>(IReadOnlyList<T> items, int page, int? limit)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (limit is < 1)
        {
            return OperationResult<ListPage<T>>.Fail("--limit takes a number of rows a page holds, 1 or more.", ExitCode.InvalidArguments);
        }

        if (page < 1)
        {
            return OperationResult<ListPage<T>>.Fail("--page counts from 1.", ExitCode.InvalidArguments);
        }

        if (limit is not { } size)
        {
            return page == 1
                ? OperationResult<ListPage<T>>.Ok(new(items, items.Count, 1, 1, null))
                : OperationResult<ListPage<T>>.Fail("--page needs --limit, to say how many a page holds.", ExitCode.InvalidArguments);
        }

        var pages = Math.Max(1, (items.Count + size - 1) / size);

        // A page past the end is refused rather than returned empty: an empty
        // page reads as a list with nothing on it, and this one has something.
        if (page > pages)
        {
            return OperationResult<ListPage<T>>.Fail(
                $"There {(pages == 1 ? "is 1 page" : $"are {pages} pages")} of {size}, so there is no page {page}.",
                ExitCode.InvalidArguments);
        }

        return OperationResult<ListPage<T>>.Ok(
            new([.. items.Skip((page - 1) * size).Take(size)], items.Count, page, pages, size));
    }
}
