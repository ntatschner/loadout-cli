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
    internal void Rebuild() => Ledger = SpendLedger.Rebuild(_journal.Read(_time.GetUtcNow() - Remembered));

    private async Task DispatchLoopAsync(CancellationToken ct)
    {
        Task<bool>? arriving = null;

        while (!ct.IsCancellationRequested)
        {
            arriving ??= _inbound.Reader.WaitToReadAsync(ct).AsTask();

            await Task.WhenAny(arriving, Task.Delay(Tick, _time, ct)).ConfigureAwait(false);

            if (arriving.IsCompleted)
            {
                if (!await arriving.ConfigureAwait(false))
                {
                    return;
                }

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

    /// <summary>Lets a paused rule act again, its caps counting from now.</summary>
    public void Resume(TriggerRule rule, string reason)
    {
        ArgumentNullException.ThrowIfNull(rule);

        Write(new TriggerEntry(
            _time.GetUtcNow(), TriggerEntryKinds.Resumed, Rule: rule.Id, Version: rule.Version, Reason: reason));
    }

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
