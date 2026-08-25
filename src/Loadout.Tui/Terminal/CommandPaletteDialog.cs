using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// Everything the command line can do, filtered as you type.
/// <para>
/// The launcher used to offer about a fifth of the command line, and the rest
/// was reachable only by quitting and typing it. The catalogue is built while
/// the commands are registered, so a command added tomorrow appears here
/// without anybody remembering to add it.
/// </para>
/// <para>
/// Commands that cannot run from a menu are listed with the reason rather than
/// hidden. Something a person cannot find is indistinguishable from something
/// that does not exist, and "why is this not here" is a worse question to leave
/// somebody with than "why can this not run here".
/// </para>
/// </summary>
internal sealed class CommandPaletteDialog : Dialog
{
    private readonly List<CatalogueEntry> _all;
    private readonly ListView _list;
    private readonly Label _explanation;
    private readonly IApplication _application;

    private List<CatalogueEntry> _shown;

    /// <summary>The command to run, or null if nothing was chosen.</summary>
    internal string? Chosen { get; private set; }

    internal CommandPaletteDialog(IReadOnlyList<CatalogueEntry> commands, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        _all = [.. commands
            .OrderBy(entry => entry.Group, StringComparer.Ordinal)
            .ThenBy(entry => entry.Path, StringComparer.Ordinal)];

        _shown = [.. _all];

        Title = "Commands";
        Width = Dim.Percent(80);
        Height = Dim.Percent(80);
        BorderStyle = LineStyle.Rounded;

        var filter = new TextField { X = 1, Y = 0, Width = Dim.Fill(1) };

        _list = new ListView
        {
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };

        // Says what the highlighted command does, and — for the ones that
        // cannot run here — why not, before somebody presses Enter on it.
        _explanation = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        filter.TextChanged += (_, _) =>
        {
            var text = filter.Text?.Trim() ?? string.Empty;

            _shown = text.Length == 0
                ? [.. _all]
                : [.. _all.Where(entry =>
                    entry.Path.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || entry.Description.Contains(text, StringComparison.OrdinalIgnoreCase))];

            Render();
        };

        _list.ValueChanged += (_, _) => Explain();

        _list.Accepted += (_, e) =>
        {
            e.Handled = true;

            // A command that needs a terminal is left showing its reason
            // rather than run into a screen that would paint over it.
            if (Current() is { Runnable: true } entry)
            {
                Chosen = entry.Path;
                _application.RequestStop(this);
            }
        };

        var close = new Button { Text = "_Close", X = Pos.AnchorEnd(11), Y = Pos.AnchorEnd(1) };

        close.Accepting += (_, e) =>
        {
            e.Handled = true;
            _application.RequestStop(this);
        };

        KeyBindings.Add(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(filter, _list, _explanation, close);

        Render();
    }

    private CatalogueEntry? Current() =>
        _list.SelectedItem is int index && index >= 0 && index < _shown.Count
            ? _shown[index]
            : null;

    private void Render()
    {
        _list.SetSource(new ObservableCollection<string>(_shown.Select(Describe)));

        if (_shown.Count > 0)
        {
            _list.SelectedItem = 0;
        }

        Explain();
    }

    private void Explain()
    {
        var entry = Current();

        _explanation.Text = entry is null
            ? string.Empty
            : entry.TerminalOnly is { Length: > 0 } reason
                ? $"Runs in a terminal only: {reason}"
                : entry.Description;
    }

    private static string Describe(CatalogueEntry entry) =>
        entry.TerminalOnly is { Length: > 0 }
            ? $"  {entry.Path}   (terminal only)"
            : $"  {entry.Path}";
}
