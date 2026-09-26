using System.Collections.ObjectModel;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Loadout.Tui.Terminal;

/// <summary>
/// The questions the ideas screen asks on its way to a command: which question
/// to answer and with what, which option, which pieces, where it goes.
/// </summary>
/// <remarks>
/// Behind an interface for the reason the teams screen takes its confirmation
/// as a delegate: a dialog waiting for a keypress cannot be driven headlessly,
/// and the path from a key to the command it builds would otherwise ship
/// unexercised. A test answers these; the launcher shows dialogs. Every one
/// returns null for "never mind", and a null anywhere means no command.
/// </remarks>
internal interface IIdeaPrompts
{
    /// <summary>A new idea's text.</summary>
    string? NewIdea();

    /// <summary>A question and its answer.</summary>
    (string Question, string Answer)? Answer(IdeaRecord record);

    /// <summary>A layer and the option chosen for it.</summary>
    (string Layer, string Option)? Choose(IdeaPlan plan);

    /// <summary>Pieces of the plan to keep or drop.</summary>
    IReadOnlyList<string>? Pieces(IdeaPlan plan, IdeaVerdict verdict);

    /// <summary>A piece, or <c>plan</c> for all of it, and what to change.</summary>
    (string Piece, string Request)? Improve(IdeaPlan plan);

    /// <summary>Where an accepted idea goes.</summary>
    IdeaTarget? Destination(IdeaPlace place, IdeaPlan plan, IReadOnlyList<string> projects);

    /// <summary>Yes or no, where a keypress alone is not agreement.</summary>
    bool Confirm(string question, string yes);
}

/// <summary>The questions, as dialogs.</summary>
internal sealed class DialogIdeaPrompts : IIdeaPrompts
{
    private const string Own = "Something else: type my own answer";

    private readonly IApplication _application;

    public DialogIdeaPrompts(IApplication application) => _application = application;

    public string? NewIdea() =>
        Text("New idea", ["What is it? In your own words; it is kept as you write it."], string.Empty);

    public (string Question, string Answer)? Answer(IdeaRecord record)
    {
        // Unanswered first, then the rest, because changing an answer is
        // allowed and is the rarer thing somebody came here for.
        var questions = record.Rounds.SelectMany(round => round.Questions)
            .OrderBy(q => q.Answer.Length > 0)
            .ToList();

        if (questions.Count == 0)
        {
            return null;
        }

        var question = questions.Count == 1
            ? questions[0]
            : Pick("Which question?", questions.Select(q =>
                $"{q.Id}  {q.Question}{(q.Answer.Length > 0 ? $"  (answered: {q.Answer})" : string.Empty)}").ToList()) is { } at
                ? questions[at]
                : null;

        if (question is null)
        {
            return null;
        }

        // The agent's likely answers as choices, the recommended one first,
        // and a way out to one's own words. Answering by picking is the quick
        // case; the choice list is also the question, read in full.
        var offered = question.Options.ToList();

        if (question.Recommendation.Length > 0)
        {
            offered.Remove(question.Recommendation);
            offered.Insert(0, question.Recommendation);
        }

        offered.Add(Own);

        var picked = Pick(
            $"{question.Id}  {question.Question}"
            + (question.Why.Length > 0 ? $"\n\nWhy it matters: {question.Why}" : string.Empty)
            + (question.Recommendation.Length > 0 ? $"\nRecommended: {question.Recommendation}" : string.Empty),
            offered);

        if (picked is not { } index)
        {
            return null;
        }

        var answer = offered[index] == Own
            ? Text(question.Id, [question.Question], question.Answer)
            : offered[index];

        return answer is { Length: > 0 } given ? (question.Id, given) : null;
    }

    public (string Layer, string Option)? Choose(IdeaPlan plan)
    {
        if (Pick("Which layer?", plan.Layers.Select(l => $"{l.Id}  {l.Name}").ToList()) is not { } at)
        {
            return null;
        }

        var layer = plan.Layers[at];

        var option = Pick(
            $"{layer.Id}  {layer.Name}: which option?",
            layer.Options.Select(o =>
                $"{o.Id}  {o.Title}{(o.Recommended ? " (recommended)" : string.Empty)}"
                + $"{(o.Id == layer.Chosen ? "  [chosen now]" : string.Empty)}").ToList());

        return option is { } picked ? (layer.Id, layer.Options[picked].Id) : null;
    }

