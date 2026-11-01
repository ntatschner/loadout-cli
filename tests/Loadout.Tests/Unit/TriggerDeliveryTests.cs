using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Delivery;
using Loadout.Core.Triggers;
using Loadout.Models.Triggers;
using Loadout.Tui;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Trigger rules that hand work to delivery: a rule that queues a task with a
/// pull request mode, and a rule that releases with what the plan proposes,
/// which always waits for a person.
/// </summary>
public sealed class TriggerDeliveryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

    private static TriggerRule Rule(TriggerAction action) => new()
    {
        Id = "fix-failures",
        On = TriggerEventKinds.RunFinished,
        Action = action,
        CapUsd = 10m,
    };

    private static TriggerEvent Finished() => new(
        "e1", Now, RunFinishedSource.Name, TriggerEventKinds.RunFinished, "run:r1", "website",
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["team"] = "builders", ["outcome"] = "failed", ["run"] = "r1" },
        []);

    [Fact]
    public void Only_a_team_rule_may_queue_and_a_pull_request_mode_needs_the_queue()
    {
        TriggerRules.Check(Rule(new TriggerAction { Name = "fixers", Goal = "Fix it", Queue = true, PrMode = "ready" }))
            .Should().BeNull();
        TriggerRules.Check(Rule(new TriggerAction { Name = "fixers", Goal = "Fix it", PrMode = "ready" }))
            .Should().Contain("--queue");
        TriggerRules.Check(Rule(new TriggerAction { Name = "fixers", Goal = "Fix it", Queue = true, PrMode = "sometimes" }))
            .Should().Contain("not a pull request mode");
        TriggerRules.Check(Rule(new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit", Queue = true }))
            .Should().Contain("Only a rule that starts a team");
    }

    [Fact]
    public void A_queued_rule_records_a_task_from_its_goal_and_queues_it_with_its_mode()
    {
        var rule = Rule(new TriggerAction { Name = "fixers", Goal = "Find out why {team} {outcome} in {run}", Queue = true, PrMode = "draft" });

        var (task, queue) = DaemonTriggerActions.QueueWords(rule, Finished(), "website", "fix-failures-20261101093000");

        task.Should().Equal(
            "fix-failures-20261101093000", "open", "--project", "website",
            "--title=Find out why builders failed in r1", "--note=Find out why builders failed in r1", "--by=trigger fix-failures");
        queue.Should().Equal(
            "fixers", "fix-failures-20261101093000", "--project", "website", "--by=trigger fix-failures", "--pr-mode=draft");
    }

    [Fact]
    public async Task A_queued_rule_starts_no_run_and_asks_for_the_task_then_the_queue()
    {
        var commands = new Recording();
        var actions = new DaemonTriggerActions(commands, null!, null!, null!, null!);
        var rule = Rule(new TriggerAction { Name = "fixers", Goal = "Fix it", Queue = true });

        var outcome = await actions.StartAsync(new TriggerStart(rule, Finished(), 5m, "res-1"), CancellationToken.None);

        outcome.Problem.Should().BeNull();
        outcome.Run.Should().BeNull("nothing ran: it waits in the queue");
        commands.Asked.Select(one => one.Path).Should().Equal("task declare", "team queue add");
        commands.Asked[1].Words.Should().NotContain(word => word.StartsWith("--pr-mode", StringComparison.Ordinal), "the rule set none");
    }

    [Fact]
    public async Task A_release_rule_writes_so_it_waits_for_approval_and_runs_with_the_proposal()
    {
        var actions = new DaemonTriggerActions(new Recording(), null!, null!, null!, null!);

        (await actions.DescribeAsync(new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "release cut" }, "website", CancellationToken.None))
            .Writes.Should().BeTrue("a release pushes, so it never starts unattended");
        (await actions.DescribeAsync(new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = "docs audit" }, "website", CancellationToken.None))
            .Writes.Should().BeFalse();

        DaemonTriggerActions.BuiltinWords("release cut", "website").Should().Equal("website", "--proposed", "--yes", "--non-interactive");
        DaemonTriggerActions.BuiltinWords("docs audit", "website").Should().Equal("website");
    }

    [Fact]
    public void Notes_nobody_wrote_say_so_and_list_what_was_committed()
    {
        var plan = new ReleasePlan(
            "website", "/r", ReleaseService.TagPush, "main", "abc", "v", "v1.2.0", "Directory.Build.props", "1.2.0",
            "1.3.0", "minor", ["Widgets: add the blue one (#5)", "Fix the red one"], string.Empty, []);

        var notes = ReleaseService.AutomaticNotes(plan);

        notes.Should().StartWith("Released by a trigger rule. Changes since v1.2.0:");
        notes.Should().Contain("- Widgets: add the blue one (#5)").And.Contain("Pull requests: #5");
        notes.Split('\n').Should().NotContain(line => line.StartsWith('#'));
    }

    private sealed class Recording : ICommandCatalogue
    {
        public List<(string Path, IReadOnlyList<string> Words)> Asked { get; } = [];

        public IReadOnlyList<CatalogueEntry> Commands => [];

        public Task<int> RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken ct = default)
        {
            Asked.Add((path, arguments));

            return Task.FromResult(0);
        }
    }
}
