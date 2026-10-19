using FluentAssertions;
using Loadout.Core.Lists;
using Loadout.Models;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Global search: one query over every kind, grouped, capped and best first,
/// with kind: deciding which kinds are read at all.
/// </summary>
public sealed class SearchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private sealed class Source(string kind, params SearchHit[] hits) : ISearchSource
    {
        public int Reads { get; private set; }

        public bool Broken { get; init; }

        public string Kind => kind;

        public Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
        {
            Reads++;

            return Task.FromResult(Broken
                ? OperationResult<IReadOnlyList<SearchHit>>.Fail("unreadable")
                : OperationResult<IReadOnlyList<SearchHit>>.Ok(hits));
        }
    }

    private static SearchHit Hit(string kind, string id, string title, int daysAgo, string? state = null, string? project = "web") =>
        new(kind, id, title, project, state, Now.AddDays(-daysAgo), $"open {id}");

    private readonly Source _tasks = new(
        "task",
        Hit("task", "t-copy", "Login copy", 1, "done"),
        Hit("task", "t-login", "Login page fix", 5, "open"),
        Hit("task", "t-other", "Release notes", 2, "open"));

    private readonly Source _ideas = new("idea", Hit("idea", "i-sso", "Single sign-on for login", 3, "captured"));

    private readonly Source _projects = new("project", Hit("project", "web", "Website", 0, "here"));

    private SearchService Service(params ISearchSource[] extra) =>
        new([_projects, _ideas, _tasks, .. extra], new FixedTime(Now));

    [Fact]
    public async Task Results_are_grouped_in_one_order_whatever_order_the_sources_came_in()
    {
        var found = (await Service().SearchAsync("", 10, default)).Value!;

        found.Select(group => group.Kind).Should().Equal("task", "idea", "project");
    }

    [Fact]
    public async Task A_title_that_starts_with_the_words_comes_first_then_one_holding_them()
    {
        var found = (await Service().SearchAsync("login", 10, default)).Value!;

        found.Single(group => group.Kind == "task").Hits.Select(hit => hit.Id).Should().Equal("t-copy", "t-login");
        found.Single(group => group.Kind == "idea").Hits.Single().Id.Should().Be("i-sso");
        found.Should().NotContain(group => group.Kind == "project");
    }

    [Fact]
    public async Task Kind_decides_which_kinds_are_read_at_all()
    {
        var found = (await Service().SearchAsync("kind:task login", 10, default)).Value!;

        found.Select(group => group.Kind).Should().Equal("task");
        _ideas.Reads.Should().Be(0, "a search narrowed to tasks has no reason to read the ideas");
        _projects.Reads.Should().Be(0);

        (await Service().SearchAsync("-kind:task,idea", 10, default)).Value!
            .Select(group => group.Kind).Should().Equal("project");
    }

    [Fact]
    public async Task Each_group_is_capped_and_says_how_many_more_there_were()
    {
        var group = (await Service().SearchAsync("kind:task", 2, default)).Value!.Single();

        group.Hits.Should().HaveCount(2);
        group.More.Should().Be(1);
    }

    [Fact]
    public async Task The_shared_terms_and_sorts_work_as_they_do_on_a_list()
    {
        var group = (await Service().SearchAsync("kind:task state:open sort:-updated", 10, default)).Value!.Single();

        group.Hits.Select(hit => hit.Id).Should().Equal("t-other", "t-login");
    }

    [Fact]
    public async Task A_field_search_does_not_have_is_refused_in_the_shared_words()
    {
        var found = await Service().SearchAsync("colour:red", 10, default);

        found.Failed.Should().BeTrue();
        found.ExitCode.Should().Be(ExitCode.InvalidArguments);
        found.Error.Should().Contain("'colour' is not something results can be filtered on");
    }

    [Fact]
    public async Task A_kind_that_cannot_be_read_is_left_out_and_the_rest_still_found()
    {
        var found = (await Service(new Source("session") { Broken = true }).SearchAsync("login", 10, default)).Value!;

        found.Select(group => group.Kind).Should().Equal("task", "idea");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
