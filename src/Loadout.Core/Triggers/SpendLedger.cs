using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>Where one rule stands against its caps.</summary>
/// <param name="Runs">Runs started inside its window.</param>
/// <param name="Landed">What finished runs spent inside its window.</param>
/// <param name="Reserved">What is set aside for runs still going.</param>
/// <param name="Headroom">What is left under the spend cap, or null for a rule with none.</param>
/// <param name="Paused">Whether it has stopped acting until somebody resumes it.</param>
public sealed record RuleStanding(int Runs, decimal Landed, decimal Reserved, decimal? Headroom, bool Paused);

/// <summary>What the ledger says to a start.</summary>
public enum LedgerVerdict
{
    /// <summary>Start it.</summary>
    Go,

    /// <summary>Keep it back: the rule is paused, or money it needs is still set aside for runs going.</summary>
    Hold,

    /// <summary>The rule has reached a cap with nothing in flight to free headroom: stop it until resumed.</summary>
    Pause,
}

/// <summary>
/// What each rule has started and spent in its window, and what is set aside
/// for runs still going.
/// </summary>
/// <remarks>
/// <para>
/// Changed only by <see cref="Apply"/>, from entries the spine has already
/// written to the journal. Rebuilding from the journal after a restart is then
/// the same calls in the same order, so the ledger after a restart is the
/// ledger before it, open reservations and all.
/// </para>
/// <para>
/// Money is set aside when a run starts and released when it ends, so the cap
/// holds while several runs from one rule are going at once: counting only
/// landed spend would let five runs start against headroom for one.
/// </para>
/// </remarks>
public sealed class SpendLedger
{
    private readonly Dictionary<string, Aside> _open = new(StringComparer.Ordinal);
    private readonly List<Spent> _landed = [];
    private readonly List<Spent> _starts = [];
    private readonly Dictionary<string, DateTimeOffset> _resumed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _paused = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>A ledger rebuilt from what the journal says.</summary>
    public static SpendLedger Rebuild(IEnumerable<TriggerEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var ledger = new SpendLedger();

        foreach (var one in entries)
        {
            ledger.Apply(one);
        }

        return ledger;
    }

    /// <summary>Takes in one journalled entry; anything not about money or caps is ignored.</summary>
    public void Apply(TriggerEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Rule is not { Length: > 0 } rule)
        {
            return;
        }

        lock (_gate)
        {
            switch (entry.Kind)
            {
                case TriggerEntryKinds.Reserved when entry.Reservation is { Length: > 0 } id:
                    _open[id] = new Aside(rule, entry.Usd ?? 0m, entry.At);
                    break;

                case TriggerEntryKinds.Started:
                    _starts.Add(new Spent(rule, 0m, entry.At));
                    break;

                case TriggerEntryKinds.Landed:
                    if (entry.Reservation is { Length: > 0 } closing)
                    {
                        _open.Remove(closing);
                    }

                    _landed.Add(new Spent(rule, entry.Usd ?? 0m, entry.At));
                    break;

                case TriggerEntryKinds.Paused:
                    _paused.Add(rule);
                    break;

                case TriggerEntryKinds.Resumed:
                    // The window counts again from here: resuming is a person
                    // deciding, having been shown what tripped the cap, and it
                    // should work at once. The journal still has the total.
                    _paused.Remove(rule);
                    _resumed[rule] = entry.At;
                    break;
            }
        }
    }

    /// <summary>Where a rule stands now.</summary>
    public RuleStanding Standing(TriggerRule rule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_gate)
        {
            var from = now - rule.Window;

            if (_resumed.TryGetValue(rule.Id, out var resumed) && resumed > from)
            {
                from = resumed;
            }

            bool Ours(Spent one) =>
                string.Equals(one.Rule, rule.Id, StringComparison.OrdinalIgnoreCase) && one.At >= from;

            var runs = _starts.Count(Ours);
            var landed = _landed.Where(Ours).Sum(one => one.Usd);
            var reserved = _open.Values
                .Where(one => string.Equals(one.Rule, rule.Id, StringComparison.OrdinalIgnoreCase))
                .Sum(one => one.Usd);
            decimal? headroom = rule.CapUsd is { } cap ? Math.Max(0m, cap - landed - reserved) : null;

            return new RuleStanding(runs, landed, reserved, headroom, _paused.Contains(rule.Id));
        }
    }

    /// <summary>Whether a rule may start something that would set aside <paramref name="budget"/>, and why not.</summary>
    public (LedgerVerdict Verdict, string? Reason) Ask(TriggerRule rule, decimal budget, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rule);

        var standing = Standing(rule, now);

        if (standing.Paused)
        {
            return (LedgerVerdict.Hold, "the rule is paused until it is resumed");
        }

        if (standing.Runs >= rule.CapRuns)
        {
            // Caps stop new starts only. A loop shows up as starts, so this is
            // the one that catches it.
            return (LedgerVerdict.Pause, $"it has started {standing.Runs} run(s) in its window, its cap");
        }

        if (budget <= 0 || standing.Headroom is not { } headroom || budget <= headroom)
        {
            return (LedgerVerdict.Go, null);
        }

        // Runs in flight usually give back part of what they set aside, so
        // ordinary overlap waits rather than being taken for a loop.
        return standing.Reserved > 0
            ? (LedgerVerdict.Hold, $"${headroom:0.00} is left under the cap and ${budget:0.00} is needed; "
                + $"${standing.Reserved:0.00} is set aside for runs still going")
            : (LedgerVerdict.Pause, $"${headroom:0.00} is left under its ${rule.CapUsd:0.00} cap and a run "
                + $"needs ${budget:0.00}");
    }

    /// <summary>Every reservation not yet landed, with its rule and when it was made.</summary>
    public IReadOnlyList<(string Reservation, string Rule, DateTimeOffset At)> Open()
    {
        lock (_gate)
        {
            return [.. _open.Select(one => (one.Key, one.Value.Rule, one.Value.At))];
        }
    }

    /// <summary>Whether a rule is paused.</summary>
    public bool Paused(string rule)
    {
        lock (_gate)
        {
            return _paused.Contains(rule);
        }
    }

    private sealed record Aside(string Rule, decimal Usd, DateTimeOffset At);

    private sealed record Spent(string Rule, decimal Usd, DateTimeOffset At);
}