    public IReadOnlyList<string>? Pieces(IdeaPlan plan, IdeaVerdict verdict)
    {
        var pieces = plan.Layers.Select(l => (l.Id, Line: $"{l.Id}  {l.Name}"))
            .Concat(plan.Additions.Select(a => (a.Id, Line: $"{a.Id}  {a.Title}")))
            .ToList();

        using var dialog = new MarkedListDialog(
            verdict == IdeaVerdict.Keep ? "Keep which pieces?" : "Drop which pieces?",
            "Space ticks and unticks.",
            pieces.Select(p => p.Line).ToList(),
            _application);

        _application.Run(dialog);

        return dialog.Chosen is { } ticked ? [.. ticked.Select(i => pieces[i].Id)] : null;
    }

    public (string Piece, string Request)? Improve(IdeaPlan plan)
    {
        var pieces = plan.Layers.Select(l => (l.Id, Line: $"{l.Id}  {l.Name}"))
            .Concat(plan.Additions.Select(a => (a.Id, Line: $"{a.Id}  {a.Title}")))
            .Prepend((Id: IdeaWork.WholePlan, Line: "The whole plan"))
            .ToList();

        if (Pick("Improve what?", pieces.Select(p => p.Line).ToList()) is not { } at)
        {
            return null;
        }

        var request = Text(pieces[at].Line, ["What should change? The next round reworks it as you say."], string.Empty);

        return request is { Length: > 0 } given ? (pieces[at].Id, given) : null;
    }

    public IdeaTarget? Destination(IdeaPlace place, IdeaPlan plan, IReadOnlyList<string> projects)
    {
        var settled = IdeaWork.Destination(place, plan, projects);
        var targets = new List<(string Line, IdeaTarget? Target)>();

        if (settled.Project is { } suggested)
        {
            targets.Add(($"{suggested}  ({settled.Reason})", new IdeaTarget(suggested, null)));
        }

        foreach (var project in projects.Where(p => p != settled.Project))
        {
            targets.Add((project, new IdeaTarget(project, null)));
        }

        var name = plan.Project.Name.Length > 0 ? plan.Project.Name : plan.Title;
        targets.Add(($"A new project, \"{name}\"...", null));

        var question = settled.Unsettled
            ? $"Where does this go? {settled.Reason}"
            : "Where does this go?";

        if (Pick(question, targets.Select(t => t.Line).ToList()) is not { } at)
        {
            return null;
        }

        if (targets[at].Target is { } chosen)
        {
            return chosen;
        }

        // Making a project is the bigger step, so its name is asked for, with
        // the agent's suggestion filled in to accept or change.
        return Text("A new project", ["What should the project be called?"], name) is { Length: > 0 } made
            ? new IdeaTarget(null, made)
            : null;
    }

    public bool Confirm(string question, string yes)
    {
        using var confirm = new ChoiceDialog(question, ["No, leave it", yes], _application);

        _application.Run(confirm);

        // Index 1 is the only yes. Dismissing, or Enter on the first row, is no.
        return confirm.ChosenIndex == 1;
    }

    private int? Pick(string question, IReadOnlyList<string> choices)
    {
        using var dialog = new ExplainedChoiceDialog(question, choices, _application);

        _application.Run(dialog);

        return dialog.ChosenIndex;
    }

    private string? Text(string title, IReadOnlyList<string> context, string initial)
    {
        using var dialog = new TextEntryDialog(title, context, initial, _application);

        _application.Run(dialog);

        return dialog.Chosen;
    }
}

/// <summary>
/// A choice with the question set out above it in full, wrapped.
/// </summary>
/// <remarks>
/// The ordinary choice dialog puts its question in the border, which is right
/// for "which dump?" and cuts off an agent's question and its reason at the
/// width of the window. Here the question is the thing being answered, so it
/// gets the room.
/// </remarks>
internal sealed class ExplainedChoiceDialog : Window
{
    private readonly ListView _choices;
    private readonly IApplication _application;

    /// <summary>The index chosen, or null when the dialog was dismissed.</summary>
    internal int? ChosenIndex { get; private set; }

