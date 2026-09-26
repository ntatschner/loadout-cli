using System.Collections.ObjectModel;
using System.Globalization;
using Loadout.Models.Ideas;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>What was pasted into the notes box, and what to do with it.</summary>
/// <param name="Text">The notes, exactly as pasted.</param>
/// <param name="OnProject">Keep them on the selected project's list rather than the workspace-wide one.</param>
/// <param name="Split">Ask an agent to split them now.</param>
internal sealed record DumpNotes(string Text, bool OnProject, bool Split)
{
    /// <summary>
    /// The command that keeps them, as its arguments. An argument list rather
    /// than a command line, because notes are prose with spaces and quotes in
    /// them and the catalogue splits a command line on spaces.
    /// </summary>
    internal IReadOnlyList<string> Arguments(string? project) =>
    [
        "--text", Text,
        .. OnProject && project is { Length: > 0 } ? (string[])["--project", project] : ["--global"],
        .. Split ? [] : (string[])["--no-split"],
    ];
}

/// <summary>
/// A box to paste notes kept elsewhere into, to be split by an agent into
/// ideas and tasks.
/// </summary>
/// <remarks>
/// It collects and nothing more: the notes are handed to <c>idea dump add</c>
/// through the same parser as a typed command.
/// </remarks>
internal sealed class DumpNotesDialog : Window
{
    // TextView is marked obsolete in favour of an editor in a separate package.
    // It still does what a paste box needs, and a dependency for one text box
    // is not worth taking.
#pragma warning disable CS0618
    private readonly TextView _notes;
#pragma warning restore CS0618
    private readonly CheckBox? _onProject;
    private readonly IApplication _application;

    /// <summary>What to do, or null when the dialog was dismissed.</summary>
    internal DumpNotes? Chosen { get; private set; }

    internal DumpNotesDialog(string? project, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = "Dump notes";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);
        BorderStyle = LauncherTheme.Lines;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Text = "Paste notes kept elsewhere. They are kept word for word, and an agent splits them into ideas and tasks for you to pick from.",
            Width = Dim.Fill(1),
        });

#pragma warning disable CS0618
        _notes = new TextView { X = 1, Y = 2, Width = Dim.Fill(1), Height = Dim.Fill(4), WordWrap = true };
#pragma warning restore CS0618
        Add(_notes);

        if (project is { Length: > 0 })
        {
            _onProject = new CheckBox
            {
                X = 1,
                Y = Pos.AnchorEnd(3),
                Text = $"Keep them on _{project}, rather than the workspace-wide list",
                Value = CheckState.Checked,
            };

            Add(_onProject);
        }

        var split = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Split them", IsDefault = true };
        var keep = new Button { X = Pos.Right(split) + 2, Y = Pos.AnchorEnd(1), Text = "_Keep without splitting" };
        var cancel = new Button { X = Pos.Right(keep) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        split.Accepting += (_, e) => { e.Handled = true; Accept(split: true); };
        keep.Accepting += (_, e) => { e.Handled = true; Accept(split: false); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(split, keep, cancel);

        _notes.SetFocus();
    }

    private void Accept(bool split)
    {
        var text = _notes.Text ?? string.Empty;

        // Nothing to keep. Closing on an empty box would run the command only
        // to have it say there was nothing in it.
        if (string.IsNullOrWhiteSpace(text))
        {
            _notes.SetFocus();

            return;
        }

        Chosen = new DumpNotes(text, _onProject?.Value == CheckState.Checked, split);

        _application.RequestStop(this);
    }
}

/// <summary>
/// The pieces an agent split some notes into, every one ticked, for the person
/// to untick what they do not want before they are recorded.
/// </summary>
internal sealed class DumpPiecesDialog : Window
{
    private readonly ListView _pieces;
    private readonly IReadOnlyList<DumpItem> _items;
    private readonly IApplication _application;

    /// <summary>The pieces to record, by number, or null when the dialog was dismissed.</summary>
    internal IReadOnlyList<int>? Chosen { get; private set; }

    internal DumpPiecesDialog(string dump, IReadOnlyList<DumpItem> items, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(application);

        _items = items;
        _application = application;

        Title = $"Record pieces of {dump}";
        Width = Dim.Percent(80);
        Height = Dim.Percent(70);
        BorderStyle = LauncherTheme.Lines;

        Add(new Label
        {
            X = 1,
            Y = 0,
            Text = "Space ticks and unticks. An idea is kept to be fleshed out; a task is ready to work on.",
            Width = Dim.Fill(1),
        });

        _pieces = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            ShowMarks = true,
            MarkMultiple = true,
        };

        _pieces.SetSource(new ObservableCollection<string>(items.Select(Line)));

        // Every piece ticked to begin with: the agent was asked to split out
        // only what is an idea or a task, so leaving one out is the choice
        // somebody makes, not the one they have to remember to undo.
        _pieces.MarkAll(true);

        var record = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Record ticked", IsDefault = true };
        var cancel = new Button { X = Pos.Right(record) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        record.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_pieces, record, cancel);

        _pieces.SetFocus();
    }

    /// <summary>How a piece reads in the list.</summary>
    internal static string Line(DumpItem item) =>
        FormattableString.Invariant($"{item.Number,2}  {(item.Kind == DumpItemKind.Idea ? "idea" : "task")}  {item.Title}")
            + (item.Project.Length > 0 ? $"  (for {item.Project})" : string.Empty);

    /// <summary>
    /// The command that records them, as its arguments. Every piece ticked is
    /// said as all of them, so a later split of the same dump is not narrowed
    /// by a list written for an earlier one.
    /// </summary>
    internal static IReadOnlyList<string> Arguments(string dump, string? project, IReadOnlyList<int> numbers, int offered) =>
    [
        dump,
        .. project is { Length: > 0 } ? (string[])["--project", project] : ["--global"],
        .. numbers.Count == offered
            ? []
            : (string[])["--only", string.Join(',', numbers.Select(n => n.ToString(CultureInfo.InvariantCulture)))],
    ];

    private void Accept()
    {
        var ticked = Enumerable.Range(0, _items.Count)
            .Where(i => _pieces.Source!.IsMarked(i))
            .Select(i => _items[i].Number)
            .ToList();

        // Nothing ticked is nothing to record, and closing would read as done.
        if (ticked.Count == 0)
        {
            _pieces.SetFocus();

            return;
        }

        Chosen = ticked;

        _application.RequestStop(this);
    }
}
