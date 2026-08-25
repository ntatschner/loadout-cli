using System.Collections.ObjectModel;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
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
    /// <summary>How often the reading indicator moves.</summary>
    private static readonly TimeSpan PulseInterval = TimeSpan.FromMilliseconds(90);

    private readonly IReadOnlyList<ProjectResolution> _projects;
    private readonly Func<ProjectResolution, CancellationToken, Task<ProjectOverview?>> _overview;
    private readonly IApplication _application;
    private readonly Action<LauncherWindow> _showPalette;
    private readonly IReadOnlyList<AgentSession> _recent;

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

    /// <summary>Recent conversations, when there are any to show.</summary>
    private readonly ListView? _recentList;

    private readonly FrameView? _recentFrame;

    /// <summary>Handle of the timer moving the reading indicator, when one is running.</summary>
    private object? _pulse;

    private int _pulseStep;

    /// <summary>What was chosen. Null until the screen is closed.</summary>
    internal LauncherIntent? Intent { get; private set; }

    internal LauncherWindow(
        IReadOnlyList<ProjectResolution> projects,
        ProjectResolution? here,
        string workspaceState,
        IReadOnlyList<string> agents,
        Func<ProjectResolution, CancellationToken, Task<ProjectOverview?>> overview,
        Action<LauncherWindow> showPalette,
        IReadOnlyList<AgentSession> recent,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(application);

        _projects = projects;
        _overview = overview;
        _recent = recent;
        _application = application;
        _showPalette = showPalette;
        _shown = [.. projects];

        Title = "Loadout";
        BorderStyle = LineStyle.Rounded;

        // Typed into rather than searched for. A list long enough to need
        // searching is a list where the search box should already be visible.
        _filter = new TextField
        {
            X = 9,
            Y = 1,
            Width = Dim.Fill(1),
        };

        var filterLabel = new Label { X = 1, Y = 1, Text = "Filter" };

        _list = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ShowMarks = false,
        };

        // Recent work gets a strip of the left column, under the projects.
        // "What was I doing?" is the question somebody opening a launcher most
        // often has, and the previous launcher could answer it before this one
        // was written — the rewrite dropped the session picker and no test
        // noticed, because nothing tested it.
        var recentHeight = _recent.Count == 0 ? 0 : Math.Min(_recent.Count + 2, 7);

        var listFrame = new FrameView
        {
            X = 0,
            Y = 3,
            Width = Dim.Percent(38),
            Height = Dim.Fill(2 + recentHeight),
            Title = "Projects",
            BorderStyle = LineStyle.Rounded,
        };

        listFrame.Add(_list);

        if (recentHeight > 0)
        {
            _recentList = new ListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };

            _recentList.SetSource(new ObservableCollection<string>(
                _recent.Select(session =>
                    $"{session.Agent} · {SessionDisplay.Ago(session.LastActive)} · {session.Label}")));

            // Selected up front. A list with nothing selected has nothing to
            // accept, so Enter on it did nothing at all — the same omission
            // that left the problems screen showing an empty preview.
            _recentList.SelectedItem = 0;

            _recentList.Accepted += (_, e) => { e.Handled = true; ResumeSelectedSession(); };

            var recentFrame = new FrameView
            {
                X = 0,
                Y = Pos.Bottom(listFrame),
                Width = Dim.Percent(38),
                Height = recentHeight,
                Title = "Recent",
                BorderStyle = LineStyle.Rounded,
            };

            recentFrame.Add(_recentList);

            _recentFrame = recentFrame;
        }

        _detail = new ProjectDetailView
        {
            X = Pos.Right(listFrame),
            Y = 3,
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
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

        // Added between the project list and the detail, so tabbing follows
        // the way the screen reads: down the left column, then across. Adding
        // it last put it behind every button in the detail pane, which is four
        // stops past where somebody would look for it.
        if (_recentFrame is not null)
        {
            Add(BuildMenu(), filterLabel, _filter, listFrame, _recentFrame, _detail, _summary);
        }
        else
        {
            Add(BuildMenu(), filterLabel, _filter, listFrame, _detail, _summary);
        }

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

        _detail.Problems += (_, _) => Close(new LauncherIntent(
            LauncherAction.Problems, Selected));

        Populate(here);

        this.Bind(Key.Q.WithCtrl, Command.Quit);
        AddCommand(Command.Quit, () => { Close(LauncherIntent.Quit); return true; });

        this.Bind(Key.N.WithCtrl, Command.New);
        AddCommand(Command.New, () =>
        {
            Close(new LauncherIntent(LauncherAction.AddProject));
            return true;
        });

        this.Bind(Key.P.WithCtrl, Command.Open);
        AddCommand(Command.Open, () => { _showPalette(this); return true; });
    }

    /// <summary>
    /// The menu. Everything reachable from the screen is in it, named, so that
    /// what the launcher can do is discoverable by looking rather than by
    /// already knowing which key to press.
    /// </summary>
    private MenuBar BuildMenu() =>
        new([
            new MenuBarItem("_Project", [
                new MenuItem { Title = "_Launch", Action = LaunchSelected },
                new MenuItem
                {
                    Title = "_Resume a session",
                    Action = () => WithSelected(p => Close(new LauncherIntent(LauncherAction.Resume, p))),
                },
                new MenuItem
                {
                    Title = "Open development _shell",
                    Action = () => WithSelected(p => Close(new LauncherIntent(LauncherAction.Shell, p))),
                },
                new MenuItem
                {
                    Title = "Open in _editor",
                    Action = () => WithSelected(p =>
                        RunCommand($"{LauncherCommands.Editor} {p.Entry.Slug}")),
                },
                new MenuItem
                {
                    Title = "Open in _file manager",
                    Action = () => WithSelected(p => Close(new LauncherIntent(LauncherAction.FileManager, p))),
                },
                new Line(),
                new MenuItem
                {
                    Title = "Review _problems…",
                    Action = () => WithSelected(p => Close(new LauncherIntent(LauncherAction.Problems, p))),
                },
                new MenuItem
                {
                    Title = "_Clone onto this machine",
                    Action = () => WithSelected(p => Close(new LauncherIntent(LauncherAction.Clone, p))),
                },
            ]),
            new MenuBarItem("_Registry", [
                new MenuItem
                {
                    Title = "_Add a project…",
                    Action = () => Close(new LauncherIntent(LauncherAction.AddProject)),
                },
            ]),
            new MenuBarItem("_Tools", [
                new MenuItem { Title = "All _commands…", Action = () => _showPalette(this) },
                new Line(),
                new MenuItem
                {
                    Title = "Check this _machine",
                    Action = () => Close(new LauncherIntent(LauncherAction.MachineCheck)),
                },
                new MenuItem
                {
                    Title = "Configuration _drift",
                    Action = () => Close(new LauncherIntent(LauncherAction.Drift)),
                },
                new MenuItem
                {
                    Title = "_Settings and paths",
                    Action = () => Close(new LauncherIntent(LauncherAction.Settings)),
                },
            ]),
            new MenuBarItem("_Help", [
                new MenuItem { Title = "_Keys", Action = ShowKeys },
            ]),
        ]);

    /// <summary>
    /// Runs an action against the selected project, and does nothing at all
    /// when there is not one. A menu entry that throws on an empty registry
    /// would be a worse answer than one that quietly declines.
    /// </summary>
    private void WithSelected(Action<ProjectResolution> action)
    {
        if (Selected is { } project)
        {
            action(project);
        }
    }

    private void ShowKeys() =>
        MessageBox.Query(
            _application,
            "Keys",
            string.Join(
                Environment.NewLine,
                "Enter      launch the selected project",
                "Ctrl+P     all commands",
                "Ctrl+Q     quit",
                "F9         menu",
                "Tab        move between the filter, the list and the buttons"),
            "_Close");

    /// <summary>
    /// Reopens the conversation under the cursor in the recent list.
    /// </summary>
    private void ResumeSelectedSession()
    {
        if (_recentList?.SelectedItem is not int index
            || index < 0
            || index >= _recent.Count)
        {
            return;
        }

        var session = _recent[index];

        // Carries the session, so this reopens the one that was chosen rather
        // than putting a picker up over a choice already made.
        Close(new LauncherIntent(
            LauncherAction.Resume,
            _projects.FirstOrDefault(p => p.Entry.Slug == session.ProjectSlug),
            SessionId: session.SessionId));
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

        return $"{projects}  ·  {workspace}  ·  {installed}"
            + "      Ctrl+P commands   Ctrl+N add   F9 menu   Ctrl+Q quit";
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
            // An empty registry is the state a new person is in, and a blank
            // screen with no way forward is the worst possible answer to it.
            // Telling somebody the command to type is not much better: the
            // launcher is already open and already knows where to look.
            _detail.ShowNothing(_projects.Count == 0
                ? "No projects are registered yet. Choose Registry ▸ Add a project, or press Ctrl+N."
                : "Nothing matches that filter.");
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

        StartPulsing();

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

                    StopPulsing();

                    _detail.Show(project, overview, failure);
                });
            },
            CancellationToken.None);
    }

    /// <summary>
    /// Moves a small bar while a project's details are read, so a slow
    /// repository looks like it is being worked on rather than like the
    /// launcher has stopped. Deliberately not a progress bar: nothing here
    /// knows how much of the read is left, and a bar that guesses is a lie.
    /// </summary>
    private void StartPulsing()
    {
        StopPulsing();

        _pulseStep = 0;

        _detail.ShowHeading(Selected!, Wordmark.Pulse(_pulseStep));

        _pulse = _application.AddTimeout(PulseInterval, () =>
        {
            if (Selected is null)
            {
                return false;
            }

            _detail.SetStatus(Wordmark.Pulse(++_pulseStep));

            return true;
        });
    }

    private void StopPulsing()
    {
        if (_pulse is not null)
        {
            _application.RemoveTimeout(_pulse);
            _pulse = null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopPulsing();

            _pending?.Cancel();
            _pending?.Dispose();
        }

        base.Dispose(disposing);
    }
}
