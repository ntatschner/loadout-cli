using System.Collections.ObjectModel;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// What is known about one project, and what can be done with it.
/// <para>
/// The old launcher printed four lines here and then asked a question. There
/// is a good deal more worth knowing before starting a session — how much of
/// the context budget is spent before a word is typed, what is uncommitted,
/// what is wrong — and a panel that stays put can show all of it at once
/// instead of trading one fact for another.
/// </para>
/// </summary>
internal sealed class ProjectDetailView : FrameView
{
    /// <summary>Width of the label column, so the values line up down the panel.</summary>
    private const int LabelWidth = 12;

    private readonly Label _name;
    private readonly Label _path;
    private readonly Label _running;
    private readonly Label _branch;
    private readonly Label _context;
    private readonly Label _rules;
    private readonly Label _memory;
    private readonly Label _specialists;
    private readonly ListView _warnings;
    private readonly Label _warningsHeading;
    private readonly Button _launch;
    private readonly Button _resume;
    private readonly Button _shell;
    private readonly Button _problems;

    /// <summary>Raised with the agent to start.</summary>
    internal event EventHandler<string>? Launch;

    /// <summary>Raised to reopen a previous conversation.</summary>
    internal event EventHandler<EventArgs>? Resume;

    /// <summary>Raised to open a development shell.</summary>
    internal event EventHandler<EventArgs>? Shell;

    /// <summary>Raised to look at what is wrong and offer to fix it.</summary>
    internal event EventHandler<EventArgs>? Problems;

    internal ProjectDetailView()
    {
        // A surface rather than a box. The list beside this is framed because
        // it is something to move through; this is something to read, and it
        // sits a shade above the ground with a margin of its own instead of a
        // line round it. The project's name is its heading, in the place a
        // frame's title used to be.
        BorderStyle = LineStyle.None;
        SetScheme(LauncherTheme.RaisedSurface);

        if (Padding is { } padding)
        {
            padding.Thickness = new Thickness(2, 1, 1, 1);
        }

        _name = new Label { X = 0, Y = 0, Width = Dim.Fill() };
        _name.SetScheme(LauncherTheme.Heading);

        _path = new Label { X = 0, Y = 1, Width = Dim.Fill() };
        _path.SetScheme(LauncherTheme.Muted);

        // Directly under the path, above every fact about the project,
        // because this is the only line here that changes what somebody does
        // next rather than describing how things stand. Empty when nothing is
        // running, which is the ordinary case and should cost no attention.
        _running = new Label { X = 0, Y = 2, Width = Dim.Fill() };

        _branch = Field("Branch", 3);
        _context = Field("Context", 4);
        _rules = Field("Rules", 5);
        _memory = Field("Memory", 6);
        _specialists = Field("Uses", 7);

        // A heading and a list, not a box in a box. The list already says
        // what it is by starting every line with a mark.
        //
        // Placed under the last fact rather than on a fixed row, because the
        // Uses line wraps when a project is built from many things, and a
        // heading on a fixed row was then hard against the wrapped line with
        // the blank row that separates sections eaten. Seen in a photograph
        // of a real console, not in the headless tests, whose projects are
        // built from nothing.
        _warningsHeading = new Label
        {
            X = 0,
            Y = Pos.Bottom(_specialists) + 1,
            Text = "Needs attention",
            Visible = false,
        };

        _warningsHeading.SetScheme(LauncherTheme.Alert);

        _warnings = new ListView
        {
            X = 0,
            Y = Pos.Bottom(_warningsHeading),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Visible = false,
        };

        _launch = new Button { X = 0, Y = Pos.AnchorEnd(1), Text = "_Launch" };
        _resume = new Button { X = Pos.Right(_launch) + 1, Y = Pos.AnchorEnd(1), Text = "_Resume" };
        _shell = new Button { X = Pos.Right(_resume) + 1, Y = Pos.AnchorEnd(1), Text = "_Shell" };

        // Only offered when there is something to look at, so its presence is
        // itself the signal that something needs attention.
        _problems = new Button
        {
            X = Pos.Right(_shell) + 1,
            Y = Pos.AnchorEnd(1),
            Text = "_Problems",
            Visible = false,
        };

        _launch.Accepting += (_, e) => { e.Handled = true; Launch?.Invoke(this, _agent); };
        _resume.Accepting += (_, e) => { e.Handled = true; Resume?.Invoke(this, EventArgs.Empty); };
        _shell.Accepting += (_, e) => { e.Handled = true; Shell?.Invoke(this, EventArgs.Empty); };
        _problems.Accepting += (_, e) => { e.Handled = true; Problems?.Invoke(this, EventArgs.Empty); };

        Add(_name, _path, _running, _warningsHeading, _warnings, _launch, _resume, _shell, _problems);
    }

