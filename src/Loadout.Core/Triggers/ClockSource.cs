using System.Text.Json;
using Loadout.Core.Teams;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>
/// Turns each clock rule's time coming round into an event for that rule.
/// </summary>
/// <remarks>
/// <para>
/// When a rule is due is answered by <see cref="ScheduleService.IsDue"/>, the
/// schedules' own answer, so a clock rule and the schedule it may replace keep
/// the same times: every so often from the last time it came round, or once a
/// day within an hour of its time of day.
/// </para>
/// <para>
/// The first look at a rule writes down the moment and posts nothing, for the
/// reason the other sources give: a rule written a minute ago that went off at
/// once is not what anybody means by "every hour". A daily rule saved before
/// its time still comes round that day.
/// </para>
/// <para>
/// When each rule last came round is kept in
/// <c>&lt;State&gt;/triggers/cursors/clock.json</c>, written before the events
/// are handed on, so a daemon killed between the two misses a turn rather than
/// taking it twice.
/// </para>
/// </remarks>
public sealed class ClockSource
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "daemon";

    private readonly string _cursor;

    /// <param name="state">The state directory.</param>
    public ClockSource(string state) => _cursor = Path.Combine(state, "triggers", "cursors", "clock.json");

    /// <summary>The clock rules among <paramref name="rules"/> that have come round, as events.</summary>
    public IReadOnlyList<TriggerEvent> Look(IEnumerable<TriggerRule> rules, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var clocks = rules
            .Where(one => one.Enabled && string.Equals(one.On, TriggerEventKinds.Clock, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var last = Read();
        var events = new List<TriggerEvent>();
        var changed = false;

        foreach (var rule in clocks)
        {
            if (!last.TryGetValue(rule.Id, out var came))
            {
                last[rule.Id] = now;
                changed = true;

                continue;
            }

            var due = ScheduleService.IsDue(
                new TeamSchedule { Every = rule.Every, At = rule.At, LastRun = came, Enabled = true },
                now);

            if (!due)
            {
                continue;
            }

            last[rule.Id] = now;
            changed = true;

            events.Add(new TriggerEvent(
                $"clock:{rule.Id}:{now.UtcDateTime:yyyyMMddTHHmmss}",
                now,
                Name,
                TriggerEventKinds.Clock,
                $"clock:{rule.Id}",
                rule.Projects.FirstOrDefault(),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["rule"] = rule.Id },
                []));
        }

        // A rule that went is forgotten, so one saved again under its name
        // starts afresh rather than catching up on its old turns.
        foreach (var gone in last.Keys.Where(id => !clocks.Any(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase))).ToList())
        {
            last.Remove(gone);
            changed = true;
        }

        if (changed)
        {
            Save(last);
        }

        return events;
    }

    private Dictionary<string, DateTimeOffset> Read()
    {
        try
        {
            return File.Exists(_cursor)
                ? new Dictionary<string, DateTimeOffset>(
                    JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_cursor), TriggerJournal.Json)
                        ?? [],
                    StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cursor that cannot be read is a first look at every rule.
            return new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, DateTimeOffset> last)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cursor)!);

        var aside = _cursor + ".tmp";
        File.WriteAllText(aside, JsonSerializer.Serialize(last, TriggerJournal.Json));
        File.Move(aside, _cursor, overwrite: true);
    }
}
