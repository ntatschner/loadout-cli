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

    private readonly Label _path;
    private readonly Label _branch;
    private readonly Label _context;
    private readonly Label _rules;
    private readonly Label _memory;
    private readonly ListView _warnings;
    private readonly FrameView _warningsFrame;
    private readonly Button _launch;
    private readonly Button _resume;
    private readonly Button _shell;

    /// <summary>Raised with the agent to start.</summary>
    internal event EventHandler<string>? Launch;

    /// <summary>Raised to reopen a previous conversation.</summary>
    internal event EventHandler<EventArgs>? Resume;

    /// <summary>Raised to open a development shell.</summary>
    internal event EventHandler<EventArgs>? Shell;

    internal ProjectDetailView()
    {
        Title = "Details";
        BorderStyle = LineStyle.Rounded;

        _path = new Label { X = 1, Y = 0, Width = Dim.Fill(1) };

        _branch = Field("Branch", 2);
        _context = Field("Context", 3);
        _rules = Field("Rules", 4);
        _memory = Field("Memory", 5);

        _warnings = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };

        _warningsFrame = new FrameView
        {
            X = 1,
            Y = 7,
            Width = Dim.Fill(1),
            Height = Dim.Fill(4),
            Title = "Needs attention",
            BorderStyle = LineStyle.Single,
            Visible = false,
        };

        _warningsFrame.Add(_warnings);

        _launch = new Button { X = 1, Y = Pos.AnchorEnd(2), Text = "_Launch" };
        _resume = new Button { X = Pos.Right(_launch) + 1, Y = Pos.AnchorEnd(2), Text = "_Resume" };
        _shell = new Button { X = Pos.Right(_resume) + 1, Y = Pos.AnchorEnd(2), Text = "_Shell" };

        _launch.Accepting += (_, e) => { e.Handled = true; Launch?.Invoke(this, _agent); };
        _resume.Accepting += (_, e) => { e.Handled = true; Resume?.Invoke(this, EventArgs.Empty); };
        _shell.Accepting += (_, e) => { e.Handled = true; Shell?.Invoke(this, EventArgs.Empty); };

        Add(_path, _warningsFrame, _launch, _resume, _shell);
    }

    /// <summary>The agent the launch button would start.</summary>
    private string _agent = string.Empty;

    /// <summary>
    /// Adds one labelled row and returns the label holding its value.
    /// </summary>
    private Label Field(string name, int row)
    {
        Add(new Label { X = 1, Y = row, Text = name });

        var value = new Label { X = 1 + LabelWidth, Y = row, Width = Dim.Fill(1) };

        Add(value);

        return value;
    }

    /// <summary>Shows the project's name and path while its details are read.</summary>
    internal void ShowHeading(ProjectResolution project, string status)
    {
        ArgumentNullException.ThrowIfNull(project);

        Title = project.Entry.Name;
        _agent = project.Entry.DefaultAgent;
        _launch.Text = $"_Launch {project.Entry.DefaultAgent}";

        _path.Text = project.LocalPath ?? "not on this machine";

        _branch.Text = status;
        _context.Text = string.Empty;
        _rules.Text = string.Empty;
        _memory.Text = string.Empty;

        _warningsFrame.Visible = false;

        SetEnabled(project.IsAvailableLocally);
    }

    /// <summary>Shows everything known about the project.</summary>
    internal void Show(ProjectResolution project, ProjectOverview? overview, string? failure)
    {
        ArgumentNullException.ThrowIfNull(project);

        ShowHeading(project, string.Empty);

        if (failure is { Length: > 0 })
        {
            _branch.Text = failure;
            return;
        }

        if (overview is null)
        {
            _branch.Text = project.IsAvailableLocally ? "no details available" : string.Empty;
            return;
        }

        _branch.Text = $"{overview.Branch ?? "detached"}   " +
            (overview.IsClean ? "clean" : "uncommitted changes");

        var budget = FormatBytes(overview.AlwaysLoadedBytes);

        _context.Text = overview.IsOverBudget
            ? $"{budget} every session — larger than it needs to be"
            : $"{budget} every session";

        _rules.Text = overview.ScopedRules == 1
            ? "1 scoped rule, loaded on demand"
            : $"{overview.ScopedRules} scoped rules, loaded on demand";

        _memory.Text = overview.MemoryTopics == 1
            ? "1 topic"
            : $"{overview.MemoryTopics} topics";

        var warnings = Warnings(overview).ToList();

        _warningsFrame.Visible = warnings.Count > 0;

        if (warnings.Count > 0)
        {
            _warnings.SetSource(new ObservableCollection<string>(warnings.Select(w => $"! {w}")));
        }
    }

    /// <summary>Clears the panel, for when there is no project to describe.</summary>
    internal void ShowNothing()
    {
        Title = "Details";
        _path.Text = "No project selected.";
        _branch.Text = string.Empty;
        _context.Text = string.Empty;
        _rules.Text = string.Empty;
        _memory.Text = string.Empty;
        _warningsFrame.Visible = false;

        SetEnabled(false);
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
