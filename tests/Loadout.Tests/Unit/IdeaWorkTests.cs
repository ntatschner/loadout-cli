using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where an idea stands, and how an agent's answer is folded into it. No agent
/// and no file: this is where the decisions are.
/// </summary>
public sealed class IdeaWorkTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static IdeaReply Questions(params string[] asked) => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A status page.",
        Questions = [.. asked.Select(q => new IdeaReply.ReplyQuestion
        {
            Question = q,
            Why = "It changes the storage.",
            Options = ["yes", "no"],
            Recommendation = "yes",
        })],
    };

    private static IdeaReply Plan(
        string[]? layers = null,
        string[]? additions = null,
        string recommendedSecond = "",
        params string[] questions)
    {
        var reply = Questions(questions);

        reply.Plan = new IdeaReply.ReplyPlan
        {
            Title = "Home lab status page",
            Layers =
            [
                .. (layers ?? ["Storage", "Interface"]).Select(name => new IdeaReply.ReplyLayer
                {
                    Name = name,
                    Purpose = $"The {name.ToLowerInvariant()}.",
                    Options =
                    [
                        new IdeaReply.ReplyOption { Title = "SQLite", Recommended = recommendedSecond.Length == 0 },
                        new IdeaReply.ReplyOption { Title = "Postgres", Recommended = recommendedSecond == name },
                    ],
                }),
            ],
            Additions = [.. (additions ?? ["Uptime alerts"]).Select(title => new IdeaReply.ReplyAddition
            {
                Title = title,
                Why = "Worth having.",
            })],
            Project = new IdeaReply.ReplyProject { Slug = "homelab", Reason = "It watches the lab." },
        };

        return reply;
    }

    private static IdeaRecord Fresh() => new() { Id = "status-page", Ask = "A status page for the home lab" };

    [Fact]
    public void A_fresh_idea_is_captured_and_questions_make_it_wait_on_the_person()
    {
        var record = Fresh();

        IdeaWork.StageOf(record).Should().Be(IdeaStage.Captured);

        IdeaWork.Merge(record, Questions("Public or private?", "Which services?"), Now);

        IdeaWork.StageOf(record).Should().Be(IdeaStage.Answering);
        record.Rounds.Single().Questions.Select(q => q.Id).Should().Equal("Q1", "Q2");
        record.Rounds[0].Revision.Should().Be(0, "no plan existed when they were asked");
    }

    [Fact]
    public void Answering_every_question_makes_the_next_round_ready()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Questions("Public or private?", "Which services?"), Now);

        record.Rounds[0].Questions[0].Answer = "private";
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Answering, "one is still unanswered");

        record.Rounds[0].Questions[1].Answer = "all of them";
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Ready);
    }

    [Fact]
    public void Questions_are_numbered_across_rounds_so_each_stays_quotable()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Questions("One?", "Two?"), Now);
        IdeaWork.Merge(record, Questions("Three?"), Now.AddMinutes(5));

        record.Rounds.SelectMany(r => r.Questions).Select(q => q.Id).Should().Equal("Q1", "Q2", "Q3");
    }

    [Fact]
    public void A_plan_chooses_the_recommended_option_and_numbers_every_piece()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(recommendedSecond: "Interface"), Now);

        var plan = record.Plan!;

        plan.Revision.Should().Be(1);
        plan.Layers.Select(l => l.Id).Should().Equal("L1", "L2");
        plan.Layers[0].Options.Select(o => o.Id).Should().Equal("L1a", "L1b");
        plan.Layers[0].Chosen.Should().Be("L1a");
        plan.Layers[1].Chosen.Should().Be("L2b", "the second option was the one recommended for it");
        plan.Additions.Single().Id.Should().Be("A1");
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Proposed);
    }

    [Fact]
    public void Keeping_dropping_and_choosing_do_not_need_another_round_but_improving_does()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(), Now);

        record.Plan!.Layers[0].Verdict = IdeaVerdict.Keep;
        record.Plan.Layers[1].Verdict = IdeaVerdict.Drop;
        record.Plan.Layers[0].Chosen = "L1b";
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Proposed,
            "those are decisions taken at acceptance, not something to ask the agent about");

        record.Plan.Additions[0].Verdict = IdeaVerdict.Improve;
        record.Plan.Additions[0].Request = "Email, not SMS";
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Ready);
    }

    [Fact]
    public void A_request_about_the_whole_plan_makes_it_ready_and_the_next_plan_clears_it()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(), Now);

        record.Request = "Make it simpler";
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Ready);

        IdeaWork.Merge(record, Plan(), Now.AddMinutes(5));

        record.Request.Should().BeEmpty("the revision answered it");
        record.Plan!.Revision.Should().Be(2);
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Proposed);
    }

    [Fact]
    public void Questions_asked_alongside_a_plan_make_it_ready_once_answered()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(questions: "Any budget?"), Now);

        IdeaWork.StageOf(record).Should().Be(IdeaStage.Answering);

        record.Rounds[0].Questions[0].Answer = "none";

        IdeaWork.StageOf(record).Should().Be(IdeaStage.Ready,
            "the plan was made before this answer, so it has not taken it in");
    }

    [Fact]
    public void Answers_to_questions_from_before_the_plan_do_not_make_it_ready()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Questions("Public?"), Now);
        record.Rounds[0].Questions[0].Answer = "no";
        IdeaWork.Merge(record, Plan(), Now.AddMinutes(5));

        IdeaWork.StageOf(record).Should().Be(IdeaStage.Proposed);
    }

    [Fact]
    public void A_revision_carries_keep_drop_and_choice_by_name_and_forgets_what_was_improved()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(layers: ["Storage", "Interface", "Hosting"], additions: ["Alerts", "Backups"]), Now);

        var first = record.Plan!;
        first.Layers[0].Verdict = IdeaVerdict.Keep;
        first.Layers[0].Chosen = "L1b";
        first.Layers[1].Verdict = IdeaVerdict.Improve;
        first.Layers[1].Request = "Use a terminal UI";
        first.Layers[2].Verdict = IdeaVerdict.Drop;
        first.Additions[0].Verdict = IdeaVerdict.Keep;

        // Reordered, as a revision is free to do.
        IdeaWork.Merge(record, Plan(layers: ["Hosting", "Interface", "Storage"], additions: ["Backups", "Alerts"]), Now.AddMinutes(5));

        var second = record.Plan!;
        var storage = second.Layers.Single(l => l.Name == "Storage");

        storage.Verdict.Should().Be(IdeaVerdict.Keep);
        storage.Chosen.Should().Be(storage.Id + "b", "Postgres was chosen, and it is still offered");
        second.Layers.Single(l => l.Name == "Interface").Verdict.Should().Be(IdeaVerdict.Undecided,
            "the improvement was asked for and this is the answer");
        second.Layers.Single(l => l.Name == "Hosting").Verdict.Should().Be(IdeaVerdict.Drop);
        second.Additions.Single(a => a.Title == "Alerts").Verdict.Should().Be(IdeaVerdict.Keep);
        second.Additions.Single(a => a.Title == "Backups").Verdict.Should().Be(IdeaVerdict.Undecided);
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Proposed);
    }

    [Fact]
    public void The_prompt_carries_the_whole_state_because_every_round_is_a_fresh_agent()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Questions("Public or private?"), Now);
        record.Rounds[0].Questions[0].Answer = "private, on the LAN";
        IdeaWork.Merge(record, Plan(), Now.AddMinutes(5));
        record.Plan!.Layers[1].Verdict = IdeaVerdict.Improve;
        record.Plan.Layers[1].Request = "no JavaScript";

        var prompt = IdeaWork.Prompt(record, [new IdeaProjectHint("homelab", "Home lab")], null);

        prompt.Should().Contain("A status page for the home lab");
        prompt.Should().Contain("private, on the LAN");
        prompt.Should().Contain("homelab: Home lab");
        prompt.Should().Contain("improve: no JavaScript");
        prompt.Should().Contain("Revise the plan");
        prompt.Should().Contain(IdeaSchema.Version);
    }

    [Fact]
    public void The_first_round_asks_for_questions_rather_than_a_plan()
    {
        IdeaWork.Prompt(Fresh(), [], null).Should().Contain("Ask the clarifying questions");
    }

    [Fact]
    public void The_document_has_what_was_kept_in_the_choices_made_and_says_what_was_left_out()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Plan(layers: ["Storage", "Hosting"], additions: ["Alerts", "Backups"]), Now);
        record.Plan!.Layers[0].Chosen = "L1b";
        record.Plan.Layers[1].Verdict = IdeaVerdict.Drop;
        record.Plan.Additions[0].Verdict = IdeaVerdict.Keep;

        var document = IdeaWork.Document(record);

        document.Should().Contain("# Home lab status page");
        document.Should().Contain("**Postgres.**");
        document.Should().Contain("Considered instead: SQLite");
        document.Should().NotContain("### Hosting");
        document.Should().Contain("**Alerts.**");
        document.Should().NotContain("**Backups.**", "an addition nobody kept was offered, not accepted");
        document.Should().Contain("## Left out on purpose").And.Contain("- Hosting");
    }

    [Fact]
    public void Where_it_was_dropped_in_outranks_the_agents_guess()
    {
        var plan = new IdeaPlan { Project = new IdeaPlacement { Slug = "homelab" } };

        IdeaWork.Destination(new IdeaPlace("homelab", "x"), plan, ["homelab"]).Project.Should().Be("homelab");

        var elsewhere = IdeaWork.Destination(new IdeaPlace("website", "x"), plan, ["homelab", "website"]);
        elsewhere.Unsettled.Should().BeTrue("the person put it on one project and the agent says another");
        elsewhere.Reason.Should().Contain("website").And.Contain("homelab");

        var silent = new IdeaPlan { Project = new IdeaPlacement() };
        IdeaWork.Destination(new IdeaPlace("website", "x"), silent, ["website"]).Project.Should().Be("website");
    }

    [Fact]
    public void A_workspace_wide_idea_goes_where_the_agent_placed_it_only_when_that_project_exists()
    {
        var global = new IdeaPlace(null, "x");

        IdeaWork.Destination(global, new IdeaPlan { Project = new IdeaPlacement { Slug = "HomeLab" } }, ["homelab"])
            .Project.Should().Be("homelab");

        IdeaWork.Destination(global, new IdeaPlan { Project = new IdeaPlacement { Slug = "ghost" } }, ["homelab"])
            .Unsettled.Should().BeTrue();

        IdeaWork.Destination(global, new IdeaPlan { Project = new IdeaPlacement { IsNew = true, Name = "Lab watch" } }, ["homelab"])
            .Should().Match<IdeaDestination>(d => d.Unsettled && d.Reason.Contains("Lab watch"),
                "making a project is the person's to decide");
    }
}
