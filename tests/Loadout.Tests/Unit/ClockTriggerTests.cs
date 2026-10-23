using FluentAssertions;
using Loadout.Core.Triggers;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Clock rules: what a schedule does, as a trigger.</summary>
public sealed class ClockTriggerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 6, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-clock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_state, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_clock_rule_comes_round_after_its_interval_and_not_on_the_first_look()
    {
        var source = new ClockSource(_state);
        var rule = Every("hourly", TimeSpan.FromHours(1));

        source.Look([rule], Now).Should().BeEmpty("a rule written a minute ago does not go off at once");
        source.Look([rule], Now.AddMinutes(59)).Should().BeEmpty();

        var came = source.Look([rule], Now.AddHours(1)).Single();
        came.Kind.Should().Be(TriggerEventKinds.Clock);
        came.Field("rule").Should().Be("hourly");
        came.Subject.Should().Be("clock:hourly");
        came.Project.Should().Be("loadout-cli");

        source.Look([rule], Now.AddHours(1).AddMinutes(1)).Should().BeEmpty("it counts from when it last came round");
        new ClockSource(_state).Look([rule], Now.AddHours(2)).Should().ContainSingle("the cursor outlives the daemon");
    }

    [Fact]
    public void A_daily_rule_saved_before_its_time_comes_round_that_day_once()
    {
        var source = new ClockSource(_state);
        var at = TimeOnly.FromDateTime(Now.ToLocalTime().AddHours(2).DateTime);
        var rule = new TriggerRule
        {
            Id = "morning", On = TriggerEventKinds.Clock, At = at,
            Action = new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit" },
        };

        source.Look([rule], Now).Should().BeEmpty();
        source.Look([rule], Now.AddHours(2).AddMinutes(1)).Should().ContainSingle();
        source.Look([rule], Now.AddHours(2).AddMinutes(30)).Should().BeEmpty("once a day");
    }

    [Fact]
    public void A_disabled_or_removed_rule_does_not_come_round_and_a_new_one_of_its_name_starts_afresh()
    {
        var source = new ClockSource(_state);
        var rule = Every("hourly", TimeSpan.FromHours(1));

        source.Look([rule], Now);
        rule.Enabled = false;
        source.Look([rule], Now.AddHours(3)).Should().BeEmpty();

        rule.Enabled = true;
        source.Look([rule], Now.AddHours(4)).Should().BeEmpty("forgotten while it was off, so this is a first look");
    }

    [Fact]
    public void A_clock_event_is_put_only_to_the_rule_it_belongs_to()
    {
        var mine = Every("mine", TimeSpan.FromHours(1));
        var other = Every("other", TimeSpan.FromHours(1));
        var happened = new TriggerEvent(
            "clock:mine:1", Now, "daemon", TriggerEventKinds.Clock, "clock:mine", "loadout-cli",
            new Dictionary<string, string> { ["rule"] = "mine" }, []);

        TriggerMatcher.Decide(happened, [new CheckedRule(mine, null), new CheckedRule(other, null)])
            .Should().ContainSingle().Which.Rule.Id.Should().Be("mine");
    }

    [Theory]
    [InlineData(true, true, "exactly one of them")]
    [InlineData(false, false, "exactly one of them")]
    public void A_clock_rule_needs_an_interval_or_a_time_but_not_both(bool every, bool at, string said)
    {
        var rule = Every("r", TimeSpan.FromHours(1));
        rule.Every = every ? TimeSpan.FromHours(1) : null;
        rule.At = at ? new TimeOnly(9, 0) : null;

        TriggerRules.Check(rule).Should().Contain(said);
    }

    [Fact]
    public void A_clock_rule_checks_its_interval_and_its_project_and_other_rules_keep_no_clock()
    {
        var quick = Every("r", TimeSpan.FromMinutes(1));
        TriggerRules.Check(quick).Should().Contain("five minutes");

        var nowhere = Every("r", TimeSpan.FromHours(1));
        nowhere.Projects = [];
        TriggerRules.Check(nowhere).Should().Contain("one project");

        var waiting = Every("r", TimeSpan.FromHours(1));
        waiting.On = TriggerEventKinds.RunFinished;
        TriggerRules.Check(waiting).Should().Contain("Only a clock rule");

        TriggerRules.Check(Every("r", TimeSpan.FromHours(1))).Should().BeNull();
    }

    [Fact]
    public void A_schedule_on_a_clock_becomes_a_clock_rule_with_its_own_figures()
    {
        var (rule, why) = TriggerAdopt.Rule(new TeamSchedule
        {
            Id = "nightly", Project = "loadout-cli", Team = "sweeper", Goal = "Sweep.", Autonomy = "supervised",
            Every = TimeSpan.FromHours(6), Budget = "2.50",
        });

        why.Should().BeNull();
        rule!.On.Should().Be(TriggerEventKinds.Clock);
        rule.Every.Should().Be(TimeSpan.FromHours(6));
        rule.Projects.Should().Equal("loadout-cli");
        rule.Action.Name.Should().Be("sweeper");
        rule.Action.Autonomy.Should().Be("supervised");
        rule.BudgetUsd.Should().Be(2.50m);
    }

    [Fact]
    public void Queue_windows_and_schedules_watching_for_a_commit_stay_schedules()
    {
        TriggerAdopt.Rule(new TeamSchedule { Id = "w", Kind = TeamSchedule.QueueKind, At = new TimeOnly(22, 0) })
            .Why.Should().Contain("queue window");
        TriggerAdopt.Rule(new TeamSchedule { Id = "c", On = "commit" }).Why.Should().Contain("waits for commit");
    }

    private static TriggerRule Every(string id, TimeSpan every) => new()
    {
        Id = id,
        On = TriggerEventKinds.Clock,
        Every = every,
        Projects = ["loadout-cli"],
        Action = new TriggerAction { Kind = TriggerActionKinds.Team, Name = "sweeper", Goal = "Sweep." },
        CapUsd = 10m,
    };
}
