using System.Collections.ObjectModel;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>What a launch was asked to do, beyond which project and which agent.</summary>
/// <param name="Task">
/// What the session is for, in the words somebody would use. This is what
/// chooses specialists, so it is the field that changes what the agent knows.
/// </param>
/// <param name="Mode">How to work: advise, implement, investigate or review.</param>
/// <param name="Offline">Do not reach the network.</param>
/// <param name="NoSync">Do not synchronise the workspace first.</param>
internal sealed record LaunchOptions(
    string? Task = null,
    string? Mode = null,
    bool Offline = false,
    bool NoSync = false);

/// <summary>
/// The options a launch can carry, asked before starting one.
/// </summary>
/// <remarks>
/// <para>
/// A launch request has fourteen fields and the launcher filled three. Task and
/// mode are the two that matter most and neither could be reached from a
/// screen: they are what selects the specialists an agent is given, so the
/// launcher could start a session but never say what the session was for. The
/// command line could, which made the screen a strict subset of it — the one
/// thing the palette exists to prevent.
/// </para>
/// <para>
/// Not on the way to every launch. Pressing Enter on a project still starts one
/// immediately, because that is the common case and a dialog in front of it
/// would be a toll paid on every session to serve the rarer one.
/// </para>
/// </remarks>
internal sealed class LaunchOptionsDialog : Window
{
    /// <summary>
    /// The modes a task can be worked in, and no mode at all.
    /// </summary>
    /// <remarks>
    /// Named here and defined in the specialist library, so a test holds the
    /// two together. A mode offered on a screen that no specialist answers to
    /// would be a choice that silently does nothing, which is the shape of
    /// several faults this launcher has already had.
    /// </remarks>
    internal static readonly string[] Modes =
        ["(let the task decide)", "advise", "implement", "investigate", "review"];

    private readonly TextField _task;
    private readonly ListView _mode;
    private readonly CheckBox _offline;
    private readonly CheckBox _noSync;
    private readonly IApplication _application;

    /// <summary>What was chosen, or null when the dialog was dismissed.</summary>
    internal LaunchOptions? Chosen { get; private set; }

    internal LaunchOptionsDialog(string projectName, IApplication application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = $"Launch {projectName}";
        Width = Dim.Percent(70);
        Height = 16;
        BorderStyle = LineStyle.Rounded;

        Add(new Label { X = 1, Y = 0, Text = "What are you about to do?" });

        _task = new TextField { X = 1, Y = 1, Width = Dim.Fill(1) };

        Add(new Label
        {
            X = 1,
            Y = 2,
            Text = "This chooses the specialists the agent is given.",
        });

        Add(new Label { X = 1, Y = 4, Text = "How to work" });

        _mode = new ListView { X = 1, Y = 5, Width = Dim.Fill(1), Height = 5 };
        _mode.SetSource(new ObservableCollection<string>(Modes));
        _mode.SelectedItem = 0;

        _offline = new CheckBox { X = 1, Y = 11, Text = "Work _offline" };
        _noSync = new CheckBox { X = 1, Y = 12, Text = "Skip workspace _sync" };

        var launch = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Launch", IsDefault = true };
        var cancel = new Button { X = Pos.Right(launch) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        launch.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        // Enter in the text field starts the launch rather than doing nothing,
        // because somebody who has just typed what they are about to do has
        // finished answering the only question that needed them.
        _task.Accepting += (_, e) => { e.Handled = true; Accept(); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_task, _mode, _offline, _noSync, launch, cancel);

        _task.SetFocus();
    }

    private void Accept()
    {
        var typed = _task.Text?.Trim();

        // Index zero is "let the task decide", which means no mode rather than
        // a mode called that.
        var mode = _mode.SelectedItem is int index && index > 0 && index < Modes.Length
            ? Modes[index]
            : null;

        Chosen = new LaunchOptions(
            string.IsNullOrWhiteSpace(typed) ? null : typed,
            mode,
            _offline.Value == CheckState.Checked,
            _noSync.Value == CheckState.Checked);

        _application.RequestStop(this);
    }
}
