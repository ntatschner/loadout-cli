using System.Text.Json;
using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Triggers;
using Loadout.Models.Platform;
using Loadout.Models.Triggers;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Changing the rules: whether triggers are on, how a change reaches the
/// running daemon, and what the daemon does to what was waiting.
/// </summary>
public sealed class TriggerControlTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-trigctl-" + Guid.NewGuid().ToString("N"));
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml = new(new NoOpFilePermissions());
    private readonly FakeProcessInspector _processes = new();

    public TriggerControlTests()
    {
        _paths = new LinuxPaths(
            Environment(on: true),
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));
        _paths.EnsureDirectoriesExist();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string State => _paths.Paths.State;

    [Fact]
    public async Task A_retry_with_the_same_id_gets_the_first_answer_and_the_change_is_made_once()
    {
        var inbox = new TriggerInbox(State);
        var asking = new TriggerAsk("req-1", TriggerVerbs.Remove, RuleId: "x");

        // The first asking gives up before the daemon gets to it.
        (await inbox.AskAsync(asking, TimeSpan.FromMilliseconds(150), TimeProvider.System, CancellationToken.None))
            .Should().BeNull();

        inbox.Waiting().Should().ContainSingle().Which.Id.Should().Be("req-1");
        inbox.Answer(new TriggerAnswer("req-1", true, "done"));

        // The second, with the same id, finds the answer and asks nothing new.
        var answer = await inbox.AskAsync(asking, TimeSpan.FromSeconds(1), TimeProvider.System, CancellationToken.None);

        answer.Should().Be(new TriggerAnswer("req-1", true, "done"));
        inbox.Waiting().Should().BeEmpty();
    }

    [Fact]
    public void A_request_written_again_after_it_was_answered_is_dropped_not_handed_on()
    {
        // The asker checked for an answer, found none, and wrote its request
        // again just after the daemon answered the first copy.
        var inbox = new TriggerInbox(State);
        inbox.Answer(new TriggerAnswer("req-1", true, "done"));

        File.WriteAllText(
            Path.Combine(inbox.Directory, "req-1.ask.json"),
            JsonSerializer.Serialize(new TriggerAsk("req-1", TriggerVerbs.Remove, RuleId: "x")));

        inbox.Waiting().Should().BeEmpty();
        File.Exists(Path.Combine(inbox.Directory, "req-1.ask.json")).Should().BeFalse();
    }

    [Fact]
    public async Task A_daemon_that_answers_after_the_first_wait_is_heard_on_the_second()
    {
        LiveDaemon();
        var (desk, console) = Desk(TimeSpan.FromMilliseconds(300));
        var inbox = new TriggerInbox(State);

        // Busy for longer than one wait, then answers what it finds.
        var daemon = Task.Run(async () =>
        {
            await Task.Delay(450);

            foreach (var asking in inbox.Waiting())
            {
                inbox.Answer(new TriggerAnswer(asking.Id, true, "applied late"));
            }
        });

        var code = await desk.ChangeAsync(
            new TriggerAsk("req-1", TriggerVerbs.Save, Rule()),
            Output(console),
            () => Task.FromResult(new TriggerAnswer("req-1", true, "should not happen")),
            CancellationToken.None);

        await daemon;

        code.Should().Be(0, "the second asking heard the answer");
        console.Output.Should().Contain("applied late");
    }

    [Fact]
    public async Task An_edit_withdraws_what_no_longer_matches_and_keeps_what_does()
    {
        var rules = new TriggerRules(State, _yaml);
        var rule = Rule();
        rule.Debounce = TimeSpan.FromMinutes(5);
        await rules.SaveAsync(rule);

        var journal = new TriggerJournal(State);
        var spine = new TriggerSpine(rules, journal, new Builtins(), new Clock(Now));
        spine.Rebuild();

        await spine.DispatchAsync(Event("e1", "loadout-cli"), CancellationToken.None);
        await spine.DispatchAsync(Event("e2", "starstats"), CancellationToken.None);

        var edited = Rule();
        edited.Debounce = TimeSpan.FromMinutes(5);
        edited.Projects = ["loadout-cli"];

        var answer = await spine.ApplyAsync(new TriggerAsk("req-1", TriggerVerbs.Save, edited), CancellationToken.None);

        answer.Applied.Should().BeTrue(answer.Message);
        answer.Version.Should().Be(2);

        var withdrawn = journal.Read(Now.AddDays(-1)).Single(one => one.Kind == TriggerEntryKinds.Withdrawn);
        withdrawn.Event.Should().Be("e2");
        withdrawn.Reason.Should().Contain("watches loadout-cli");

        await spine.FlushAsync(Now.AddMinutes(10), CancellationToken.None);

        journal.Read(Now.AddDays(-1)).Where(one => one.Kind == TriggerEntryKinds.Started)
            .Should().ContainSingle().Which.Version.Should().Be(2, "it went ahead under the rule as it is now");
    }

    [Fact]
    public async Task A_request_already_applied_is_not_applied_again_even_by_a_new_daemon()
    {
        var rules = new TriggerRules(State, _yaml);
        var journal = new TriggerJournal(State);
        var spine = new TriggerSpine(rules, journal, new Builtins(), new Clock(Now));
        spine.Rebuild();

        var asking = new TriggerAsk("req-1", TriggerVerbs.Save, Rule());

        (await spine.ApplyAsync(asking, CancellationToken.None)).Version.Should().Be(1);
        (await spine.ApplyAsync(asking, CancellationToken.None)).Message.Should().Contain("Already applied");

        var after = new TriggerSpine(rules, journal, new Builtins(), new Clock(Now));
        after.Rebuild();

        (await after.ApplyAsync(asking, CancellationToken.None)).Applied.Should().BeTrue();
        (await rules.ListAsync()).Value!.Single().Rule.Version.Should().Be(1, "the edit was made once");
    }

    [Fact]
    public async Task Removing_a_rule_withdraws_everything_waiting_on_it()
    {
        var rules = new TriggerRules(State, _yaml);
        var rule = Rule();
        rule.Debounce = TimeSpan.FromMinutes(5);
        await rules.SaveAsync(rule);

        var journal = new TriggerJournal(State);
        var spine = new TriggerSpine(rules, journal, new Builtins(), new Clock(Now));
        spine.Rebuild();

        await spine.DispatchAsync(Event("e1", "loadout-cli"), CancellationToken.None);
        (await spine.ApplyAsync(new TriggerAsk("req-1", TriggerVerbs.Remove, RuleId: rule.Id), CancellationToken.None))
            .Applied.Should().BeTrue();

        await spine.FlushAsync(Now.AddMinutes(10), CancellationToken.None);

        var kinds = journal.Read(Now.AddDays(-1)).Select(one => one.Kind).ToList();
        kinds.Should().Contain([TriggerEntryKinds.Removed, TriggerEntryKinds.Withdrawn]);
        kinds.Should().NotContain(TriggerEntryKinds.Started);
    }

    [Fact]
    public async Task With_no_daemon_a_change_is_written_to_the_file_and_the_journal_and_says_when_it_applies()
    {
        var (desk, console) = Desk();

        var code = await desk.ChangeAsync(
            new TriggerAsk("req-1", TriggerVerbs.Save, Rule()),
            Output(console),
            async () =>
            {
                var saved = await desk.Rules.SaveAsync(Rule());

                return desk.Journalled("req-1", TriggerEntryKinds.Edited, saved.Value!.Id, 1, "offline", "saved offline");
            },
            CancellationToken.None);

        code.Should().Be(0);
        console.Output.Should().Contain("saved offline");
        (await desk.Rules.ListAsync()).Value!.Should().ContainSingle();
        desk.Journal.Read(Now.AddDays(-1)).Single().Request.Should().Be("req-1");
    }

    [Fact]
    public async Task A_daemon_that_does_not_answer_is_asked_twice_and_the_file_is_never_written_round_it()
    {
        LiveDaemon();
        var (desk, console) = Desk(TimeSpan.FromMilliseconds(200));
        var offline = false;

        var code = await desk.ChangeAsync(
            new TriggerAsk("req-1", TriggerVerbs.Save, Rule()),
            Output(console),
            () =>
            {
                offline = true;

                return Task.FromResult(new TriggerAnswer("req-1", true, "should not happen"));
            },
            CancellationToken.None);

        code.Should().Be((int)Loadout.Models.ExitCode.GeneralFailure, "no answer is a failure, not a refusal");
        offline.Should().BeFalse("a running daemon is never written round");
        new TriggerInbox(State).Waiting().Should().ContainSingle("the same request was left, not two");
    }

    [Fact]
    public async Task A_daemon_that_refuses_is_believed_and_not_asked_again()
    {
        LiveDaemon();
        var (desk, console) = Desk(TimeSpan.FromSeconds(5));
        var inbox = new TriggerInbox(State);
        var answered = 0;

        using var stop = new CancellationTokenSource();
        var daemon = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                foreach (var asking in inbox.Waiting())
                {
                    answered++;
                    inbox.Answer(new TriggerAnswer(asking.Id, false, "A rule that starts a model needs a spend cap."));
                }

                await Task.Delay(20);
            }
        });

        var code = await desk.ChangeAsync(
            new TriggerAsk("req-1", TriggerVerbs.Save, Rule()),
            Output(console),
            () => Task.FromResult(new TriggerAnswer("req-1", true, "should not happen")),
            CancellationToken.None);

        await stop.CancelAsync();
        await daemon;

        code.Should().Be((int)Loadout.Models.ExitCode.InvalidArguments, "a refusal is an answer");
        answered.Should().Be(1);
    }

    private void LiveDaemon()
    {
        var note = DaemonNote.PathFor(_paths);
        Directory.CreateDirectory(Path.GetDirectoryName(note)!);
        File.WriteAllText(note, JsonSerializer.Serialize(new DaemonState(777, Now, null, Now, Triggers: true)));
        _processes.MarkLive(777, Now);
    }

    private (TriggerDesk Desk, TestConsole Console) Desk(TimeSpan? wait = null) =>
        (new TriggerDesk(_paths, _processes, _yaml, new NoOpFilePermissions(), new Clock(Now))
        {
            Wait = wait ?? TimeSpan.FromSeconds(1),
        }, new TestConsole());

    private static CommandOutput Output(IAnsiConsole console) => new(console, new GlobalSettings());

    private FakeEnvironmentProvider Environment(bool on) =>
        new(
            Path.Combine(_root, "home"),
            new Dictionary<string, string>
            {
                ["XDG_CONFIG_HOME"] = Path.Combine(_root, "config"),
                ["XDG_DATA_HOME"] = Path.Combine(_root, "data"),
                ["XDG_STATE_HOME"] = Path.Combine(_root, "state"),
                ["XDG_CACHE_HOME"] = Path.Combine(_root, "cache"),
            });

    private static TriggerRule Rule() => new()
    {
        Id = "audit-after-run",
        On = TriggerEventKinds.RunFinished,
        Action = new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit" },
    };

    private static TriggerEvent Event(string id, string project, string? subject = null, DateTimeOffset? at = null) =>
        new(id, at ?? Now, "daemon", TriggerEventKinds.RunFinished, subject ?? $"run:{id}", project,
            new Dictionary<string, string> { ["team"] = "alpha", ["project"] = project, ["outcome"] = "done" }, []);

    private sealed class Builtins : ITriggerActions
    {
        public Task<ActionTraits> DescribeAsync(TriggerAction action, string? project, CancellationToken ct) =>
            Task.FromResult(new ActionTraits(false));

        public Task<ActionOutcome> StartAsync(TriggerStart start, CancellationToken ct) =>
            Task.FromResult(new ActionOutcome(0m));

        public Task<Settling> SettleAsync(string reservation, CancellationToken ct) =>
            Task.FromResult(new Settling(SettleState.Unknown));
    }

    [Fact]
    public async Task Resuming_decides_the_latest_held_event_of_each_subject_and_supersedes_the_rest()
    {
        var (spine, journal) = await PausedAsync();

        // Two events about one subject and one about another, while paused.
        await spine.DispatchAsync(Event("e2", "loadout-cli", subject: "run:s"), CancellationToken.None);
        await spine.DispatchAsync(Event("e3", "loadout-cli", subject: "run:s"), CancellationToken.None);

        // Raised while paused, as somebody resuming a rule that hit its cap would.
        var raised = Rule();
        raised.CapRuns = 5;
        await spine.ApplyAsync(new TriggerAsk("req-0", TriggerVerbs.Save, raised), CancellationToken.None);

        var answer = await spine.ApplyAsync(new TriggerAsk("req-1", TriggerVerbs.Resume, RuleId: raised.Id), CancellationToken.None);

        // The event that tripped the cap was held too, so three came to two.
        answer.Message.Should().Contain("3 event(s) it held came to 2 decided now");

        var after = journal.Read(Now.AddDays(-1)).SkipWhile(one => one.Kind != TriggerEntryKinds.Resumed).ToList();
        after.Single(one => one.Kind == TriggerEntryKinds.Superseded).Event.Should().Be("e2");
        after.Where(one => one.Kind == TriggerEntryKinds.Started).Select(one => one.Event)
            .Should().BeEquivalentTo(["e1b", "e3"]);
    }

    [Fact]
    public async Task Resuming_keeps_what_was_held_past_the_catch_up_window_as_stale_and_withdraws_what_no_longer_matches()
    {
        var (_, journal, clock) = await StaleAsync();

        var entries = journal.Read(Now.AddDays(-1));
        entries.Where(one => one.Kind == TriggerEntryKinds.Stale).Select(one => one.Event)
            .Should().BeEquivalentTo(["e1b", "old"], "both were held 30 hours before the resume");
        entries.Single(one => one.Kind == TriggerEntryKinds.Withdrawn).Reason.Should().Contain("watches loadout-cli");
        entries.Count(one => one.Kind == TriggerEntryKinds.Started).Should().Be(1, "only the run before the pause");

        var listed = TriggerSpine.Stale(entries, clock.Now);
        listed.Select(one => one.Event.Subject).Should().BeEquivalentTo(["run:second", "run:old"]);
        listed.Should().OnlyContain(one => one.AgesOut == clock.Now + TriggerSpine.StaleKept);
    }

    [Fact]
    public async Task Releasing_a_stale_event_decides_it_under_the_caps_and_takes_it_off_the_list()
    {
        var (spine, journal, clock) = await StaleAsync();

        var answer = await spine.ApplyAsync(new TriggerAsk("req-2", TriggerVerbs.Release, RuleId: "audit-after-run"), CancellationToken.None);

        answer.Applied.Should().BeTrue(answer.Message);
        var entries = journal.Read(Now.AddDays(-1));
        entries.Should().Contain(one => one.Kind == TriggerEntryKinds.Released && one.Event == "old");
        entries.Count(one => one.Kind == TriggerEntryKinds.Started).Should().Be(2, "released, it starts like any other match");
        TriggerSpine.Stale(entries, clock.Now).Should().BeEmpty();

        (await spine.ApplyAsync(new TriggerAsk("req-3", TriggerVerbs.Release, RuleId: "audit-after-run"), CancellationToken.None))
            .Message.Should().Contain("nothing on the stale list");
    }

    [Fact]
    public async Task A_stale_event_nobody_releases_is_withdrawn_after_a_week_and_said_so()
    {
        var (spine, journal, clock) = await StaleAsync();

        (await spine.AgeOutAsync(CancellationToken.None)).Should().Be(0, "it has only just gone stale");

        clock.Now += TriggerSpine.StaleKept + TimeSpan.FromMinutes(1);

        (await spine.AgeOutAsync(CancellationToken.None)).Should().Be(2);
        var entries = journal.Read(Now.AddDays(-1));
        entries.Last().Reason.Should().Contain("never released");
        TriggerSpine.Stale(entries, clock.Now).Should().BeEmpty();
        TriggerSpine.Stale(entries, clock.Now, aged: true).Should().BeEmpty("withdrawn once, never again");
    }

    [Fact]
    public async Task A_newer_event_about_the_same_subject_takes_a_stale_one_off_the_list()
    {
        var (spine, journal, clock) = await StaleAsync();

        // Of a kind the rule does not watch, so the rule writes nothing about
        // it: only the event itself says the subject has moved on.
        clock.Now += TimeSpan.FromMinutes(5);
        await spine.DispatchAsync(
            Event("newer", "loadout-cli", subject: "run:old", at: clock.Now) with { Kind = TriggerEventKinds.DocsDrift },
            CancellationToken.None);

        TriggerSpine.Stale(journal.Read(Now.AddDays(-1)), clock.Now).Select(one => one.Event.Subject)
            .Should().Equal(["run:second"], "only the subject that moved on leaves the list");
    }

    /// <summary>
    /// A paused spine resumed 30 hours after it held "e1b" and "old", so both
    /// are stale, and after its rule was narrowed so "elsewhere" no longer matches.
    /// </summary>
    private async Task<(TriggerSpine Spine, TriggerJournal Journal, Clock Clock)> StaleAsync()
    {
        var clock = new Clock(Now);
        var (spine, journal) = await PausedAsync(clock);

        await spine.DispatchAsync(Event("old", "loadout-cli", subject: "run:old"), CancellationToken.None);

        clock.Now = Now.AddHours(30);
        await spine.DispatchAsync(Event("elsewhere", "starstats", subject: "run:new", at: clock.Now), CancellationToken.None);

        // While paused, somebody narrowed the rule to one project.
        var edited = Rule();
        edited.CapRuns = 1;
        edited.Projects = ["loadout-cli"];
        await spine.ApplyAsync(new TriggerAsk("req-0", TriggerVerbs.Save, edited), CancellationToken.None);

        await spine.ApplyAsync(new TriggerAsk("req-1", TriggerVerbs.Resume, RuleId: edited.Id), CancellationToken.None);

        return (spine, journal, clock);
    }

    /// <summary>A spine whose one rule has run once and paused at its cap of one run.</summary>
    private async Task<(TriggerSpine Spine, TriggerJournal Journal)> PausedAsync(Clock? clock = null)
    {
        var rules = new TriggerRules(State, _yaml);
        var rule = Rule();
        rule.CapRuns = 1;
        await rules.SaveAsync(rule);

        var journal = new TriggerJournal(State);
        var spine = new TriggerSpine(rules, journal, new Builtins(), clock ?? new Clock(Now));
        spine.Rebuild();

        await spine.DispatchAsync(Event("e1", "loadout-cli", subject: "run:first"), CancellationToken.None);
        await spine.DispatchAsync(Event("e1b", "loadout-cli", subject: "run:second"), CancellationToken.None);

        spine.Ledger.Paused(rule.Id).Should().BeTrue("the second start met the cap of one run");

        return (spine, journal);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
