using FluentAssertions;
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
        new(_space.Ideas, new FakeProjects("homelab", repository), launcher, _space.Paths);

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
        options.OutputSchemaJson.Should().Be(IdeaSchema.Version1);
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
        record.LastError.Should().Contain("error");
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
}