    /// <summary>The agent the launch button would start.</summary>
    private string _agent = string.Empty;

    /// <summary>
    /// Adds one labelled row and returns the label holding its value.
    /// </summary>
    private Label Field(string name, int row)
    {
        var label = new Label { X = 0, Y = row, Text = name };

        label.SetScheme(LauncherTheme.Muted);

        Add(label);

        var value = new Label { X = LabelWidth, Y = row, Width = Dim.Fill() };

        Add(value);

        return value;
    }

    /// <summary>Shows the project's name and path while its details are read.</summary>
    internal void ShowHeading(ProjectResolution project, string status)
    {
        ArgumentNullException.ThrowIfNull(project);

        _name.Text = project.Entry.Name;
        _agent = project.Entry.DefaultAgent;
        _launch.Text = $"_Launch {project.Entry.DefaultAgent}";

        _path.Text = project.LocalPath ?? "not on this machine";

        // Cleared with the rest. This line was left alone while a project was
        // read, so the previous project's "a session is running here" sat
        // under the next project's name until its own answer arrived — seen
        // in a photograph taken mid-read, and a wrong answer to the one
        // question on this pane that changes what somebody does next.
        _running.Text = string.Empty;

        _branch.Text = status;
        _context.Text = string.Empty;
        _rules.Text = string.Empty;
        _memory.Text = string.Empty;
        _specialists.Text = string.Empty;

        ShowWarnings(false);
        _problems.Visible = false;

        SetEnabled(project.IsAvailableLocally);
    }

    /// <summary>
    /// Replaces the status line without redrawing the rest, which is what an
    /// animation needs: rebuilding the panel every frame would flicker.
    /// </summary>
    internal void SetStatus(string status) => _branch.Text = status;

    /// <summary>Shows everything known about the project.</summary>
    internal void Show(ProjectResolution project, ProjectOverview? overview, string? failure)
    {
        ArgumentNullException.ThrowIfNull(project);

        ShowHeading(project, string.Empty);

        if (failure is { Length: > 0 })
        {
            // Whatever went wrong is usually Git's, and Git quotes the remote
            // it could not reach.
            _branch.Text = Shown.Plainly(failure);
            return;
        }

        if (overview is null)
        {
            _branch.Text = project.IsAvailableLocally ? "no details available" : string.Empty;
            return;
        }

        _running.Text = RunningNote(overview);

        _branch.Text = $"{overview.Branch ?? "detached"}   " +
            (overview.IsClean ? "clean" : "uncommitted changes");

        var budget = FormatBytes(overview.AlwaysLoadedBytes);

        var carried = Carried(overview);

        _context.Text = overview.IsOverBudget
            ? $"{budget} every session — larger than it needs to be{carried}"
            : $"{budget} every session{carried}";

        _rules.Text = overview.ScopedRules == 1
            ? "1 scoped rule, loaded on demand"
            : $"{overview.ScopedRules} scoped rules, loaded on demand";

        _memory.Text = overview.MemoryTopics == 1
            ? "1 topic"
            : $"{overview.MemoryTopics} topics";

        // Rendered, not worked out. What a project appears to use is decided
        // by the same service the command line uses, so the screen and
        // 'loadout instructions' cannot come to different conclusions.
        _specialists.Text = Detected(overview);

        var warnings = Warnings(overview).ToList();

        ShowWarnings(warnings.Count > 0);
        _problems.Visible = warnings.Count > 0;

        if (warnings.Count > 0)
        {
            _warnings.SetSource(new ObservableCollection<string>(warnings.Select(w => $"! {w}")));
        }
    }

