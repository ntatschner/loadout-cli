using FluentAssertions;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>What an edit to a trigger rule would change, as a dry run shows it.</summary>
public sealed class TriggerRuleChangesTests
{
    [Fact]
    public void Each_changed_field_is_named_with_what_it_was_and_would_be()
    {
        var before = Rule();
        var after = Rule();
        after.CapUsd = 8m;
        after.Projects = ["loadout-cli", "starstats"];
        after.Debounce = TimeSpan.Zero;

        TriggerRuleChanges.Of(before, after).Should().Equal(
            "projects: loadout-cli -> loadout-cli, starstats",
            "spend cap: $6.00 -> $8.00",
            "debounce: 10m -> (none)");
    }

    [Fact]
    public void An_edit_that_changes_nothing_says_nothing()
    {
        var after = Rule();
        after.Version = 4;

        TriggerRuleChanges.Of(Rule(), after).Should().BeEmpty("the version is the store's to count, not part of the edit");
    }

    [Fact]
    public void Where_conditions_and_projects_are_compared_whatever_order_they_were_given_in()
    {
        var before = Rule();
        before.Where = new Dictionary<string, string> { ["outcome"] = "failed", ["team"] = "alpha" };
        before.Projects = ["b", "a"];
        var after = Rule();
        after.Where = new Dictionary<string, string> { ["team"] = "alpha", ["outcome"] = "failed" };
        after.Projects = ["a", "b"];

        TriggerRuleChanges.Of(before, after).Should().BeEmpty();
    }

    private static TriggerRule Rule() => new()
    {
        Id = "watch-failures",
        On = TriggerEventKinds.RunFinished,
        Projects = ["loadout-cli"],
        Where = new Dictionary<string, string> { ["outcome"] = "failed" },
        Action = new TriggerAction { Kind = TriggerActionKinds.Team, Name = "watcher", Goal = "Find out why." },
        BudgetUsd = 1m,
        CapUsd = 6m,
        Debounce = TimeSpan.FromMinutes(10),
    };
}
