using FluentAssertions;
using Loadout.Core.Ask;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Usage;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Asking Loadout, end to end over a real workspace: placing, answering, and
/// setting out a plan that starts nothing.
/// </summary>
public sealed class AskServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    private readonly TemporaryWorkspace _space = new();
    private readonly ConfigurationService _configuration;
    private readonly RecordingUsage _usage = new();
    private readonly AskService _ask;

    public AskServiceTests()
    {
        var environment = new FakeEnvironmentProvider(_space.Scratch("home"));

        _configuration = new ConfigurationService(_space.Paths, environment, _space.Yaml);

        var projects = new ProjectService(_configuration, _space.Workspace, new FakeGit(_space.Scratch("git")), new PathSemantics(), _space.Tasks);

        _ask = new AskService(
            _usage,
            _space.Tasks,
            projects,
            _configuration,
            new TeamCatalogue(),
            new SpecialistLibrary(),
            _space.Workspace,
            _space.Paths,
            new FixedTime(Now));
    }

    public void Dispose() => _space.Dispose();

    private async Task RegisterAsync(params string[] slugs)
    {
        var registry = new ProjectRegistry();

        registry.Projects.AddRange(slugs.Select(slug => new ProjectRegistryEntry { Id = slug, Slug = slug, Name = slug }));

        (await _space.Workspace.WriteRegistryAsync(registry)).Succeeded.Should().BeTrue();
    }

    private async Task TaskAsync(string project, string id, string title, int doneWhen = 0, string detail = "")
    {
        (await _space.Tasks.DeclareAsync(
            project,
            id,
            TaskState.Open,
            "test",
            title: title,
            detail: detail,
            doneWhen: Enumerable.Range(1, doneWhen).Select(n => $"condition {n}").ToList())).Succeeded.Should().BeTrue();
    }

    private async Task<AskReply> AskAsync(string query)
    {
        var result = await _ask.AskAsync(query);

        result.Succeeded.Should().BeTrue(result.Error);

        return result.Value!;
    }

    [Fact]
    public async Task A_spend_question_is_answered_by_loadout_as_a_list_rate_estimate_for_the_day_asked()
    {
        await RegisterAsync("alpha");

        _usage.Models = [new UsageGroup("claude-opus-5-5", new UsageTotals(0, 0, 0, 0, 1_000_000, 0))];

        var reply = await AskAsync("? how much did i spend last tuesday, across all registered projects");

        reply.Kind.Should().Be(AskReplyKind.Answer);
        reply.AnsweredBy.Should().Be("loadout");
        reply.Spend!.Estimate.Usd.Should().Be(20m);
        reply.Summary.Should().Contain("$20.00").And.Contain("list-rate estimate").And.Contain("every session and team run");

        _usage.Asked.Should().NotBeNull();
        _usage.Asked!.From.Should().Be(new DateOnly(2026, 10, 20));
        _usage.Asked.Until.Should().Be(new DateOnly(2026, 10, 20));
        _usage.Asked.ProjectSlug.Should().BeNull();
    }

    [Fact]
    public async Task A_team_request_is_a_plan_of_tasks_with_a_done_when_that_needs_a_cap_when_none_is_set()
    {
        await RegisterAsync("alpha", "beta");
        await TaskAsync("alpha", "small", "Rename a flag", doneWhen: 1, detail: "One line.");
        await TaskAsync("alpha", "unbounded", "Tidy the readme");
        await TaskAsync("beta", "medium", "Add an option", doneWhen: 2, detail: new string('x', 600));

        var reply = await AskAsync("start a team and queue up 5 easy pending tasks and start it");

        reply.Kind.Should().Be(AskReplyKind.Plan);

        var plan = reply.Plan!;

        plan.Steps.Where(step => step.Kind == AskStepKind.QueueTask).Select(step => step.Task)
            .Should().Equal("small", "medium");
        plan.Steps.Last().Kind.Should().Be(AskStepKind.StartTeam);
        plan.Steps[0].Reasons.Should().NotBeEmpty();
        plan.Left.Should().ContainSingle(left => left.StartsWith("unbounded on alpha", StringComparison.Ordinal));

        plan.Capped.Should().BeTrue();
        plan.Cap.IsSet.Should().BeFalse();
        plan.Needs.Should().Contain(need => need.StartsWith("a spending cap", StringComparison.Ordinal));
        plan.TeamChoices.Should().NotBeEmpty("the built-in teams can all be chosen");
        plan.Needs.Should().Contain("which team to use");
    }

    [Fact]
    public async Task A_team_plan_takes_its_cap_from_team_budget_and_a_named_team_needs_no_choosing()
    {
        await RegisterAsync("alpha");
        await TaskAsync("alpha", "small", "Rename a flag", doneWhen: 1);

        (await _configuration.UpdateMachineAsync(machine => machine.Teams.Budget = "25")).Succeeded.Should().BeTrue();

        var reply = await AskAsync("queue one easy task for bug-hunt and start it");

        reply.Plan!.Team.Should().Be("bug-hunt");
        reply.Plan.Cap.Usd.Should().Be(25m);
        reply.Plan.Needs.Should().BeEmpty();
    }

    [Fact]
    public async Task Terminals_go_one_per_project_before_any_project_gets_a_second()
    {
        await RegisterAsync("alpha", "beta");
        await TaskAsync("alpha", "a1", "Fix the crash");
        await TaskAsync("alpha", "a2", "Fix the other crash");
        await TaskAsync("beta", "b1", "Add a panel");

        var reply = await AskAsync("open 2 terminals with tasks that need work first");

        reply.Plan!.Capped.Should().BeFalse();
        reply.Plan.Steps.Select(step => step.Project).Should().BeEquivalentTo(["alpha", "beta"]);
    }

    [Fact]
    public async Task A_request_it_cannot_place_offers_near_misses_and_is_kept_locally_redacted()
    {
        await RegisterAsync("alpha");

        var reply = await AskAsync("who broke the build, token ghp_abcdefghijklmnopqrstuvwxyz0123456789");

        reply.Kind.Should().Be(AskReplyKind.Unplaced);
        reply.NearMisses.Should().NotBeEmpty();

        var log = await File.ReadAllTextAsync(AskService.UnplacedLog(_space.Paths));

        log.Should().Contain("who broke the build").And.NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    /// <summary>A usage service that answers with whatever models it is given, and remembers what it was asked.</summary>
    private sealed class RecordingUsage : IUsageService
    {
        public IReadOnlyList<UsageGroup> Models { get; set; } = [];

        public UsageQuery? Asked { get; private set; }

        public Task<OperationResult<UsageReport>> ReportAsync(UsageQuery query, CancellationToken ct = default)
        {
            Asked = query;

            var totals = Models.Aggregate(UsageTotals.Zero, (sum, model) => sum + model.Totals);

            return Task.FromResult(OperationResult<UsageReport>.Ok(new UsageReport(
                query.From ?? DateOnly.MinValue, totals, [], [], Models, [], UsageIntegrity.Empty)));
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
