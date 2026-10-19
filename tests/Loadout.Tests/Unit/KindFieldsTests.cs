using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Core.Lists;
using Loadout.Core.Sessions;
using Loadout.Core.Teams;
using Loadout.Models.Ideas;
using Loadout.Models.Projects;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The fields each list kind can be filtered and sorted on: that each field
/// reads what its name says, and that each kind sorts by default the way its
/// list always has.
/// </summary>
public sealed class KindFieldsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static List<string> Apply<T>(ListFields<T> fields, string query, IEnumerable<T> items, Func<T, string> id)
    {
        var applied = fields.Apply(ListQuery.Parse(query).Value!, items, Now);

        applied.Succeeded.Should().BeTrue(applied.Error);

        return [.. applied.Value!.Select(id)];
    }

    [Theory]
    [InlineData("", "b", "a", "c")]
    [InlineData("stage:answering", "a")]
    [InlineData("unanswered:>0", "a")]
    [InlineData("project:web", "a", "c")]
    [InlineData("sort:id", "a", "b", "c")]
    public void Ideas(string query, params string[] expected)
    {
        IdeaSummary[] ideas =
        [
            new(new IdeaPlace("web", "a"), "Single sign-on", IdeaStage.Answering, TaskState.Open, "nigel", Now.AddDays(-3), 2),
            new(new IdeaPlace(null, "b"), "Dark mode", IdeaStage.Captured, TaskState.Open, "nigel", Now.AddDays(-1), 0),
            new(new IdeaPlace("web", "c"), "Export", IdeaStage.Proposed, TaskState.Open, "nigel", Now.AddDays(-9), 0),
        ];

        Apply(IdeaFields.All, query, ideas, idea => idea.Place.Id).Should().Equal(expected);
    }

    [Theory]
    [InlineData("", "pinned", "recent", "old")]
    [InlineData("where:elsewhere", "old")]
    [InlineData("agent:codex", "recent")]
    [InlineData("launches:>=5 sort:-launches", "old", "pinned")]
    public void Projects(string query, params string[] expected)
    {
        ProjectResolution[] projects =
        [
            Project("old", "claude", Now.AddDays(-30), launches: 9, local: false, pinned: false),
            Project("recent", "codex", Now.AddDays(-1), launches: 2, local: true, pinned: false),
            Project("pinned", "claude", Now.AddDays(-10), launches: 5, local: true, pinned: true),
        ];

        Apply(ProjectFields.All, query, projects, project => project.Entry.Slug).Should().Equal(expected);
    }

    [Theory]
    [InlineData("", "s2", "s1", "s3")]
    [InlineData("agent:codex", "s3")]
    [InlineData("login", "s1")]
    [InlineData("active:<2d", "s2", "s1")]
    public void Sessions(string query, params string[] expected)
    {
        AgentSession[] sessions =
        [
            new("claude", "s1", "Fix the login page", "D:/web", "main", Now.AddDays(-1), "t1", "web"),
            new("claude", "s2", "Release notes", "D:/web", "main", Now.AddHours(-1), "t2", "web"),
            new("codex", "s3", null, "D:/api", null, Now.AddDays(-5), "t3"),
        ];

        Apply(SessionFields.All, query, sessions, session => session.SessionId).Should().Equal(expected);
    }

    [Theory]
    [InlineData("", "r3", "r2", "r1")]
    [InlineData("state:waiting", "r3")]
    [InlineData("state:running", "r2")]
    [InlineData("-state:ended sort:started", "r2", "r3")]
    [InlineData("cost:>1 sort:-cost", "r1", "r3")]
    public void Runs(string query, params string[] expected)
    {
        RunSummary[] runs =
        [
            Run("r1", Now.AddDays(-3), finished: Now.AddDays(-2), cost: 4.5m),
            Run("r2", Now.AddDays(-2), finished: null, cost: 0.5m),
            Run("r3", Now.AddDays(-1), finished: null, cost: 2m, waiting: true),
        ];

        Apply(RunFields.All, query, runs, run => run.RunId).Should().Equal(expected);
    }

    private static ProjectResolution Project(string slug, string agent, DateTimeOffset launched, int launches, bool local, bool pinned) =>
        new(new ProjectRegistryEntry { Slug = slug, Name = slug, DefaultAgent = agent }, local ? $"D:/{slug}" : null, launched, launches, pinned);

    private static RunSummary Run(string id, DateTimeOffset started, DateTimeOffset? finished, decimal cost, bool waiting = false) =>
        new(id, $"D:/runs/{id}", "release-crew", $"goal of {id}", "supervised", started, finished, finished is null ? null : "done",
            cost, 2, [], [], [],
            Gates: waiting ? [new PendingAsk("g1", "lead", "lead", "gate", null, started)] : null);
}
