using FluentAssertions;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Models.Delivery;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// What a person can do with a branch waiting to land: open its pull request,
/// mark it ready, ask for it to be merged, and have the merge noticed.
/// </summary>
/// <remarks>
/// Against real landings and a real task list; git and gh are doubles, since
/// what is under test is which calls are made, in what order, and what the
/// landing says afterwards.
/// </remarks>
public sealed class DeliveryServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();
    private readonly FakeGit _git;
    private readonly StubProcessLauncher _gh = new("https://github.com/o/website/pull/7\n");
    private string _state = "OPEN";
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly LandingService _landings;
    private readonly DeliveryService _delivery;

    public DeliveryServiceTests()
    {
        var repository = _space.Scratch("website");

        _git = new FakeGit(repository) { AllowPush = true };
        _landings = new LandingService(_space.Paths, _space.Yaml, _clock);
        _gh.Answering = request => request.Arguments is ["pr", "view", ..]
            ? new Loadout.Platform.Abstractions.ProcessOutcome(0, $"{{\"state\":\"{_state}\"}}", string.Empty)
            : null;
        _delivery = With(new GhRepositoryHost(_gh, new StubResolver("gh")));
    }

    public void Dispose() => _space.Dispose();

    private DeliveryService With(IRepositoryHost host) =>
        new(_landings, new FakeProjects("website", _space.Scratch("website")), _git, new FixedHosts(host),
            new LocalSquash(new StubProcessLauncher(string.Empty), new StubResolver("git")), _space.Tasks, _clock);

    private int StateReads => _gh.Requests.Count(one => one.Arguments is ["pr", "view", ..]);

    private async Task<LandingFound> WaitingAsync(LandingState state = LandingState.Pending, string pullRequest = "")
    {
        await _space.Tasks.DeclareAsync(
            "website", "login", TaskState.Doing, "nigel", "Fix the login", doneWhen: ["the suite passes"]);
        (await _landings.RecordAsync("website",
        [
            new BranchLanding
            {
                Branch = "teams-r1-implementer-2",
                Team = "builders",
                RunId = "r1",
                Node = "implementer-2",
                Task = "login",
                State = state,
                PullRequest = pullRequest,
            },
        ])).Value.Should().BeEmpty();
        _git.Commits["refs/heads/teams-r1-implementer-2"] = "abc123";

        var found = await _delivery.FindAsync("teams-r1-implementer-2", null);

        found.Succeeded.Should().BeTrue(found.Error);

        return found.Value!;
    }

    /// <summary>The same landing, found through another service and so through its host.</summary>
    private static async Task<LandingFound> ThroughAsync(DeliveryService delivery, LandingFound found) =>
        (await delivery.FindAsync(found.Landing.Branch, null)).Value!;

    private async Task<BranchLanding> KeptAsync() =>
        (await _landings.LoadAsync("website")).Value!.Landings.Should().ContainSingle().Subject;

    [Fact]
    public async Task Opening_a_pending_branch_pushes_it_then_opens_it_and_keeps_the_address()
    {
        var found = await WaitingAsync();
        var opening = await _delivery.OpeningAsync(found, draft: true, title: null, body: null);

        opening.Title.Should().Be("Fix the login", "a branch made for a task is titled after it");
        opening.Body.Should().Contain("- the suite passes").And.Contain("From implementer-2 of team run r1, team builders.");

        var opened = await _delivery.OpenAsync(found, opening);

        opened.Succeeded.Should().BeTrue(opened.Error);
        _git.Pushed.Should().Equal("teams-r1-implementer-2");
        _gh.Requests.Select(one => one.Arguments[0]).Should().Equal(["auth", "pr"], "the sign-in is asked about before anything is pushed");
        _gh.Requests[0].Arguments.Should().Equal("auth", "status", "--active");
        _gh.Requests[1].Arguments.Should().StartWith(["pr", "create", "--draft"]);

        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Draft);
        kept.PullRequest.Should().Be("https://github.com/o/website/pull/7");
        kept.Node.Should().Be("implementer-2", "the rest of the landing is kept as it was");
    }

    [Fact]
    public async Task A_branch_that_has_gone_is_not_pushed_and_nothing_is_opened()
    {
        var found = await WaitingAsync();
        _git.Commits.Remove("refs/heads/teams-r1-implementer-2");

        var opened = await _delivery.OpenAsync(found, await _delivery.OpeningAsync(found, false, null, null));

        opened.Failed.Should().BeTrue();
        opened.Error.Should().Contain("has gone");
        _git.Pushed.Should().BeEmpty();
        _gh.Requests.Should().NotContain(one => one.Arguments[0] == "pr");
        (await KeptAsync()).State.Should().Be(LandingState.Pending);
    }

    [Fact]
    public async Task Without_gh_the_branch_is_never_pushed()
    {
        var found = await WaitingAsync();
        var delivery = With(new GhRepositoryHost(_gh, new StubResolver(null)));

        var opened = await delivery.OpenAsync(await ThroughAsync(delivery, found), await delivery.OpeningAsync(await ThroughAsync(delivery, found), false, null, null));

        opened.Failed.Should().BeTrue();
        _git.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task With_gh_signed_out_the_branch_is_never_pushed_and_it_says_how_to_sign_in()
    {
        var found = await WaitingAsync();
        var signedOut = new StubProcessLauncher(string.Empty, exitCode: 1);
        var delivery = With(new GhRepositoryHost(signedOut, new StubResolver("gh")));

        var opened = await delivery.OpenAsync(await ThroughAsync(delivery, found), await delivery.OpeningAsync(await ThroughAsync(delivery, found), false, null, null));

        opened.Failed.Should().BeTrue();
        opened.Error.Should().Contain("gh auth login");
        _git.Pushed.Should().BeEmpty();
        signedOut.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("auth", "status", "--active");
        (await KeptAsync()).State.Should().Be(LandingState.Pending);
    }

    [Fact]
    public async Task A_pull_request_closed_unmerged_leaves_its_branch_pending_with_nothing_of_it_kept()
    {
        await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");
        var asked = (await _landings.LoadAsync("website")).Value!.Landings[0];
        asked.MergeRequestedUtc = _clock.Now;
        await _landings.RecordAsync("website", [asked]);
        _state = "CLOSED";

        (await _delivery.MergedAsync()).Should().Be((0, 1));
        _gh.Requests.Where(one => one.Arguments is ["pr", "view", ..]).Should().ContainSingle()
            .Which.Arguments.Should().Equal("pr", "view", "https://github.com/o/website/pull/7", "--json", "state");

        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Pending);
        kept.PullRequest.Should().BeEmpty("it can be opened afresh");
        kept.MergeRequestedUtc.Should().BeNull();
        kept.Why.Should().Contain("closed without being merged").And.Contain("pull/7");
    }

    [Fact]
    public async Task A_pull_request_the_host_cannot_read_is_left_and_looked_at_again_next_time()
    {
        await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");
        _state = "SOMETHING-NEW";

        (await _delivery.MergedAsync()).Should().Be((0, 0));

        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Ready);
        kept.MergeChecked.Should().BeNull("an answer it could not read is not a look");
    }

    [Fact]
    public async Task Abandoning_a_pending_branch_asks_nothing_of_gh()
    {
        var found = await WaitingAsync();

        var abandoned = await _delivery.AbandonAsync(found);

        abandoned.Succeeded.Should().BeTrue(abandoned.Error);
        _gh.Requests.Should().BeEmpty();
        (await KeptAsync()).State.Should().Be(LandingState.Abandoned);
    }

    [Fact]
    public async Task Abandoning_a_branch_with_a_pull_request_closes_it_first()
    {
        var found = await WaitingAsync(LandingState.Draft, "https://github.com/o/website/pull/7");

        (await _delivery.AbandonAsync(found)).Succeeded.Should().BeTrue();

        _gh.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("pr", "close", "https://github.com/o/website/pull/7");
        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Abandoned);
        kept.Why.Should().Contain("closed");
    }

    [Fact]
    public async Task A_pull_request_that_will_not_close_leaves_the_branch_waiting()
    {
        var refusing = new StubProcessLauncher(string.Empty, exitCode: 1);
        var delivery = With(new GhRepositoryHost(refusing, new StubResolver("gh")));
        var found = await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");

        var abandoned = await delivery.AbandonAsync(await ThroughAsync(delivery, found));

        abandoned.Failed.Should().BeTrue();
        abandoned.Error.Should().Contain("was not abandoned");
        (await KeptAsync()).State.Should().Be(LandingState.Ready);
    }

    [Fact]
    public async Task A_branch_with_a_merge_asked_for_is_not_abandoned()
    {
        var found = await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");
        found.Landing.MergeRequestedUtc = _clock.Now;

        var abandoned = await _delivery.AbandonAsync(found);

        abandoned.Error.Should().Contain("auto-merge");
        _gh.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_branch_with_a_pull_request_is_not_opened_again()
    {
        var found = await WaitingAsync(LandingState.Draft, "https://github.com/o/website/pull/7");

        var opened = await _delivery.OpenAsync(found, await _delivery.OpeningAsync(found, false, null, null));

        opened.Error.Should().Contain("already has a pull request");
        _git.Pushed.Should().BeEmpty();
    }

    [Fact]
    public async Task Marking_a_draft_ready_asks_gh_and_keeps_it_ready()
    {
        var found = await WaitingAsync(LandingState.Draft, "https://github.com/o/website/pull/7");

        (await _delivery.ReadyAsync(found)).Succeeded.Should().BeTrue();

        _gh.Requests.Should().ContainSingle().Which.Arguments.Should().Equal("pr", "ready", "https://github.com/o/website/pull/7");
        (await KeptAsync()).State.Should().Be(LandingState.Ready);
    }

    [Fact]
    public async Task A_draft_is_not_merged()
    {
        var found = await WaitingAsync(LandingState.Draft, "https://github.com/o/website/pull/7");

        var asked = await _delivery.MergeAsync(found, "Fix the login", string.Empty);

        asked.Error.Should().Contain("Mark it ready first");
        _gh.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Fix the login\nand more")]
    public async Task A_merge_needs_a_subject_of_one_line(string subject)
    {
        var found = await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");

        (await _delivery.MergeAsync(found, subject, "body")).Failed.Should().BeTrue();
        _gh.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Merging_asks_for_a_squash_with_the_message_given_and_leaves_it_ready_until_seen()
    {
        var found = await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");

        var asked = await _delivery.MergeAsync(found, "  Fix the login  ", string.Empty);

        asked.Succeeded.Should().BeTrue(asked.Error);
        _gh.Requests.Should().ContainSingle().Which.Arguments.Should().Equal(
            "pr", "merge", "https://github.com/o/website/pull/7", "--squash", "--auto",
            "--subject", "Fix the login", "--body", string.Empty);

        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Ready, "the host merges it, and the merge is only believed once it is seen");
        kept.MergeRequestedUtc.Should().Be(_clock.Now);
    }

    [Fact]
    public async Task A_merge_the_host_refuses_is_not_recorded_as_asked_for()
    {
        var refusing = new StubProcessLauncher(string.Empty, exitCode: 1);
        var delivery = With(new GhRepositoryHost(refusing, new StubResolver("gh")));
        var found = await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");

        (await delivery.MergeAsync(await ThroughAsync(delivery, found), "Fix the login", string.Empty)).Failed.Should().BeTrue();

        (await KeptAsync()).MergeRequestedUtc.Should().BeNull();
    }

    [Fact]
    public async Task A_pull_request_seen_merged_marks_its_landing_merged_and_is_not_asked_about_again_for_a_while()
    {
        await WaitingAsync(LandingState.Ready, "https://github.com/o/website/pull/7");
        _state = "MERGED";

        (await _delivery.MergedAsync()).Merged.Should().Be(1);

        var kept = await KeptAsync();
        kept.State.Should().Be(LandingState.Merged);
        kept.Why.Should().Contain("pull/7");

        var reads = StateReads;
        (await _delivery.MergedAsync()).Merged.Should().Be(0);
        StateReads.Should().Be(reads, "a merged landing is not waiting any more");
    }

    [Fact]
    public async Task A_pull_request_not_merged_yet_is_looked_for_again_only_after_a_while()
    {
        await WaitingAsync(LandingState.Draft, "https://github.com/o/website/pull/7");
        (await _delivery.MergedAsync()).Merged.Should().Be(0);
        (await KeptAsync()).State.Should().Be(LandingState.Draft);

        var reads = StateReads;
        await _delivery.MergedAsync();
        StateReads.Should().Be(reads, "it was looked for a moment ago");

        _clock.Now += DeliveryService.MergeEvery;
        await _delivery.MergedAsync();
        StateReads.Should().Be(reads + 1, "a quarter of an hour has gone");
    }

    [Fact]
    public async Task A_branch_nothing_is_waiting_for_says_where_to_look()
    {
        var found = await _delivery.FindAsync("nothing-here", null);

        found.Error.Should().Contain("loadout pr list");
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
