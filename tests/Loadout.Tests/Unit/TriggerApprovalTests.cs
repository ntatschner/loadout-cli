using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Approvals for actions that write, and starts held until money comes back:
/// what starts them, what replaces them, and what lets them go.
/// </summary>
public sealed class TriggerApprovalTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-appr-" + Guid.NewGuid().ToString("N"));
    private readonly TriggerRules _rules;
    private readonly TriggerJournal _journal;
    private readonly Actions _actions = new();
    private readonly Clock _clock = new(Now);

    public TriggerApprovalTests()
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
    public async Task Approving_a_write_sets_its_money_aside_and_starts_it_and_rejecting_one_starts_nothing()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);

        var pending = Pending();
        pending.Should().HaveCount(2);
        pending.Should().AllSatisfy(one => one.BudgetUsd.Should().Be(6m));
        Taken(spine).Should().BeEmpty();

        var approved = await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Approve, Approval: pending[0].Id), CancellationToken.None);
        approved.Message.Should().Contain("approved and has started");
        Taken(spine).Should().ContainSingle().Which.Event.Id.Should().Be("e1");
        spine.Ledger.Standing(Rule(), Now).Reserved.Should().Be(6m);

        var rejected = await spine.ApplyAsync(new TriggerAsk("q2", TriggerVerbs.Reject, Approval: pending[1].Id), CancellationToken.None);
        rejected.Applied.Should().BeTrue();
        Pending().Should().BeEmpty();
        Taken(spine).Should().BeEmpty();

        (await spine.ApplyAsync(new TriggerAsk("q3", TriggerVerbs.Approve, Approval: pending[1].Id), CancellationToken.None))
            .Applied.Should().BeFalse("a rejected one is no longer waiting");
    }

    [Fact]
    public async Task An_approved_write_short_of_headroom_is_held_and_starts_without_asking_again_once_money_comes_back()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);
        var pending = Pending();

        await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Approve, Approval: pending[0].Id), CancellationToken.None);
        var first = Taken(spine).Single();

        var held = await spine.ApplyAsync(new TriggerAsk("q2", TriggerVerbs.Approve, Approval: pending[1].Id), CancellationToken.None);
        held.Message.Should().Contain("held until its rule has headroom");
        Pending().Should().ContainSingle().Which.Approved.Should().BeTrue();

        await spine.ReleaseHeldAsync(Now, CancellationToken.None);
        Taken(spine).Should().BeEmpty("the first run's money is still set aside");

        _actions.Spends = 2m;
        await spine.RunOneAsync(first, CancellationToken.None);
        await spine.ReleaseHeldAsync(Now, CancellationToken.None);

        Taken(spine).Should().ContainSingle().Which.Event.Id.Should().Be("e2");
        Reasons(TriggerEntryKinds.Asked).Should().HaveCount(2, "it was not asked about a second time");
        Pending().Should().BeEmpty();
    }

    [Fact]
    public async Task A_newer_event_on_the_subject_replaces_an_approval_and_asks_afresh_saying_when_it_had_been_approved()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        // Use up the headroom so an approval is held rather than started.
        await spine.DispatchAsync(Pull("e0", "pr:acme/app#9"), CancellationToken.None);
        await spine.ApplyAsync(new TriggerAsk("q0", TriggerVerbs.Approve, Approval: Pending()[0].Id), CancellationToken.None);

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Approve, Approval: Pending()[0].Id), CancellationToken.None);

        await spine.DispatchAsync(Pull("e2", "pr:acme/app#1", head: "newer"), CancellationToken.None);

        Reasons(TriggerEntryKinds.Superseded).Should().ContainSingle()
            .Which.Should().Be("replaced by e2, which is asked about afresh; it had been approved, so you are told");
        var now = Pending().Should().ContainSingle().Subject;
        now.Event.Id.Should().Be("e2");
        now.Approved.Should().BeFalse("approval means the state that was seen");

        await spine.ReleaseHeldAsync(Now, CancellationToken.None);
        Taken(spine).Should().ContainSingle().Which.Event.Id.Should().Be("e0", "the replaced approval never starts");
    }

    [Fact]
    public async Task Closing_the_subject_withdraws_its_approval_and_an_unanswered_one_goes_after_a_day()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);
        await spine.DispatchAsync(
            Pull("close", "pr:acme/app#1", kind: TriggerEventKinds.PrClosed, state: "closed"), CancellationToken.None);

        Reasons(TriggerEntryKinds.Withdrawn).Should().ContainSingle().Which.Should().Be("the pull request was closed");
        Pending().Should().ContainSingle().Which.Event.Id.Should().Be("e2");

        await spine.ReleaseHeldAsync(Now.AddHours(25), CancellationToken.None);

        Reasons(TriggerEntryKinds.Withdrawn).Should().Contain("not answered within 24 hours");
        Pending().Should().BeEmpty();
    }

    [Fact]
    public async Task Editing_what_a_rule_spends_asks_its_approvals_again_and_removing_the_rule_withdraws_them()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        var first = Pending().Single();

        var renamed = Rule();
        renamed.Debounce = TimeSpan.FromSeconds(1);
        await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Save, Rule: renamed), CancellationToken.None);
        Pending().Single().Id.Should().Be(first.Id, "nothing it would start or spend changed");

        var dearer = Rule();
        dearer.BudgetUsd = 8m;
        await spine.ApplyAsync(new TriggerAsk("q2", TriggerVerbs.Save, Rule: dearer), CancellationToken.None);

        var asked = Pending().Should().ContainSingle().Subject;
        asked.Id.Should().NotBe(first.Id);
        asked.BudgetUsd.Should().Be(8m);
        asked.Version.Should().Be(3);

        await spine.ApplyAsync(new TriggerAsk("q3", TriggerVerbs.Remove, RuleId: "review"), CancellationToken.None);
        Pending().Should().BeEmpty();
        Reasons(TriggerEntryKinds.Withdrawn).Should().Contain("the rule was removed");
    }

    [Fact]
    public async Task Approving_after_the_rule_changed_under_it_is_refused()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        var pending = Pending().Single();

        // Edited behind the daemon's back, as a race between an edit and an approval would leave it.
        var edited = Rule();
        edited.BudgetUsd = 9m;
        await SaveAsync(edited);

        var answer = await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Approve, Approval: pending.Id), CancellationToken.None);

        answer.Applied.Should().BeFalse();
        answer.Message.Should().Contain("has changed since");
        Taken(spine).Should().BeEmpty();
    }

    [Fact]
    public async Task Starts_held_for_headroom_go_oldest_first_survive_a_restart_and_go_stale_after_a_day()
    {
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e3", "pr:acme/app#3"), CancellationToken.None);

        var first = Taken(spine).Should().ContainSingle().Subject;
        Kinds().Count(one => one == TriggerEntryKinds.Held).Should().Be(2);

        // A new daemon reads the held starts back from the journal.
        var restarted = Spine();
        restarted.Rebuild();

        _actions.Spends = 1m;
        await restarted.RunOneAsync(first, CancellationToken.None);
        await restarted.ReleaseHeldAsync(Now, CancellationToken.None);

        Taken(restarted).Should().ContainSingle().Which.Event.Id.Should().Be("e2", "the first held is the first started");

        await restarted.ReleaseHeldAsync(Now.AddHours(25), CancellationToken.None);

        TriggerSpine.Stale(_journal.Read(Now.AddDays(-1)), Now.AddHours(25)).Should().ContainSingle()
            .Which.Event.Id.Should().Be("e3");
    }

    [Fact]
    public async Task A_newer_event_for_a_held_start_replaces_it_in_its_place()
    {
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2b", "pr:acme/app#2", head: "later"), CancellationToken.None);

        var first = Taken(spine).Single();
        _actions.Spends = 1m;
        await spine.RunOneAsync(first, CancellationToken.None);
        await spine.ReleaseHeldAsync(Now, CancellationToken.None);

        Reasons(TriggerEntryKinds.Superseded).Should().Contain("replaced by e2b");
        Taken(spine).Should().ContainSingle().Which.Event.Id.Should().Be("e2b");
    }

    [Fact]
    public async Task Taking_an_author_off_the_allow_list_withdraws_their_approvals()
    {
        _actions.Writes = true;
        await SaveAsync(Rule());
        var spine = Spine();

        await spine.DispatchAsync(Pull("e1", "pr:acme/app#1", because: "allow-list bot (everywhere)"), CancellationToken.None);
        await spine.DispatchAsync(Pull("e2", "pr:acme/app#2"), CancellationToken.None);

        var answer = await spine.ApplyAsync(new TriggerAsk("q1", TriggerVerbs.Distrust, Author: 7, Login: "bot"), CancellationToken.None);

        answer.Message.Should().Contain("1 waiting on bot's trust withdrawn");
        Pending().Should().ContainSingle().Which.Event.Id.Should().Be("e2");
    }

    private TriggerSpine Spine() => new(_rules, _journal, _actions, _clock);

    private async Task SaveAsync(TriggerRule rule) => (await _rules.SaveAsync(rule)).Succeeded.Should().BeTrue();

    private List<PendingApproval> Pending() => [.. TriggerSpine.Approvals(_journal.Read(Now.AddDays(-2)))];

    private IEnumerable<string> Kinds() => _journal.Read(Now.AddDays(-2)).Select(one => one.Kind);

    private IEnumerable<string?> Reasons(string kind) =>
        _journal.Read(Now.AddDays(-2)).Where(one => one.Kind == kind).Select(one => one.Reason);

    private static List<TriggerStart> Taken(TriggerSpine spine)
    {
        var taken = new List<TriggerStart>();

        while (spine.TryTake(out var start))
        {
            taken.Add(start);
        }

        return taken;
    }

    private static TriggerRule Rule() => new()
    {
        Id = "review",
        On = TriggerEventKinds.PrUpdated,
        Action = new TriggerAction { Name = "fixer", Goal = "Fix {title}." },
        CapUsd = 10m,
    };

    private static TriggerEvent Pull(
        string id,
        string subject,
        string head = "abc",
        string kind = TriggerEventKinds.PrUpdated,
        string state = "open",
        string because = "association OWNER") =>
        new(id, Now, GitHubSource.Name, kind, subject, "app",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["repo"] = "acme/app",
                ["head"] = head,
                ["author-id"] = "7",
                ["trust"] = "trusted",
                ["because"] = because,
                ["draft"] = "false",
                ["state"] = state,
                ["title"] = "a change",
            },
            []);

    private sealed class Actions : ITriggerActions
    {
        public bool Writes { get; set; }

        public decimal Spends { get; set; }

        public Task<ActionTraits> DescribeAsync(TriggerAction action, string? project, CancellationToken ct) =>
            Task.FromResult(new ActionTraits(Writes, BudgetUsd: 6m));

        public Task<ActionOutcome> StartAsync(TriggerStart start, CancellationToken ct) =>
            Task.FromResult(new ActionOutcome(Spends));

        public Task<Settling> SettleAsync(string reservation, CancellationToken ct) =>
            Task.FromResult(new Settling(SettleState.Unknown));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
