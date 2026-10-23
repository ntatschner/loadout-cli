using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Subjects and trust in the spine: what a new event does to the actions
/// waiting on its subject, the stale list for events that arrive late, and
/// lowering trust at once.
/// </summary>
public sealed class TriggerSubjectTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-subj-" + Guid.NewGuid().ToString("N"));
    private readonly TriggerRules _rules;
    private readonly TriggerJournal _journal;
    private readonly Actions _actions = new();

    public TriggerSubjectTests()
    {
        _rules = new TriggerRules(_state, new YamlStore(new NoOpFilePermissions()));
        _journal = new TriggerJournal(_state);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_state, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_closed_pull_request_matches_only_a_rule_that_asks_about_the_closing()
    {
        var closed = Pull(TriggerEventKinds.PrMerged, state: "merged");

        TriggerMatcher.Decide(closed, Rule(TriggerEventKinds.PrMerged)).Matched.Should().BeTrue();
        TriggerMatcher.Decide(closed with { Kind = TriggerEventKinds.CiFailed }, Rule(TriggerEventKinds.CiFailed))
            .Reason.Should().Be("the pull request is merged");
    }

    [Fact]
    public void A_draft_is_passed_by_unless_the_rule_asks_for_drafts_or_for_going_back_to_draft()
    {
        var draft = Pull(TriggerEventKinds.PrUpdated, draft: true);

        TriggerMatcher.Decide(draft, Rule(TriggerEventKinds.PrUpdated)).Reason.Should().Contain("draft=true");

        var wants = Rule(TriggerEventKinds.PrUpdated);
        wants.Where["draft"] = "true";
        TriggerMatcher.Decide(draft, wants).Matched.Should().BeTrue();

        TriggerMatcher.Decide(draft with { Kind = TriggerEventKinds.PrDraft }, Rule(TriggerEventKinds.PrDraft))
            .Matched.Should().BeTrue();
    }

    [Fact]
    public async Task An_event_that_arrives_older_than_the_catch_up_window_goes_on_the_stale_list()
    {
        await SaveAsync(Rule(TriggerEventKinds.PrUpdated));

        await Spine().DispatchAsync(Pull(TriggerEventKinds.PrUpdated, at: Now.AddDays(-2)), CancellationToken.None);

        Kinds().Should().Contain(TriggerEntryKinds.Stale).And.NotContain(TriggerEntryKinds.Started);
        TriggerSpine.Stale(_journal.Read(Now.AddDays(-3)), Now).Should().ContainSingle()
            .Which.Event.Subject.Should().Be("pr:acme/app#7");
        Reasons(TriggerEntryKinds.Stale).Should().ContainSingle().Which.Should().Contain("2 days ago");
    }

    [Fact]
    public async Task A_push_withdraws_an_action_waiting_on_the_head_it_moved_past()
    {
        await SaveAsync(Rule(TriggerEventKinds.CiFailed, debounce: TimeSpan.FromMinutes(5)));
        var spine = Spine();

        await spine.DispatchAsync(Pull(TriggerEventKinds.CiFailed, id: "ci", head: "aaa"), CancellationToken.None);
        await spine.DispatchAsync(Pull(TriggerEventKinds.PrUpdated, id: "push", head: "bbb"), CancellationToken.None);
        await spine.FlushAsync(Now.AddHours(1), CancellationToken.None);

        Reasons(TriggerEntryKinds.Withdrawn).Should().ContainSingle().Which.Should().Be("the branch moved on to bbb");
        _actions.Described.Should().Be(0, "nothing was decided on the old head");
    }

    [Fact]
    public async Task Closing_withdraws_what_waits_and_going_back_to_draft_withdraws_what_does_not_take_drafts()
    {
        await SaveAsync(Rule(TriggerEventKinds.CiFailed, debounce: TimeSpan.FromMinutes(5)));
        var spine = Spine();

        await spine.DispatchAsync(Pull(TriggerEventKinds.CiFailed, id: "ci1"), CancellationToken.None);
        await spine.DispatchAsync(Pull(TriggerEventKinds.PrClosed, id: "close", state: "closed"), CancellationToken.None);

        await spine.DispatchAsync(Pull(TriggerEventKinds.CiFailed, id: "ci2", subject: "pr:acme/app#8"), CancellationToken.None);
        await spine.DispatchAsync(
            Pull(TriggerEventKinds.PrDraft, id: "draft", subject: "pr:acme/app#8", draft: true), CancellationToken.None);

        Reasons(TriggerEntryKinds.Withdrawn).Should().Equal(
            "the pull request was closed",
            "draft arrived and it is a draft, and the rule takes drafts only with --where draft=true");
    }

    [Fact]
    public async Task A_new_event_never_raises_the_trust_of_an_action_already_waiting()
    {
        var rule = Rule(TriggerEventKinds.CiFailed, debounce: TimeSpan.FromMinutes(5));
        await SaveAsync(rule);
        var spine = Spine();

        await spine.DispatchAsync(Pull(TriggerEventKinds.CiFailed, id: "ci", trust: "outside"), CancellationToken.None);
        await spine.DispatchAsync(Pull(TriggerEventKinds.Comment, id: "c", trust: "trusted"), CancellationToken.None);
        await spine.FlushAsync(Now.AddHours(1), CancellationToken.None);

        Taken(spine).Should().ContainSingle().Which.Event.Field("trust").Should().Be("outside");
    }

    [Fact]
    public async Task A_branch_failure_folds_into_the_pull_request_opened_from_it_and_takes_its_trust()
    {
        await SaveAsync(Rule(TriggerEventKinds.CiFailed, debounce: TimeSpan.FromMinutes(5)));
        var spine = Spine();

        await spine.DispatchAsync(
            Pull(TriggerEventKinds.CiFailed, id: "ci", subject: "branch:acme/app:feature", number: string.Empty),
            CancellationToken.None);
        await spine.DispatchAsync(Opened("pr:acme/app#7", trust: "trusted"), CancellationToken.None);
        await spine.FlushAsync(Now.AddHours(1), CancellationToken.None);

        Reasons(TriggerEntryKinds.Folded).Should().ContainSingle().Which.Should().Contain("folded into pr:acme/app#7");
        var started = Taken(spine).Should().ContainSingle().Subject;
        started.Event.Subject.Should().Be("pr:acme/app#7");
        started.Event.Field("number").Should().Be("7");
        started.Event.Field("run").Should().Be("42", "it is still the same failure");
    }

    [Fact]
    public async Task A_fold_into_an_outside_pull_request_is_withdrawn_from_a_rule_that_needs_trust()
    {
        var rule = Rule(TriggerEventKinds.CiFailed, debounce: TimeSpan.FromMinutes(5));
        rule.Where["trust"] = "trusted";
        await SaveAsync(rule);
        var spine = Spine();

        await spine.DispatchAsync(
            Pull(TriggerEventKinds.CiFailed, id: "ci", subject: "branch:acme/app:feature"), CancellationToken.None);
        await spine.DispatchAsync(Opened("pr:acme/app#7", trust: "outside"), CancellationToken.None);
        await spine.FlushAsync(Now.AddHours(1), CancellationToken.None);

        Reasons(TriggerEntryKinds.Withdrawn).Should().ContainSingle().Which.Should().Contain("trust is 'outside'");
        Taken(spine).Should().BeEmpty();
    }

    [Fact]
    public async Task Taking_somebody_off_the_allow_list_withdraws_at_once_what_waits_on_their_word_and_nothing_else()
    {
        await SaveAsync(Rule(TriggerEventKinds.PrUpdated, debounce: TimeSpan.FromMinutes(5)));
        var spine = Spine();

        await spine.DispatchAsync(
            Pull(TriggerEventKinds.PrUpdated, id: "bot", subject: "pr:acme/app#1", authorId: "496", because: "allow-list bot (everywhere)"),
            CancellationToken.None);
        await spine.DispatchAsync(
            Pull(TriggerEventKinds.PrUpdated, id: "member", subject: "pr:acme/app#2", authorId: "496", because: "association MEMBER"),
            CancellationToken.None);
        await spine.DispatchAsync(
            Pull(TriggerEventKinds.PrUpdated, id: "late", subject: "pr:acme/app#3", authorId: "496",
                because: "allow-list bot (everywhere)", at: Now.AddDays(-3)),
            CancellationToken.None);

        var answer = await spine.ApplyAsync(
            new TriggerAsk("q1", TriggerVerbs.Distrust, Author: 496, Login: "bot"), CancellationToken.None);
        await spine.FlushAsync(Now.AddHours(1), CancellationToken.None);

        answer.Applied.Should().BeTrue();
        answer.Message.Should().Contain("2 waiting on bot's trust withdrawn");
        Taken(spine).Should().ContainSingle().Which.Event.Id.Should().Be("member", "GitHub's association decided that one");
        TriggerSpine.Stale(_journal.Read(Now.AddDays(-4)), Now).Should().BeEmpty();
        Kinds().Should().Contain(TriggerEntryKinds.Distrusted);
    }

    [Fact]
    public void Trust_is_a_collaborator_association_first_then_an_allow_list_entry_that_covers_the_repository()
    {
        TriggerAllowEntry[] allowed =
        [
            new() { Id = 1, Login = "bot" },
            new() { Id = 2, Login = "pat", Owner = "acme" },
            new() { Id = 3, Login = "sam", Repo = "acme/app" },
        ];

        TriggerTrust.Decide("COLLABORATOR", 9, "acme/app", allowed).Should().Be(new TrustVerdict(true, "association COLLABORATOR"));
        TriggerTrust.Decide("NONE", 1, "other/x", allowed).Trusted.Should().BeTrue();
        TriggerTrust.Decide("NONE", 2, "acme/lib", allowed).Because.Should().Be("allow-list pat (acme/*)");
        TriggerTrust.Decide("NONE", 2, "acmeco/lib", allowed).Trusted.Should().BeFalse("acmeco is not acme");
        TriggerTrust.Decide("NONE", 3, "acme/lib", allowed).Trusted.Should().BeFalse();
        TriggerTrust.Decide(null, null, "acme/app", allowed).Because.Should().Be("no association given");
    }

    [Fact]
    public async Task The_allow_list_keeps_one_entry_per_account_and_scope_and_removes_by_scope()
    {
        var store = new TriggerAllowStore(_state, new YamlStore(new NoOpFilePermissions()));

        (await store.AddAsync(new TriggerAllowEntry { Id = 5, Login = "pat" })).Succeeded.Should().BeTrue();
        (await store.AddAsync(new TriggerAllowEntry { Id = 5, Login = "pat", Repo = "acme/app" })).Succeeded.Should().BeTrue();
        (await store.AddAsync(new TriggerAllowEntry { Id = 5, Login = "pat-renamed" })).Succeeded.Should().BeTrue();
        (await store.AddAsync(new TriggerAllowEntry { Id = 6, Login = "x", Repo = "a/b", Owner = "a" })).Error.Should().Contain("not both");
        (await store.AddAsync(new TriggerAllowEntry { Id = 6, Login = "x", Repo = "nonsense" })).Error.Should().Contain("owner/name");

        (await store.ListAsync()).Value!.Select(one => (one.Login, one.Scope)).Should().BeEquivalentTo(
            [("pat", "acme/app"), ("pat-renamed", "everywhere")]);

        var gone = await store.RemoveAsync("pat", repo: "acme/app");
        gone.Value!.Should().ContainSingle();
        (await store.ListAsync()).Value!.Should().ContainSingle().Which.Login.Should().Be("pat-renamed");
        (await store.RemoveAsync("nobody")).Error.Should().Contain("not on the allow-list");
    }

    [Fact]
    public void A_repository_is_named_from_a_github_remote_and_nothing_else()
    {
        GitHubRepo.From("git@github.com:acme/app.git").Should().Be("acme/app");
        GitHubRepo.From("https://github.com/acme/app").Should().Be("acme/app");
        GitHubRepo.From("https://gitlab.com/acme/app").Should().BeNull();
        GitHubRepo.From(null).Should().BeNull();
        GitHubRepo.Parse("acme/app/extra").Should().BeNull();
    }

    /// <summary>What the spine decided to start, taken off its queue with no workers running.</summary>
    private static List<TriggerStart> Taken(TriggerSpine spine)
    {
        var taken = new List<TriggerStart>();

        while (spine.TryTake(out var start))
        {
            taken.Add(start);
        }

        return taken;
    }

    private TriggerSpine Spine() => new(_rules, _journal, _actions, new Clock(Now));

    private async Task SaveAsync(TriggerRule rule) => (await _rules.SaveAsync(rule)).Succeeded.Should().BeTrue();

    private IEnumerable<string> Kinds() => _journal.Read(Now.AddDays(-5)).Select(one => one.Kind);

    private IEnumerable<string?> Reasons(string kind) =>
        _journal.Read(Now.AddDays(-5)).Where(one => one.Kind == kind).Select(one => one.Reason);

    private static TriggerRule Rule(string on, TimeSpan? debounce = null) => new()
    {
        Id = "watch",
        On = on,
        Action = new TriggerAction { Name = "reviewer", Goal = "Look at {title}." },
        CapUsd = 10m,
        Debounce = debounce ?? TimeSpan.Zero,
    };

    private static TriggerEvent Opened(string subject, string trust) =>
        Pull(TriggerEventKinds.PrOpened, id: "open", subject: subject, trust: trust) is var opened
            ? opened with
            {
                Fields = new Dictionary<string, string>(opened.Fields, StringComparer.OrdinalIgnoreCase)
                {
                    [TriggerSubjects.FoldsField] = "branch:acme/app:feature",
                },
            }
            : throw new InvalidOperationException();

    private static TriggerEvent Pull(
        string kind,
        string id = "e1",
        string subject = "pr:acme/app#7",
        string head = "abc",
        string state = "open",
        bool draft = false,
        string trust = "trusted",
        string number = "7",
        string authorId = "1",
        string because = "association OWNER",
        DateTimeOffset? at = null) =>
        new(id, at ?? Now, GitHubSource.Name, kind, subject, "loadout-cli",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = "acme/app",
                ["number"] = number,
                ["base"] = "main",
                ["branch"] = "feature",
                ["head"] = head,
                ["author"] = "someone",
                ["author-id"] = authorId,
                ["trust"] = trust,
                ["because"] = because,
                ["draft"] = draft ? "true" : "false",
                ["state"] = state,
                ["title"] = "A change",
                ["run"] = "42",
            },
            []);

    private sealed class Actions : ITriggerActions
    {
        public int Described { get; private set; }

        public Task<ActionTraits> DescribeAsync(TriggerAction action, string? project, CancellationToken ct)
        {
            Described++;

            return Task.FromResult(new ActionTraits(false, BudgetUsd: 1m));
        }

        public Task<ActionOutcome> StartAsync(TriggerStart start, CancellationToken ct) =>
            Task.FromResult(new ActionOutcome(0m));

        public Task<Settling> SettleAsync(string reservation, CancellationToken ct) =>
            Task.FromResult(new Settling(SettleState.Unknown));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
