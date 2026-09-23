using System.Collections.ObjectModel;
using Loadout.Core.Teams;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What the team runs on this machine are doing, while they do it.
/// </summary>
/// <remarks>
/// <para>
/// The same status the dashboard serves and <c>team status</c> prints, in the
/// launcher. All three read one file per run written by the run itself, so
/// there is one account of what happened and three ways to look at it rather
/// than three things that could disagree.
/// </para>
/// <para>
/// Nothing is started or stopped from here. Every key hands back the command
/// somebody would otherwise have typed, and it is run once the toolkit has
/// given the terminal back — the rule the launcher keeps everywhere, because a
/// screen with its own copy of a command's behaviour is a second implementation
/// to keep in step, and one of them drifts.
/// </para>
/// <para>
/// It refreshes on a timer, except where the person reading has asked for no
/// redraws. A screen that repaints itself every two seconds is the thing that
/// setting exists to refuse: a screen reader is handed the whole list again on
/// every pass and never gets to the end of it. There, the same key that
/// refreshes everywhere else refreshes once, when asked.
/// </para>
/// </remarks>
internal sealed class TeamsWindow : Window
{
    /// <summary>
    /// How often the runs are read again. Two seconds because a node's line
    /// changes about that often and a run that finished should not sit there
    /// saying it is going; the read is a handful of small files, and it happens
    /// off the drawing thread either way.
    /// </summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly ListView _rows;
    private readonly Label _detail;
    private readonly IApplication _application;
    private readonly Func<CancellationToken, Task<IReadOnlyList<RunSummary>>> _read;
    private readonly bool _live;
    private readonly Func<RunSummary, bool> _agreed;

    private IReadOnlyList<RunSummary> _runs;
    private object? _timer;

    /// <summary>Whether a read is already in flight, so a slow one cannot stack up.</summary>
    private bool _reading;

    /// <summary>
    /// The command to run once this screen has closed, or null if nothing was
    /// asked for.
    /// </summary>
    internal string? Chosen { get; private set; }

    /// <param name="runs">What was read before the screen opened.</param>
    /// <param name="read">How to read them again.</param>
    /// <param name="live">
    /// Whether to refresh on a timer. False for a profile that has refused
    /// redraws, which then refreshes on the key instead.
    /// </param>
    /// <param name="application">The running toolkit.</param>
    /// <param name="agreed">
    /// How somebody agrees to forgetting a run. The dialog by default; a test
    /// passes its own answer, because a dialog waiting for a keypress cannot
    /// be driven headlessly and the path would otherwise ship unexercised.
    /// What the real dialog does with the answer it gets is not covered by
    /// that - only what this screen does with the answer.
    /// </param>
    internal TeamsWindow(
        IReadOnlyList<RunSummary> runs,
        Func<CancellationToken, Task<IReadOnlyList<RunSummary>>> read,
        bool live,
        IApplication application,
        Func<RunSummary, bool>? agreed = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(application);

        _runs = runs;
        _read = read;
        _live = live;
        _application = application;
        _agreed = agreed ?? Asks;

        Title = "Team runs";
        BorderStyle = LauncherTheme.Lines;

        var listFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(45),
            Title = runs.Count == 0 ? "No runs yet" : "Runs",
            BorderStyle = LauncherTheme.Inner,
        };

