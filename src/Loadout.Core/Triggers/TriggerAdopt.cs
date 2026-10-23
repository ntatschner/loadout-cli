using Loadout.Models.Teams;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>The clock rule a team schedule becomes, where it can become one.</summary>
/// <remarks>
/// <para>
/// Schedules are being folded into triggers, so that a clock and an event
/// start runs the same way, under the same caps and in the same journal.
/// While triggers are unfinished the fold is one schedule at a time and asked
/// for: the schedules keep running as they always have until then.
/// </para>
/// <para>
/// Only a schedule on a clock moves. A queue window starts no run of its own,
/// and a schedule watching for a commit waits for an event triggers do not
/// carry yet; both stay schedules, and say why.
/// </para>
/// </remarks>
public static class TriggerAdopt
{
    /// <summary>The rule, before any spend cap, or why the schedule cannot become one.</summary>
    public static (TriggerRule? Rule, string? Why) Rule(TeamSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (schedule.IsQueueWindow)
        {
            return (null, $"'{schedule.Id}' is a queue window, which starts no run of its own, so it stays a schedule.");
        }

        if (schedule.On is { Length: > 0 } on)
        {
            return (null, $"'{schedule.Id}' waits for {on} rather than a clock, and triggers do not watch for that yet, "
                + "so it stays a schedule.");
        }

        if (schedule.Every is null && schedule.At is null)
        {
            return (null, $"'{schedule.Id}' has neither an interval nor a time of day to move.");
        }

        UsdCap.TryParse(schedule.Budget, out var budget);

        var rule = new TriggerRule
        {
            Id = schedule.Id,
            On = TriggerEventKinds.Clock,
            Every = schedule.Every,
            At = schedule.Every is null ? schedule.At : null,
            Projects = [schedule.Project],
            Action = new TriggerAction
            {
                Kind = TriggerActionKinds.Team,
                Name = schedule.Team,
                Goal = schedule.Goal,
                Autonomy = schedule.Autonomy,
            },

            // A schedule's "none" means no limit on a run, which a rule does not
            // have: the team's own figure stands in, under the rule's cap.
            BudgetUsd = budget.Usd,
            Enabled = schedule.Enabled,
        };

        return (rule, null);
    }
}
