using System.Threading.Channels;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>What an action is like, known before anything runs.</summary>
/// <param name="Writes">Whether it changes anything: a write waits for a person's approval.</param>
/// <param name="BudgetUsd">For a team: its own figure for each run, or null where it has none.</param>
/// <param name="Problem">Why it cannot be started at all, such as a team that does not exist.</param>
public sealed record ActionTraits(bool Writes, decimal? BudgetUsd = null, string? Problem = null);

/// <summary>One action the spine has decided to start.</summary>
/// <param name="Rule">The rule that matched.</param>
/// <param name="Event">The event it matched, the latest of its subject's.</param>
/// <param name="BudgetUsd">What it may spend, which is what was set aside for it.</param>
/// <param name="Reservation">The reservation its landing closes.</param>
public sealed record TriggerStart(TriggerRule Rule, TriggerEvent Event, decimal BudgetUsd, string Reservation);

/// <summary>How a started action went.</summary>
/// <param name="SpentUsd">What it spent, which is what lands against the rule.</param>
/// <param name="Run">The run it became, where it became one.</param>
/// <param name="Problem">What went wrong, where something did.</param>
public sealed record ActionOutcome(decimal SpentUsd, string? Run = null, string? Problem = null);

/// <summary>Where the action behind an open reservation stands.</summary>
/// <param name="State">Whether it is still going, has ended, or cannot be found.</param>
/// <param name="Outcome">How it went, once it has ended.</param>
public sealed record Settling(SettleState State, ActionOutcome? Outcome = null);

/// <summary>Where the action behind an open reservation stands.</summary>
public enum SettleState
{
    /// <summary>Still going: leave the money set aside.</summary>
    Going,

    /// <summary>Ended: land what it spent.</summary>
    Ended,

    /// <summary>Nothing carries the reservation, which an hour on means it never started.</summary>
    Unknown,
}

/// <summary>What starts the things rules ask for. The daemon supplies it; a test scripts it.</summary>
public interface ITriggerActions
{
    /// <summary>What an action is like, before the decision to start it.</summary>
    Task<ActionTraits> DescribeAsync(TriggerAction action, string? project, CancellationToken ct);

    /// <summary>Starts it and waits for it to end, so what it spent is known.</summary>
    Task<ActionOutcome> StartAsync(TriggerStart start, CancellationToken ct);

    /// <summary>
    /// How the action a reservation was made for stands, for a reservation no
    /// worker here is waiting on: one a stopped daemon left open.
    /// </summary>
    Task<Settling> SettleAsync(string reservation, CancellationToken ct);
}

/// <summary>
/// Carries events from sources to the rules, and what the rules start to a
/// small number of workers.
/// </summary>
/// <remarks>
/// <para>
/// Three stages joined by bounded channels. Sources post events and wait if
/// the spine is behind, rather than it dropping what they saw. One dispatcher
/// reads them, writes each to the journal, matches it against the rules and
/// decides; being one, every decision about money is made in order, so two
/// events arriving together cannot both spend the last of a cap. A few
/// workers start what was decided and wait for it to end, which bounds how
/// many triggered runs go at once.
/// </para>
/// <para>
/// Every decision is written to the journal before it is acted on, and the
/// ledger learns of it only from what was written. Its state in memory is a
/// reading of the journal, which is what lets a restart pick up where the
/// last daemon stopped.
/// </para>
/// </remarks>
public sealed class TriggerSpine
{
    /// <summary>How far back the ledger is rebuilt from: the longest window a rule can have.</summary>
    public static readonly TimeSpan Remembered = TimeSpan.FromDays(31);

    /// <summary>How often waiting subjects are looked at to see whether they have gone quiet.</summary>
    internal static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    private readonly ITriggerRules _rules;
    private readonly TriggerJournal _journal;
    private readonly ITriggerActions _actions;
    private readonly TimeProvider _time;
    private readonly int _workers;
    private readonly Channel<TriggerEvent> _inbound;
    private readonly Channel<TriggerStart> _work;
    private readonly Dictionary<(string Rule, string Subject), Waiting> _waiting = [];
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _working = new(StringComparer.Ordinal);
    private readonly HashSet<string> _applied = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _settled = DateTimeOffset.MinValue;