    internal ExplainedChoiceDialog(string question, IReadOnlyList<string> choices, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Width = Dim.Percent(85);
        Height = Dim.Percent(80);
        BorderStyle = LauncherTheme.Lines;

        var explained = IdeaSteps.Wrap(question, 90).ToList();

        Add(new Label
        {
            Id = "choice-question",
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = explained.Count,
            Text = string.Join(Environment.NewLine, explained),
        });

        _choices = new ListView
        {
            Id = "choice-list",
            X = 1,
            Y = explained.Count + 1,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
        };

        _choices.SetSource(new ObservableCollection<string>(choices));

        if (choices.Count > 0)
        {
            _choices.SelectedItem = 0;
        }

        // Accepting, not Accepted: the palette found Accepted is not raised on
        // a list like this one. Enter on a row is how anybody answers.
        _choices.Accepting += (_, e) => { e.Handled = true; Accept(); };

        var choose = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_Choose", IsDefault = true };
        var cancel = new Button { X = Pos.Right(choose) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        choose.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_choices, choose, cancel);

        _choices.SetFocus();
    }

    private void Accept()
    {
        ChosenIndex = _choices.SelectedItem;

        _application.RequestStop(this);
    }
}

/// <summary>A line of text, asked for with whatever explains what it is for.</summary>
internal sealed class TextEntryDialog : Window
{
    private readonly TextField _value;
    private readonly IApplication _application;

    /// <summary>What was typed, or null when the dialog was dismissed.</summary>
    internal string? Chosen { get; private set; }

    internal TextEntryDialog(string title, IReadOnlyList<string> context, string initial, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(application);

        _application = application;

        Title = title;
        Width = Dim.Percent(80);
        Height = context.Count + 6;
        BorderStyle = LauncherTheme.Lines;

        for (var i = 0; i < context.Count; i++)
        {
            Add(new Label { X = 1, Y = i, Width = Dim.Fill(1), Text = context[i] });
        }

        _value = new TextField { Id = "text-entry", X = 1, Y = context.Count + 1, Width = Dim.Fill(1), Text = initial };

        var ok = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(ok) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        ok.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };
        _value.Accepting += (_, e) => { e.Handled = true; Accept(); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_value, ok, cancel);

        _value.SetFocus();
    }

    private void Accept()
    {
        var typed = _value.Text?.Trim();

        if (string.IsNullOrWhiteSpace(typed))
        {
            _value.SetFocus();

            return;
        }

        Chosen = typed;

        _application.RequestStop(this);
    }
}

/// <summary>A list to tick several things in.</summary>
internal sealed class MarkedListDialog : Window
{
    private readonly ListView _list;
    private readonly int _count;
    private readonly IApplication _application;

    /// <summary>The indices ticked, or null when the dialog was dismissed.</summary>
    internal IReadOnlyList<int>? Chosen { get; private set; }

    internal MarkedListDialog(string title, string hint, IReadOnlyList<string> lines, IApplication application)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(application);

        _count = lines.Count;
        _application = application;

        Title = title;
        Width = Dim.Percent(70);
        Height = Math.Min(lines.Count + 7, 30);
        BorderStyle = LauncherTheme.Lines;

        Add(new Label { X = 1, Y = 0, Width = Dim.Fill(1), Text = hint });

        _list = new ListView
        {
            Id = "marked-list",
            X = 1,
            Y = 2,
            Width = Dim.Fill(1),
            Height = Dim.Fill(3),
            ShowMarks = true,
            MarkMultiple = true,
        };

        _list.SetSource(new ObservableCollection<string>(lines));

        var ok = new Button { X = 1, Y = Pos.AnchorEnd(1), Text = "_OK", IsDefault = true };
        var cancel = new Button { X = Pos.Right(ok) + 2, Y = Pos.AnchorEnd(1), Text = "Cance_l" };

        ok.Accepting += (_, e) => { e.Handled = true; Accept(); };
        cancel.Accepting += (_, e) => { e.Handled = true; _application.RequestStop(this); };

        this.Bind(Key.Esc, Command.Quit);
        AddCommand(Command.Quit, () => { _application.RequestStop(this); return true; });

        Add(_list, ok, cancel);

        _list.SetFocus();
    }

    private void Accept()
    {
        var ticked = Enumerable.Range(0, _count).Where(i => _list.Source!.IsMarked(i)).ToList();

        // Nothing ticked is nothing to do, and closing would read as done.
        if (ticked.Count == 0)
        {
            _list.SetFocus();

            return;
        }

        Chosen = ticked;

        _application.RequestStop(this);
    }
}
