using System.Collections.ObjectModel;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// The launcher screen: the project list on the left, what is known about the
/// selected one on the right.
/// <para>
/// Knows nothing about how any of it is fetched. It is given the projects and
/// two callbacks, which is what lets it be tested against injected keystrokes
/// with no workspace, no git and no agents anywhere on the machine.
/// </para>
/// </summary>
internal sealed class LauncherWindow : Window
{
    /// <summary>Shown while an overview is still being read.</summary>
    private const string Loading = "reading…";

    private readonly IReadOnlyList<ProjectResolution> _projects;
    private readonly Func<ProjectResolution, CancellationToken, Task<ProjectOverview?>> _overview;
    private readonly IApplication _application;

    private readonly TextField _filter;
    private readonly ListView _list;
    private readonly ProjectDetailView _detail;
    private readonly Label _summary;

    /// <summary>Projects currently shown, after the filter has been applied.</summary>
    private List<ProjectResolution> _shown;

    /// <summary>
    /// Cancels the overview being read for a project that is no longer
    /// selected. Moving down a long list starts one read per row, and without
    /// this a slow one arriving late would overwrite the row now under the
    /// cursor with another project's details.
    /// </summary>
    private CancellationTokenSource? _pending;

    /// <summary>What was chosen. Null until the screen is closed.</summary>
    internal LauncherIntent? Intent { get; private set; }

    internal LauncherWindow(
        IReadOnlyList<ProjectResolution> projects,
        ProjectResolution? here,
        string workspaceState,
        IReadOnlyList<string> agents,
        Func<ProjectResolution, CancellationToken, Task<ProjectOverview?>> overview,
        Action<LauncherWindow> showPalette,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(application);

        _projects = projects;
        _overview = overview;
        _application = application;
        _shown = [.. projects];

        Title = "Loadout";
        BorderStyle = LineStyle.Rounded;

        // Typed into rather than searched for. A list long enough to need
        // searching is a list where the search box should already be visible.
        _filter = new TextField
        {
            X = 1,
            Y = 1,
            Width = Dim.Fill(1),
        };

        var filterLabel = new Label { X = 1, Y = 0, Text = "Filter" };

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = false,
        };

        var listFrame = new FrameView
        {
            X = 0,
            Y = 3,
            Width = Dim.Percent(38),
            Height = Dim.Fill(1),
            Title = "Projects",
            BorderStyle = LineStyle.Rounded,
        };

        listFrame.Add(_list);

        _detail = new ProjectDetailView
        {
            X = Pos.Right(listFrame),
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };

