using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// Asks for the one argument a command cannot run without.
/// </summary>
/// <remarks>
/// <para>
/// The palette starts a command with nothing after it. For most that is right;
/// for the few that require an argument it meant choosing one and being told it
/// was missing, every time, with no way from that screen to supply it. An entry
/// that can only ever fail is the same fault as a palette that lists commands
/// and runs none — which this launcher shipped once already.
/// </para>
/// <para>
/// It collects and nothing more. What the value means is the command's
/// business, and it is handed to the same parser as if it had been typed.
/// </para>
/// </remarks>
internal sealed class CommandArgumentDialog : Window
{
    private readonly TextField _value;
    private readonly IApplication _application;

    /// <summary>What was typed, or null when the dialog was dismissed.</summary>
    internal string? Chosen { get; private set; }

    internal CommandArgumentDialog(
        string command,
        string argument,
        string? example,
        IApplication application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(argument);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = command;
        Width = Dim.Percent(70);
        Height = 9;
        BorderStyle = LineStyle.Rounded;

        Add(new Label { X = 1, Y = 0, Text = $"{command} needs a {argument.ToLowerInvariant()}." });

        _value = new TextField { X = 1, Y = 2, Width = Dim.Fill(1) };

        if (example is { Length: > 0 })
        {
            // Shown rather than filled in. Running somebody else's example by
            // accident is worse than typing four words.
            Add(new Label { X = 1, Y = 3, Text = $"for example: {command} {example}" });
        }

        var run = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Run", IsDefault = true };
        var cancel = new Button { X = Pos.Right(run) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        run.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };
        _value.Accepting += (_, e) => { e.Handled = true; Accept(); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_value, run, cancel);

        _value.SetFocus();
    }

    private void Accept()
    {
        var typed = _value.Text?.Trim();

        // Nothing to run without one. Closing on an empty value would put the
        // missing-argument error back on the screen, which is what this exists
        // to prevent.
        if (string.IsNullOrWhiteSpace(typed))
        {
            _value.SetFocus();

            return;
        }

        Chosen = typed;

        _application.RequestStop(this);
    }
}
