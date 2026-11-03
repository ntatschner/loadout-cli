using FluentAssertions;
using Loadout.Core.Capabilities;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Triggers;
using Loadout.Models.Capabilities;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Offers against real task files, fixture repositories and a GitHub that
/// answers as scripted: only a seen gap is offered, a link is made only by
/// accepting, and a fresh profile closes a prerequisite by the rules for its check.
/// </summary>
public sealed class CapabilityOffersTests : IDisposable
{
    private const string Slug = "website";
    private const string Repo = "me/site";

    private readonly TemporaryWorkspace _space = new();
    private readonly string _root;
    private readonly Scripted _github = new();
    private readonly Journal _journal = new();

    public CapabilityOffersTests()
    {
        _root = _space.Scratch("repository");
        Directory.CreateDirectory(_root);
    }

    public void Dispose() => _space.Dispose();

    private sealed class Scripted : IGitHubReads
    {
        public Dictionary<string, GitHubReply> Answers { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = [];

        public Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct)
        {
            Asked.Add(path);

            return Task.FromResult(Answers.TryGetValue(path, out var reply) ? reply : new GitHubReply(404));
        }
    }

    /// <summary>A journal holding only the runs a test says are going.</summary>
    private sealed class Journal : IRunJournal
    {
        public List<RunSummary> Runs { get; } = [];

        public IReadOnlyList<string> List(int limit = 20) => [.. Runs.Select(run => run.RunId)];

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Ok(Runs.Single(run => run.RunId == runId));

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) => throw new NotSupportedException();

