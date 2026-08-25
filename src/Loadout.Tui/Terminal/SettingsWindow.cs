using Loadout.Models.Configuration;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What the settings screen was asked to change.
/// <para>
/// Values rather than a mutated config, so nothing is written by the act of
/// looking. The caller compares these against what was loaded and does only
/// what actually differs.
/// </para>
/// </summary>
/// <param name="WorkspaceRemote">The central repository holding projects and instructions.</param>
/// <param name="WorkspaceBranch">Which branch of it to track.</param>
/// <param name="DefaultAgent">The agent used when a project does not name one.</param>
/// <param name="SyncAtLaunch">Whether the workspace is fetched before a launch.</param>
/// <param name="SyncAtExit">Whether work is pushed when a session ends.</param>
/// <param name="EditorCommand">The editor's command-line name.</param>
internal sealed record SettingsEdit(
    string WorkspaceRemote,
    string WorkspaceBranch,
    string DefaultAgent,
    string SyncAtLaunch,
    string SyncAtExit,
    string EditorCommand);

/// <summary>
/// The launcher's settings, and where everything lives.
/// <para>
/// A screen rather than a printed table. The old one could show the settings or
/// let you change one, never both at once, so changing two meant going round
/// twice and losing sight of the rest each time.
/// </para>
/// <para>
/// Nothing is saved from here. The screen hands back what was typed and closes,
/// and the caller writes it — which matters more than it sounds, because
/// changing the workspace repository has to move an existing clone aside first,
/// and that is not something to do while a screen is still drawing.
/// </para>
/// </summary>
internal sealed class SettingsWindow : Window
{
    private const int LabelWidth = 22;

    private readonly IApplication _application;

    private readonly TextField _remote;
    private readonly TextField _branch;
    private readonly TextField _agent;
    private readonly TextField _syncLaunch;
    private readonly TextField _syncExit;
    private readonly TextField _editor;

    /// <summary>What was typed, or null if the screen was dismissed.</summary>
    internal SettingsEdit? Edit { get; private set; }

    /// <param name="config">The settings as they stand.</param>
    /// <param name="places">Where things are kept, as label and path pairs.</param>
    /// <param name="agents">Installed agents, named so the field is not a guess.</param>
    /// <param name="application">The running application.</param>
    internal SettingsWindow(
        LauncherConfig config,
        IReadOnlyList<(string Label, string Value)> places,
        IReadOnlyList<string> agents,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = "Settings";
        BorderStyle = LineStyle.Rounded;

        var settings = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 10,
            Title = "Settings",
            BorderStyle = LineStyle.Single,
        };

        _remote = Field(settings, "Workspace repository", config.Workspace.Remote ?? string.Empty, 0);
        _branch = Field(settings, "Branch", config.Workspace.Branch, 1);
        _agent = Field(settings, "Default agent", config.DefaultAgent, 2);
        _syncLaunch = Field(settings, "Sync at launch", config.Sync.Launch, 3);
        _syncExit = Field(settings, "Sync at exit", config.Sync.Exit, 4);
        _editor = Field(settings, "Editor command", config.Editor.Command, 5);

        // Said rather than left to be discovered by typing something that is
        // not installed and finding out at launch.
        settings.Add(new Label
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill(1),
            Text = agents.Count == 0
                ? "No agents are installed on this machine."
                : $"Installed: {string.Join(", ", agents)}",
        });

        var whereFrame = new FrameView
        {
            X = 0,
            Y = Pos.Bottom(settings),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Title = "Where things are kept",
            BorderStyle = LineStyle.Single,
        };

        for (var i = 0; i < places.Count; i++)
        {
            whereFrame.Add(new Label { X = 1, Y = i, Text = places[i].Label });
            whereFrame.Add(new Label { X = 1 + LabelWidth, Y = i, Text = places[i].Value });
        }

        var save = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Save", IsDefault = true };
        var cancel = new Button { X = Pos.Right(save) + 2, Y = Pos.AnchorEnd(1), Text = "_Close" };

        save.Accepting += (_, e) =>
        {
            e.Handled = true;

            Edit = new SettingsEdit(
                (_remote.Text ?? string.Empty).Trim(),
                (_branch.Text ?? string.Empty).Trim(),
                (_agent.Text ?? string.Empty).Trim(),
                (_syncLaunch.Text ?? string.Empty).Trim(),
                (_syncExit.Text ?? string.Empty).Trim(),
                (_editor.Text ?? string.Empty).Trim());

            _application.RequestStop(this);
        };

        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(settings, whereFrame, save, cancel);
    }

    private static TextField Field(View parent, string label, string value, int row)
    {
        parent.Add(new Label { X = 1, Y = row, Text = label });

        var field = new TextField
        {
            X = 1 + LabelWidth,
            Y = row,
            Width = Dim.Fill(2),
            Text = value,
        };

        parent.Add(field);

        return field;
    }
}