        // One line that says what state the machine is in, so it is answered
        // before it is asked rather than hidden behind a menu.
        _summary = new Label
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(1),
            Text = Describe(projects.Count, workspaceState, agents),
        };

        Add(filterLabel, _filter, listFrame, _detail, _summary);

        _filter.TextChanged += (_, _) => ApplyFilter();

        _list.ValueChanged += (_, _) => ShowSelected();

        // Enter on the list starts the usual thing for that project, which is
        // the reason somebody opened the launcher at all.
        _list.Accepted += (_, _) => LaunchSelected();

        _detail.Launch += (_, agent) => Close(new LauncherIntent(
            LauncherAction.Launch, Selected, agent));

        _detail.Resume += (_, _) => Close(new LauncherIntent(
            LauncherAction.Resume, Selected));

        _detail.Shell += (_, _) => Close(new LauncherIntent(
            LauncherAction.Shell, Selected));

        Populate(here);

        KeyBindings.Add(Key.Q.WithCtrl, Command.Quit);
        AddCommand(Command.Quit, () => { Close(LauncherIntent.Quit); return true; });

        KeyBindings.Add(Key.P.WithCtrl, Command.Open);
        AddCommand(Command.Open, () => { showPalette(this); return true; });
    }

    /// <summary>The project under the cursor, if the list is not empty.</summary>
    internal ProjectResolution? Selected =>
        _list.SelectedItem is int index && index >= 0 && index < _shown.Count
            ? _shown[index]
            : null;

    /// <summary>Closes the screen, recording what was asked for.</summary>
    internal void Close(LauncherIntent intent)
    {
        Intent = intent;
        _application.RequestStop(this);
    }

    /// <summary>
    /// Runs a command from the palette, which means leaving the screen: a
    /// command writes to the terminal the toolkit is drawing on.
    /// </summary>
    internal void RunCommand(string path) =>
        Close(new LauncherIntent(LauncherAction.Command, Selected, CommandPath: path));

    private static string Describe(int count, string workspace, IReadOnlyList<string> agents)
    {
        var projects = count == 1 ? "1 project" : $"{count} projects";

        var installed = agents.Count == 0
            ? "no agents installed"
            : string.Join(", ", agents);

        return $"{projects}  ·  {workspace}  ·  {installed}      Ctrl+P commands   Ctrl+Q quit";
    }

    /// <summary>
    /// Fills the list, putting the repository somebody is standing in first.
    /// It is almost always the one they meant, and hunting for it in a list
    /// ordered by something else is work the launcher can do instead.
    /// </summary>
    private void Populate(ProjectResolution? here)
    {
        if (here is not null)
        {
            _shown = [
                .. _projects.Where(p => p.Entry.Slug == here.Entry.Slug),
                .. _projects.Where(p => p.Entry.Slug != here.Entry.Slug),
            ];
        }

        Render(here);
    }

    private void Render(ProjectResolution? here)
    {
        var rows = new ObservableCollection<string>(
            _shown.Select(project => Row(project, here)));

        _list.SetSource(rows);

        if (_shown.Count > 0)
        {
            _list.SelectedItem = 0;
            ShowSelected();
        }
        else
        {
            _detail.ShowNothing();
        }
    }

    private static string Row(ProjectResolution project, ProjectResolution? here)
    {
        var marker = here is not null && project.Entry.Slug == here.Entry.Slug
            ? "▸"
            : project.Pinned ? "★" : " ";

        var suffix = project.IsAvailableLocally
            ? project.Entry.DefaultAgent
            : "not on this machine";

        return $"{marker} {project.Entry.Name}  ({suffix})";
    }

    private void ApplyFilter()
    {
        var text = _filter.Text?.Trim() ?? string.Empty;

        _shown = text.Length == 0
            ? [.. _projects]
            : [.. _projects.Where(p =>
                p.Entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Entry.Slug.Contains(text, StringComparison.OrdinalIgnoreCase))];

        Render(here: null);
    }

    private void LaunchSelected()
    {
        if (Selected is { IsAvailableLocally: true } project)
        {
            Close(new LauncherIntent(
                LauncherAction.Launch, project, project.Entry.DefaultAgent));
        }
    }

    /// <summary>
    /// Shows what is known about the selected project, reading the parts that
    /// need git and the filesystem off the main loop so a slow repository does
    /// not freeze the list somebody is moving through.
    /// </summary>
    private void ShowSelected()
    {
        var project = Selected;

        if (project is null)
        {
            _detail.ShowNothing();
            return;
        }

        _pending?.Cancel();
        _pending?.Dispose();
        _pending = new CancellationTokenSource();

        var token = _pending.Token;

        var reading = _overview(project, token);

        // Already known — a cached overview, or a source that had nothing to
        // wait for. Showing it now rather than after a trip through the main
        // loop avoids a "reading…" that appears and disappears in the same
        // frame, which reads as a flicker rather than as progress.
        if (reading.IsCompletedSuccessfully)
        {
            _detail.Show(project, reading.Result, failure: null);
            return;
        }

        _detail.ShowHeading(project, Loading);

        _ = Task.Run(
            async () =>
            {
                ProjectOverview? overview = null;
                string? failure = null;

                try
                {
                    overview = await reading.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Shown rather than swallowed. A project whose details
                    // cannot be read is worth saying so about; a launcher that
                    // silently shows nothing looks broken.
                    failure = ex.Message;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _application.Invoke(() =>
                {
                    if (token.IsCancellationRequested || !ReferenceEquals(project, Selected))
                    {
                        return;
                    }

                    _detail.Show(project, overview, failure);
                });
            },
            CancellationToken.None);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pending?.Cancel();
            _pending?.Dispose();
        }

        base.Dispose(disposing);
    }
}
