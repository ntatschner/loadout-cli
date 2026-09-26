using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Ideas and the workspace-wide task list, against real files in a temporary
/// workspace: capture, the person's side of each round, and acceptance.
/// </summary>
public sealed class IdeaServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly WorkspaceManager _workspace;
    private readonly TaskService _tasks;
    private readonly IdeaService _ideas;

    public IdeaServiceTests()
    {
        _workspace = _space.Workspace;
        _tasks = _space.Tasks;
        _ideas = _space.Ideas;
    }

    public void Dispose() => _space.Dispose();

    private Task ProjectAsync(string slug) => _space.ProjectAsync(slug);

    private static IdeaReply Plan(string slug = "") => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A page showing what is up.",
        Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            Layers =
            [
                new IdeaReply.ReplyLayer
                {
                    Name = "Storage",
                    Options =
                    [
                        new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true },
                        new IdeaReply.ReplyOption { Title = "Postgres" },
                    ],
                },
            ],
            Additions = [new IdeaReply.ReplyAddition { Title = "Alerts", Why = "So you hear first." }],
            Project = new IdeaReply.ReplyProject { Slug = slug },
        },
    };

    [Fact]
    public async Task The_workspace_wide_list_is_its_own_file_and_leaves_projects_alone()
    {
        await ProjectAsync("homelab");

        (await _tasks.DeclareAsync(null, "someday", TaskState.Open, "nigel", "Learn Rust")).Succeeded.Should().BeTrue();

        File.Exists(Path.Combine(_workspace.LocalPath, "tasks.yaml")).Should().BeTrue();
        (await _tasks.ListAsync(null)).Value!.Single().Id.Should().Be("someday");
        (await _tasks.ListAsync("homelab")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Declaring_a_state_leaves_the_kind_alone()
    {
        (await _tasks.DeclareAsync(null, "thought", TaskState.Open, "me", "x", null, default, TaskKind.Idea))
            .Succeeded.Should().BeTrue();

        (await _tasks.DeclareAsync(null, "thought", TaskState.Blocked, "me")).Succeeded.Should().BeTrue();

        (await _tasks.ListAsync(null)).Value!.Single().Kind.Should().Be(TaskKind.Idea);
    }

    [Fact]
    public async Task A_move_refuses_to_overwrite_a_task_of_the_same_name()
    {
        await ProjectAsync("homelab");
        await _tasks.DeclareAsync(null, "same", TaskState.Open, "me", "from the workspace");
        await _tasks.DeclareAsync("homelab", "same", TaskState.Open, "me", "already here");

        var moved = await _tasks.MoveAsync(null, "homelab", "same");

        moved.Failed.Should().BeTrue();
        (await _tasks.ListAsync(null)).Value!.Should().ContainSingle("nothing was taken off the source");
        (await _tasks.ListAsync("homelab")).Value!.Single().Title.Should().Be("already here");
    }

    [Fact]
    public async Task Capturing_names_the_idea_from_its_words_and_never_twice_the_same()
    {
        var first = await _ideas.CaptureAsync(null, "A status page for the home lab, with alerts", "me");
        var second = await _ideas.CaptureAsync(null, "A status page for the home lab again", "me");

        first.Value!.Id.Should().Be("a-status-page-for-the");
        second.Value!.Id.Should().Be("a-status-page-for-the-2");

        var task = (await _tasks.ListAsync(null)).Value!.First();
        task.Kind.Should().Be(TaskKind.Idea);
        task.Title.Should().Be("A status page for the home lab, with alerts");
    }

    [Fact]
    public async Task A_credential_in_an_idea_is_refused_and_nothing_is_written()
    {
        var token = "ghp_" + new string('c', 36);

        var captured = await _ideas.CaptureAsync(null, $"Use the token {token} for the page", "me");

        captured.Failed.Should().BeTrue();
        captured.ExitCode.Should().Be(ExitCode.PolicyViolation);
        captured.Error.Should().NotContain(token, "the refusal names the pattern, never the value");
        (await _tasks.ListAsync(null)).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task A_credential_in_an_agents_answer_is_refused_and_said_on_the_record()
    {
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var reply = Plan();
        reply.Understanding = "It reads ghp_" + new string('d', 36) + " from the environment.";

        var recorded = await _ideas.RecordReplyAsync(place, reply);

        recorded.Failed.Should().BeTrue();
        var record = (await _ideas.ReadAsync(place)).Value!;
        record.Plan.Should().BeNull();
        record.LastError.Should().Contain("credential");
    }

    [Fact]
    public async Task Answers_choices_and_verdicts_are_kept_on_the_record()
    {
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);

        await _ideas.RecordReplyAsync(place, new IdeaReply
        {
            Contract = IdeaSchema.Version,
            Questions = [new IdeaReply.ReplyQuestion { Question = "Public?" }],
        });

        (await _ideas.AnswerAsync(place, "q1", "private")).Succeeded.Should().BeTrue("ids are matched without regard to case");
        (await _ideas.RecordReplyAsync(place, Plan())).Succeeded.Should().BeTrue();
        (await _ideas.ChooseAsync(place, "L1", "b")).Succeeded.Should().BeTrue("the letter alone is how it reads off the screen");
        (await _ideas.JudgeAsync(place, "A1", IdeaVerdict.Keep)).Succeeded.Should().BeTrue();

        var record = (await _ideas.ReadAsync(place)).Value!;
        record.Rounds[0].Questions[0].Answer.Should().Be("private");
        record.Plan!.Layers[0].Chosen.Should().Be("L1b");
        record.Plan.Additions[0].Verdict.Should().Be(IdeaVerdict.Keep);
    }

    [Fact]
    public async Task Pieces_that_do_not_exist_are_refused_with_the_ones_that_do()
    {
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        await _ideas.RecordReplyAsync(place, Plan());

        var judged = await _ideas.JudgeAsync(place, "L9", IdeaVerdict.Drop);
        judged.Failed.Should().BeTrue();
        judged.Error.Should().Contain("L1").And.Contain("A1");

        (await _ideas.ChooseAsync(place, "L1", "z")).Error.Should().Contain("L1a, L1b");
        (await _ideas.JudgeAsync(place, "L1", IdeaVerdict.Improve, "")).Failed.Should().BeTrue(
            "an improvement with no request gives the agent nothing to act on");
    }

    [Fact]
    public async Task Accepting_a_workspace_wide_idea_moves_it_onto_the_project_as_work_with_its_plan()
    {
        await ProjectAsync("homelab");
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        await _ideas.RecordReplyAsync(place, Plan());
        await _ideas.JudgeAsync(place, "A1", IdeaVerdict.Keep);

        var accepted = await _ideas.AcceptAsync(place, "homelab", "nigel");

        accepted.Succeeded.Should().BeTrue(accepted.Error);
        accepted.Value!.PlanPath.Should().Be($"projects/homelab/ideas/{idea.Id}.md");

        (await _tasks.ListAsync(null)).Value!.Should().BeEmpty("it left the workspace-wide list");

        var task = (await _tasks.ListAsync("homelab")).Value!.Single();
        task.Id.Should().Be(idea.Id, "the id and the history travel with it");
        task.Kind.Should().Be(TaskKind.Task);
        task.Title.Should().Be("Status page");
        task.Note.Should().Contain(accepted.Value.PlanPath);

        var document = await File.ReadAllTextAsync(Path.Combine(_workspace.LocalPath, "projects", "homelab", "ideas", $"{idea.Id}.md"));
        document.Should().Contain("**SQLite.**").And.Contain("**Alerts.**");

        File.Exists(Path.Combine(_workspace.LocalPath, "ideas", $"{idea.Id}.yaml")).Should().BeFalse();
        var moved = (await _ideas.ReadAsync(new IdeaPlace("homelab", idea.Id))).Value!;
        IdeaWork.StageOf(moved).Should().Be(IdeaStage.Accepted);
        (await _ideas.ListAsync()).Value!.Should().BeEmpty("an accepted idea is work now, not an idea");
    }

    [Fact]
    public async Task An_idea_with_something_the_plan_has_not_taken_in_is_not_accepted()
    {
        await ProjectAsync("homelab");
        var idea = (await _ideas.CaptureAsync("homelab", "Status page", "me")).Value!;
        var place = new IdeaPlace("homelab", idea.Id);

        (await _ideas.AcceptAsync(place, "homelab", "me")).Error.Should().Contain("no plan");

        await _ideas.RecordReplyAsync(place, Plan());
        await _ideas.JudgeAsync(place, "L1", IdeaVerdict.Improve, "Use files");

        var refused = await _ideas.AcceptAsync(place, "homelab", "me");

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("Refine it first");
        (await _tasks.ListAsync("homelab")).Value!.Single().Kind.Should().Be(TaskKind.Idea);
    }

    [Fact]
    public async Task An_idea_is_found_on_whichever_list_it_is_on_the_preferred_one_first()
    {
        await ProjectAsync("homelab");
        await _ideas.CaptureAsync("homelab", "Status page", "me", "same-name");
        await _ideas.CaptureAsync(null, "Status page", "me", "same-name");

        (await _ideas.LocateAsync("same-name", "homelab")).Value!.Project.Should().Be("homelab");
        (await _ideas.LocateAsync("same-name", null)).Value!.Project.Should().BeNull();
        (await _ideas.LocateAsync("nothing", null)).Failed.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_an_idea_takes_its_task_and_its_record()
    {
        var idea = (await _ideas.CaptureAsync(null, "Status page", "me")).Value!;

        (await _ideas.RemoveAsync(new IdeaPlace(null, idea.Id))).Succeeded.Should().BeTrue();

        (await _tasks.ListAsync(null)).Value!.Should().BeEmpty();
        File.Exists(Path.Combine(_workspace.LocalPath, "ideas", $"{idea.Id}.yaml")).Should().BeFalse();
    }
}
