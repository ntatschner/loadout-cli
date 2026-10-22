using FluentAssertions;
using Loadout.Core.Triggers;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Trigger rules a team suggests, and what accepting one makes.</summary>
public sealed class TeamSuggestionsTests
{
    [Fact]
    public void Accepting_a_suggestion_makes_an_ordinary_rule_that_remembers_where_it_came_from()
    {
        var rule = TeamSuggestions.Rule("watcher", Suggested(), ["loadout-cli"]);

        rule.Id.Should().Be("watcher-after-failure");
        rule.On.Should().Be(TriggerEventKinds.RunFinished);
        rule.Where.Should().ContainKey("outcome").WhoseValue.Should().Be("failed");
        rule.Projects.Should().Equal("loadout-cli");
        rule.Action.Kind.Should().Be(TriggerActionKinds.Team);
        rule.Action.Name.Should().Be("watcher");
        rule.CapUsd.Should().Be(6m);
        rule.Debounce.Should().Be(TimeSpan.FromMinutes(10));
        rule.SuggestedBy.Should().Be("watcher");
        rule.Suggestion.Should().StartWith("after-failure@");

        TriggerRules.Check(rule).Should().BeNull();
    }

    [Fact]
    public void A_suggestion_the_team_changes_after_it_was_accepted_reads_as_changed_down_to_a_figure()
    {
        var accepted = TeamSuggestions.Rule("watcher", Suggested(), []);
        var team = Team(Suggested());

        TeamSuggestions.Of([team], [accepted]).Single().Changed.Should().BeFalse();

        team.Triggers[0].CapUsd = 8m;

        var after = TeamSuggestions.Of([team], [accepted]).Single();
        after.Changed.Should().BeTrue("a team raising its cap is the change most worth seeing");
        after.Accepted!.CapUsd.Should().Be(6m, "the rule stays as it was accepted");
    }

    [Fact]
    public void A_rule_written_by_hand_with_the_same_name_is_not_taken_for_an_accepted_suggestion()
    {
        var mine = new TriggerRule { Id = "watcher-after-failure", On = TriggerEventKinds.RunFinished };

        TeamSuggestions.Of([Team(Suggested())], [mine]).Single().Accepted.Should().BeNull();
    }

    [Fact]
    public void Templates_suggest_nothing()
    {
        var template = Team(Suggested());
        template.Template = true;

        TeamSuggestions.Of([template], []).Should().BeEmpty();
    }

    private static TeamDefinition Team(TeamTrigger suggested) => new() { Name = "watcher", Triggers = [suggested] };

    private static TeamTrigger Suggested() => new()
    {
        Name = "after-failure",
        On = "run-finished",
        Where = new Dictionary<string, string> { ["outcome"] = "failed" },
        Goal = "Find out why {run} failed.",
        CapUsd = 6m,
        Debounce = "10m",
    };
}
