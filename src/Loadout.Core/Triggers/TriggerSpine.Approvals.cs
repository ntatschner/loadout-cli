using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>An action that writes, waiting for a person to approve or reject it.</summary>
/// <param name="Id">Its short name, which the approve and reject commands take.</param>
/// <param name="Rule">The rule that asked.</param>
/// <param name="Version">That rule's version when it asked.</param>
/// <param name="Event">The event it would act on.</param>
/// <param name="Asked">When it was asked.</param>
/// <param name="BudgetUsd">What the run may spend, which is set aside when it is approved.</param>
/// <param name="Approved">Approved, and held until there is headroom for it.</param>
public sealed record PendingApproval(
    string Id,
    string Rule,
    int? Version,
    TriggerEvent Event,
    DateTimeOffset Asked,
    decimal? BudgetUsd,
    bool Approved)
{
    /// <summary>When it is withdrawn if nobody answers, or if it is approved and never gets to start.</summary>
    public DateTimeOffset Expires => Asked + TriggerSpine.CatchUp;
}

/// <summary>
/// The parts of the spine that wait for something other than an event:
/// actions that write, waiting for a person's approval, and starts held until
/// the money set aside for runs in flight comes back.
/// </summary>
/// <remarks>
/// <para>
/// Both are written to the journal before anything else knows of them, like
/// every other decision, and both are read back from it when the daemon
/// starts, so a restart neither forgets an approval nor starts a held run twice.
/// </para>
/// <para>
/// One rule governs what happens to them while they wait, the same rule
/// waiting events follow: each is checked against its subject as it is now.
/// A newer event for the same rule and subject replaces it and asks again. A
/// newer event of another kind that moves the subject on, closes it or stops
/// the rule matching withdraws it. So does the rule being removed, its author
/// leaving the allow-list, or 24 hours passing. An approval means the state
/// the person saw: one approved and still held is asked again rather than
/// started on something they never looked at.
/// </para>
/// </remarks>
public sealed partial class TriggerSpine
{
    /// <summary>Starts held for headroom, by rule and subject, waiting for runs in flight to give money back.</summary>
    private readonly Dictionary<(string Rule, string Subject), Holding> _holding = [];

    /// <summary>When held starts were last looked at.</summary>
    private DateTimeOffset _released = DateTimeOffset.MinValue;

    /// <summary>How often held starts are looked at, to see whether money has come back.</summary>
    internal static readonly TimeSpan ReleaseEvery = TimeSpan.FromSeconds(5);

    /// <summary>The approvals waiting for an answer, and approved ones not yet started, as the journal has them.</summary>
    /// <remarks>Read from the journal, so the list is the same with or without a daemon running.</remarks>
    public static IReadOnlyList<PendingApproval> Approvals(IReadOnlyList<TriggerEntry> journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        var events = journal
            .Where(one => one.Kind == TriggerEntryKinds.Event && one.Event is not null)
            .GroupBy(one => one.Event!, StringComparer.Ordinal)
            .ToDictionary(one => one.Key, one => one.First().ToEvent(), StringComparer.Ordinal);

        var later = journal
            .Where(one => one.Approval is not null && one.Kind != TriggerEntryKinds.Asked)
            .GroupBy(one => one.Approval!, StringComparer.Ordinal)
            .ToDictionary(one => one.Key, one => one.Select(each => each.Kind).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);

        var pending = new List<PendingApproval>();

        foreach (var asked in journal.Where(one => one.Kind == TriggerEntryKinds.Asked && one.Approval is not null))
        {
            if (asked.Event is not { } id || !events.TryGetValue(id, out var happened) || asked.Rule is not { } rule)
            {
                continue;
            }

            var after = later.GetValueOrDefault(asked.Approval!) ?? [];

            if (after.Overlaps(Ended))
            {
                continue;
            }

            pending.Add(new PendingApproval(
                asked.Approval!, rule, asked.Version, happened, asked.At, asked.Usd, after.Contains(TriggerEntryKinds.Approved)));
        }

        return pending;
    }

