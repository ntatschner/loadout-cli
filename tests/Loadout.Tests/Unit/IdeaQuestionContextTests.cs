using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What each question says it relates to, what is kept of that when it is
/// asked, and when a later change makes the question possibly outdated.
/// </summary>
public sealed class IdeaQuestionContextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static IdeaReply.ReplyQuestion Asking(string question, string relatesTo, string context = "So the plan fits.") => new()
    {
        Question = question,
        Why = "It changes the design.",
        Options = ["yes", "no"],
        Recommendation = "yes",
        RelatesTo = relatesTo,
        Context = context,
    };

    private static IdeaReply Asked(params IdeaReply.ReplyQuestion[] questions) => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A status page for the lab.",
        Questions = [.. questions],
    };

    private static IdeaReply.ReplyLayer Layer(string name, string purpose = "Keeps things.", params string[] options) => new()
    {
        Name = name,
        Purpose = purpose,
        Options = [.. (options.Length == 0 ? ["SQLite", "Postgres"] : options).Select((title, at) => new IdeaReply.ReplyOption
        {
            Title = title,
            Detail = $"{title} detail.",
            Recommended = at == 0,
        })],
    };

    private static IdeaReply Planned(IdeaReply.ReplyLayer[] layers, string[]? additions = null, params IdeaReply.ReplyQuestion[] questions)
    {
        var reply = Asked(questions);

        reply.Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            Layers = [.. layers],
            Additions = [.. (additions ?? ["Uptime alerts"]).Select(title => new IdeaReply.ReplyAddition { Title = title, Why = "Worth it." })],
            Project = new IdeaReply.ReplyProject { Slug = "homelab", Reason = "It watches the lab." },
        };

        return reply;
    }

    private static IdeaRecord Fresh() => new() { Id = "status-page", Ask = "A status page for the home lab" };

    private static IdeaRecord WithPlan()
    {
        var record = Fresh();

        IdeaWork.Merge(record, Planned([Layer("Storage"), Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);

        return record;
    }

    private static IdeaQuestion Last(IdeaRecord record) => record.Rounds[^1].Questions[^1];

    [Fact]
    public void A_question_about_a_layer_says_so_by_id_and_name_with_its_context()
    {
        var record = WithPlan();

        IdeaWork.Merge(record, Asked(Asking("Must it survive a restart?", "l2", "The interface keeps state between visits.")), Now);

        var question = Last(record);
        var about = IdeaWork.About(record, question);

        question.RelatesTo.Should().Be("L2", "the reference is kept in one spelling");
        question.Snapshot!.Name.Should().Be("Interface");
        about.Label.Should().Be("L2 Interface");
        about.Line.Should().Be("Relates to L2 Interface: The interface keeps state between visits.");
        about.Outdated.Should().BeNull();
    }

    [Theory]
    [InlineData("ask", "the ask")]
    [InlineData("The ask", "the ask")]
    [InlineData("N1", "N1")]
    [InlineData("L1b", "L1b Postgres in Storage")]
    [InlineData("A1", "A1 Uptime alerts")]
    [InlineData("Q1", "Q1")]
    public void Every_kind_of_reference_is_labelled_readably(string given, string label)
    {
        var record = WithPlan();
        IdeaWork.AddNote(record, "It should work offline.", "me", Now);
        IdeaWork.Merge(record, Asked(Asking("First?", string.Empty)), Now);
        record.Rounds[^1].Questions[0].Answer = "yes";

        IdeaWork.Merge(record, Asked(Asking("Second?", given)), Now);

        IdeaWork.About(record, Last(record)).Label.Should().Be(label);
    }

    [Theory]
    [InlineData("N9")]
    [InlineData("L7")]
    [InlineData("L1z")]
    [InlineData("A4")]
    [InlineData("Q12")]
    [InlineData("the storage layer")]
    public void A_reference_the_record_does_not_have_is_cleared_and_the_context_kept(string given)
    {
        var record = WithPlan();

        IdeaWork.Merge(record, Asked(Asking("Anything?", given, "Kept all the same.")), Now);

        var question = Last(record);

        question.RelatesTo.Should().BeEmpty();
        question.Snapshot.Should().BeNull();
        IdeaWork.About(record, question).Line.Should().Be("Kept all the same.");
    }

    [Fact]
    public void A_question_asked_alongside_a_plan_refers_to_that_plan()
    {
        var record = WithPlan();

        IdeaWork.Merge(record, Planned([Layer("Hosting", "Runs it.", "Docker", "Bare metal")], [], Asking("Which host?", "L1")), Now);

        var question = Last(record);

        question.Snapshot!.Name.Should().Be("Hosting", "the plan in the same answer is the one its ids number");
        question.Snapshot.Revision.Should().Be(2);
    }

    [Fact]
    public void A_question_may_refer_to_one_asked_earlier_in_the_same_answer()
    {
        var record = Fresh();

        IdeaWork.Merge(record, Asked(Asking("Public?", "ask"), Asking("Then behind a login?", "Q1")), Now);

        Last(record).RelatesTo.Should().Be("Q1");
    }

    [Fact]
    public void A_round_that_only_asks_keeps_its_understanding_and_one_with_a_plan_does_not()
    {
        var record = Fresh();

        IdeaWork.Merge(record, Asked(Asking("Public?", "ask")), Now);
        IdeaWork.Merge(record, Planned([Layer("Storage")], null, Asking("Backups?", "L1")), Now);

        record.Rounds[0].Understanding.Should().Be("A status page for the lab.");
        record.Rounds[1].Understanding.Should().BeEmpty("the plan carries its own");
    }

    [Fact]
    public void A_removed_note_makes_its_question_possibly_outdated()
    {
        var record = Fresh();
        IdeaWork.AddNote(record, "It should work offline.", "me", Now);
        IdeaWork.Merge(record, Asked(Asking("Offline for how long?", "N1")), Now);

        IdeaWork.Outdated(record, Last(record)).Should().BeNull();

        IdeaWork.RemoveNote(new IdeaPlace(null, record.Id), record, "N1").Succeeded.Should().BeTrue();

        IdeaWork.Outdated(record, Last(record)).Should().Be("N1 was removed");
        IdeaWork.OutdatedCount(record).Should().Be(1);
    }

    [Fact]
    public void A_layer_reworked_in_a_later_revision_makes_its_question_possibly_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1")), Now);

        IdeaWork.Merge(record, Planned([Layer("Storage", "Keeps checks for a year."), Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);

        IdeaWork.Outdated(record, Last(record)).Should().Be("layer Storage changed in revision 2");
    }

    [Fact]
    public void A_layer_dropped_in_a_later_revision_makes_its_question_possibly_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1")), Now);

        IdeaWork.Merge(record, Planned([Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);

        IdeaWork.Outdated(record, Last(record)).Should().Be("layer Storage was dropped in revision 2");
    }

    [Fact]
    public void A_layer_only_reordered_is_the_same_layer_and_not_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1")), Now);

        IdeaWork.Merge(record, Planned([Layer("Interface", "Shows it.", "Web", "Terminal"), Layer("Storage")]), Now);

        record.Plan!.Layers[1].Name.Should().Be("Storage", "it is L2 now");
        IdeaWork.Outdated(record, Last(record)).Should().BeNull();
    }

    [Fact]
    public void An_option_dropped_from_its_layer_makes_its_question_possibly_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Is Postgres already running?", "L1b")), Now);

        IdeaWork.Merge(record, Planned([Layer("Storage", "Keeps things.", "SQLite", "Files")]), Now);

        IdeaWork.Outdated(record, Last(record)).Should().Be("option Postgres of layer Storage was dropped in revision 2");
    }

    [Fact]
    public void An_addition_dropped_in_a_later_revision_makes_its_question_possibly_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Alerts by email?", "A1")), Now);

        IdeaWork.Merge(record, Planned([Layer("Storage")], ["Dark mode"]), Now);

        IdeaWork.Outdated(record, Last(record)).Should().Be("addition Uptime alerts was dropped in revision 2");
    }

    [Fact]
    public void An_earlier_answer_changed_makes_a_question_about_it_possibly_outdated()
    {
        var record = Fresh();
        IdeaWork.Merge(record, Asked(Asking("Public?", "ask")), Now);
        record.Rounds[0].Questions[0].Answer = "yes";
        IdeaWork.Merge(record, Asked(Asking("Behind a login, then?", "Q1")), Now);

        IdeaWork.Outdated(record, Last(record)).Should().BeNull();

        record.Rounds[0].Questions[0].Answer = "no";

        IdeaWork.Outdated(record, Last(record)).Should().Be("the answer to Q1 has changed");
    }

    [Fact]
    public void A_question_about_the_ask_is_never_outdated()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Public?", "ask")), Now);

        IdeaWork.Merge(record, Planned([Layer("Hosting")], []), Now);

        IdeaWork.Outdated(record, Last(record)).Should().BeNull();
    }

    [Fact]
    public void Answering_again_takes_a_fresh_snapshot_and_clears_the_marker()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1")), Now);
        IdeaWork.Merge(record, Planned([Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);
        var question = Last(record);

        IdeaWork.Outdated(record, question).Should().NotBeNull();

        IdeaWork.Retake(record, question);

        IdeaWork.Outdated(record, question).Should().BeNull("the person answered knowing the layer had gone");
        IdeaWork.About(record, question).Label.Should().Be("L1 Storage", "what it was about is still said");
    }

    [Fact]
    public void A_question_from_before_idea_3_has_no_reference_context_or_marker()
    {
        var record = Fresh();
        record.Rounds.Add(new IdeaRound { Questions = [new IdeaQuestion { Id = "Q1", Question = "Public?", Answer = "yes" }] });

        var about = IdeaWork.About(record, record.Rounds[0].Questions[0]);

        about.Label.Should().BeNull();
        about.Line.Should().BeNull();
        about.Outdated.Should().BeNull();
        IdeaWork.OutdatedCount(record).Should().Be(0);
    }

    [Fact]
    public void The_prompt_lists_ids_and_each_past_question_with_its_reference_context_and_any_outdated_reason()
    {
        var record = WithPlan();
        IdeaWork.AddNote(record, "It should work offline.", "me", Now);
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1", "Storage sizes depend on it.")), Now);
        record.Rounds[^1].Questions[0].Answer = "yes";
        IdeaWork.Merge(record, Planned([Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);

        var prompt = IdeaWork.Prompt(record, [], null);

        prompt.Should().Contain("L1 layer 'Interface': options L1a \"Web\", L1b \"Terminal\"");
        prompt.Should().Contain("A1 addition 'Uptime alerts'");
        prompt.Should().Contain("N1 (by me");
        prompt.Should().Contain("Relates to L1 Storage: Storage sizes depend on it.");
        prompt.Should().Contain("Possibly outdated: layer Storage was dropped in revision 2, so this answer may need revisiting.");
        prompt.Should().Contain("Set relatesTo");
    }

    [Fact]
    public void The_plan_document_says_what_each_decision_related_to_and_flags_an_outdated_one()
    {
        var record = WithPlan();
        IdeaWork.Merge(record, Asked(Asking("Keep a week of checks?", "L1", "Storage sizes depend on it."), Asking("Web first?", "L2", "The interface comes first.")), Now);
        record.Rounds[^1].Questions[0].Answer = "yes";
        record.Rounds[^1].Questions[1].Answer = "yes";
        IdeaWork.Merge(record, Planned([Layer("Interface", "Shows it.", "Web", "Terminal")]), Now);

        var document = IdeaWork.Document(record);

        document.Should().Contain("- Keep a week of checks? **yes**\n  Relates to L1 Storage: Storage sizes depend on it.\n  Possibly outdated: layer Storage was dropped in revision 2.".ReplaceLineEndings());
        document.Should().Contain("- Web first? **yes**\n  Relates to L2 Interface: The interface comes first.\n".ReplaceLineEndings());
    }
}
