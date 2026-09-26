using System.Collections.ObjectModel;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>One idea, as the screen was given it.</summary>
/// <param name="Summary">Where it is and where it stands.</param>
/// <param name="Record">Its working record, or null when there is none to read.</param>
internal sealed record IdeaEntry(IdeaSummary Summary, IdeaRecord? Record);

/// <summary>
/// Every idea, and one of them in full, with a key for each thing that can be
/// done to it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is changed from here. Each key asks what it needs and hands back
/// the command somebody would otherwise have typed; the launcher runs it and
/// opens this again on the same idea. That is the rule the launcher keeps
/// everywhere: a screen with its own copy of a command's behaviour is a second
/// implementation, and one of them drifts.
/// </para>
/// <para>
/// The keys are bound on the lists rather than the window. A window binding
/// that fires while a dialog is open would turn the letter typed into an
/// answer into a command, and a list that has the focus does not pass a letter
/// up to the window at all.
/// </para>
/// </remarks>
internal sealed class IdeasWindow : Window
{
    private readonly IReadOnlyList<IdeaEntry> _ideas;
    private readonly IReadOnlyList<string> _projects;
    private readonly string? _here;
    private readonly IIdeaPrompts _prompts;
    private readonly IApplication _application;
    private readonly KeyedListView _rows;
    private readonly KeyedListView _detail;

    /// <summary>What to run once the screen has closed, or null when nothing was asked for.</summary>
    internal IdeaStep? Chosen { get; private set; }

    /// <param name="ideas">Every idea, as read before the screen opened.</param>
    /// <param name="projects">The registered projects, for where an accepted idea may go.</param>
    /// <param name="here">The project selected in the launcher, where a new idea goes, or null.</param>
    /// <param name="select">The idea to start on, or null for the first.</param>
    /// <param name="prompts">How each step asks what it needs.</param>
    /// <param name="application">The running toolkit.</param>
    internal IdeasWindow(
        IReadOnlyList<IdeaEntry> ideas,
        IReadOnlyList<string> projects,
        string? here,
        string? select,
        IIdeaPrompts prompts,
        IApplication application)
    {
        ArgumentNullException.ThrowIfNull(ideas);
        ArgumentNullException.ThrowIfNull(projects);
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(application);

        _ideas = ideas;
        _projects = projects;
        _here = here;
        _prompts = prompts;
        _application = application;

        Title = "Ideas";
        BorderStyle = LauncherTheme.Lines;

        var listFrame = new FrameView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(35),
            Title = ideas.Count == 0 ? "No ideas yet" : "Ideas",
            BorderStyle = LauncherTheme.Inner,
        };