        public string DirectoryOf(string runId) => throw new NotSupportedException();

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) => throw new NotSupportedException();
    }

    private CapabilityOffers Offers(bool token = true)
    {
        var profiles = new CapabilityProfiles(
            _space.Workspace,
            _space.Yaml,
            _github,
            _ => Task.FromResult(token ? TriggerCredential.Of(TriggerCredential.GhToken, "t0ken") : TriggerCredential.Missing),
            TimeProvider.System);

        return new CapabilityOffers(profiles, new FakeProjects(Slug, _root), _space.Tasks, _journal, _space.Workspace, _space.Yaml, TimeProvider.System);
    }

    private ProjectResolution Project() =>
        new(new ProjectRegistryEntry { Slug = Slug, Remote = "https://github.com/me/site.git" }, _root, null, 0, false);

    private void Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private void WriteCi() => Write(".github/workflows/ci.yml", "on: pull_request\njobs:\n  b:\n    steps: []\n");

    private void WriteRelease() => Write(".github/workflows/release.yml", "on:\n  push:\n    tags: ['v*']\njobs:\n  r:\n    steps: []\n");

    /// <summary>A repository GitHub can read, with no environments and no releases published.</summary>
    private void GitHubSaysNothingSetUp()
    {
        _github.Answers[$"repos/{Repo}"] = new GitHubReply(200, """{"default_branch":"main"}""");
        _github.Answers[$"repos/{Repo}/environments"] = new GitHubReply(200, """{"total_count":0}""");
        _github.Answers[$"repos/{Repo}/releases?per_page=1"] = new GitHubReply(200, "[]");
    }

    private void GitHubListsEnvironments() =>
        _github.Answers[$"repos/{Repo}/environments"] = new GitHubReply(200, """{"total_count":2}""");

    private async Task<TaskItem> WorkAsync(TaskType type = TaskType.Release, IReadOnlyList<string>? doneWhen = null)
    {
        await _space.ProjectAsync(Slug);
        await _space.Tasks.DeclareAsync(Slug, "ship", TaskState.Open, "nigel", "Ship it", type: type, doneWhen: doneWhen);

        return await OneAsync("ship");
    }

    private async Task<TaskItem> OneAsync(string id) =>
        (await _space.Tasks.ListAsync(Slug)).Value!.Single(task => task.Id == id);

    private async Task<IReadOnlyList<TaskUnmet>> UnmetAsync(string id)
    {
        var every = (await _space.Tasks.EveryListAsync()).Value!;

        return TaskRequires.Unmet(await OneAsync(id), key => every.TryGetValue(key, out var list) ? list : null);
    }

    private static RunSummary Going(string task) => new(
        "run-1", string.Empty, "team", "goal", "autonomy", DateTimeOffset.UtcNow, null, null, 0, 0, [], [], [], Project: Slug, Task: task);

    [Fact]
    public async Task Accepting_makes_one_frozen_task_with_its_version_and_stacks_and_links_the_work_to_it()
    {
        await WorkAsync();
        Write("global.json", "{}");
        Write("web/package.json", "{}");

        var accepted = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel");

        accepted.Succeeded.Should().BeTrue(accepted.Error);
        accepted.Value!.Created.Should().BeTrue();

        var task = await OneAsync(accepted.Value.Prerequisite.Id);

        task.Prerequisite!.Check.Should().Be(CapabilityChecks.Ci);
        task.Prerequisite.TemplateVersion.Should().Be(PrerequisiteTemplates.Version);
        task.Prerequisite.Stacks.Should().Equal("dotnet", "node");
        task.Detail.Should().Contain("dotnet test").And.Contain("npm ci");
        task.DoneWhen.Should().NotBeEmpty();
        (await OneAsync("ship")).Requires.Select(TaskRequires.Name).Should().Equal($"{Slug}/{task.Id}");
        (await UnmetAsync("ship")).Should().ContainSingle("the work is held until it is done");
    }

    [Fact]
    public async Task A_second_item_taking_the_same_offer_shares_the_open_task()
    {
        await WorkAsync();
        await _space.Tasks.DeclareAsync(Slug, "notes", TaskState.Open, "nigel", "Release notes", type: TaskType.Release);

        var first = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel");
        var second = await Offers().AcceptAsync(Project(), "notes", CapabilityChecks.Ci, null, null, "nigel");

        second.Value!.Created.Should().BeFalse();
        second.Value.Prerequisite.Id.Should().Be(first.Value!.Prerequisite.Id);
        (await _space.Tasks.ListAsync(Slug)).Value!.Count(task => task.Prerequisite is not null).Should().Be(1);
    }

    [Fact]
    public async Task A_chosen_stack_is_written_and_release_remembers_it()
    {
        await WorkAsync();
        Write("global.json", "{}");
        Write("package.json", "{}");
        GitHubSaysNothingSetUp();

        var accepted = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Release, "node", null, "nigel");

        accepted.Succeeded.Should().BeTrue(accepted.Error);
        accepted.Value!.Prerequisite.Detail.Should().Contain("NPM_TOKEN");
        (await Offers().AnswersAsync(Slug)).ReleaseStack.Should().Be("node");

        var bad = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, "cobol", null, "nigel");

        bad.Failed.Should().BeTrue();
        bad.Error.Should().Contain("cobol");
    }

    [Fact]
    public void A_remembered_release_stack_that_is_gone_is_not_offered_first()
    {
        var stacks = new List<CapabilityStack> { new() { Name = "dotnet" }, new() { Name = "node" } };

        CapabilityOffers.StackChoices(CapabilityChecks.Release, stacks, new CapabilityAnswers { ReleaseStack = "node" })
            .Should().Equal("node", "dotnet", PrerequisiteTemplates.Generic);
        CapabilityOffers.StackChoices(CapabilityChecks.Release, stacks, new CapabilityAnswers { ReleaseStack = "python" })
            .Should().Equal("dotnet", "node", PrerequisiteTemplates.Generic);
        CapabilityOffers.StackChoices(CapabilityChecks.Ci, stacks, new CapabilityAnswers())
            .Should().Equal(PrerequisiteTemplates.AllDetected, "dotnet", "node", PrerequisiteTemplates.Generic);
    }

    [Fact]
    public async Task With_no_token_a_check_only_github_can_answer_is_never_offered_and_holds_nothing()
    {
        await WorkAsync();

        var sheet = (await Offers(token: false).ReadAsync(Project(), "ship")).Value!;

        sheet.Offers.Select(offer => offer.Check).Should().Equal(CapabilityChecks.Ci);
        sheet.Unseen.Select(one => one.Check).Should().Contain([CapabilityChecks.Release, CapabilityChecks.Environments]);

        var refused = await Offers(token: false).AcceptAsync(Project(), "ship", CapabilityChecks.Environments, null, null, "nigel");

        refused.Failed.Should().BeTrue("a gap nobody has seen cannot be offered");
        (await OneAsync("ship")).Requires.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_from_github_names_the_permission_and_makes_no_offer()
    {
        await WorkAsync();
        _github.Answers[$"repos/{Repo}"] = new GitHubReply(200, """{"default_branch":"main"}""");
        _github.Answers[$"repos/{Repo}/environments"] = new GitHubReply(403);

        var refused = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Environments, null, null, "nigel");

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("403");
    }

    [Fact]
    public async Task A_check_with_no_template_is_refused_by_name()
    {
        await WorkAsync();

        var refused = await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.BranchProtection, null, null, "nigel");

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("no template");
    }

    [Fact]
    public async Task A_done_when_only_ci_can_meet_raises_ci_on_any_type()
    {
        var task = await WorkAsync(TaskType.Docs, ["CI is green on the docs build"]);
        var profile = new CapabilityProfile { Checks = [new CapabilityResult { Id = CapabilityChecks.Ci, State = CapabilityState.Gap }] };

        CapabilityOffers.SeenGaps(task, profile, new CapabilityAnswers()).Should().Be(1);

        task.DoneWhen = [];
        CapabilityOffers.SeenGaps(task, profile, new CapabilityAnswers()).Should().Be(0, "docs raises no check by its type alone");
    }

    [Fact]
    public void A_prerequisite_skips_only_its_own_check_and_manual_release_is_offered_without_counting()
    {
        var profile = new CapabilityProfile
        {
            Checks =
            [
                new CapabilityResult { Id = CapabilityChecks.Ci, State = CapabilityState.Gap },
                new CapabilityResult { Id = CapabilityChecks.Release, State = CapabilityState.ManualRelease },
                new CapabilityResult { Id = CapabilityChecks.Environments, State = CapabilityState.Unknown },
            ],
        };
        var prerequisite = new TaskItem { Id = "p", Type = TaskType.Release, Prerequisite = new TaskPrerequisite { Check = CapabilityChecks.Release } };
        var plain = new TaskItem { Id = "w", Type = TaskType.Release };

        CapabilityOffers.Relevant(prerequisite, profile, new CapabilityAnswers()).Select(one => one.Id)
            .Should().Contain(CapabilityChecks.Ci).And.NotContain(CapabilityChecks.Release);
        CapabilityOffers.SeenGaps(plain, profile, new CapabilityAnswers()).Should().Be(1, "manual release is not a gap, and unknown never is");
        CapabilityOffers.UnseenChecks(plain, profile, new CapabilityAnswers()).Should().Be(1);
    }

    [Fact]
    public void A_specialists_check_counts_by_the_types_it_declares_and_has_no_offer()
    {
        var profile = new CapabilityProfile
        {
            Checks =
            [
                new CapabilityResult { Id = "code-owners", Name = "Code owners", State = CapabilityState.Gap, Types = [TaskType.Docs], Declared = true },
                new CapabilityResult { Id = CapabilityChecks.Ci, State = CapabilityState.Gap, Types = [TaskType.Docs], Declared = true },
            ],
        };
        var docs = new TaskItem { Id = "d", Type = TaskType.Docs };
        var release = new TaskItem { Id = "r", Type = TaskType.Release };

        CapabilityOffers.Relevant(docs, profile, new CapabilityAnswers()).Select(one => one.Id).Should().Equal("code-owners", CapabilityChecks.Ci);
        CapabilityOffers.Relevant(release, profile, new CapabilityAnswers()).Should().BeEmpty("an override's types replace the built-in's");
        PrerequisiteTemplates.Has("code-owners").Should().BeFalse();
    }

    [Fact]
    public async Task Retyping_outgrows_a_template_link_only_and_releasing_leaves_the_prerequisite_and_hand_made_links()
    {
        await WorkAsync();
        await _space.Tasks.DeclareAsync(Slug, "other", TaskState.Open, "nigel", "Something else");
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel")).Value!.Prerequisite;

        await _space.Tasks.RequireAsync(Slug, "ship", [new TaskLink { Project = Slug, Id = "other" }], []);
        (await Offers().OutgrownAsync(Slug, "ship")).Should().BeEmpty("a release still depends on CI");

        await _space.Tasks.RetypeAsync(Slug, "ship", TaskType.BugFix, TaskTypeSource.Person, "nigel");

        (await Offers().OutgrownAsync(Slug, "ship")).Should().ContainSingle().Which.Prerequisite.Should().Be(made.Id);
        (await Offers().ReadAsync(Project(), "ship")).Value!.Links.Should().ContainSingle().Which.Outgrown.Should().BeTrue();

        (await Offers().ReleaseOutgrownAsync(Slug, "ship")).Value.Should().Equal(made.Id);

        (await OneAsync("ship")).Requires.Select(link => link.Id).Should().Equal("other");
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Open, "other work may still wait for it");
    }

    [Fact]
    public async Task A_done_when_that_needs_ci_keeps_the_ci_link_whatever_the_type()
    {
        await WorkAsync(doneWhen: ["CI is green on it"]);
        await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel");

        await _space.Tasks.RetypeAsync(Slug, "ship", TaskType.Docs, TaskTypeSource.Person, "nigel");

        (await Offers().OutgrownAsync(Slug, "ship")).Should().BeEmpty();
    }

    [Fact]
    public void Appeared_says_only_what_was_there_and_has_gone()
    {
        CapabilityProfile Of(params (string Id, CapabilityState State)[] checks) => new()
        {
            Checks = [.. checks.Select(one => new CapabilityResult { Id = one.Id, State = one.State, Evidence = "seen" })],
        };

        CapabilityOffers.Appeared(Of(("ci", CapabilityState.Present)), Of(("ci", CapabilityState.Gap))).Should().ContainSingle();
        CapabilityOffers.Appeared(Of(("ci", CapabilityState.Delegated)), Of(("ci", CapabilityState.Gap))).Should().ContainSingle();
        CapabilityOffers.Appeared(Of(("ci", CapabilityState.Gap)), Of(("ci", CapabilityState.Gap))).Should().BeEmpty("it was already missing");
        CapabilityOffers.Appeared(Of(("ci", CapabilityState.Unknown)), Of(("ci", CapabilityState.Gap))).Should().BeEmpty("nobody had seen it");
        CapabilityOffers.Appeared(Of(("ci", CapabilityState.Present)), Of(("ci", CapabilityState.Unknown))).Should().BeEmpty("it could not be looked at");
        CapabilityOffers.Appeared(null, Of(("ci", CapabilityState.Gap))).Should().BeEmpty();
    }

    [Fact]
    public async Task One_look_gives_every_open_item_its_sheet()
    {
        await WorkAsync();
        await _space.Tasks.DeclareAsync(Slug, "gone", TaskState.Dropped, "nigel", "Not this");

        var sheets = await Offers().SheetsAsync(Slug);

        sheets.Keys.Should().Contain("ship").And.NotContain("gone");
        sheets["ship"].Offers.Select(offer => offer.Check).Should().Contain(CapabilityChecks.Ci);
    }

    [Fact]
    public async Task With_no_open_prerequisite_settling_does_not_ask_github()
    {
        await WorkAsync();

        var settled = await Offers().SettleAsync(Project(), "loadout");

        settled.Value.Should().BeEmpty();
        _github.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task A_fresh_profile_closes_a_ci_prerequisite_with_a_dated_reason_and_the_work_goes()
    {
        await WorkAsync();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel")).Value!.Prerequisite;

        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().BeEmpty("the capability is still not there");
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Open);

        WriteCi();

        var settled = (await Offers().SettleAsync(Project(), "loadout")).Value!;

        settled.Should().ContainSingle().Which.Closed.Should().BeTrue();

        var task = await OneAsync(made.Id);

        task.State.Should().Be(TaskState.Done);
        task.History.Last().Reason.Should().StartWith("ci seen ").And.Contain("ci.yml");
        (await UnmetAsync("ship")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_release_workflow_alone_does_not_close_the_prerequisite_until_a_person_ticks_its_step()
    {
        await WorkAsync();
        GitHubSaysNothingSetUp();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Release, null, null, "nigel")).Value!.Prerequisite;

        WriteRelease();

        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().BeEmpty();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Open, "the file does not prove the secret exists");

        (await Offers().TickAsync(Slug, made.Id, "nigel")).Succeeded.Should().BeTrue();
        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().ContainSingle();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Done);
    }

    [Fact]
    public async Task Releases_published_by_hand_do_not_close_a_release_prerequisite()
    {
        await WorkAsync();
        GitHubSaysNothingSetUp();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Release, null, null, "nigel")).Value!.Prerequisite;

        await Offers().TickAsync(Slug, made.Id, "nigel");
        _github.Answers[$"repos/{Repo}/releases?per_page=1"] = new GitHubReply(200, """[{"id":1}]""");

        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().BeEmpty();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Open, "the work was waiting on automation");
    }

    [Fact]
    public async Task Environments_github_lists_tick_the_step_and_close_the_task()
    {
        await WorkAsync();
        GitHubSaysNothingSetUp();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Environments, null, ["staging", "live"], "nigel")).Value!.Prerequisite;

        made.Detail.Should().Contain("staging and live");
        (await Offers().AnswersAsync(Slug)).Environments.Should().Equal("staging", "live");

        GitHubListsEnvironments();

        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().ContainSingle();

        var task = await OneAsync(made.Id);

        task.State.Should().Be(TaskState.Done);
        task.Prerequisite!.PersonStepUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task While_a_run_owns_the_prerequisite_the_work_goes_and_the_task_stays_with_the_run()
    {
        await WorkAsync();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel")).Value!.Prerequisite;

        WriteCi();
        _journal.Runs.Add(Going(made.Id));

        var settled = (await Offers().SettleAsync(Project(), "loadout")).Value!;

        settled.Should().ContainSingle().Which.Closed.Should().BeFalse();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Open);
        (await UnmetAsync("ship")).Should().BeEmpty();

        _journal.Runs.Clear();

        (await Offers().SettleAsync(Project(), "loadout")).Value.Should().ContainSingle().Which.Closed.Should().BeTrue();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Done);
    }

    [Fact]
    public async Task Not_needed_quiets_the_offer_keeps_the_link_unless_confirmed_and_can_be_undone()
    {
        await WorkAsync();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel")).Value!.Prerequisite;

        var kept = (await Offers().NotNeededAsync(Project(), CapabilityChecks.Ci, false, "nigel")).Value!;

        kept.Released.Should().BeFalse();
        kept.Waiting.Select(TaskRequires.Name).Should().Equal($"{Slug}/ship");
        (await UnmetAsync("ship")).Should().ContainSingle("releasing work quietly is what the link prevents");
        (await Offers().ReadAsync(Project(), "ship")).Value!.Offers.Should().NotContain(offer => offer.Check == CapabilityChecks.Ci);

        var released = (await Offers().NotNeededAsync(Project(), CapabilityChecks.Ci, true, "nigel")).Value!;

        released.Released.Should().BeTrue();
        (await OneAsync("ship")).Requires.Should().BeEmpty();
        (await OneAsync(made.Id)).State.Should().Be(TaskState.Dropped);

        var back = await Offers().NeededAsync(Project(), CapabilityChecks.Ci);

        back.Value.Should().Equal("ship");
        (await Offers().NeededAsync(Project(), CapabilityChecks.Ci)).Failed.Should().BeTrue("there is no answer left to undo");
    }

    [Fact]
    public async Task A_prerequisite_for_stacks_that_changed_is_listed_and_renewing_clears_a_tick_when_the_release_stack_moves()
    {
        await WorkAsync();
        Write("package.json", "{}");
        GitHubSaysNothingSetUp();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Release, null, null, "nigel")).Value!.Prerequisite;

        await Offers().TickAsync(Slug, made.Id, "nigel");

        var current = new CapabilityProfile { Stacks = [new CapabilityStack { Name = "node" }] };

        (await Offers().StaleAsync(Slug, current)).Should().BeEmpty();

        File.Delete(Path.Combine(_root, "package.json"));
        Write("global.json", "{}");

        var moved = new CapabilityProfile { Stacks = [new CapabilityStack { Name = "dotnet" }] };

        (await Offers().StaleAsync(Slug, moved)).Should().ContainSingle().Which.Why.Should().Contain("dotnet");

        var renewed = await Offers().RenewAsync(Project(), made.Id, "nigel");

        renewed.Succeeded.Should().BeTrue(renewed.Error);
        renewed.Value!.Prerequisite!.Stacks.Should().Equal("dotnet");
        renewed.Value.Prerequisite.PersonStepUtc.Should().BeNull("a tick about one stack's secret says nothing of another's");
        renewed.Value.Detail.Should().Contain("NUGET_API_KEY");
        renewed.Value.History.Last().Reason.Should().Contain("template version");
    }

    [Fact]
    public async Task Renewing_is_refused_while_a_run_owns_the_task()
    {
        await WorkAsync();
        var made = (await Offers().AcceptAsync(Project(), "ship", CapabilityChecks.Ci, null, null, "nigel")).Value!.Prerequisite;

        _journal.Runs.Add(Going(made.Id));

        var refused = await Offers().RenewAsync(Project(), made.Id, "nigel");

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("run");
    }

    [Fact]
    public async Task A_new_project_gets_what_was_left_ticked_and_remembers_the_rest_as_not_needed()
    {
        await WorkAsync(TaskType.NewProject);

        var made = await Offers().SetUpNewProjectAsync(Slug, "ship", [CapabilityChecks.Environments], "nigel");

        made.Value!.Select(task => task.Prerequisite!.Check).Should().Equal(CapabilityChecks.Ci, CapabilityChecks.Release);
        (await OneAsync("ship")).Requires.Should().HaveCount(2);
        (await Offers().AnswersAsync(Slug)).NotNeeded.Select(one => one.Check).Should().Equal(CapabilityChecks.Environments);
    }

    [Fact]
    public async Task Gap_counts_come_from_the_kept_profile_only()
    {
        await WorkAsync();

        (await Offers().GapCountsAsync(Slug)).Should().BeEmpty("none has been kept, and a list is polled");

        var offers = Offers();
        var profiles = new CapabilityProfiles(_space.Workspace, _space.Yaml, _github, _ => Task.FromResult(TriggerCredential.Missing), TimeProvider.System);

        await profiles.KeepAsync(await profiles.BuildAsync(Project(), askGitHub: false));

        var count = (await offers.GapCountsAsync(Slug))["ship"];

        count.Seen.Should().Be(1, "CI is the one gap the files can show");
        count.Unseen.Should().Be(2, "release and environments could not be looked at");
    }
}

