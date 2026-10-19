using System.Collections.ObjectModel;
using Loadout.Core.Lists;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// A list with the shared query over it: a filter field above, the rows, and
/// a line beneath saying what the filter is and how much it hides.
/// </summary>
/// <typeparam name="T">The item listed.</typeparam>
/// <remarks>
/// <para>
/// Filtered and sorted by the kind's <see cref="ListFields{T}"/>, the code
/// Studio and 'task list --filter' use, so the terminal cannot come to a
/// different list from the same query or refuse it in different words.
/// </para>
/// <para>
/// '/' moves to the field from the list, Enter there applies the query and
/// goes back, and Esc goes back without applying. The window owning the list
/// binds its own keys on <see cref="List"/>, as every screen here does.
/// </para>
/// </remarks>
internal sealed class FilterableListView<T> : View
{
    private readonly ListFields<T> _fields;
    private readonly Func<T, string> _row;
    private readonly TimeProvider _time;
    private readonly TextField _filter;
    private readonly Label _status;
    private IReadOnlyList<T> _items = [];
    private IReadOnlyList<T> _shown = [];
    private string? _problem;

    /// <param name="fields">What the items can be filtered and sorted on.</param>
    /// <param name="row">How one item is written as a row.</param>
    /// <param name="query">The query it opens with: a saved one, or the kind's default.</param>
    /// <param name="time">The clock ages are measured against.</param>
    internal FilterableListView(ListFields<T> fields, Func<T, string> row, string query, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(row);

        _fields = fields;
        _row = row;
        _time = time ?? TimeProvider.System;
        Query = query ?? string.Empty;
        CanFocus = true;

        _filter = new TextField { Id = "list-filter", X = 0, Y = 0, Width = Dim.Fill(), Text = Query };
        List = new KeyedListView { Id = "list-rows", X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(1) };
        _status = new Label { Id = "list-status", X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill() };

        Add(_filter, List, _status);

        List.OnKey(new Key('/'), Command.New, () => { _filter.SetFocus(); return true; });

        _filter.Accepting += (_, e) =>
        {
            e.Handled = true;
            Apply(_filter.Text ?? string.Empty);
            List.SetFocus();
        };

        _filter.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                key.Handled = true;
                _filter.Text = Query;
                List.SetFocus();
            }
        };
    }

    /// <summary>The rows, where the owner binds its keys.</summary>
    internal KeyedListView List { get; }

    /// <summary>The query in force: the last one applied that the kind accepted.</summary>
    internal string Query { get; private set; }

    /// <summary>What the query kept, in its order.</summary>
    internal IReadOnlyList<T> Shown => _shown;

    /// <summary>The item the cursor is on, or the default when there is none.</summary>
    internal T? Selected =>
        List.SelectedItem is int at && at >= 0 && at < _shown.Count ? _shown[at] : default;

    /// <summary>What was refused about the last query typed, or null.</summary>
    internal string? Problem => _problem;

    /// <summary>Lists these, through the query in force.</summary>
    internal void SetItems(IReadOnlyList<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _items = items;
        Apply(Query);
    }

    /// <summary>
    /// Applies a query. One the kind refuses leaves the list as it was, with
    /// the refusal on the status line in the words every surface uses.
    /// </summary>
    internal void Apply(string query)
    {
        var parsed = ListQuery.Parse(query);
        var applied = parsed.Succeeded ? _fields.Apply(parsed.Value!, _items, _time.GetUtcNow()) : null;

        if (parsed.Failed || applied!.Failed)
        {
            _problem = parsed.Failed ? parsed.Error : applied!.Error;
            ShowStatus();

            return;
        }

        _problem = null;
        Query = parsed.Value!.ToString();
        _filter.Text = Query;

        var keep = Selected;
        _shown = applied.Value!;

        List.SetSource(new ObservableCollection<string>(_shown.Count > 0
            ? _shown.Select(_row)
            : [_items.Count > 0 ? "Nothing matches. Press a to show everything." : "Nothing recorded yet."]));

        var again = keep is null ? -1 : _shown.ToList().IndexOf(keep);
        List.SelectedItem = _shown.Count > 0 ? Math.Max(0, again) : null;

        ShowStatus();
    }

    /// <summary>The query with its sort replaced by this one, or none for "".</summary>
    internal void SortBy(string sort)
    {
        var parsed = ListQuery.Parse(Query);

        if (parsed.Failed)
        {
            return;
        }

        var current = parsed.Value!;
        var sorts = sort.Length == 0
            ? []
            : new[] { new ListSort(sort.TrimStart('-'), sort.StartsWith('-')) };

        Apply(new ListQuery(current.Terms, current.Words, sorts).ToString());
    }

    /// <summary>The sort the query names, as written after 'sort:', or "".</summary>
    internal string Sort =>
        ListQuery.Parse(Query) is { Succeeded: true, Value: { Sorts.Count: > 0 } query }
            ? string.Join(',', query.Sorts.Select(s => (s.Descending ? "-" : string.Empty) + s.Field))
            : string.Empty;

    private void ShowStatus()
    {
        _status.Text = _problem is not null
            ? _problem
            : $"{(Query.Length > 0 ? Query : "everything")} - showing {_shown.Count} of {_items.Count}";
    }
}