        // Named so a test finds it by what it is rather than where it sits.
        // Selecting a view by position picks the wrong one as soon as anything
        // above it changes height, and reads as a fresh bug.
        _rows = new ListView
        {
            Id = "teams-runs",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        _rows.SetSource(new ObservableCollection<string>(Rows(_runs)));
        _rows.ValueChanged += (_, _) => ShowDetail();

        listFrame.Add(_rows);

        var detailFrame = new FrameView
        {
            X = 0,
            Y = Pos.Bottom(listFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Title = "Nodes",
            BorderStyle = LauncherTheme.Inner,
        };

        _detail = new Label
        {
            Id = "teams-detail",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        detailFrame.Add(_detail);

        var footer = new KeyLine(
        [
            ("Enter", "log"),
            ("s", "status"),
            ("d", "dashboard"),
            ("r", "forget"),
            ("F5", live ? "read now" : "refresh"),
            ("Esc", "close"),
        ])
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
        };

        Add(listFrame, detailFrame, footer);

        this.Bind(Key.Esc, Command.Quit);

        AddCommand(Command.Quit, () =>
        {
            _application.RequestStop();
            return true;
        });

        // A distinct Command slot per key, never three registrations of one:
        // AddCommand keys its handler on the Command, so two keys sharing a
        // slot silently share a handler and the last one registered answers
        // for both.
        Hand(Key.Enter, Command.Accept, () => Selected() is { } run ? $"team log {run.RunId}" : null);
        // Save is chosen for being inert on a list, not for reading well. The
        // slot only has to be one nothing else raises.
        Hand(Key.S, Command.Save, () => Selected() is { } run ? $"team status {run.RunId}" : null);
        Hand(Key.D, Command.Open, () => "team dashboard");

        // The one key here that changes anything. Everything else hands back
        // a command that prints; this one deletes the only copy of what a run
        // did, so it asks first and takes the same slot the manager screen
        // uses for removing something.
        Hand(Key.R, Command.Cut, Forget);

        // Reading again is a read, so it happens here rather than being handed
        // back as a command: closing the screen to refresh it would be a
        // strange thing to make somebody do.
        this.BindEverywhere(Key.F5, Command.Refresh);

        AddCommand(Command.Refresh, () =>
        {
            Reread();
            return true;
        });

        ShowDetail();

        if (_live)
        {
            _timer = _application.AddTimeout(Interval, () =>
            {
                Reread();
                return true;
            });
        }
    }

    /// <summary>The run the cursor is on, or null when there are none.</summary>
    private RunSummary? Selected() =>
        _rows.SelectedItem is int at && at >= 0 && at < _runs.Count ? _runs[at] : null;

    /// <summary>
    /// The command for forgetting the run the cursor is on, or null where
    /// there is nothing to forget or the answer was no.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A keypress is not agreement. <c>team runs remove</c> does not ask,
    /// because naming a run on a command line is itself the agreement - and
    /// here the run was named by a cursor somebody may not have looked at, so
    /// the asking has to happen before the command is handed back.
    /// </para>
    /// <para>
    /// A run still going does nothing at all, the way the dashboard does not
    /// draw the button on one. The command refuses it as well, which is the
    /// half that holds.
    /// </para>
    /// </remarks>
    private string? Forget() =>
        Selected() is { Running: false } run && _agreed(run)
            ? $"team runs remove {run.RunId}"
            : null;

    /// <summary>Puts the question on the screen.</summary>
    private bool Asks(RunSummary run)
    {
        using var confirm = new ChoiceDialog(
            $"Forget {run.RunId}? Everything it wrote down goes, and there is no other copy.",
            ["No, leave it", $"Yes, forget {run.RunId}"],
            _application);

        _application.Run(confirm);

        // Index 1 is the only yes. Dismissing the question, or the default
        // landing on the first row, both mean no - a confirmation that can be
        // passed by pressing Enter without reading is not a confirmation.
        return confirm.ChosenIndex == 1;
    }

    /// <summary>
    /// Binds a key to whatever command the selected run makes of it.
    /// </summary>
    /// <remarks>
    /// A key that does not apply does nothing rather than reporting an error:
    /// pressing "log" with no runs at all is a mis-aim, not a mistake worth a
    /// dialog.
    /// </remarks>
    private void Hand(Key key, Command slot, Func<string?> command)
    {
        this.BindEverywhere(key, slot);

        AddCommand(slot, () =>
        {
            if (command() is { Length: > 0 } path)
            {
                Chosen = path;
                _application.RequestStop();
            }

            return true;
        });
    }

    /// <summary>
    /// Reads the runs again, off the drawing thread, and draws what came back.
    /// </summary>
    /// <remarks>
    /// Reading a run means opening a file per run and folding its events, which
    /// is fast and is still file access; doing it on the thread that draws
    /// would stall the screen on the one machine slow enough to matter. One
    /// read at a time, so a slow one cannot stack up behind the timer.
    /// </remarks>
    private void Reread()
    {
        if (_reading)
        {
            return;
        }

        _reading = true;

        _ = Task.Run(async () =>
        {
            IReadOnlyList<RunSummary>? read = null;

            try
            {
                read = await _read(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A run being written while it is read is the ordinary case,
                // not a fault. The next pass gets it.
            }
            finally
            {
                // In a finally rather than after the catch, because this is the
                // only place the flag above goes back down. Anything thrown
                // that the catch does not name - and the read is somebody
                // else's delegate, so that is not a list this can close -
                // skipped the line, left the flag up, and every later read
                // returned at the first line of this method. The screen went
                // on drawing what it had, for ever, saying nothing: the one
                // failure a refresh loop must not have.
                _application.Invoke(() =>
                {
                    _reading = false;

                    if (read is null)
                    {
                        return;
                    }

                    Show(read);
                });
            }
        });
    }

    /// <summary>
    /// Replaces the list, keeping the cursor on the run it was on.
    /// </summary>
    /// <remarks>
    /// By identifier, never by position. A run that starts while this is open
    /// arrives at the top and pushes everything down, so keeping the index
    /// would move the selection onto a different run under somebody's hands.
    /// </remarks>
    internal void Show(IReadOnlyList<RunSummary> runs)
    {
        var was = Selected()?.RunId;

        _runs = runs;
        _rows.SetSource(new ObservableCollection<string>(Rows(runs)));

        if (was is { Length: > 0 })
        {
            var now = runs.ToList().FindIndex(run => string.Equals(run.RunId, was, StringComparison.Ordinal));

            if (now >= 0)
            {
                _rows.SelectedItem = now;
            }
        }

        ShowDetail();
    }

    /// <summary>One line per run: what it is, where it got to, how long.</summary>
    private static IEnumerable<string> Rows(IReadOnlyList<RunSummary> runs) =>
        runs.Count == 0
            ? ["Nothing has been run yet. Start one with: loadout team run <team> \"<goal>\""]
            : runs.Select(run =>
                $"{run.RunId,-22} {Shorten(run.Team, 16),-16} "
                + $"{Shorten(run.Project ?? string.Empty, 14),-14} "
                + $"{State(run),-12} {Length(run.Elapsed)}");

    /// <summary>
    /// Where a run got to, as a word.
    /// </summary>
    /// <remarks>
    /// A word and not only a colour, for the reason the dashboard has it: a
    /// state somebody has to see the colour of is a state some people cannot
    /// read at all.
    /// </remarks>
    private static string State(RunSummary run) =>
        run.WaitingForYou ? "waiting for you"
            : run.Running ? "running"
            : run.Ended is { Length: > 0 } ended ? ended : "ended";

    /// <summary>The nodes of the selected run, and what each is doing.</summary>
    private void ShowDetail()
    {
        if (Selected() is not { } run)
        {
            _detail.Text = string.Empty;
            return;
        }

        var lines = new List<string>
        {
            run.Goal,
            string.Empty,
        };

        foreach (var node in run.Nodes)
        {
            var doing = node.Doing is { Length: > 0 } what ? "  " + what : string.Empty;

            lines.Add(
                $"{Shorten(node.Node, 16),-16} {Shorten(node.Role, 20),-20} {Shorten(run.Activity(node), 24),-15}"
                + $" {node.Turns} turn(s){doing}");

            // The node's own words on their own line, marked as such. Two
            // accounts, and the useful part is where they differ.
            if (node.Said is { Length: > 0 } said)
            {
                lines.Add($"{string.Empty,-16} says: {said}");
            }
        }

        if (run.Nodes.Count == 0)
        {
            lines.Add("No node has written anything yet.");
        }

        if (run.AtMostRemaining is { } left)
        {
            lines.Add(string.Empty);

            // A ceiling and said as one. What a lead asks for next is not known
            // to anybody, so anything phrased as a prediction would be one.
            lines.Add($"At most {Length(left)} still to go, on the rounds it has left.");
        }

        _detail.Text = string.Join(Environment.NewLine, lines);
    }

    private static string Length(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1)
            ? $"{span.TotalSeconds:F0}s"
            : span < TimeSpan.FromHours(1)
                ? $"{span.TotalMinutes:F0}m"
                : $"{span.TotalHours:F1}h";

    /// <summary>
    /// Cuts a value to fit its column.
    /// </summary>
    /// <remarks>
    /// Three dots rather than the one character for them. This screen is drawn
    /// under whatever profile is in force, one of which asks for nothing beyond
    /// ASCII, and a cut mark the console font cannot draw is a replacement box
    /// on every long line.
    /// </remarks>
    private static string Shorten(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(1, width - 3)] + "...";

    protected override void Dispose(bool disposing)
    {
        if (disposing && _timer is not null)
        {
            _application.RemoveTimeout(_timer);
            _timer = null;
        }

        base.Dispose(disposing);
    }
}
