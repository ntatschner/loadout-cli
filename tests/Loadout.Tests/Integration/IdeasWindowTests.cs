using System.Drawing;
using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The ideas screen: which key builds which command, for which idea, and which
/// keys do nothing where the command would only refuse.
/// </summary>
public sealed class IdeasWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Answers every question the screen asks, and says which were asked.</summary>
    private sealed class Scripted : IIdeaPrompts
    {
        public List<string> Asked { get; } = [];

        public string? Idea { get; init; }

        public (string, string)? Answered { get; init; }

        public (string, string)? Chosen { get; init; }

        public IReadOnlyList<string>? Ticked { get; init; }

        public (string, string)? Improved { get; init; }

        public IdeaTarget? Target { get; init; }

        public bool Yes { get; init; }

        public IReadOnlyList<string>? ProjectsOffered { get; private set; }

        public string? NewIdea() { Asked.Add("new"); return Idea; }

        public (string, bool)? Added { get; init; }

        public string? TakenBack { get; init; }

        public IReadOnlyList<string>? NotesOffered { get; private set; }

        public (string Text, bool Refine)? AddTo(IdeaPlace place, IdeaRecord record) { Asked.Add("addto"); return Added; }

        public string? TakeBack(IdeaPlace place, IReadOnlyList<IdeaNote> unread)
        {
            Asked.Add("takeback");
            NotesOffered = [.. unread.Select(note => note.Id)];

            return TakenBack;
        }

        public (string Question, string Answer)? Answer(IdeaRecord record) { Asked.Add("answer"); return Answered; }

        public (string Layer, string Option)? Choose(IdeaPlan plan) { Asked.Add("choose"); return Chosen; }

        public IReadOnlyList<string>? Pieces(IdeaPlan plan, IdeaVerdict verdict) { Asked.Add($"pieces:{verdict}"); return Ticked; }

        public (string Piece, string Request)? Improve(IdeaPlan plan) { Asked.Add("improve"); return Improved; }

        public IdeaTarget? Destination(IdeaPlace place, IdeaPlan plan, IReadOnlyList<string> projects)
        {
            Asked.Add("destination");
            ProjectsOffered = projects;

            return Target;
        }

        public IdeaHome? Home { get; init; }

        public IdeaHome? MoveTo(IdeaPlace place, IReadOnlyList<string> projects) { Asked.Add("move"); return Home; }

        public bool Confirm(string question, string yes) { Asked.Add("confirm"); return Yes; }

        public string? Typed { get; init; }

        public (TaskType? Type, TaskTypeSource? By)? TypeShown { get; private set; }

        public string? Type(IdeaPlace place, TaskType? type, TaskTypeSource? by)
        {
            Asked.Add("type");
            TypeShown = (type, by);

            return Typed;
        }
    }

    [Fact]
    public void Choosing_a_type_runs_idea_type_with_what_was_chosen()
    {
        foreach (var chosen in new[] { "bug-fix", TaskTypes.HandBack })
        {
            var guessed = Entry("asking", "website", Questions()) is var entry
                ? entry with { Summary = entry.Summary with { Type = TaskType.Feature, TypeBy = TaskTypeSource.Rule } }
                : null!;
            var prompts = new Scripted { Typed = chosen };
            var (window, app) = Open(prompts, select: "asking", ideas: [guessed]);

            using (app)
            using (window)
            {
                var step = Press(window, Key.P);

                prompts.TypeShown.Should().Be(((TaskType?)TaskType.Feature, (TaskTypeSource?)TaskTypeSource.Rule), "the question says what it is now");
                step!.Command.Should().Be(LauncherCommands.IdeaType);
                step.Arguments.Should().Equal("asking", chosen, "--project", "website");
                step.Select.Should().Be("asking");
            }
        }
    }

    [Fact]
    public void Choosing_no_type_builds_nothing()
    {
        var prompts = new Scripted { Typed = null };
        var (window, app) = Open(prompts, select: "asking");

        using (app)
        using (window)
        {
            Press(window, Key.P).Should().BeNull();
            prompts.Asked.Should().Equal("type");
        }
    }

    [Fact]
    public void Moving_names_the_list_it_leaves_and_the_one_it_goes_to()
    {
        var cases = new (IdeaHome Home, string[] Arguments)[]
        {
            (new IdeaHome("homelab"), ["asking", "--project", "website", "--to", "homelab"]),
            (new IdeaHome(null), ["asking", "--project", "website", "--to-global"]),
        };

        foreach (var (home, arguments) in cases)
        {
            var (window, app) = Open(new Scripted { Home = home }, select: "asking");

            using (app)
            using (window)
            {
                var step = Press(window, Key.M);

                step!.Command.Should().Be(LauncherCommands.IdeaMove);
                step.Arguments.Should().Equal(arguments);
                step.Select.Should().Be("asking");
            }
        }
    }

    [Fact]
    public void Moving_and_then_saying_never_mind_builds_nothing()
    {
        var prompts = new Scripted { Home = null };
        var (window, app) = Open(prompts, select: "asking");

        using (app)
        using (window)
        {
            Press(window, Key.M).Should().BeNull();
            prompts.Asked.Should().Equal("move");
        }
    }

    [Fact]
    public void A_move_is_never_offered_to_the_list_the_idea_is_on()
    {
        DialogIdeaPrompts.IdeaHomes(new IdeaPlace("website", "x"), ["homelab", "website"])
            .Should().Equal(new IdeaHome(null), new IdeaHome("homelab"));

        DialogIdeaPrompts.IdeaHomes(new IdeaPlace(null, "x"), ["homelab", "website"])
            .Should().Equal(new IdeaHome("homelab"), new IdeaHome("website"));
    }

    private static IdeaReply Questions() => new()
    {
        Contract = IdeaSchema.Version,
        Questions = [new IdeaReply.ReplyQuestion { Question = "Public or private?", Recommendation = "private" }],
    };

    private static IdeaReply Plan() => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A page.",
        Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            Layers =
            [
                new IdeaReply.ReplyLayer
                {
                    Name = "Storage",
                    Options = [new() { Title = "SQLite", Recommended = true }, new() { Title = "Postgres" }],
                },
            ],
            Additions = [new IdeaReply.ReplyAddition { Title = "Alerts" }],
        },
    };

    private static IdeaEntry Entry(string id, string? project, params IdeaReply[] replies)
    {
        var record = new IdeaRecord { Id = id, Ask = $"The idea called {id}" };

        foreach (var reply in replies)
        {
            IdeaWork.Merge(record, reply, Now);
        }

        var stage = IdeaWork.StageOf(record);

        return new IdeaEntry(
            new IdeaSummary(new IdeaPlace(project, id), $"Title of {id}", stage, TaskState.Open, "me", Now, IdeaWork.Unanswered(record).Count),
            record);
    }

    /// <summary>A captured idea, one waiting on answers, and one with a plan, on two lists.</summary>
    private static IReadOnlyList<IdeaEntry> Three() =>
    [
        Entry("captured", null),
        Entry("asking", "website", Questions()),
        Entry("planned", null, Plan()),
    ];

    private static (IdeasWindow Window, IApplication App) Open(
        IIdeaPrompts prompts,
        string? select = null,
        IReadOnlyList<IdeaEntry>? ideas = null,
        IReadOnlyList<IdeaMovedOn>? movedOn = null)
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 140, 40);

        var window = new IdeasWindow(ideas ?? Three(), ["homelab", "website"], "website", select, prompts, app, movedOn);

        app.Begin(window);
        app.LayoutAndDraw();

        return (window, app);
    }

    private static ListView Rows(IdeasWindow window) =>
        window.SubViews.SelectMany(v => v.SubViews).OfType<ListView>().Single(v => v.Id == "ideas-list");

    private static ListView Detail(IdeasWindow window) =>
        window.SubViews.SelectMany(v => v.SubViews).OfType<ListView>().Single(v => v.Id == "ideas-detail");

    private static string Text(ListView list) =>
        string.Join('\n', list.Source!.ToList().Cast<object?>().Select(line => line?.ToString()));

    [Fact]
    public void An_idea_that_became_work_elsewhere_is_listed_after_the_ideas_and_nothing_acts_on_it()
    {
        var prompts = new Scripted { Yes = true, Home = new IdeaHome("homelab"), Answered = ("Q1", "x") };
        var gone = new IdeaMovedOn("status-page", "Status page", "website", "homelab", Now);
        var (window, app) = Open(prompts, movedOn: [gone]);

        using (app)
        using (window)
        {
            Text(Rows(window)).Should().Contain("status-page").And.Contain("now a task");

            Rows(window).SelectedItem = Three().Count;
            app.LayoutAndDraw();

            window.MovedOnSelected.Should().Be(gone);
            window.Selected.Should().BeNull("it is not an idea any more");
            Text(Detail(window)).Should().Contain("a task on homelab").And.Contain("loadout task list --project homelab");

            foreach (var key in new[] { Key.Delete, Key.M, Key.A, Key.R, Key.Y })
            {
                Press(window, key).Should().BeNull($"{key} has no idea to act on");
            }

            prompts.Asked.Should().BeEmpty();
        }
    }

    [Fact]
    public void A_moved_idea_says_where_it_came_from()
    {
        var moved = Entry("asking", "website", Questions()) is var entry
            ? entry with { Summary = entry.Summary with { Moved = new TaskMove { From = "homelab", Utc = Now } } }
            : null!;

        var (window, app) = Open(new Scripted(), select: "asking", ideas: [moved]);

        using (app)
        using (window)
        {
            Text(Detail(window)).Should().Contain("Moved here from homelab on 2026-09-26.");
        }
    }

    private static IdeaStep? Press(IdeasWindow window, Key key, ListView? on = null)
    {
        (on ?? Rows(window)).NewKeyDownEvent(key);

        return window.Chosen;
    }

    [Fact]
    public void It_opens_on_the_idea_asked_for_and_shows_it_with_the_ids_every_key_asks_for()
    {
        var (window, app) = Open(new Scripted(), select: "planned");

        using (app)
        using (window)
        {
            window.Selected!.Summary.Place.Id.Should().Be("planned");

            var shown = string.Join('\n', Enumerable.Range(0, Detail(window).Source!.Count)
                .Select(i => Detail(window).Source!.ToList()[i]?.ToString()));

            shown.Should().Contain("L1").And.Contain("L1a").And.Contain("chosen").And.Contain("A1");
        }
    }

    [Fact]
    public void Answering_builds_the_command_for_the_idea_on_the_list_it_is_on()
    {
        var prompts = new Scripted { Answered = ("Q1", "private, on the LAN") };
        var (window, app) = Open(prompts, select: "asking");

        using (app)
        using (window)
        {
            var step = Press(window, Key.A);

            step!.Command.Should().Be(LauncherCommands.IdeaAnswer);
            step.Arguments.Should().Equal("asking", "Q1", "--answer=private, on the LAN", "--project", "website");
            step.Select.Should().Be("asking", "the screen comes back on the idea being worked on");
        }
    }

    [Fact]
    public void Refining_is_offered_only_where_there_is_a_round_to_run()
    {
        var (asking, app) = Open(new Scripted(), select: "asking");

        using (app)
        using (asking)
        {
            Press(asking, Key.R).Should().BeNull("it is waiting on answers, and the command would refuse");
        }

        var (captured, again) = Open(new Scripted(), select: "captured");

        using (again)
        using (captured)
        {
            var step = Press(captured, Key.R);

            step!.Arguments.Should().Equal("captured", "--global");
            step.Pause.Should().BeTrue("a round's questions are worth stopping to read");
        }
    }

    [Fact]
    public void Choosing_keeping_dropping_and_improving_each_build_their_command()
    {
        var prompts = new Scripted
        {
            Chosen = ("L1", "L1b"),
            Ticked = ["L1", "A1"],
            Improved = ("plan", "make it smaller"),
        };

        var cases = new (Key Key, string Command, string[] Arguments)[]
        {
            (Key.C, LauncherCommands.IdeaChoose, ["planned", "L1", "L1b", "--global"]),
            (Key.K, LauncherCommands.IdeaKeep, ["planned", "L1", "A1", "--global"]),
            (Key.D, LauncherCommands.IdeaDrop, ["planned", "L1", "A1", "--global"]),
            (Key.I, LauncherCommands.IdeaImprove, ["planned", "plan", "--request=make it smaller", "--global"]),
        };

        foreach (var (key, command, arguments) in cases)
        {
            var (window, app) = Open(prompts, select: "planned");

            using (app)
            using (window)
            {
                var step = Press(window, key);

                step!.Command.Should().Be(command, $"that is what {key} is for");
                step.Arguments.Should().Equal(arguments);
            }
        }
    }

    [Fact]
    public void The_plan_keys_ask_nothing_of_an_idea_with_no_plan()
    {
        var prompts = new Scripted { Chosen = ("L1", "L1a") };
        var (window, app) = Open(prompts, select: "asking");

        using (app)
        using (window)
        {
            Press(window, Key.C).Should().BeNull();
            Press(window, Key.Y).Should().BeNull();
            prompts.Asked.Should().BeEmpty("a question with no answer that could be used is not worth asking");
        }
    }

    [Fact]
    public void Accepting_offers_every_project_and_names_where_it_goes_outright()
    {
        var prompts = new Scripted { Target = new IdeaTarget("homelab", null) };
        var (window, app) = Open(prompts, select: "planned");

        using (app)
        using (window)
        {
            var step = Press(window, Key.Y);

            prompts.ProjectsOffered.Should().Equal("homelab", "website");
            step!.Command.Should().Be(LauncherCommands.IdeaAccept);
            step.Arguments.Should().Equal("planned", "--global", "--to", "homelab");
        }

        var making = new Scripted { Target = new IdeaTarget(null, "Lab watch") };
        var (second, again) = Open(making, select: "planned");

        using (again)
        using (second)
        {
            Press(second, Key.Y)!.Arguments.Should().Equal("planned", "--global", "--new-project=Lab watch");
        }
    }

    [Fact]
    public void An_idea_with_an_improvement_pending_is_not_offered_for_acceptance()
    {
        var pending = Entry("planned", null, Plan());
        pending.Record!.Plan!.Layers[0].Verdict = IdeaVerdict.Improve;
        pending.Record.Plan.Layers[0].Request = "use files";

        var prompts = new Scripted { Target = new IdeaTarget("homelab", null) };
        var (window, app) = Open(prompts, ideas: [pending]);

        using (app)
        using (window)
        {
            Press(window, Key.Y).Should().BeNull();
            prompts.Asked.Should().NotContain("destination");
        }
    }

    [Fact]
    public void Removing_asks_first_and_a_no_does_nothing()
    {
        var (window, app) = Open(new Scripted { Yes = false }, select: "captured");

        using (app)
        using (window)
        {
            Press(window, Key.Delete).Should().BeNull();
        }

        var (second, again) = Open(new Scripted { Yes = true }, select: "captured");

        using (again)
        using (second)
        {
            Press(second, Key.Delete)!.Command.Should().Be(LauncherCommands.IdeaRemove);
        }
    }

    [Fact]
    public void A_new_idea_goes_on_the_project_selected_in_the_launcher_as_text_that_may_start_with_a_dash()
    {
        var (window, app) = Open(new Scripted { Idea = "- a bullet copied from somewhere" });

        using (app)
        using (window)
        {
            Press(window, Key.N)!.Arguments.Should().Equal(
                "--text=- a bullet copied from somewhere", "--project", "website");
        }
    }

    [Fact]
    public void The_keys_work_from_the_detail_pane_as_well()
    {
        var (window, app) = Open(new Scripted { Answered = ("Q1", "yes") }, select: "asking");

        using (app)
        using (window)
        {
            Press(window, Key.A, Detail(window))!.Command.Should().Be(LauncherCommands.IdeaAnswer,
                "somebody scrolling a long plan should not have to go back up to act on it");
        }
    }

    private static IdeaEntry WithNotes(IdeaEntry entry, params (string Id, bool Read)[] notes)
    {
        foreach (var (id, read) in notes)
        {
            entry.Record!.Notes.Add(new IdeaNote
            {
                Id = id,
                Text = $"what {id} says",
                By = "me",
                AddedUtc = Now,
                TakenUtc = read ? Now : null,
            });
        }

        return entry;
    }

    [Fact]
    public void Adding_to_an_idea_says_whether_to_refine_and_passes_the_text_whole()
    {
        var cases = new (bool Refine, string Flag, bool Pause)[] { (true, "--refine", true), (false, "--no-refine", false) };

        foreach (var (refine, flag, pause) in cases)
        {
            var (window, app) = Open(new Scripted { Added = ("- and a status badge", refine) }, select: "asking");

            using (app)
            using (window)
            {
                var step = Press(window, Key.E);

                step!.Command.Should().Be(LauncherCommands.IdeaAdd);
                step.Arguments.Should().Equal("--to", "asking", "--text=- and a status badge", flag, "--project", "website");
                step.Pause.Should().Be(pause, "a round's questions or plan are worth stopping to read");
                step.Select.Should().Be("asking");
            }
        }
    }

    [Fact]
    public void Adding_is_not_offered_for_an_accepted_idea()
    {
        var accepted = Entry("planned", null, Plan());
        accepted.Record!.Accepted = new IdeaAcceptance();

        var prompts = new Scripted { Added = ("more", true) };
        var (window, app) = Open(prompts, ideas: [accepted]);

        using (app)
        using (window)
        {
            Press(window, Key.E).Should().BeNull();
            prompts.Asked.Should().BeEmpty();
        }
    }

    [Fact]
    public void Only_a_note_no_round_has_read_is_offered_to_take_back()
    {
        var prompts = new Scripted { TakenBack = "N2" };
        var (window, app) = Open(prompts, ideas: [WithNotes(Entry("asking", "website", Questions()), ("N1", true), ("N2", false))]);

        using (app)
        using (window)
        {
            var step = Press(window, Key.T);

            prompts.NotesOffered.Should().Equal("N2");
            step!.Command.Should().Be(LauncherCommands.IdeaNoteRemove);
            step.Arguments.Should().Equal("asking", "N2", "--project", "website");

            var shown = Text(Detail(window));
            shown.Should().Contain("N1 by me, 2026-09-26: what N1 says").And.Contain("Read by a round, so it stays.");
            shown.Should().Contain("N2 by me, 2026-09-26: what N2 says").And.Contain("Not read by a round yet. Take it back with t.");
        }
    }

    [Fact]
    public void A_read_note_offers_no_removal()
    {
        var prompts = new Scripted { TakenBack = "N1" };
        var (window, app) = Open(prompts, ideas: [WithNotes(Entry("asking", "website", Questions()), ("N1", true))]);

        using (app)
        using (window)
        {
            Press(window, Key.T).Should().BeNull();
            prompts.Asked.Should().BeEmpty("there is nothing it could take back");
        }
    }

    [Fact]
    public void An_unread_note_is_said_in_the_ideas_row()
    {
        var (window, app) = Open(new Scripted(), ideas:
        [
            WithNotes(Entry("asking", "website", Questions()), ("N1", true), ("N2", false)),
            WithNotes(Entry("planned", null, Plan()), ("N1", true)),
        ]);

        using (app)
        using (window)
        {
            var rows = Rows(window).Source!.ToList().Cast<object?>().Select(line => line?.ToString() ?? string.Empty).ToList();

            rows[0].Should().EndWith("(1 note not read yet)");
            rows[1].Should().NotContain("not read yet");
        }
    }

    private static T Find<T>(View root, string id)
        where T : View =>
        Descendants(root).OfType<T>().Single(view => view.Id == id);

    private static IEnumerable<View> Descendants(View root) =>
        root.SubViews.SelectMany(view => (IEnumerable<View>)[view, .. Descendants(view)]);

    private static (AddToIdeaDialog Dialog, IApplication App) AddDialog(IdeaRecord record)
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 140, 40);

        var dialog = new AddToIdeaDialog(new IdeaPlace("website", record.Id), record, app);

        app.Begin(dialog);
        app.LayoutAndDraw();

        return (dialog, app);
    }

    [Fact]
    public void The_refine_box_starts_ticked_and_unticking_it_says_not_to_refine()
    {
        var (dialog, app) = AddDialog(Entry("captured", null).Record!);

        using (app)
        using (dialog)
        {
            var box = Find<CheckBox>(dialog, "refine-now");

            box.Value.Should().Be(CheckState.Checked, "at a keyboard the command says Yes by default");

            Find<TextField>(dialog, "add-text").Text = "and a badge";
            box.Value = CheckState.UnChecked;
            Find<Button>(dialog, "add-ok").InvokeCommand(Command.Accept);

            dialog.Chosen.Should().Be(("and a badge", false));
        }

        var (ticked, again) = AddDialog(Entry("captured", null).Record!);

        using (again)
        using (ticked)
        {
            Find<TextField>(ticked, "add-text").Text = "and a badge";
            Find<Button>(ticked, "add-ok").InvokeCommand(Command.Accept);

            ticked.Chosen.Should().Be(("and a badge", true));
        }
    }

    [Fact]
    public void The_add_step_reminds_of_open_questions_and_says_nothing_when_there_are_none()
    {
        var (asking, app) = AddDialog(Entry("asking", "website", Questions()).Record!);

        using (app)
        using (asking)
        {
            Find<Label>(asking, "add-reminder").Text.Should().StartWith("1 question still unanswered");
        }

        var (captured, again) = AddDialog(Entry("captured", null).Record!);

        using (again)
        using (captured)
        {
            Descendants(captured).Should().NotContain(view => view.Id == "add-reminder");
        }
    }
}
