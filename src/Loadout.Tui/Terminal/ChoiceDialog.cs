using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// One question with a list of answers.
/// <para>
/// Cancelling is a real answer rather than a way of picking the first option by
/// accident: <see cref="ChosenIndex"/> stays null, and the caller is expected to
/// carry on with whatever it would have done had it never asked.
/// </para>
/// </summary>
internal sealed class ChoiceDialog : Window
{
    private readonly ListView _choices;
    private readonly IApplication _application;

    /// <summary>Which answer was picked, or null if the question was dismissed.</summary>
    internal int? ChosenIndex { get; private set; }

    internal ChoiceDialog(string question, IReadOnlyList<string> choices, IApplication application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = question;
        BorderStyle = LineStyle.Rounded;

        _choices = new ListView
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };

        _choices.SetSource(new ObservableCollection<string>(choices));

        if (choices.Count > 0)
        {
            _choices.SelectedItem = 0;
        }

        _choices.Accepted += (_, e) => { e.Handled = true; Accept(); };

        var choose = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Choose", IsDefault = true };
        var cancel = new Button { X = Pos.Right(choose) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        choose.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_choices, choose, cancel);
    }

    private void Accept()
    {
        ChosenIndex = _choices.SelectedItem;

        _application.RequestStop(this);
    }
}