    /// <summary>What ends an approval: answered no, overtaken, or started.</summary>
    private static readonly IReadOnlySet<string> Ended = new HashSet<string>(StringComparer.Ordinal)
    {
        TriggerEntryKinds.Rejected,
        TriggerEntryKinds.Withdrawn,
        TriggerEntryKinds.Superseded,
        TriggerEntryKinds.Started,
        TriggerEntryKinds.Refused,
    };

    /// <summary>A short name for an approval, easy to type.</summary>
    private static string ApprovalId() => Guid.NewGuid().ToString("N")[..8];

    private async Task<TriggerAnswer> ApproveAsync(string request, string id, CancellationToken ct)
    {
        var now = _time.GetUtcNow();

        if (Approvals(_journal.Read(now - Remembered)).FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase))
            is not { } pending)
        {
            return new TriggerAnswer(request, false, $"Nothing is waiting for approval as '{id}'. 'loadout trigger approvals' lists what is.");
        }

        if (pending.Approved)
        {
            return new TriggerAnswer(request, true, $"'{pending.Id}' was already approved; it starts when there is headroom for it.");
        }

        var listed = await _rules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return new TriggerAnswer(request, false, listed.Error!);
        }

        var happened = pending.Event;

        TriggerEntry Entry(string kind, string reason, int? version) =>
            new(now, kind, happened.Id, pending.Rule, version, happened.Subject, reason, Request: request, Approval: pending.Id);

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, pending.Rule, StringComparison.OrdinalIgnoreCase))
            is not { Usable: true } found)
        {
            Write(Entry(TriggerEntryKinds.Withdrawn, "the rule is gone or no longer usable", pending.Version));

            return new TriggerAnswer(request, false, $"'{pending.Rule}' is gone or no longer usable, so '{pending.Id}' was withdrawn.");
        }

        var rule = found.Rule;

        // An approval is for what was shown. A rule changed since then asks
        // again rather than starting something nobody looked at; the edit
        // itself re-asks, so this is only the race between the two.
        if (rule.Version != pending.Version)
        {
            return new TriggerAnswer(
                request, false, $"'{rule.Id}' has changed since '{pending.Id}' was asked; look again: loadout trigger approvals");
        }

        if (TriggerMatcher.Decide(happened, rule) is { Matched: false } passed)
        {
            Write(Entry(TriggerEntryKinds.Withdrawn, passed.Reason!, rule.Version));

            return new TriggerAnswer(request, false, $"'{pending.Id}' no longer applies ({passed.Reason}), so it was withdrawn.");
        }

        Write(Entry(TriggerEntryKinds.Approved, "approved", rule.Version));

        // Money is set aside on approval, not on asking, so a quiet inbox
        // holds nothing back from the rules that only read.
        await DecideAsync(rule, happened, now, ct, pending.Id).ConfigureAwait(false);

        var started = _journal.Read(now.AddMinutes(-1))
            .Any(one => one.Approval == pending.Id && one.Kind == TriggerEntryKinds.Started);

        return new TriggerAnswer(
            request,
            true,
            started
                ? $"Applied by the daemon: '{pending.Id}' is approved and has started."
                : $"Applied by the daemon: '{pending.Id}' is approved, and held until its rule has headroom for it. "
                  + "What it is waiting for is in: loadout trigger journal");
    }

    private TriggerAnswer Reject(string request, string id)
    {
        var now = _time.GetUtcNow();

        if (Approvals(_journal.Read(now - Remembered)).FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase))
            is not { } pending)
        {
            return new TriggerAnswer(request, false, $"Nothing is waiting for approval as '{id}'. 'loadout trigger approvals' lists what is.");
        }

        _holding.Remove((pending.Rule.ToLowerInvariant(), pending.Event.Subject));
        Write(new TriggerEntry(
            now, TriggerEntryKinds.Rejected, pending.Event.Id, pending.Rule, pending.Version, pending.Event.Subject,
            "rejected", Request: request, Approval: pending.Id));

        return new TriggerAnswer(request, true, $"Applied by the daemon: '{pending.Id}' is rejected and will not start.");
    }

    /// <summary>
    /// Whether a triggered run may now spend <paramref name="usd"/> in all,
    /// and the entry that sets the new figure aside when it may.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run that spends its budget asks for more, the way an attended run
    /// does. The answer can never take the rule past its spend cap: what the
    /// run may have is what it already has set aside plus the rule's headroom,
    /// which counts every other run in flight. The trigger cap is a ceiling,
    /// not a starting figure for a conversation.
    /// </para>
    /// <para>
    /// The new figure replaces the reservation, so the ledger holds it from
    /// now and another start meets the cap as it really stands. Lowering is
    /// always allowed, and gives the difference back.
    /// </para>
    /// </remarks>
    public static (string? Problem, TriggerEntry? Entry) Raising(
        SpendLedger ledger, TriggerRule rule, string reservation, decimal usd, DateTimeOffset now, string? request = null)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(rule);

        if (ledger.SetAside(reservation) is not { } aside)
        {
            return ("the run's money is no longer set aside, so it has ended or was never started by a rule", null);
        }

        var standing = ledger.Standing(rule, now);

        // No headroom figure means no cap to keep under; adding the reservation
        // to an unbounded figure is not a number at all.
        if (standing.Headroom is { } headroom && usd > aside.Usd + headroom)
        {
            var most = aside.Usd + headroom;

            return (
                $"{rule.Id} can let it have ${most:0.00} at most: its cap is ${rule.CapUsd:0.00}, with "
                + $"${standing.Landed:0.00} spent and ${standing.Reserved:0.00} set aside in its window",
                null);
        }

        return (null, new TriggerEntry(
            now, TriggerEntryKinds.Reserved, Rule: rule.Id, Version: rule.Version, Usd: usd, Reservation: reservation,
            Reason: $"a person changed what the run may spend from ${aside.Usd:0.00} to ${usd:0.00}", Request: request));
    }

    private async Task<TriggerAnswer> RaiseAsync(string request, string reservation, decimal usd, CancellationToken ct)
    {
        if (Ledger.SetAside(reservation) is not { } aside)
        {
            return new TriggerAnswer(request, false, "That run's money is no longer set aside, so there is nothing to change.");
        }

        var listed = await _rules.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return new TriggerAnswer(request, false, listed.Error!);
        }

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, aside.Rule, StringComparison.OrdinalIgnoreCase))
            is not { } found)
        {
            return new TriggerAnswer(request, false, $"'{aside.Rule}', which started the run, is gone, so its cap cannot be checked.");
        }

        var (problem, entry) = Raising(Ledger, found.Rule, reservation, usd, _time.GetUtcNow(), request);

        if (problem is not null)
        {
            return new TriggerAnswer(request, false, problem);
        }

        Write(entry!);

        return new TriggerAnswer(request, true, $"Applied by the daemon: ${usd:0.00} is set aside for the run under '{aside.Rule}'.");
    }

    /// <summary>The words for an approval that is withdrawn: its approver is told, because they expect it to run.</summary>
    private static string Withdrawing(PendingApproval one, string why) =>
        one.Approved ? $"{why}; it had been approved, so you are told" : why;

    /// <summary>
    /// What a new event about a subject does to the approvals and held starts
    /// on it, for the rules it did not match; the same check as for waiting
    /// actions.
    /// </summary>
    private void AbsorbPending(TriggerEvent happened, IReadOnlySet<string> matched, DateTimeOffset now)
    {
        foreach (var one in Approvals(_journal.Read(now - Remembered))
            .Where(each => string.Equals(each.Event.Subject, happened.Subject, StringComparison.Ordinal)
                && !matched.Contains(each.Rule.ToLowerInvariant())))
        {
            if (Overtaking(one.Event, happened, one.Rule) is not { } why)
            {
                continue;
            }

            _holding.Remove((one.Rule.ToLowerInvariant(), one.Event.Subject));
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, one.Event.Id, one.Rule, one.Version, one.Event.Subject,
                Withdrawing(one, why), Approval: one.Id));
        }

        foreach (var (key, held) in _holding
            .Where(each => string.Equals(each.Key.Subject, happened.Subject, StringComparison.Ordinal) && !matched.Contains(each.Key.Rule))
            .ToList())
        {
            if (held.Approval is not null || Overtaking(held.Event, happened, held.Rule) is not { } why)
            {
                continue;
            }

            _holding.Remove(key);
            Write(new TriggerEntry(now, TriggerEntryKinds.Withdrawn, held.Event.Id, held.Rule, Subject: held.Event.Subject, Reason: why));
        }
    }

    /// <summary>Why a pending event is overtaken by a newer one about its subject, or null; its rule is looked up from the cached list.</summary>
    private string? Overtaking(TriggerEvent waiting, TriggerEvent newer, string rule)
    {
        if (TriggerSubjects.Overtaken(waiting, newer) is { } why)
        {
            return why;
        }

        return _lastRules.FirstOrDefault(one => string.Equals(one.Rule.Id, rule, StringComparison.OrdinalIgnoreCase)) is { } found
            && TriggerMatcher.Decide(TriggerSubjects.Refreshed(waiting, newer), found.Rule) is { Matched: false } passed
                ? $"{newer.Id} arrived and {passed.Reason}"
                : null;
    }

    /// <summary>
    /// A newer event for the same rule and subject replaces what was waiting
    /// on the older one: the approval is asked again for the state as it is
    /// now, and a held start gives way.
    /// </summary>
    private void Replace(TriggerRule rule, TriggerEvent happened, DateTimeOffset now)
    {
        foreach (var one in Approvals(_journal.Read(now - Remembered))
            .Where(each => string.Equals(each.Rule, rule.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(each.Event.Subject, happened.Subject, StringComparison.Ordinal)))
        {
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Superseded, one.Event.Id, rule.Id, rule.Version, happened.Subject,
                Withdrawing(one, $"replaced by {happened.Id}, which is asked about afresh"), Approval: one.Id));
        }

        // An approved one held for headroom was written down above, as an approval.
        if (_holding.Remove((rule.Id.ToLowerInvariant(), happened.Subject), out var held) && held.Approval is null)
        {
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Superseded, held.Event.Id, rule.Id, rule.Version, happened.Subject,
                $"replaced by {happened.Id}", Approval: held.Approval));
        }
    }

    /// <summary>
    /// What a saved edit does to a rule's approvals: withdrawn where the rule
    /// no longer matches, asked again where it now starts or spends something
    /// else.
    /// </summary>
    private async Task ReaskAsync(TriggerRule? was, TriggerRule rule, string request, DateTimeOffset now, CancellationToken ct)
    {
        var changed = was is null
            || !string.Equals(was.Action.Kind, rule.Action.Kind, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(was.Action.Name, rule.Action.Name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(was.Action.Goal, rule.Action.Goal, StringComparison.Ordinal)
            || !string.Equals(was.Action.Autonomy, rule.Action.Autonomy, StringComparison.OrdinalIgnoreCase)
            || was.BudgetUsd != rule.BudgetUsd;

        foreach (var one in Approvals(_journal.Read(now - Remembered))
            .Where(each => string.Equals(each.Rule, rule.Id, StringComparison.OrdinalIgnoreCase)))
        {
            _holding.Remove((rule.Id.ToLowerInvariant(), one.Event.Subject));

            if (TriggerMatcher.Decide(one.Event, rule) is { Matched: false } passed)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Withdrawn, one.Event.Id, rule.Id, rule.Version, one.Event.Subject,
                    Withdrawing(one, $"the rule changed and {passed.Reason}"), Request: request, Approval: one.Id));

                continue;
            }

            if (!changed)
            {
                // Nothing that was approved has changed; it keeps its place,
                // checked against the rule as it is now when it is released.
                if (one.Approved)
                {
                    _holding[(rule.Id.ToLowerInvariant(), one.Event.Subject)] = new Holding(rule.Id, one.Event, one.Asked, one.Id);
                }

                continue;
            }

            Write(new TriggerEntry(
                now, TriggerEntryKinds.Superseded, one.Event.Id, rule.Id, rule.Version, one.Event.Subject,
                Withdrawing(one, "the rule changed what it starts or spends, so it is asked again"), Request: request, Approval: one.Id));

            await DecideAsync(rule, one.Event, now, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Withdraws a removed rule's approvals and held starts.</summary>
    private void WithdrawPending(string rule, string why, string request, DateTimeOffset now)
    {
        foreach (var one in Approvals(_journal.Read(now - Remembered))
            .Where(each => string.Equals(each.Rule, rule, StringComparison.OrdinalIgnoreCase)))
        {
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, one.Event.Id, rule, one.Version, one.Event.Subject,
                Withdrawing(one, why), Request: request, Approval: one.Id));
        }

        foreach (var (key, held) in _holding.Where(each => string.Equals(each.Key.Rule, rule, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _holding.Remove(key);

            if (held.Approval is null)
            {
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Withdrawn, held.Event.Id, rule, Subject: held.Event.Subject, Reason: why, Request: request));
            }
        }
    }

    /// <summary>The approvals and held starts an author's leaving the allow-list withdraws.</summary>
    private int DistrustPending(long author, string login, string request, DateTimeOffset now)
    {
        var withdrawn = 0;

        foreach (var one in Approvals(_journal.Read(now - Remembered)).Where(each => TrustedBy(each.Event, author)))
        {
            _holding.Remove((one.Rule.ToLowerInvariant(), one.Event.Subject));
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, one.Event.Id, one.Rule, one.Version, one.Event.Subject,
                Withdrawing(one, Untrusted(login)), Request: request, Approval: one.Id));
            withdrawn++;
        }

        foreach (var (key, held) in _holding.Where(each => each.Value.Approval is null && TrustedBy(each.Value.Event, author)).ToList())
        {
            _holding.Remove(key);
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, held.Event.Id, held.Rule, Subject: held.Event.Subject, Reason: Untrusted(login),
                Request: request));
            withdrawn++;
        }

        return withdrawn;
    }

    /// <summary>
    /// Holds a start until the money set aside for runs in flight comes back,
    /// replacing one already held for the same rule and subject.
    /// </summary>
    private void Hold(TriggerRule rule, TriggerEvent happened, DateTimeOffset now, string? approval)
    {
        var key = (rule.Id.ToLowerInvariant(), happened.Subject);

        if (_holding.TryGetValue(key, out var was) && !string.Equals(was.Event.Id, happened.Id, StringComparison.Ordinal))
        {
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Superseded, was.Event.Id, rule.Id, rule.Version, happened.Subject,
                $"replaced by {happened.Id}", Approval: was.Approval));
        }

        // A start held again after a release keeps the place it first took.
        _holding[key] = new Holding(rule.Id, happened, was?.Since ?? now, approval);
    }

    /// <summary>
    /// Starts what was held, oldest first within each rule, as far as the
    /// money that has come back allows; and lets go of what has waited too long.
    /// </summary>
    /// <remarks>
    /// Oldest first and no jumping the queue: a smaller run behind a larger one
    /// waits for it, so the first to be held is the first to start. A start
    /// held for longer than the catch-up window goes on the stale list, for a
    /// person to release if it still matters; an approved one is withdrawn,
    /// because what was approved was the state of a day ago.
    /// </remarks>
    internal async Task ReleaseHeldAsync(DateTimeOffset now, CancellationToken ct)
    {
        _released = now;

        if (_holding.Count > 0)
        {
            var listed = await _rules.ListAsync(ct).ConfigureAwait(false);

            if (listed.Succeeded)
            {
                _lastRules = listed.Value!;

                foreach (var rule in _holding.Values.Select(one => one.Rule).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                {
                    await ReleaseRuleAsync(rule, now, ct).ConfigureAwait(false);
                }
            }
        }

        foreach (var one in Approvals(_journal.Read(now - Remembered)).Where(each => each.Expires <= now))
        {
            _holding.Remove((one.Rule.ToLowerInvariant(), one.Event.Subject));
            Write(new TriggerEntry(
                now, TriggerEntryKinds.Withdrawn, one.Event.Id, one.Rule, one.Version, one.Event.Subject,
                one.Approved
                    ? Withdrawing(one, $"approved but not started within {CatchUp.TotalHours:0} hours")
                    : $"not answered within {CatchUp.TotalHours:0} hours",
                Approval: one.Id));
        }
    }

    private async Task ReleaseRuleAsync(string id, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var (key, held) in _holding
            .Where(each => string.Equals(each.Key.Rule, id, StringComparison.OrdinalIgnoreCase))
            .OrderBy(each => each.Value.Since)
            .ToList())
        {
            if (_lastRules.FirstOrDefault(one => string.Equals(one.Rule.Id, id, StringComparison.OrdinalIgnoreCase))
                is not { Usable: true } found)
            {
                _holding.Remove(key);

                if (held.Approval is null)
                {
                    Write(new TriggerEntry(
                        now, TriggerEntryKinds.Withdrawn, held.Event.Id, id, Subject: held.Event.Subject,
                        Reason: "the rule is gone or no longer usable"));
                }

                continue;
            }

            var rule = found.Rule;

            if (held.Approval is null && now - held.Since > CatchUp)
            {
                _holding.Remove(key);
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Stale, held.Event.Id, rule.Id, rule.Version, held.Event.Subject,
                    $"held for headroom for longer than the {CatchUp.TotalHours:0}-hour window; "
                    + $"release it with: loadout trigger release {rule.Id}"));

                continue;
            }

            if (TriggerMatcher.Decide(held.Event, rule) is { Matched: false } passed)
            {
                _holding.Remove(key);
                Write(new TriggerEntry(
                    now, TriggerEntryKinds.Withdrawn, held.Event.Id, rule.Id, rule.Version, held.Event.Subject,
                    passed.Reason, Approval: held.Approval));

                continue;
            }

            var budget = rule.BudgetUsd
                ?? (await _actions.DescribeAsync(rule.Action, held.Event.Project, ct).ConfigureAwait(false)).BudgetUsd
                ?? 0m;

            if (!TriggerRules.CostsMoney(rule.Action))
            {
                budget = 0m;
            }

            var (verdict, _) = Ledger.Ask(rule, budget, now);

            // Still short, or paused: everything behind it waits too.
            if (verdict == LedgerVerdict.Hold)
            {
                break;
            }

            _holding.Remove(key);
            await DecideAsync(rule, held.Event, now, ct, held.Approval).ConfigureAwait(false);
        }
    }

    /// <summary>The held starts the journal has, for a daemon starting: held for headroom and nothing decided since.</summary>
    private void RebuildHolding(IReadOnlyList<TriggerEntry> entries)
    {
        _holding.Clear();

        var events = entries
            .Where(one => one.Kind == TriggerEntryKinds.Event && one.Event is not null)
            .GroupBy(one => one.Event!, StringComparer.Ordinal)
            .ToDictionary(one => one.Key, one => one.First().ToEvent(), StringComparer.Ordinal);

        for (var at = 0; at < entries.Count; at++)
        {
            var entry = entries[at];

            if (entry.Kind != TriggerEntryKinds.Held || entry.Reason == SpendLedger.PausedReason
                || entry.Rule is not { } rule || entry.Event is not { } id || !events.TryGetValue(id, out var happened))
            {
                continue;
            }

            var since = entry.At;
            var decided = entries.Skip(at + 1).Any(later =>
                string.Equals(later.Event, id, StringComparison.Ordinal)
                && string.Equals(later.Rule, rule, StringComparison.OrdinalIgnoreCase));

            if (!decided)
            {
                _holding[(rule.ToLowerInvariant(), happened.Subject)] = new Holding(rule, happened, since, entry.Approval);
            }
        }
    }

    /// <summary>The approval a held event was approved under, read from the journal, for a resume deciding it.</summary>
    private static string? ApprovalOf(IReadOnlyList<TriggerEntry> journal, string eventId, string rule) =>
        journal.LastOrDefault(one => one.Kind == TriggerEntryKinds.Held
            && string.Equals(one.Event, eventId, StringComparison.Ordinal)
            && string.Equals(one.Rule, rule, StringComparison.OrdinalIgnoreCase))?.Approval;

    /// <summary>The rules as last read, for checks made between reads.</summary>
    private IReadOnlyList<CheckedRule> _lastRules = [];

    private sealed record Holding(string Rule, TriggerEvent Event, DateTimeOffset Since, string? Approval);
}
