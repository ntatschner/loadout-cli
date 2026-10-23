using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>The trigger spine: rules, the journal, the spend ledger and the dispatcher.</summary>
public sealed class TriggerSpineTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-trig-" + Guid.NewGuid().ToString("N"));
    private readonly TriggerRules _rules;
    private readonly TriggerJournal _journal;
    private readonly Clock _clock = new(Now);
    private readonly ScriptedActions _actions = new();

    public TriggerSpineTests()
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
    public void A_rule_that_starts_a_model_needs_a_spend_cap_and_a_built_in_does_not()
    {
        var uncapped = Review();
        uncapped.CapUsd = null;

        TriggerRules.Check(uncapped).Should().Contain("spend cap");

        var builtin = Review();
        builtin.CapUsd = null;
        builtin.Action = new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit" };

        TriggerRules.Check(builtin).Should().BeNull("a built-in has no model behind it");
    }

    [Fact]
    public void A_budget_above_the_cap_and_a_condition_on_a_field_the_event_never_carries_are_refused()
    {
        var greedy = Review();
        greedy.BudgetUsd = 50m;

        TriggerRules.Check(greedy).Should().Contain("no more than its spend cap");

        var impossible = Review();
        impossible.Where["branch"] = "main";

        TriggerRules.Check(impossible).Should().Contain("carries no 'branch'");
    }

    [Fact]
    public async Task Saving_a_rule_again_counts_its_version_on_whatever_the_caller_said()
    {
        (await _rules.SaveAsync(Review())).Value!.Version.Should().Be(1);

        var again = Review();
        again.Version = 7;

        (await _rules.SaveAsync(again)).Value!.Version.Should().Be(2);

        var listed = await _rules.ListAsync();
        listed.Value!.Should().ContainSingle().Which.Rule.Version.Should().Be(2);
    }

    [Fact]
    public async Task A_rule_edited_by_hand_without_a_cap_is_listed_with_why_and_not_acted_on()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_rules.FilePath)!);
        await File.WriteAllTextAsync(
            _rules.FilePath,
            "items:\n- id: loose\n  on: run-finished\n  action:\n    kind: team\n    name: reviewer\n    goal: look\n");

        var listed = (await _rules.ListAsync()).Value!;

        listed.Should().ContainSingle().Which.Usable.Should().BeFalse();
        TriggerMatcher.Decide(Finished("alpha"), listed).Should().BeEmpty("an unusable rule decides nothing");
    }

    [Fact]
    public void The_matcher_says_why_a_rule_passes_an_event_by()
    {
        var rule = Review();
        rule.Projects = ["loadout-cli"];
        rule.Where["outcome"] = "failed";

        Reason(Finished("alpha", project: "starstats")).Should().Contain("watches loadout-cli");
        Reason(Finished("alpha", outcome: "done")).Should().Be("outcome is 'done', not 'failed'");
        Reason(Finished("reviewer", outcome: "failed")).Should().Contain("reviewer's own");
        Reason(Finished("alpha", outcome: "failed", origins: ["review-failures"])).Should().Contain("own action");
        Reason(Finished("alpha", outcome: "failed", origins: ["a", "b", "c"])).Should().Contain("3 rules");
        TriggerMatcher.Decide(Finished("alpha", outcome: "failed"), rule).Matched.Should().BeTrue();

        string? Reason(TriggerEvent happened) => TriggerMatcher.Decide(happened, rule).Reason;
    }

    [Fact]
    public void Money_set_aside_for_runs_going_holds_a_start_and_with_nothing_going_the_rule_pauses()
    {
        var rule = Review();
        var ledger = new SpendLedger();

        ledger.Apply(Reserved(rule, "r1", 4m));
        ledger.Apply(Reserved(rule, "r2", 4m));

        ledger.Ask(rule, 4m, Now).Verdict.Should().Be(LedgerVerdict.Hold, "$2 is left and two runs are going");

        ledger.Apply(Landed(rule, "r1", 3m));
        ledger.Apply(Landed(rule, "r2", 4m));

        ledger.Standing(rule, Now).Headroom.Should().Be(3m);
        ledger.Ask(rule, 4m, Now).Verdict.Should().Be(LedgerVerdict.Pause, "nothing in flight can free headroom");
        ledger.Ask(rule, 4m, Now + TimeSpan.FromHours(25)).Verdict
            .Should().Be(LedgerVerdict.Go, "the spend has left the rolling window");
    }

    [Fact]
    public void Resuming_starts_the_window_afresh_and_the_ledger_rebuilt_from_the_journal_agrees()
    {
        var rule = Review();
        rule.CapRuns = 1;

        var entries = new List<TriggerEntry>
        {
            Reserved(rule, "r1", 2m),
            new(Now, TriggerEntryKinds.Started, Rule: rule.Id, Reservation: "r1"),
            new(Now, TriggerEntryKinds.Paused, Rule: rule.Id),
        };

        var ledger = SpendLedger.Rebuild(entries);
        ledger.Ask(rule, 2m, Now).Verdict.Should().Be(LedgerVerdict.Hold, "a paused rule holds what it matches");

        entries.Add(new TriggerEntry(Now.AddMinutes(5), TriggerEntryKinds.Resumed, Rule: rule.Id));
        var rebuilt = SpendLedger.Rebuild(entries);

        rebuilt.Ask(rule, 2m, Now.AddMinutes(6)).Verdict.Should().Be(LedgerVerdict.Go);
        rebuilt.Standing(rule, Now.AddMinutes(6)).Reserved
            .Should().Be(2m, "a run still going keeps what it set aside across a resume");
    }

    [Fact]
    public async Task A_match_is_journalled_reserved_started_and_landed_in_that_order()
    {
        await _rules.SaveAsync(Review());
        _actions.Spends = 1.25m;

        var spine = Spine();
        await spine.DispatchAsync(Finished("alpha"), CancellationToken.None);
        spine.TryTake(out var start).Should().BeTrue();
        await spine.RunOneAsync(start, CancellationToken.None);

        Kinds().Should().Equal(
            TriggerEntryKinds.Event, TriggerEntryKinds.Matched, TriggerEntryKinds.Reserved,
            TriggerEntryKinds.Started, TriggerEntryKinds.Landed);

        var landed = _journal.Read(Now.AddDays(-1)).Last();
        landed.Usd.Should().Be(1.25m);
        landed.Run.Should().Be("run-1");
        spine.Ledger.Standing(Review(), Now).Should().Be(new RuleStanding(1, 1.25m, 0m, 8.75m, false));
    }

    [Fact]
    public async Task An_action_that_writes_waits_for_approval_rather_than_starting()
    {
        await _rules.SaveAsync(Review());
        _actions.Writes = true;

        await Spine().DispatchAsync(Finished("alpha"), CancellationToken.None);

        var asked = _journal.Read(Now.AddDays(-1)).Last();
        asked.Kind.Should().Be(TriggerEntryKinds.Asked);
        asked.Reason.Should().Contain("waits for your approval");
        asked.Approval.Should().NotBeNullOrEmpty();
        _actions.Started.Should().BeEmpty();
        Kinds().Should().NotContain(TriggerEntryKinds.Reserved, "nothing is set aside until it is approved");
    }

    [Fact]
    public async Task A_burst_about_one_subject_becomes_one_start_on_the_latest_event()
    {
        var rule = Review();
        rule.Debounce = TimeSpan.FromMinutes(2);
        await _rules.SaveAsync(rule);

        var spine = Spine();
        await spine.DispatchAsync(Finished("alpha", id: "e1", subject: "run:1"), CancellationToken.None);
        await spine.DispatchAsync(Finished("alpha", id: "e2", subject: "run:1"), CancellationToken.None);

        await spine.FlushAsync(Now.AddMinutes(1), CancellationToken.None);
        Kinds().Should().NotContain(TriggerEntryKinds.Started, "the subject has not gone quiet");

        await spine.FlushAsync(Now.AddMinutes(3), CancellationToken.None);

        var entries = _journal.Read(Now.AddDays(-1));
        entries.Single(one => one.Kind == TriggerEntryKinds.Superseded).Event.Should().Be("e1");
        entries.Single(one => one.Kind == TriggerEntryKinds.Started).Event.Should().Be("e2");
    }

    [Fact]
    public async Task The_run_cap_pauses_the_rule_and_holds_what_matches_after()
    {
        var rule = Review();
        rule.CapRuns = 1;
        await _rules.SaveAsync(rule);

        var spine = Spine();
        await spine.DispatchAsync(Finished("alpha", id: "e1", subject: "run:1"), CancellationToken.None);
        await spine.DispatchAsync(Finished("alpha", id: "e2", subject: "run:2"), CancellationToken.None);
        await spine.DispatchAsync(Finished("alpha", id: "e3", subject: "run:3"), CancellationToken.None);

        var entries = _journal.Read(Now.AddDays(-1));
        entries.Count(one => one.Kind == TriggerEntryKinds.Started).Should().Be(1);
        entries.Single(one => one.Kind == TriggerEntryKinds.Paused).Event.Should().Be("e2");
        entries.Where(one => one.Kind == TriggerEntryKinds.Held).Select(one => one.Event)
            .Should().Equal("e2", "e3");
    }

    [Fact]
    public async Task A_new_spine_over_the_same_journal_still_counts_what_the_last_one_set_aside()
    {
        var rule = Review();
        rule.BudgetUsd = 6m;
        await _rules.SaveAsync(rule);

        await Spine().DispatchAsync(Finished("alpha", id: "e1", subject: "run:1"), CancellationToken.None);

        // The daemon restarts with that run's reservation still open.
        var after = Spine();
        using var stop = new CancellationTokenSource();
        var running = after.RunAsync(stop.Token);

        await after.PostAsync(Finished("alpha", id: "e2", subject: "run:2"));
        await Eventually(() => _journal.Read(Now.AddDays(-1)).Any(one => one.Event == "e2" && one.Kind == TriggerEntryKinds.Held));

        await stop.CancelAsync();
        await running;

        after.Ledger.Standing(rule, Now).Reserved.Should().Be(6m);
    }

    [Fact]
    public async Task Money_a_stopped_daemon_left_set_aside_lands_once_its_run_ends_and_not_before()
    {
        var rule = Review();
        rule.BudgetUsd = 6m;
        await _rules.SaveAsync(rule);

        await Spine().DispatchAsync(Finished("alpha", id: "e1", subject: "run:1"), CancellationToken.None);
        var reservation = _journal.Read(Now.AddDays(-1)).Single(one => one.Kind == TriggerEntryKinds.Reserved).Reservation!;

        var after = new TriggerSpine(_rules, _journal, _actions, _clock);
        after.Rebuild();

        _actions.Settled[reservation] = new Settling(SettleState.Going);
        await after.SettleAsync(Now.AddHours(3), CancellationToken.None);
        after.Ledger.Standing(rule, Now).Reserved.Should().Be(6m, "the run is still going");

        _actions.Settled[reservation] = new Settling(SettleState.Ended, new ActionOutcome(4.5m, "run-7"));
        await after.SettleAsync(Now.AddHours(3), CancellationToken.None);

        var standing = after.Ledger.Standing(rule, Now.AddHours(3));
        standing.Reserved.Should().Be(0m);
        standing.Landed.Should().Be(4.5m);
    }

    [Fact]
    public async Task A_reservation_no_run_carries_is_released_after_an_hour_and_not_sooner()
    {
        await _rules.SaveAsync(Review());

        var spine = Spine();
        await spine.DispatchAsync(Finished("alpha"), CancellationToken.None);

        await spine.SettleAsync(Now.AddMinutes(30), CancellationToken.None);
        spine.Ledger.Standing(Review(), Now).Reserved.Should().Be(3m, "a run may still be about to name itself");

        await spine.SettleAsync(Now.AddHours(2), CancellationToken.None);
        spine.Ledger.Standing(Review(), Now.AddHours(2)).Reserved.Should().Be(0m);
        _journal.Read(Now.AddDays(-1)).Last().Reason.Should().Contain("stopped before it began");
    }

    [Fact]
    public async Task Events_posted_to_a_running_spine_reach_a_worker_and_land()
    {
        await _rules.SaveAsync(Review());
        _actions.Spends = 0.5m;

        var spine = Spine();
        using var stop = new CancellationTokenSource();
        var running = spine.RunAsync(stop.Token);

        await spine.PostAsync(Finished("alpha"));
        await Eventually(() => _journal.Read(Now.AddDays(-1)).Any(one => one.Kind == TriggerEntryKinds.Landed));

        await stop.CancelAsync();
        await running;

        _actions.Started.Should().ContainSingle().Which.BudgetUsd.Should().Be(3m, "the team's own figure");
    }

    private TriggerSpine Spine() => new(_rules, _journal, _actions, _clock);

    private IEnumerable<string> Kinds() => _journal.Read(Now.AddDays(-1)).Select(one => one.Kind);

    private static async Task Eventually(Func<bool> condition)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);

        while (!condition())
        {
            DateTime.UtcNow.Should().BeBefore(until, "the spine should have got there by now");
            await Task.Delay(20);
        }
    }

    private static TriggerRule Review() => new()
    {
        Id = "review-failures",
        On = TriggerEventKinds.RunFinished,
        Action = new TriggerAction { Name = "reviewer", Goal = "Look at why it failed." },
        CapUsd = 10m,
    };

    private static TriggerEvent Finished(
        string team,
        string project = "loadout-cli",
        string outcome = "failed",
        string id = "e1",
        string subject = "run:1",
        IReadOnlyList<string>? origins = null) =>
        new(id, Now, "daemon", TriggerEventKinds.RunFinished, subject, project,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["team"] = team,
                ["project"] = project,
                ["outcome"] = outcome,
            },
            origins ?? []);

    private static TriggerEntry Reserved(TriggerRule rule, string id, decimal usd) =>
        new(Now, TriggerEntryKinds.Reserved, Rule: rule.Id, Usd: usd, Reservation: id);

    private static TriggerEntry Landed(TriggerRule rule, string id, decimal usd) =>
        new(Now, TriggerEntryKinds.Landed, Rule: rule.Id, Usd: usd, Reservation: id);

    private sealed class ScriptedActions : ITriggerActions
    {
        public bool Writes { get; set; }

        public decimal Spends { get; set; }

        public List<TriggerStart> Started { get; } = [];

        public Task<ActionTraits> DescribeAsync(TriggerAction action, string? project, CancellationToken ct) =>
            Task.FromResult(new ActionTraits(Writes, BudgetUsd: 3m));

        public Task<ActionOutcome> StartAsync(TriggerStart start, CancellationToken ct)
        {
            lock (Started)
            {
                Started.Add(start);
            }

            return Task.FromResult(new ActionOutcome(Spends, Run: $"run-{Started.Count}"));
        }

        public Dictionary<string, Settling> Settled { get; } = [];

        public Task<Settling> SettleAsync(string reservation, CancellationToken ct) =>
            Task.FromResult(Settled.GetValueOrDefault(reservation) ?? new Settling(SettleState.Unknown));
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
