using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Ideas;
using Loadout.Core.Ideas;
using Loadout.Models.Agents;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// One round of refinement against a scripted agent: what it is allowed, where
/// it starts, and what its answer does to the record.
/// </summary>
public sealed class IdeaRefinerTests : IDisposable
{
    private const string Questions =
        """{"contract":"idea/1","understanding":"A page.","questions":[{"question":"Public or private?","why":"Hosting.","options":["public","private"],"recommendation":"private"}]}""";

    private const string APlan =
        """{"contract":"idea/1","understanding":"A private page.","questions":[],"plan":{"title":"Status page","layers":[{"name":"Storage","purpose":"Checks.","options":[{"title":"SQLite","detail":"One file.","pros":[],"cons":[],"recommended":true}]}],"additions":[],"project":{"slug":"homelab","is_new":false,"name":"","reason":"It watches the lab."}}}""";

    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private IdeaRefiner Refiner(ScriptedDetachedLauncher launcher, string repository) =>
        new(_space.Ideas, new FakeProjects("homelab", repository), launcher, _space.Paths, _space.Locks);

    [Fact]
    public async Task A_first_round_records_the_agents_questions()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));

        var refined = await Refiner(launcher, _space.Scratch("repo")).RefineAsync(new RefineRequest(new IdeaPlace(null, idea.Id)));

        refined.Succeeded.Should().BeTrue(refined.Error);
        refined.Value!.Before.Should().Be(IdeaStage.Captured);
        refined.Value.After.Should().Be(IdeaStage.Answering);
        refined.Value.Record.Rounds.Single().Questions.Single().Recommendation.Should().Be("private");

        var said = launcher.Pipe!.Written.ToString();
        said.Should().Contain("A status page", "the idea is in the prompt");
        said.Should().Contain("homelab", "the projects it might belong to are in the prompt");
    }

    [Fact]
    public async Task The_agent_may_read_and_do_nothing_else()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));

        await Refiner(launcher, _space.Scratch("repo")).RefineAsync(new RefineRequest(new IdeaPlace(null, idea.Id)));

        var options = launcher.Options!;
        options.Permission.Should().Be(HeadlessPermission.DenyUnlessAllowed, "nothing asks, so nothing is allowed that is not named");
        options.AllowedTools.Should().BeEquivalentTo(["Read", "Glob", "Grep"]);
        options.DeniedTools.Should().Contain(["Write", "Edit", "Bash"]);
        options.IsolateMcpServers.Should().BeTrue();
        options.DisableHooks.Should().BeTrue();
        options.OutputSchemaJson.Should().Be(IdeaSchema.Version2);
    }

    [Fact]
    public async Task An_idea_on_a_cloned_project_is_refined_in_its_repository_and_one_on_no_project_is_not()
    {
        await _space.ProjectAsync("homelab");
        var repository = _space.Scratch("homelab-repo");

        var onProject = (await _space.Ideas.CaptureAsync("homelab", "Alerts", "me")).Value!;
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));
        await Refiner(launcher, repository).RefineAsync(new RefineRequest(new IdeaPlace("homelab", onProject.Id)));

        launcher.Request!.WorkingDirectory.Should().Be(repository);
        launcher.Pipe!.Written.ToString().Should().Contain("started in its repository");

        var nowhere = (await _space.Ideas.CaptureAsync(null, "Something new", "me")).Value!;
        var second = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));
        await Refiner(second, repository).RefineAsync(new RefineRequest(new IdeaPlace(null, nowhere.Id)));

        second.Request!.WorkingDirectory.Should().NotBe(repository);
        second.Request.WorkingDirectory.Should().StartWith(_space.Paths.Paths.State,
            "an idea on no project gets a directory of its own, not somebody's repository");
    }

    [Fact]
    public async Task An_idea_waiting_on_answers_starts_no_agent()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        await Refiner(new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions)), _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(APlan));
        var refused = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("Q1");
        launcher.Started.Should().Be(0, "a round run now would be asked the same questions again");
    }

    [Fact]
    public async Task An_answer_in_the_wrong_shape_is_asked_for_once_more()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result("""{"contract":"report/1"}"""), ScriptedDetachedLauncher.Result(Questions));

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(new IdeaPlace(null, idea.Id)));

        refined.Succeeded.Should().BeTrue(refined.Error);
        launcher.Pipe!.Written.ToString().Should().Contain("That answer was not usable");
        refined.Value!.CostUsd.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task A_round_that_goes_wrong_says_so_on_the_record_and_changes_nothing_else()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(null, error: true));

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        refined.Failed.Should().BeTrue();

        var record = (await _space.Ideas.ReadAsync(place)).Value!;
        IdeaWork.FailureOf(record)!.Error.Should().Contain("error");
        record.Failure!.Agent.Should().Be("claude", "the failure says which agent it was asked of");
        IdeaWork.StageOf(record).Should().Be(IdeaStage.Captured);
    }

    [Fact]
    public async Task A_dry_run_starts_nothing_and_changes_nothing()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place, DryRun: true));

        refined.Succeeded.Should().BeTrue(refined.Error);
        launcher.Request!.DryRun.Should().BeTrue();
        Directory.Exists(Path.Combine(_space.Paths.Paths.State, "ideas", idea.Id)).Should().BeFalse(
            "a dry run does not even make the directory the agent would have started in");
        (await _space.Ideas.ReadAsync(place)).Value!.Rounds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_later_round_asks_the_agent_and_model_the_last_round_used()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions))
            .Then(ScriptedDetachedLauncher.Result(APlan));
        var refiner = Refiner(launcher, _space.Scratch("r"));

        (await refiner.RefineAsync(new RefineRequest(place, "codex", "gpt-5"))).Succeeded.Should().BeTrue();
        await _space.Ideas.AnswerAsync(place, "Q1", "private");

        var second = await refiner.RefineAsync(new RefineRequest(place));

        second.Succeeded.Should().BeTrue(second.Error);
        launcher.Request!.AgentName.Should().Be("codex", "a later round should not quietly switch agent");
        launcher.Request.Model.Should().Be("gpt-5");
        second.Value!.Choice.FromLastRound.Should().BeTrue();
        second.Value.Record.LastRound!.Model.Should().Be("gpt-5");
    }

    [Fact]
    public async Task A_round_that_went_wrong_is_not_taken_as_the_next_default()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions))
            .Then(ScriptedDetachedLauncher.Result(null, error: true))
            .Then(ScriptedDetachedLauncher.Result(APlan));
        var refiner = Refiner(launcher, _space.Scratch("r"));

        (await refiner.RefineAsync(new RefineRequest(place, Model: "opus"))).Succeeded.Should().BeTrue();
        await _space.Ideas.AnswerAsync(place, "Q1", "private");

        // A mistyped model, as it would be typed.
        (await refiner.RefineAsync(new RefineRequest(place, Model: "opsu"))).Failed.Should().BeTrue();

        var failed = (await _space.Ideas.ReadAsync(place)).Value!.Failure!;
        failed.Model.Should().Be("opsu");
        failed.Agent.Should().Be("claude");

        var third = await refiner.RefineAsync(new RefineRequest(place));

        third.Succeeded.Should().BeTrue(third.Error);
        launcher.Request!.Model.Should().Be("opus", "the typo should not repeat on every later round");
        third.Value!.Choice.FailedDifferently.Should().BeTrue("the person is told the different choice went wrong");
        IdeaWork.FailureOf(third.Value.Record).Should().BeNull("a round that came back clears the failure");
    }

    [Fact]
    public async Task A_round_already_running_on_this_machine_is_not_started_twice()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));
        var refiner = Refiner(launcher, _space.Scratch("r"));

        // Another process on this machine, still running, holding the idea.
        var other = new FakeProcessInspector { CurrentProcessId = 999 };
        _space.Processes.MarkLive(999, other.CurrentProcessStartedAt);
        using var held = new IdeaLocks(_space.Paths, other).Take(place).Value!;

        var refused = await refiner.RefineAsync(new RefineRequest(place));
        var previewed = await refiner.RefineAsync(new RefineRequest(place, DryRun: true));

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("already running");
        previewed.Failed.Should().BeTrue("a preview of a round that would be refused describes nothing real");
        launcher.Started.Should().Be(0);
    }

    [Fact]
    public async Task A_lock_left_by_a_round_that_died_does_not_hold_the_idea()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));

        // Taken by a process that is not running, and never let go.
        new IdeaLocks(_space.Paths, new FakeProcessInspector { CurrentProcessId = 999 }).Take(place)
            .Succeeded.Should().BeTrue();

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        refined.Succeeded.Should().BeTrue(refined.Error);
        _space.Locks.Held(place).Should().BeFalse("the round let it go when it ended");
    }

    [Fact]
    public async Task The_idea_is_let_go_when_its_round_goes_wrong()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(null, error: true));

        (await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place))).Failed.Should().BeTrue();

        _space.Locks.Held(place).Should().BeFalse();
    }

    [Fact]
    public async Task A_note_the_round_was_shown_is_marked_taken()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        (await _space.Ideas.AddToAsync(place, "It must also show the backup jobs.", "me")).Succeeded.Should().BeTrue();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(APlan));

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        refined.Succeeded.Should().BeTrue(refined.Error);
        launcher.Pipe!.Written.ToString().Should().Contain("It must also show the backup jobs.");
        (await _space.Ideas.ReadAsync(place)).Value!.Notes.Single().TakenUtc.Should().NotBeNull(
            "the round was shown the note and returned a plan, so it has been taken in");
    }

    [Fact]
    public async Task A_note_shown_to_a_round_that_only_asks_questions_stays_unread()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        (await _space.Ideas.AddToAsync(place, "It must also show the backup jobs.", "me")).Succeeded.Should().BeTrue();
        var launcher = new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(Questions));

        var refined = await Refiner(launcher, _space.Scratch("r")).RefineAsync(new RefineRequest(place));

        refined.Succeeded.Should().BeTrue(refined.Error);
        launcher.Pipe!.Written.ToString().Should().Contain("It must also show the backup jobs.");
        (await _space.Ideas.ReadAsync(place)).Value!.Notes.Single().TakenUtc.Should().BeNull(
            "no plan has taken it in yet, so the next round must be given it again");
    }

    [Fact]
    public async Task A_note_added_while_the_round_runs_stays_unread()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "A status page", "me")).Value!;
        var place = new IdeaPlace(null, idea.Id);
        (await _space.Ideas.AddToAsync(place, "Shown to the round.", "me")).Succeeded.Should().BeTrue();
        var launcher = new AddingWhileRunning(
            new ScriptedDetachedLauncher(ScriptedDetachedLauncher.Result(APlan)),
            () => _space.Ideas.AddToAsync(place, "Added while it ran.", "me"));

        var refined = await new IdeaRefiner(_space.Ideas, new FakeProjects("homelab", _space.Scratch("r")), launcher, _space.Paths, _space.Locks)
            .RefineAsync(new RefineRequest(place));

        refined.Succeeded.Should().BeTrue(refined.Error);
        var notes = (await _space.Ideas.ReadAsync(place)).Value!.Notes;
        notes.Single(note => note.Text == "Shown to the round.").TakenUtc.Should().NotBeNull();
        notes.Single(note => note.Text == "Added while it ran.").TakenUtc.Should().BeNull(
            "no round has read it yet");
    }

    /// <summary>A launcher that has the person add a note once the agent has started.</summary>
    private sealed class AddingWhileRunning(ScriptedDetachedLauncher inner, Func<Task> adding) : IDetachedLauncher
    {
        public async Task<Loadout.Models.Results.OperationResult<HeadlessLaunch>> StartAsync(
            DetachedLaunchRequest request,
            HeadlessOptions options,
            CancellationToken ct = default)
        {
            var started = await inner.StartAsync(request, options, ct);
            await adding();

            return started;
        }
    }
}
