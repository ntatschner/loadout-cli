using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Saying what picking a run up is likely to cost, before it is spent.
/// </summary>
/// <remarks>
/// The case these come from: a run resumed at 109.47 of a 120 budget, whose
/// lead then spent 15.08 in one turn of 14 exchanges. The budget is checked
/// between rounds, so nothing stopped it, and nothing said it was likely.
/// </remarks>
public sealed class ResumeCostTests
{
    [Fact]
    public void A_lead_whose_last_turn_costs_more_than_is_left_is_said_to_before_it_starts()
    {
        var said = Resumed(lastTurn: 15.08m, session: "3c38").LikelyCost(spent: 109.47m, cap: 120m);

        said.Should().Contain("$15.08").And.Contain("14 exchange(s)");
        said.Should().Contain("more than the $10.53 the budget has left");
    }

    [Fact]
    public void A_lead_whose_last_turn_fits_in_what_is_left_is_given_the_figure_alone()
    {
        var said = Resumed(lastTurn: 0.15m, session: "3c38").LikelyCost(spent: 109.47m, cap: 120m);

        said.Should().Contain("$0.15").And.NotContain("budget has left");
    }

    [Fact]
    public void A_run_already_past_its_budget_says_any_turn_goes_further_past_it()
    {
        Resumed(lastTurn: 1m, session: "3c38").LikelyCost(spent: 124.56m, cap: 120m)
            .Should().Contain("already spent");
    }

    [Fact]
    public void A_fresh_lead_is_not_given_a_figure_from_a_conversation_it_will_not_carry()
    {
        Resumed(lastTurn: 15.08m, session: null).LikelyCost(spent: 109.47m, cap: 120m).Should().BeNull();
    }

    [Fact]
    public void Only_the_leads_own_turns_count()
    {
        // The last turn in the journal is the verifier's, and cheap; the lead's
        // own, earlier, is the one it will be like.
        var said = Resumed(lastTurn: 15.08m, session: "3c38", after: ("verifier", 0.46m))
            .LikelyCost(spent: 109.47m, cap: 120m);

        said.Should().Contain("$15.08");
    }

    private static RunResumption Resumed(decimal lastTurn, string? session, (string Node, decimal Cost)? after = null)
    {
        var at = new DateTimeOffset(2026, 9, 24, 17, 0, 0, TimeSpan.Zero);
        var events = new List<RunEvent>
        {
            Event(at, null, "run.started", new { team = "t", goal = "g", autonomy = "autonomous", budget = 120 }),
            Event(at.AddMinutes(1), "lead", "node.launched", new { role = "role.project-lead" }),
            Event(at.AddMinutes(2), "lead", "node.turn", new { attempt = 1, turns = 14, cost = lastTurn, completed = true }),
        };

        if (after is { } other)
        {
            events.Add(Event(at.AddMinutes(3), other.Node, "node.launched", new { role = "role.verifier" }));
            events.Add(Event(at.AddMinutes(4), other.Node, "node.turn", new { attempt = 1, turns = 7, cost = other.Cost, completed = true }));
        }

        events.Add(Event(at.AddMinutes(5), null, "run.finished", new { ended = "blocked", outcome = "blocked", cost = 109.47 }));

        var summary = RunJournal.Fold("r", "C:/runs/r", events);

        return new RunResumption(summary, null, false, new Dictionary<string, string>(), new Dictionary<string, string>(), session, null);
    }

    private static RunEvent Event(DateTimeOffset at, string? node, string kind, object data) =>
        new(at, node, kind, JsonSerializer.SerializeToElement(data));
}