    /// <param name="rules">The rules to match against, read afresh for every event.</param>
    /// <param name="journal">Where every event and decision is written.</param>
    /// <param name="actions">What starts what the rules ask for.</param>
    /// <param name="time">The clock.</param>
    /// <param name="workers">How many actions may go at once.</param>
    /// <param name="capacity">How many events may wait for the dispatcher before sources are made to wait.</param>
    public TriggerSpine(
        ITriggerRules rules,
        TriggerJournal journal,
        ITriggerActions actions,
        TimeProvider time,
        int workers = 2,
        int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _rules = rules;
        _journal = journal;
        _actions = actions;
        _time = time;
        _workers = workers;
        _inbound = Channel.CreateBounded<TriggerEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
        _work = Channel.CreateBounded<TriggerStart>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
        });
        Ledger = new SpendLedger();
    }

    /// <summary>What each rule has started and spent, as the journal says.</summary>
    public SpendLedger Ledger { get; private set; }

    /// <summary>Hands the spine an event, waiting while it is behind.</summary>
    public ValueTask PostAsync(TriggerEvent happened, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(happened);

        return _inbound.Writer.WriteAsync(happened, ct);
    }

    /// <summary>
    /// Reads the journal back, then carries events and starts actions until
    /// told to stop.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        Rebuild();

        var workers = Enumerable.Range(0, _workers).Select(_ => WorkAsync(ct)).ToArray();

        try
        {
            await DispatchLoopAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Asked to stop, which is how this ends.
        }
        finally
        {
            _work.Writer.TryComplete();

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The workers were stopped mid-action; what they set aside
                // stays open in the journal for the next daemon to settle.
            }
        }
    }

    /// <summary>Reads the ledger back from the journal.</summary>
    internal void Rebuild()
    {
        var entries = _journal.Read(_time.GetUtcNow() - Remembered);

        Ledger = SpendLedger.Rebuild(entries);
        _applied.UnionWith(entries.Select(one => one.Request).OfType<string>());
    }

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        Task<bool>? arriving = null;

        while (!ct.IsCancellationRequested)
        {
            arriving ??= _inbound.Reader.WaitToReadAsync(ct).AsTask();

            await Task.WhenAny(arriving, Task.Delay(Tick, _time, ct)).ConfigureAwait(false);

            if (arriving.IsCompleted && !await arriving.ConfigureAwait(false))
            {
                return;
            }

            await _gate.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                if (arriving.IsCompleted)
                {
                    arriving = null;

                    while (_inbound.Reader.TryRead(out var happened))
                    {
                        await DispatchAsync(happened, ct).ConfigureAwait(false);
                    }
                }

                await FlushAsync(_time.GetUtcNow(), ct).ConfigureAwait(false);

                if (_time.GetUtcNow() - _settled >= SettleEvery)
                {
                    await SettleAsync(_time.GetUtcNow(), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        ct.ThrowIfCancellationRequested();
    }

    /// <summary>Writes one event down and decides what each rule does about it.</summary>
    /// <remarks>Internal so a test can drive the dispatcher one event at a time, without a clock.</remarks>
    internal async Task DispatchAsync(TriggerEvent happened, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        _journal.Append(TriggerEntry.Of(happened, now));

        var listed = await _rules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Refused, Event: happened.Id, Subject: happened.Subject,
                Reason: $"the rules could not be read: {listed.Error}"));

            return;
        }

        foreach (var decision in TriggerMatcher.Decide(happened, listed.Value!))
        {
            var rule = decision.Rule;

            if (!decision.Matched)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Skipped, happened.Id, rule.Id, rule.Version, happened.Subject,
                    decision.Reason));

                continue;
            }

            Write(new TriggerEntry(now, TriggerEntryKinds.Matched, happened.Id, rule.Id, rule.Version, happened.Subject));

            if (rule.Debounce <= TimeSpan.Zero)
            {
                await DecideAsync(rule, happened, now, ct).ConfigureAwait(false);

                continue;
            }

            var key = (rule.Id.ToLowerInvariant(), happened.Subject);

            if (_waiting.TryGetValue(key, out var was))
            {
                // A burst becomes one action on the latest of it.
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Superseded, was.Event.Id, rule.Id, rule.Version, happened.Subject,
                    $"replaced by {happened.Id}"));
            }

            _waiting[key] = new Waiting(rule, happened, now + rule.Debounce);
        }
    }

    /// <summary>Decides on every waiting subject that has gone quiet.</summary>
    internal async Task FlushAsync(DateTimeOffset now, CancellationToken ct)
    {
        foreach (var (key, waiting) in _waiting.Where(one => one.Value.Due <= now).ToList())
        {
            _waiting.Remove(key);

            await DecideAsync(waiting.Rule, waiting.Event, now, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Makes a change to the rules somebody asked the daemon for, and answers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here and not by writing the file, because the spine holds what a direct
    /// write would race: actions waiting for their subject to go quiet, each
    /// decided against the rule as it was. A change is made under the same
    /// gate the dispatcher holds, then every waiting action of that rule is
    /// checked against it again; one that no longer matches is withdrawn and
    /// written down rather than left to start under a rule that has changed.
    /// </para>
    /// <para>
    /// A request applied once is never applied again. The asker retries with
    /// the same id when an answer is slow, and an edit applied twice would
    /// count the rule's version on twice.
    /// </para>
    /// </remarks>
    public async Task<TriggerAnswer> ApplyAsync(TriggerAsk asking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asking);

        await _gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_applied.Contains(asking.Id))
            {
                return new TriggerAnswer(asking.Id, true, "Already applied by an earlier copy of this request.");
            }

            var answer = asking.Verb switch
            {
                TriggerVerbs.Save when asking.Rule is { } rule => await SaveAsync(asking.Id, rule, ct).ConfigureAwait(false),
                TriggerVerbs.Remove when asking.RuleId is { Length: > 0 } id => await RemoveAsync(asking.Id, id, ct).ConfigureAwait(false),
                TriggerVerbs.Resume when asking.RuleId is { Length: > 0 } id => await ResumeAsync(asking.Id, id, ct).ConfigureAwait(false),
                _ => new TriggerAnswer(asking.Id, false, $"'{asking.Verb}' is not a change the daemon can make to the rules."),
            };

            if (answer.Applied)
            {
                _applied.Add(asking.Id);
            }

            return answer;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TriggerAnswer> SaveAsync(string request, TriggerRule rule, CancellationToken ct)
    {
        var saved = await _rules.SaveAsync(rule, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return new TriggerAnswer(request, false, saved.Error!);
        }

        var now = _time.GetUtcNow();
        rule = saved.Value!;

        Write(new TriggerEntry(
            now, TriggerEntryKinds.Edited, Rule: rule.Id, Version: rule.Version, Reason: "saved", Request: request));

        foreach (var (key, waiting) in WaitingOn(rule.Id))
        {
            var decision = TriggerMatcher.Decide(waiting.Event, rule);

            if (decision.Matched)
            {
                // Still wanted: it goes ahead under the rule as it is now,
                // caps and budget included, when its subject goes quiet.
                _waiting[key] = waiting with { Rule = rule };

                continue;
            }

            _waiting.Remove(key);
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, waiting.Event.Id, rule.Id, rule.Version, waiting.Event.Subject,
                $"the rule changed and {decision.Reason}", Request: request));
        }

        return new TriggerAnswer(request, true, $"Applied by the daemon: '{rule.Id}' is now version {rule.Version}.", rule.Version);
    }

    private async Task<TriggerAnswer> RemoveAsync(string request, string id, CancellationToken ct)
    {
        var removed = await _rules.RemoveAsync(id, ct).ConfigureAwait(false);

        if (removed.Failed)
        {
            return new TriggerAnswer(request, false, removed.Error!);
        }

        var now = _time.GetUtcNow();

        Write(new TriggerEntry(now, TriggerEntryKinds.Removed, Rule: id, Reason: "removed", Request: request));

        foreach (var (key, waiting) in WaitingOn(id))
        {
            _waiting.Remove(key);
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, waiting.Event.Id, id, waiting.Rule.Version, waiting.Event.Subject,
                "the rule was removed", Request: request));
        }

        return new TriggerAnswer(request, true, $"Applied by the daemon: '{id}' is gone, and nothing waiting on it will start.");
    }

    private async Task<TriggerAnswer> ResumeAsync(string request, string id, CancellationToken ct)
    {
        var listed = await _rules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return new TriggerAnswer(request, false, listed.Error!);
        }

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, id, StringComparison.OrdinalIgnoreCase))
            is not { } found)
        {
            return new TriggerAnswer(request, false, $"There is no trigger rule called '{id}' on this machine.");
        }

        if (!Ledger.Paused(found.Rule.Id))
        {
            return new TriggerAnswer(request, true, $"'{found.Rule.Id}' was not paused, so nothing changed.");
        }

        var rule = found.Rule;
        var now = _time.GetUtcNow();
        var held = Held(rule.Id, now);

        Write(new TriggerEntry(
            now, TriggerEntryKinds.Resumed, Rule: rule.Id, Version: rule.Version,
            Reason: "resumed; its caps count from now", Request: request));

        var decided = 0;

        // The latest of each subject, the way a daemon that was off catches
        // up: whoever acts on a pull request wants it as it is now, not every
        // push it went through while the rule was paused.
        foreach (var subject in held.GroupBy(one => one.Subject, StringComparer.Ordinal))
        {
            // A stable sort, so of two stamped the same moment the later arrival wins.
            var latest = subject.OrderBy(one => one.At).Last();

            foreach (var older in subject.Where(one => !ReferenceEquals(one, latest)))
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Superseded, older.Id, rule.Id, rule.Version, older.Subject,
                    $"held while paused; {latest.Id} is the latest about its subject", Request: request));
            }

            if (now - latest.At > CatchUp)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Withdrawn, latest.Id, rule.Id, rule.Version, latest.Subject,
                    $"held while paused for longer than the {CatchUp.TotalHours:0}-hour catch-up window", Request: request));

                continue;
            }

            // Checked against the rule as it is now: it may have been edited
            // while it was paused.
            if (TriggerMatcher.Decide(latest, rule) is { Matched: false } passed)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Withdrawn, latest.Id, rule.Id, rule.Version, latest.Subject,
                    passed.Reason, Request: request));

                continue;
            }

            await DecideAsync(rule, latest, now, ct).ConfigureAwait(false);
            decided++;
        }

        return new TriggerAnswer(
            request,
            true,
            $"Applied by the daemon: '{rule.Id}' acts again, its caps counting from now"
            + (held.Count == 0 ? "." : $"; {held.Count} event(s) it held came to {decided} decided now."));
    }

    /// <summary>
    /// How far back held events are caught up on resume: the same 24 hours the
    /// rest of the design counts in.
    /// </summary>
    internal static readonly TimeSpan CatchUp = TimeSpan.FromHours(24);

    /// <summary>The events a rule held since it last paused, oldest first, as they arrived.</summary>
    private List<TriggerEvent> Held(string rule, DateTimeOffset now) => Held(_journal.Read(now - Remembered), rule);

    /// <summary>The events a rule held since it last paused, read from journal entries.</summary>
    /// <remarks>Public so a command can say what a resume with no daemon running would leave undecided.</remarks>
    public static List<TriggerEvent> Held(IReadOnlyList<TriggerEntry> journal, string rule)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var entries = journal.ToList();

        var paused = entries.FindLastIndex(one =>
            one.Kind == TriggerEntryKinds.Paused && string.Equals(one.Rule, rule, StringComparison.OrdinalIgnoreCase));

        if (paused < 0)
        {
            return [];
        }

        var events = entries
            .Where(one => one.Kind == TriggerEntryKinds.Event && one.Event is not null)
            .GroupBy(one => one.Event!, StringComparer.Ordinal)
            .ToDictionary(one => one.Key, one => one.First(), StringComparer.Ordinal);

        return
        [
            .. entries
                .Skip(paused)
                .Where(one => one.Kind == TriggerEntryKinds.Held
                    && string.Equals(one.Rule, rule, StringComparison.OrdinalIgnoreCase)
                    && one.Event is not null)
                .Select(one => one.Event!)
                .Distinct(StringComparer.Ordinal)
                .Where(events.ContainsKey)
                .Select(id => events[id].ToEvent()),
        ];
    }

    /// <summary>The actions waiting on one rule for their subject to go quiet.</summary>
    private List<KeyValuePair<(string Rule, string Subject), Waiting>> WaitingOn(string rule) =>
        [.. _waiting.Where(one => string.Equals(one.Key.Rule, rule, StringComparison.OrdinalIgnoreCase))];

    private async Task DecideAsync(TriggerRule rule, TriggerEvent happened, DateTimeOffset now, CancellationToken ct)
    {
        TriggerEntry Entry(string kind, string? reason = null, decimal? usd = null, string? reservation = null) =>
            new(now, kind, happened.Id, rule.Id, rule.Version, happened.Subject, reason, usd, reservation);

        var traits = await _actions.DescribeAsync(rule.Action, happened.Project, ct).ConfigureAwait(false);

        if (traits.Problem is { } problem)
        {
            Write(Entry(TriggerEntryKinds.Refused, problem));

            return;
        }

        if (traits.Writes)
        {
            // Approvals are what lets a write run unattended, and they are not
            // built yet. Refused and written down, never started quietly.
            Write(Entry(
                TriggerEntryKinds.Refused,
                $"{rule.Action.Name} writes, and a write needs your approval, which triggers cannot ask for yet"));

            return;
        }

        var costs = TriggerRules.CostsMoney(rule.Action);
        var budget = costs ? rule.BudgetUsd ?? traits.BudgetUsd : 0m;

        if (budget is not { } each)
        {
            Write(Entry(
                TriggerEntryKinds.Refused,
                $"{rule.Action.Name} has no budget for a run of its own, so the rule has to set one"));

            return;
        }

        var (verdict, why) = Ledger.Ask(rule, each, now);

        switch (verdict)
        {
            case LedgerVerdict.Hold:
                Write(Entry(TriggerEntryKinds.Held, why));

                return;

            case LedgerVerdict.Pause:
                Write(Entry(TriggerEntryKinds.Paused, why));
                Write(Entry(TriggerEntryKinds.Held, "the rule is paused until it is resumed"));

                return;
        }

        var reservation = Guid.NewGuid().ToString("N");

        if (each > 0)
        {
            Write(Entry(TriggerEntryKinds.Reserved, usd: each, reservation: reservation));
        }

        // Counted as a start now, not when a worker reaches it, so a second
        // event behind this one meets the run cap as it really stands.
        Write(Entry(TriggerEntryKinds.Started, reservation: reservation));

        await _work.Writer.WriteAsync(new TriggerStart(rule, happened, each, reservation), ct).ConfigureAwait(false);
    }

    private async Task WorkAsync(CancellationToken ct)
    {
        await foreach (var start in _work.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            await RunOneAsync(start, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Takes the next decided start off the queue, for a test driving no workers.</summary>
    internal bool TryTake(out TriggerStart start) => _work.Reader.TryRead(out start!);

    /// <summary>Starts one action, waits for it, and lands what it spent.</summary>
    internal async Task RunOneAsync(TriggerStart start, CancellationToken ct)
    {
        ActionOutcome outcome;

        try
        {
            _working[start.Reservation] = 0;
            outcome = await _actions.StartAsync(start, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            outcome = new ActionOutcome(0m, Problem: ex.Message);
        }
        finally
        {
            _working.TryRemove(start.Reservation, out _);
        }

        Write(new TriggerEntry(
            _time.GetUtcNow(), TriggerEntryKinds.Landed, start.Event.Id, start.Rule.Id, start.Rule.Version,
            start.Event.Subject, outcome.Problem, outcome.SpentUsd, start.Reservation, Run: outcome.Run));
    }

    /// <summary>How often reservations nobody here is waiting on are looked at.</summary>
    internal static readonly TimeSpan SettleEvery = TimeSpan.FromMinutes(1);

    /// <summary>How long a reservation with no run carrying it is given before it is taken as never started.</summary>
    /// <remarks>A run names itself within seconds of starting; an hour is generous and still releases the money the same day.</remarks>
    internal static readonly TimeSpan Orphaned = TimeSpan.FromHours(1);

    /// <summary>
    /// Lands the reservations a stopped daemon left open, once the runs they
    /// were made for have ended.
    /// </summary>
    /// <remarks>
    /// The ledger is rebuilt with them still open, which is right while their
    /// runs go on - a run outlives the daemon that started it - and wrong for
    /// ever after unless something closes them. Each is asked about until it
    /// has ended or plainly never started.
    /// </remarks>
    internal async Task SettleAsync(DateTimeOffset now, CancellationToken ct)
    {
        _settled = now;

        foreach (var (reservation, rule, at) in Ledger.Open())
        {
            if (_working.ContainsKey(reservation))
            {
                continue;
            }

            var settling = await _actions.SettleAsync(reservation, ct).ConfigureAwait(false);

            if (settling.State == SettleState.Ended && settling.Outcome is { } outcome)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Landed, Rule: rule, Usd: outcome.SpentUsd, Reservation: reservation,
                    Run: outcome.Run, Reason: "settled after the daemon that started it stopped"
                        + (outcome.Problem is { } problem ? $": {problem}" : string.Empty)));
            }
            else if (settling.State == SettleState.Unknown && now - at >= Orphaned)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Landed, Rule: rule, Usd: 0m, Reservation: reservation,
                    Reason: "no run carries it, so the daemon stopped before it began"));
            }
        }
    }

    /// <summary>Writes an entry, then lets the ledger know: never the other way round.</summary>
    private void Write(TriggerEntry entry)
    {
        _journal.Append(entry);
        Ledger.Apply(entry);
    }

    private sealed record Waiting(TriggerRule Rule, TriggerEvent Event, DateTimeOffset Due);
}
