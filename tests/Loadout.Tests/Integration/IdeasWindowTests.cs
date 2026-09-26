using System.Drawing;
using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
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

        public bool Confirm(string question, string yes) { Asked.Add("confirm"); return Yes; }
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
        IReadOnlyList<IdeaEntry>? ideas = null)
    {
        var app = Application.Create();
        app.Init(DriverRegistry.Names.ANSI);
        app.Screen = new Rectangle(0, 0, 140, 40);

        var window = new IdeasWindow(ideas ?? Three(), ["homelab", "website"], "website", select, prompts, app);

        app.Begin(window);
        app.LayoutAndDraw();

        return (window, app);
    }

    private static ListView Rows(IdeasWindow window) =>
        window.SubViews.SelectMany(v => v.SubViews).OfType<ListView>().Single(v => v.Id == "ideas-list");

    private static ListView Detail(IdeasWindow window) =>
        window.SubViews.SelectMany(v => v.SubViews).OfType<ListView>().Single(v => v.Id == "ideas-detail");

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
}
