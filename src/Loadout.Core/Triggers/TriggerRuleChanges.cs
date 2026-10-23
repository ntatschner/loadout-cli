using System.Globalization;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>What an edit to a trigger rule would change, field by field.</summary>
/// <remarks>
/// An edit replaces the whole rule, so a dry run that only said "this would
/// replace version 3" left the person to work out the difference themselves,
/// and a flag left out by mistake reads the same as one meant. Each changed
/// field is named with what it was and what it would be.
/// </remarks>
public static class TriggerRuleChanges
{
    /// <summary>Each field <paramref name="after"/> changes from <paramref name="before"/>, as "field: was -> now".</summary>
    public static IReadOnlyList<string> Of(TriggerRule before, TriggerRule after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var changes = new List<string>();

        void Compare(string field, string was, string now)
        {
            if (!string.Equals(was, now, StringComparison.Ordinal))
            {
                changes.Add($"{field}: {was} -> {now}");
            }
        }

        Compare("on", before.On, after.On);
        Compare("every", Duration(before.Every ?? TimeSpan.Zero), Duration(after.Every ?? TimeSpan.Zero));
        Compare(
            "at",
            before.At?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "(none)",
            after.At?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "(none)");
        Compare("projects", List(before.Projects), List(after.Projects));
        Compare("where", Pairs(before.Where), Pairs(after.Where));
        Compare("starts", Action(before.Action), Action(after.Action));
        Compare("goal", Quoted(before.Action.Goal), Quoted(after.Action.Goal));
        Compare("autonomy", before.Action.Autonomy, after.Action.Autonomy);
        Compare("budget per run", Money(before.BudgetUsd), Money(after.BudgetUsd));
        Compare("spend cap", Money(before.CapUsd), Money(after.CapUsd));
        Compare("run cap", before.CapRuns.ToString(CultureInfo.InvariantCulture), after.CapRuns.ToString(CultureInfo.InvariantCulture));
        Compare("window", TeamDuration.Spell(before.Window), TeamDuration.Spell(after.Window));
        Compare("debounce", Duration(before.Debounce), Duration(after.Debounce));
        Compare("enabled", before.Enabled ? "yes" : "no", after.Enabled ? "yes" : "no");

        return changes;
    }

    private static string List(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "(all)" : string.Join(", ", values.Order(StringComparer.OrdinalIgnoreCase));

    private static string Pairs(IReadOnlyDictionary<string, string> values) =>
        values.Count == 0
            ? "(anything)"
            : string.Join(", ", values.OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase).Select(one => $"{one.Key}={one.Value}"));

    private static string Action(TriggerAction action) =>
        action.Kind == TriggerActionKinds.Team ? $"team {action.Name}" : $"{action.Kind} {action.Name}";

    private static string Quoted(string text) => text.Length == 0 ? "(none)" : $"\"{text}\"";

    private static string Money(decimal? usd) =>
        usd is { } value ? "$" + value.ToString("0.00", CultureInfo.InvariantCulture) : "(none)";

    private static string Duration(TimeSpan span) => span <= TimeSpan.Zero ? "(none)" : TeamDuration.Spell(span);
}
