using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>What one rule made of one event.</summary>
/// <param name="Rule">The rule.</param>
/// <param name="Matched">Whether it will act.</param>
/// <param name="Reason">Why not, where it will not.</param>
public sealed record RuleDecision(TriggerRule Rule, bool Matched, string? Reason = null);

/// <summary>Which rules an event matches, and why the others pass it by.</summary>
/// <remarks>
/// Pure, so it can be asked about a past event without anything happening,
/// which is what replaying a rule against the journal is. Only rules waiting
/// for the event's kind are reported at all: a journal that said, for every
/// event, that every other rule did not want it would bury the decisions that
/// matter.
/// </remarks>
public static class TriggerMatcher
{
    /// <summary>
    /// The longest chain of rules starting each other that is let through.
    /// </summary>
    /// <remarks>
    /// A rule never answers an event its own action led to. That stops a rule
    /// starting itself, but not two rules starting each other in turn; a chain
    /// this long is a loop or a design nobody could follow.
    /// </remarks>
    public const int LongestChain = 3;

    /// <summary>What each rule waiting for this kind of event makes of it.</summary>
    public static IReadOnlyList<RuleDecision> Decide(TriggerEvent happened, IEnumerable<CheckedRule> rules)
    {
        ArgumentNullException.ThrowIfNull(happened);
        ArgumentNullException.ThrowIfNull(rules);

        return
        [
            .. rules
                .Where(one => one.Usable
                    && string.Equals(one.Rule.On, happened.Kind, StringComparison.OrdinalIgnoreCase)

                    // A clock event is one rule's own; every other clock rule
                    // passing it by would be a skip a minute each.
                    && (!string.Equals(happened.Kind, TriggerEventKinds.Clock, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(happened.Field("rule"), one.Rule.Id, StringComparison.OrdinalIgnoreCase)))
                .Select(one => Decide(happened, one.Rule)),
        ];
    }

    /// <summary>What one rule makes of an event.</summary>
    public static RuleDecision Decide(TriggerEvent happened, TriggerRule rule)
    {
        ArgumentNullException.ThrowIfNull(happened);
        ArgumentNullException.ThrowIfNull(rule);

        if (!rule.Enabled)
        {
            return new RuleDecision(rule, false, "the rule is disabled");
        }

        if (happened.Origins.Contains(rule.Id, StringComparer.OrdinalIgnoreCase))
        {
            return new RuleDecision(rule, false, "the event came from this rule's own action");
        }

        if (happened.Origins.Count >= LongestChain)
        {
            return new RuleDecision(
                rule, false, $"the event came at the end of {happened.Origins.Count} rules starting each other");
        }

        if (string.Equals(happened.Kind, TriggerEventKinds.RunFinished, StringComparison.OrdinalIgnoreCase)
            && string.Equals(rule.Action.Kind, TriggerActionKinds.Team, StringComparison.OrdinalIgnoreCase)
            && string.Equals(happened.Field("team"), rule.Action.Name, StringComparison.OrdinalIgnoreCase))
        {
            // The rule a schedule already keeps: a team whose finishing starts
            // itself is a loop, whoever started the first run.
            return new RuleDecision(rule, false, $"the run that finished was {rule.Action.Name}'s own");
        }

        if (rule.Projects.Count > 0
            && !rule.Projects.Contains(happened.Project ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            return new RuleDecision(
                rule, false, $"it watches {string.Join(", ", rule.Projects)}, not {happened.Project ?? "no project"}");
        }

        foreach (var (field, wanted) in rule.Where)
        {
            var has = happened.Field(field);

            if (!string.Equals(has, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return new RuleDecision(
                    rule, false, $"{field} is {(has is null ? "not given" : $"'{has}'")}, not '{wanted}'");
            }
        }

        return new RuleDecision(rule, true);
    }
}
