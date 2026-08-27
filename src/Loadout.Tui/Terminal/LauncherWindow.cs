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
    private readonly IReadOnlyList<string> _agents;

    private readonly TextField _filter;
    private readonly KeyedListView _list;
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
    private readonly KeyedListView? _recentList;

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
        _here = here;
        _overview = overview;
        _recent = recent;
        _application = application;
        _showPalette = showPalette;
        _agents = agents;
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

        _list = new KeyedListView
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

        // Thirty-eight per cent left the detail pane two thirds empty while
        // the list beside it was cutting names in half. The detail pane holds
        // five short labelled lines and a row of buttons; it does not need
        // most of the screen, and the list does.
        var columnWidth = Dim.Percent(46);

        var listFrame = new FrameView
        {
            X = 0,
            Y = 3,
            Width = columnWidth,
            Height = Dim.Fill(2 + recentHeight),
            Title = "Projects",
            BorderStyle = LineStyle.Rounded,
        };

        listFrame.Add(_list);

        if (recentHeight > 0)
        {
            _recentList = new KeyedListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };

            _recentLabels = [.. _recent.Select(session =>
                $"{session.Agent} · {SessionDisplay.Ago(session.LastActive)} · {session.Label}")];

            _recentList.SetSource(new ObservableCollection<string>(_recentLabels));

            // Selected when it is reached, not before. A list with nothing
            // selected has nothing to accept, so Enter on it did nothing at
            // all — but selecting up front drew a highlighted row in Recent
            // while the highlighted row in Projects was the one with the
            // focus, so two rows on the screen looked chosen at once and
            // neither the keyboard nor the eye agreed on which. Enter still
            // works, because reaching it is what focusing it means.
            _recentList.HasFocusChanged += (_, e) =>
            {
                if (e.NewValue && _recentList.SelectedItem is null)
                {
                    _recentList.SelectedItem = 0;
                }
            };

            _recentList.Accepted += (_, e) => { e.Handled = true; ResumeSelectedSession(); };

            var recentFrame = new FrameView
            {
                X = 0,
                Y = Pos.Bottom(listFrame),
                Width = columnWidth,
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

        // Kept, so that anything said along the bottom can be taken back when
        // the cursor moves off whatever it was about.
        _state = _summary.Text;

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

        // A row cannot be laid out against a width the list does not have
        // yet, and the width changes again whenever the window is resized.
        // Both lists are therefore refitted whenever the layout settles.
        for (var i = 0; i < KeyList.Length; i++)
        {
            _keys.Add(new Label { X = 1, Y = i, Text = KeyList[i] });
        }

        Add(_keys);

        SubViewsLaidOut += (_, _) => FitToLayout();

        _filter.TextChanged += (_, _) => ApplyFilter();

        _list.ValueChanged += (_, _) =>
        {
            // Ignored while the rows are being redrawn. Redrawing sets the
            // source and restores the cursor, and both raise this — so reading
            // a project's details, which redraws the rows to show its
            // readiness, asked for those details again. Each turn restarted the
            // read and put the reading indicator back, so the branch line
            // pulsed for ever and the answer never settled: seventeen reads of
            // one project in a second and a half, growing.
            if (_refreshing)
            {
                return;
            }

            ShowSelected();
        };

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

        Populate();

        this.Bind(Key.Q.WithCtrl, Command.Quit);
        AddCommand(Command.Quit, () => { Close(LauncherIntent.Quit); return true; });

        this.Bind(Key.N.WithCtrl, Command.New);
        AddCommand(Command.New, () =>
        {
            Close(new LauncherIntent(LauncherAction.AddProject));
            return true;
        });

        this.Bind(Key.P.WithCtrl, Command.Open);

        BindTheKeyboard();
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
                new MenuItem
                {
                    Title = "Token _usage for this project",
                    Action = () => WithSelected(p =>
                        RunCommand($"{LauncherCommands.Usage} --project {p.Entry.Slug} --by day")),
                },
                new MenuItem
                {
                    Title = "Explain _instructions",
                    Action = () => WithSelected(p =>
                        RunCommand($"{LauncherCommands.Instructions} --project {p.Entry.Slug}")),
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
                    Title = "Token _usage",
                    Action = () => RunCommand($"{LauncherCommands.Usage} --days 30"),
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

    /// <summary>
    /// The keys somebody would try, bound to what they would expect.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole explicit key surface was Esc, Ctrl+Q, Ctrl+N and Ctrl+P.
    /// Everything else — reaching the list, opening settings, finding out what
    /// any of the keys were — went through a menu bar behind F9, which is the
    /// least discoverable thing on the screen and the one thing a person
    /// coming from any other terminal application will not think to press.
    /// </para>
    /// <para>
    /// Scope is the whole design here. <c>j</c> and <c>k</c> belong to the
    /// list and nowhere else, because a filter that cannot spell "jamboree" is
    /// a worse tool than one that costs a keystroke. <c>/</c> and <c>?</c> are
    /// the same: bound where letters are not being typed.
    /// </para>
    /// </remarks>
    private void BindTheKeyboard()
    {
        // Settings, from anywhere. Ctrl+comma is what an editor uses and it
        // was the first choice; it works under the ANSI driver the tests run
        // on and does nothing whatever in a real Windows console, where
        // Ctrl+Q and the arrows and F10 all work. A key that passes its test
        // and fails on the machine is worse than no key, so this is F2, which
        // was pressed in a real console before being written down.
        this.Bind(Key.F2, Command.Edit);

        AddCommand(Command.Edit, () =>
        {
            Close(new LauncherIntent(LauncherAction.Settings));
            return true;
        });

        // j and k are bound on the lists themselves, to commands a list
        // already implements. Nothing custom is needed: Down is Down.
        //
        // KeyDown looked like the seam and is not one — the event does not
        // fire for a focused ListView — and AddCommand is protected, so a
        // view's commands cannot be added from outside it. Binding an existing
        // command is the mechanism the toolkit actually intends.
        // Bound on the lists rather than the window, and that is the whole
        // design. A window's own bindings are not consulted while a child has
        // the focus, so these would never fire there; on the lists they fire
        // exactly where they should, and the filter keeps every character it
        // is given. A project can still be searched for by a name with a
        // slash or a question mark in it.
        foreach (var list in new[] { _list, _recentList }.OfType<KeyedListView>())
        {
            list.Bind(Key.J, Command.Down);
            list.Bind(Key.K, Command.Up);

            list.OnKey(new Key('?'), Command.Context, () => { ShowKeys(); return true; });
        }

        // An arrow next to a filtered list means "go down the list" in every
        // tool that has one. Here it meant nothing, and reaching the list took
        // a Tab that nobody thinks to press.
        _filter.KeyDown += (_, key) =>
        {
            if (key == Key.CursorDown)
            {
                MoveInto(_list, by: 1);
                key.Handled = true;
            }
            else if (key == Key.Enter)
            {
                // Reported from use: opening a project did not work and had to
                // be tried again. Enter was handled by the list alone, and the
                // cursor starts in the filter — so narrowing the list and
                // pressing Enter, which is the shortest path anybody would
                // take, did nothing at all. Going back and arrowing into the
                // list first worked, which is what made it look intermittent
                // rather than simply missing.
                LaunchSelected();
                key.Handled = true;
            }
        };

    }

    /// <summary>Moves the focus onto a list and the cursor with it.</summary>
    private void MoveInto(ListView list, int by)
    {
        FocusOn(list);

        Step(list, by);
    }

    /// <summary>
    /// Puts the focus on a view, the way pressing Tab would.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Forward only, and that is a limitation rather than a choice. There is
    /// no way to ask for a particular view: ApplicationNavigation offers
    /// AdvanceFocus and GetFocused and nothing else, and SetFocus moves what
    /// is reported without moving where keys are routed — after it, an arrow
    /// pressed at the project list moved nothing and the keystroke reached
    /// neither view.
    /// </para>
    /// <para>
    /// This is why there is no <c>/</c> key for returning to the filter. The
    /// filter sits before the list in the tab order, advancing forty times
    /// does not wrap round to it, and advancing backwards does not arrive
    /// either. A key listed in the help that silently does nothing is worse
    /// than no key at all, so it is not listed and not bound. Shift+Tab
    /// already goes back.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <c>SetFocus</c> is the obvious call and it is not enough. It moves what
    /// the application reports as focused, and keys carry on being routed as
    /// though nothing had changed: after it, an arrow pressed at the project
    /// list moved nothing, and the keystroke went nowhere at all rather than
    /// to either view. Advancing focus is what the real Tab handler does, and
    /// keys follow it.
    /// </para>
    /// <para>
    /// Advanced until the view has it, rather than once, so that the tab order
    /// can change without silently landing this on the wrong view. The bound
    /// is there because a screen with nothing focusable would otherwise spin.
    /// </para>
    /// </remarks>
    private void FocusOn(View view)
    {
        // Both ways round. The filter sits before the list in the tab order,
        // so going only forward to reach it means walking past every button
        // in the detail pane and hoping the order wraps — which it need not.
        for (var guard = 0; guard < 40 && !view.HasFocus; guard++)
        {
            _application.Navigation?.AdvanceFocus(NavigationDirection.Forward, behavior: null);
        }
    }

    /// <summary>Moves a list's cursor, stopping at either end.</summary>
    private static void Step(ListView list, int by)
    {
        if (list.Source is null || list.Source.Count == 0)
        {
            return;
        }

        var at = list.SelectedItem ?? 0;

        list.SelectedItem = Math.Clamp(at + by, 0, list.Source.Count - 1);
    }

    /// <summary>Shows or hides the key list.</summary>
    /// <remarks>
    /// <para>
    /// A panel rather than a message box. MessageBox.Query runs a nested loop
    /// and blocks until somebody dismisses it, which is right for a question
    /// and wrong for a reference: a test that pressed ? never came back, and
    /// what hangs a test hangs whatever else is driving the screen.
    /// </para>
    /// <para>
    /// It also reads better. Help you can leave open while you try the key it
    /// just told you about is help; help that blocks the screen it describes
    /// is a quiz.
    /// </para>
    /// </remarks>
    private void ShowKeys()
    {
        _keys.Visible = !_keys.Visible;

        // Added last and left there. Subviews draw in order, so the panel is
        // already on top; moving it to the start would draw it first, which
        // is to say underneath the screen it is meant to cover.
        SetNeedsDraw();
    }

    /// <summary>The key list, shown over the screen it describes.</summary>
    private readonly FrameView _keys = new()
    {
        X = Pos.Center(),
        Y = Pos.Center(),
        Width = 58,
        Height = 15,
        Title = "Keys",
        BorderStyle = LineStyle.Rounded,
        Visible = false,
    };

    /// <summary>
    /// The keys, named from the toolkit where the toolkit owns them.
    /// </summary>
    /// <remarks>
    /// The menu key was written here as F9 and F9 does nothing: Terminal.Gui
    /// used it in version 1 and the default is F10 in version 2. Both the
    /// status line and this list advertised a key that had not worked for as
    /// long as the launcher had been on version 2, and nothing noticed,
    /// because a string in a status bar agrees with a string in a help panel
    /// no matter how wrong they both are. Asking the menu bar what its key is
    /// cannot drift.
    /// </remarks>
    private static readonly string[] KeyList =
    [
        "Enter      launch the selected project",
        "j / k      move down and up the list, as the arrows do",
        "?          show or hide this list",
        "Ctrl+P     all commands",
        "Ctrl+N     add a project",
        "F2         settings and paths",
        "Ctrl+Q     quit",
        MenuBar.DefaultKey + "        menu",
        "Tab        the filter, the list, the buttons",
    ];

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
            + $"      Ctrl+P commands   Ctrl+N add   {MenuBar.DefaultKey} menu   Ctrl+Q quit";
    }

    /// <summary>
    /// Fills the list, putting the repository somebody is standing in first.
    /// It is almost always the one they meant, and hunting for it in a list
    /// ordered by something else is work the launcher can do instead.
    /// </summary>
    private void Populate()
    {
        if (_here is not null)
        {
            _shown = [
                .. _projects.Where(p => p.Entry.Slug == _here.Entry.Slug),
                .. _projects.Where(p => p.Entry.Slug != _here.Entry.Slug),
            ];
        }

        Render();
    }

    /// <summary>
    /// The width a row has to fill, or zero before the list has been laid out.
    /// </summary>
    private int RowWidth => _list.Viewport.Width;

    /// <summary>The width the rows were last built for.</summary>
    private int _fittedTo;

    /// <summary>Recent sessions in full, before being cut to the column.</summary>
    private IReadOnlyList<string> _recentLabels = [];

    /// <summary>
    /// Rebuilds both lists for the width they now have.
    /// </summary>
    private void FitToLayout()
    {
        if (RowWidth == _fittedTo)
        {
            return;
        }

        _fittedTo = RowWidth;

        RefreshRows();
        FitRecent();
    }

    private void FitRecent()
    {
        if (_recentList is null)
        {
            return;
        }

        var selected = _recentList.SelectedItem;

        _recentList.SetSource(new ObservableCollection<string>(
            _recentLabels.Select(label => Shorten(label, _recentList.Viewport.Width))));

        _recentList.SelectedItem = selected;
    }

    private void Render()
    {
        var rows = new ObservableCollection<string>(
            _shown.Select(project => Row(project, RowWidth)));

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

    /// <summary>
    /// The repository somebody is standing in, if it is one of theirs.
    /// </summary>
    /// <remarks>
    /// Held rather than passed, because it is needed every time the rows are
    /// redrawn and redrawing happens for reasons that have nothing to do with
    /// it. Passing it once meant the marker was drawn at startup and lost the
    /// instant the first overview arrived, which is to say it was never seen.
    /// </remarks>
    private readonly ProjectResolution? _here;

    private string Row(ProjectResolution project, int width)
    {
        var marker = _here is not null && project.Entry.Slug == _here.Entry.Slug
            ? "▸"
            : project.Pinned ? "★" : " ";

        // Readiness in words as well as a mark, never colour alone: a
        // monochrome terminal, and somebody who cannot tell red from green,
        // must read the same thing everybody else does.
        var readiness = _readiness.TryGetValue(project.Entry.Slug, out var known)
            ? known
            : ProjectReadinessRules.Provisional(
                project.IsAvailableLocally,
                _agents.Count == 0
                    || _agents.Any(agent => agent.Contains(
                        project.Entry.DefaultAgent, StringComparison.OrdinalIgnoreCase)));

        return Fit(marker, project.Entry.Name, Badge(readiness), width);
    }

    /// <summary>
    /// The label a row carries, which for most rows is nothing at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a project that cannot be started says so in the list. Everything
    /// else is either fine or has something worth reading, and reading it is
    /// what the pane beside the list is for — a column of identical badges
    /// down a list of sixteen projects distinguishes none of them from each
    /// other, which is the one job a list has.
    /// </para>
    /// <para>
    /// This costs nothing in timeliness, because both things that block a
    /// launch are known from the registry: whether the repository is on this
    /// machine, and whether its agent is installed. A blocked project is
    /// marked the instant the list is drawn, without reading anything.
    /// </para>
    /// </remarks>
    private static string Badge(Readiness readiness) =>
        readiness is Readiness.Blocked or Readiness.Unsupported
            ? $"[{ProjectReadinessRules.Mark(readiness)} {ProjectReadinessRules.Label(readiness)}]"
            : string.Empty;

    /// <summary>
    /// Lays a row out across the width the list actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The row used to be built by joining its parts with two spaces and
    /// hoping. In a column of forty-three characters — which is what a
    /// hundred-and-twenty column terminal gives it — that produced rows like:
    /// </para>
    /// <code>
    ///   TheCodeSaiyan-PowerShell-tcs.core  [! At
    ///   home-servers-build  [! Attention]  claud
    /// </code>
    /// <para>
    /// Cut wherever the edge happened to fall, so the state — the part worth
    /// scanning down — was the part that went missing, and only for the
    /// projects with the longest names. Here the state is placed against the
    /// right edge where it lines up between rows, and the name gives way
    /// first, because a shortened name is still recognisable and a shortened
    /// state is not.
    /// </para>
    /// </remarks>
    /// <param name="marker">Whether this is the current or a pinned project.</param>
    /// <param name="name">The project's name.</param>
    /// <param name="state">Its readiness, already bracketed.</param>
    /// <param name="width">
    /// The column's width. Zero or less when the list has not been laid out
    /// yet, in which case nothing is trimmed and the first layout will redraw.
    /// </param>
    internal static string Fit(string marker, string name, string state, int width)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(state);

        var head = $"{marker} {name}";

        if (state.Length == 0)
        {
            return width <= 0 ? head : Shorten(head, width);
        }

        if (width <= 0)
        {
            return $"{head}  {state}";
        }

        // One space so the state never touches the border, and two so it is
        // never mistaken for part of the name.
        var room = width - state.Length - 1;

        if (room < MinimumNameWidth)
        {
            // Too narrow to show both. The name wins: somebody who cannot tell
            // which project a row is cannot use the list at all, whereas the
            // state is also written in the detail pane beside it.
            return Shorten(head, width);
        }

        return Shorten(head, room - 1).PadRight(room) + state;
    }

    /// <summary>How little of a name is still worth showing beside a state.</summary>
    private const int MinimumNameWidth = 12;

    /// <summary>
    /// Cuts text to a width, marking the cut so it does not read as the whole.
    /// </summary>
    private static string Shorten(string text, int width) =>
        text.Length <= width ? text
            : width <= 1 ? text[..Math.Max(0, width)]
            : text[..(width - 1)] + "…";

    /// <summary>
    /// Readiness per project, filled in as each overview arrives.
    /// </summary>
    /// <remarks>
    /// A project's state cannot be known until its details have been read, and
    /// reading every project's details before drawing anything would make the
    /// launcher wait on the slowest repository somebody owns. Rows therefore
    /// start with what is knowable without a read and are corrected as the
    /// answers come in.
    /// </remarks>
    private readonly Dictionary<string, Readiness> _readiness = new(StringComparer.Ordinal);

    /// <summary>
    /// Notes a project's readiness and redraws the list.
    /// </summary>
    /// <remarks>
    /// Called from both paths that produce an overview. It was originally only
    /// on the one that waits, so a project whose details were already to hand
    /// stayed at its provisional state for ever — which is every project under
    /// test, and any project read from a cache.
    /// </remarks>
    private void Record(ProjectResolution project, ProjectOverview? overview)
    {
        _readiness[project.Entry.Slug] = ProjectReadinessRules.Of(
            overview,
            project.IsAvailableLocally,
            _agents.Count == 0
                || _agents.Any(agent => agent.Contains(
                    project.Entry.DefaultAgent, StringComparison.OrdinalIgnoreCase)));

        RefreshRows();
    }

    /// <summary>
    /// Redraws the rows in place, keeping the cursor where it was.
    /// </summary>
    private void RefreshRows()
    {
        // Redrawing sets the source and restores the cursor, and both raise the
        // selection-changed event that asks for an overview — which redraws.
        // Without this the first answer to arrive recurses until the stack runs
        // out, which it did, immediately.
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;

        try
        {
            RefreshRowsCore();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private bool _refreshing;

    private void RefreshRowsCore()
    {
        var selected = _list.SelectedItem;

        _list.SetSource(new ObservableCollection<string>(
            _shown.Select(project => Row(project, RowWidth))));

        // Moving the cursor because a row was relabelled would be its own bug:
        // somebody arrowing down a list must not be dragged back to the top by
        // an answer arriving behind them.
        if (selected is int index && index >= 0 && index < _shown.Count)
        {
            _list.SelectedItem = index;
        }
    }

    private void ApplyFilter()
    {
        var text = _filter.Text?.Trim() ?? string.Empty;

        _shown = text.Length == 0
            ? [.. _projects]
            : [.. _projects.Where(p =>
                p.Entry.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Entry.Slug.Contains(text, StringComparison.OrdinalIgnoreCase))];

        Render();
    }

    private void LaunchSelected()
    {
        if (Selected is not { } project)
        {
            return;
        }

        // Said rather than swallowed. This used to be one pattern match: a
        // project that is not on this machine failed it, and Enter did nothing
        // whatever — no launch, no message, nothing to distinguish it from a
        // key that had not registered.
        if (!project.IsAvailableLocally)
        {
            Say($"{project.Entry.Name} is not on this machine. "
                + "Registry ▸ Clone onto this machine.");

            return;
        }

        Close(new LauncherIntent(
            LauncherAction.Launch, project, project.Entry.DefaultAgent));
    }

    /// <summary>
    /// Says something along the bottom, until the cursor moves off whatever it
    /// was about.
    /// </summary>
    private void Say(string message)
    {
        _summary.Text = message;

        SetNeedsDraw();
    }

    /// <summary>What the bottom line says when it has nothing else to say.</summary>
    private readonly string _state = string.Empty;

    /// <summary>
    /// Shows what is known about the selected project, reading the parts that
    /// need git and the filesystem off the main loop so a slow repository does
    /// not freeze the list somebody is moving through.
    /// </summary>
    private void ShowSelected()
    {
        // Whatever was said about the last project stops being true the moment
        // the cursor leaves it.
        if (_summary.Text != _state)
        {
            _summary.Text = _state;
        }

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
            Record(project, reading.Result);

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

                    Record(project, overview);

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