    /// <summary>
    /// What this project carries beyond its instructions, appended to the
    /// context line rather than given a row of its own.
    /// </summary>
    /// <remarks>
    /// Shown because the menu can switch these on and off, and a toggle whose
    /// current state is nowhere on the screen is a guess. Silent when both are
    /// off, which is the ordinary case: the line is about what a session costs,
    /// and "carrying nothing extra" costs nothing to say and nothing to know.
    /// </remarks>
    internal static string Carried(ProjectOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        var carried = new List<string>();

        if (overview.CarriesTasks)
        {
            carried.Add("open tasks");
        }

        if (overview.CarriesCodeMap)
        {
            carried.Add("the code map");
        }

        return carried.Count == 0 ? string.Empty : ", plus " + string.Join(" and ", carried);
    }

    /// <summary>
    /// What the project appears to be built from, in one line.
    /// </summary>
    /// <remarks>
    /// Names only, and only as many as fit. This says what the project uses, not
    /// what a task would load: those are different questions, and running them
    /// together on a project screen is how people come to expect every
    /// technology in every prompt.
    /// </remarks>
    private static string Detected(ProjectOverview overview)
    {
        var detected = overview.DetectedSpecialists ?? [];

        if (detected.Count == 0)
        {
            return string.Empty;
        }

        var names = detected.Select(d => d.Specialist.Title).ToList();

        const int Most = 5;

        return names.Count <= Most
            ? string.Join(", ", names)
            : string.Join(", ", names.Take(Most)) + $" and {names.Count - Most} more";
    }

    /// <summary>Clears the panel, for when there is no project to describe.</summary>
    /// <param name="because">What to say instead, which is the only thing on
    /// screen when the registry is empty and so has to be a way forward rather
    /// than a statement of fact.</param>
    internal void ShowNothing(string because = "No project selected.")
    {
        _name.Text = string.Empty;
        _path.Text = because;
        _running.Text = string.Empty;
        _branch.Text = string.Empty;
        _context.Text = string.Empty;
        _rules.Text = string.Empty;
        _memory.Text = string.Empty;
        _specialists.Text = string.Empty;
        ShowWarnings(false);
        _problems.Visible = false;

        SetEnabled(false);
    }

    private void ShowWarnings(bool shown)
    {
        _warningsHeading.Visible = shown;
        _warnings.Visible = shown;
    }

    private void SetEnabled(bool enabled)
    {
        _launch.Enabled = enabled;
        _resume.Enabled = enabled;
        _shell.Enabled = enabled;
    }

    /// <summary>
    /// Things worth saying before a launch, in the order they matter.
    /// </summary>
    /// <summary>
    /// What to say about sessions already open against this project.
    /// </summary>
    /// <remarks>
    /// Not a warning, and deliberately not in the list of them: a session
    /// running is not a problem with the project. It is a fact about right now,
    /// and the reason it is worth a line is that launching a second agent into
    /// a repository somebody is already working in is a mistake nothing else
    /// here would catch.
    /// </remarks>
    internal static string RunningNote(ProjectOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        return overview.RunningSessions switch
        {
            <= 0 => string.Empty,
            1 => "a session is running here",
            var many => $"{many} sessions are running here",
        };
    }

    internal static IEnumerable<string> Warnings(ProjectOverview overview)
    {
        ArgumentNullException.ThrowIfNull(overview);

        if (overview.TrackedAgentFiles > 0)
        {
            yield return $"{overview.TrackedAgentFiles} agent file(s) are committed to this repository";
        }

        if (overview.PendingImports > 0)
        {
            yield return $"{overview.PendingImports} memory topic(s) recorded outside the workspace";
        }

        if (overview.IsOverBudget)
        {
            yield return "the always-loaded instructions are larger than they need to be";
        }

        if (!overview.Protected)
        {
            yield return "no pre-commit protection in this clone";
        }
    }

    internal static string FormatBytes(long bytes) => bytes < 1024
        ? $"{bytes} B"
        : bytes < 1024 * 1024
            ? $"{bytes / 1024.0:0.#} KB"
            : $"{bytes / (1024.0 * 1024.0):0.#} MB";
}