/// <summary>What the templates say, and how a run will read their finish lines.</summary>
public sealed class PrerequisiteTemplatesTests
{
    [Fact]
    public void Ci_can_be_finished_by_an_unattended_run()
    {
        var ci = PrerequisiteTemplates.Render(CapabilityChecks.Ci, ["dotnet", "node"], [])!;

        ci.DoneWhen.Select(CriterionTags.Guess).Should().AllSatisfy(needs => needs.Should().BeNull());
        ci.Detail.Should().Contain("A job for dotnet").And.Contain("A job for node");
    }

    [Fact]
    public void Release_and_environments_hand_the_last_step_to_a_person()
    {
        var release = PrerequisiteTemplates.Render(CapabilityChecks.Release, ["dotnet"], [])!;
        var environments = PrerequisiteTemplates.Render(CapabilityChecks.Environments, [], ["dev", "production"])!;

        CriterionTags.Guess(release.DoneWhen[^1]).Should().Be(CriterionNeeds.Person);
        release.DoneWhen[^1].Should().Contain("NUGET_API_KEY");
        CriterionTags.Guess(environments.DoneWhen[^1]).Should().Be(CriterionNeeds.Person);
        environments.DoneWhen[^1].Should().Contain("dev and production");
    }

    [Fact]
    public void Every_stack_detected_has_text_and_no_stack_falls_back_to_generic()
    {
        foreach (var stack in LocalProbes.StackOrder)
        {
            PrerequisiteTemplates.Render(CapabilityChecks.Ci, [stack], [])!.Detail.Should().Contain(stack);
            PrerequisiteTemplates.Render(CapabilityChecks.Release, [stack], [])!.Detail.Should().Contain(stack);
        }

        PrerequisiteTemplates.Render(CapabilityChecks.Ci, [], [])!.Detail.Should().Contain("own build and test commands");
    }

    [Fact]
    public void A_check_without_a_template_renders_nothing()
    {
        PrerequisiteTemplates.Render(CapabilityChecks.BranchProtection, [], []).Should().BeNull();
        PrerequisiteTemplates.Has(CapabilityChecks.BranchProtection).Should().BeFalse();
    }
}