        _rows = new KeyedListView { Id = "ideas-list", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        _rows.SetSource(new ObservableCollection<string>(Rows(ideas)));
        _rows.ValueChanged += (_, _) => ShowDetail();
        listFrame.Add(_rows);

        var detailFrame = new FrameView
        {
            X = 0,
            Y = Pos.Bottom(listFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Title = "The idea (Tab to scroll it)",
            BorderStyle = LauncherTheme.Inner,
        };

        _detail = new KeyedListView { Id = "ideas-detail", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        detailFrame.Add(_detail);

        var footer = new KeyLine(
        [
            ("n", "new"),
            ("r", "refine"),
            ("a", "answer"),
            ("c", "choose"),
            ("k", "keep"),
            ("d", "drop"),
            ("i", "improve"),
            ("y", "accept"),
            ("Del", "remove"),
            ("Esc", "close"),
        ])
        {
            X = 1,
            Y = Pos.AnchorEnd(1),
        };

        Add(listFrame, detailFrame, footer);

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        // One Command slot per key, each one a list does not implement, so the
        // list's own behaviour is added to rather than replaced. Never
        // Activate or Accept: those are raised by clicking about the screen.
        foreach (var list in (KeyedListView[])[_rows, _detail])
        {
            list.OnKey(Key.N, Command.New, () => Take(New()));
            list.OnKey(Key.R, Command.Open, () => Take(Refine()));
            list.OnKey(Key.A, Command.Edit, () => Take(Answer()));
            list.OnKey(Key.C, Command.Save, () => Take(Choose()));
            list.OnKey(Key.K, Command.Copy, () => Take(Judge(IdeaVerdict.Keep)));
            list.OnKey(Key.D, Command.Cut, () => Take(Judge(IdeaVerdict.Drop)));
            list.OnKey(Key.I, Command.Paste, () => Take(Improve()));
            list.OnKey(Key.Y, Command.SaveAs, () => Take(Accept()));
            list.OnKey(Key.Delete, Command.DeleteCharRight, () => Take(Remove()));
        }

        var start = select is null ? -1 : ideas.ToList().FindIndex(i => i.Summary.Place.Id == select);

        if (ideas.Count > 0)
        {
            _rows.SelectedItem = Math.Max(0, start);
        }

        ShowDetail();
        _rows.SetFocus();
    }

    /// <summary>The idea the cursor is on, or null when there are none.</summary>
    internal IdeaEntry? Selected =>
        _rows.SelectedItem is int at && at >= 0 && at < _ideas.Count ? _ideas[at] : null;

    /// <summary>
    /// Closes with a command, or does nothing. A key that does not apply, or
    /// a question answered with "never mind", is not worth a complaint.
    /// </summary>
    private bool Take(IdeaStep? step)
    {
        if (step is not null)
        {
            Chosen = step;
            _application.RequestStop(this);
        }

        return true;
    }

    private IdeaStep? New() =>
        _prompts.NewIdea() is { Length: > 0 } text ? IdeaSteps.Add(text, _here) : null;

    private IdeaStep? Refine() =>
        Selected is { Record: { } record } idea && IdeaWork.StageOf(record) is IdeaStage.Captured or IdeaStage.Ready
            ? IdeaSteps.Refine(idea.Summary.Place)
            : null;

    private IdeaStep? Answer() =>
        Selected is { Record: { Accepted: null } record } idea
            && _prompts.Answer(record) is { } answered
            ? IdeaSteps.Answer(idea.Summary.Place, answered.Question, answered.Answer)
            : null;

    private IdeaStep? Choose() =>
        Plan() is { } open && _prompts.Choose(open.Plan) is { } chosen
            ? IdeaSteps.Choose(open.Place, chosen.Layer, chosen.Option)
            : null;

    private IdeaStep? Judge(IdeaVerdict verdict) =>
        Plan() is { } open && _prompts.Pieces(open.Plan, verdict) is { Count: > 0 } pieces
            ? IdeaSteps.Judge(open.Place, verdict, pieces)
            : null;

    private IdeaStep? Improve() =>
        Plan() is { } open && _prompts.Improve(open.Plan) is { } asked
            ? IdeaSteps.Improve(open.Place, asked.Piece, asked.Request)
            : null;

    /// <remarks>
    /// Only once there is a plan that has taken in everything asked of it. The
    /// command would refuse the rest, and closing the screen to be told so is
    /// a worse answer than the key doing nothing.
    /// </remarks>
    private IdeaStep? Accept() =>
        Selected is { Record: { Plan: { } plan } record } idea
            && IdeaWork.StageOf(record) == IdeaStage.Proposed
            && _prompts.Destination(idea.Summary.Place, plan, _projects) is { } target
            ? IdeaSteps.Accept(idea.Summary.Place, target)
            : null;

    private IdeaStep? Remove() =>
        Selected is { } idea
            && _prompts.Confirm(
                $"Forget {idea.Summary.Place.Id}? Its questions, answers and plan go with it.",
                $"Yes, forget {idea.Summary.Place.Id}")
            ? IdeaSteps.Remove(idea.Summary.Place)
            : null;

    /// <summary>The selected idea's plan, when it has one still open to change.</summary>
    private (IdeaPlace Place, IdeaPlan Plan)? Plan() =>
        Selected is { Record: { Accepted: null, Plan: { } plan } } idea ? (idea.Summary.Place, plan) : null;

    private void ShowDetail()
    {
        var width = _detail.Viewport.Width > 20 ? _detail.Viewport.Width - 1 : 100;

        var lines = Selected is { } idea
            ? IdeaSteps.Detail(idea.Summary.Place, idea.Record, width)
            : ["Drop an idea in with n, or from the Ideas menu dump notes kept elsewhere."];

        _detail.SetSource(new ObservableCollection<string>(lines));
    }

    private static IEnumerable<string> Rows(IReadOnlyList<IdeaEntry> ideas) =>
        ideas.Count == 0
            ? ["Nothing here yet. Press n to drop an idea in."]
            : ideas.Select(idea =>
                $"{Shorten(idea.Summary.Place.Id, 28),-28} {Shorten(IdeaSteps.Stage(idea.Summary.Stage, idea.Summary.Unanswered), 16),-16} "
                + $"{Shorten(idea.Summary.Place.Project ?? "workspace-wide", 16),-16} {idea.Summary.Title}");

    /// <summary>Three dots rather than the one character, for profiles that ask for ASCII only.</summary>
    private static string Shorten(string text, int width) =>
        text.Length <= width ? text : text[..Math.Max(1, width - 3)] + "...";
}
