using FluentAssertions;
using Loadout.Core.Lists;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The one query syntax every list takes, and what a kind's fields make of it.
/// </summary>
/// <remarks>
/// Studio, the TUI and the CLI all filter through these two types, so a
/// difference between surfaces can only come from here. The parser is tested
/// for the grammar alone, and the task registry for what the grammar means.
/// </remarks>
public sealed class ListQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    private static readonly TaskItem[] Tasks =
    [
        Task("b-open", TaskState.Open, "Fix the login page", daysAgo: 1),
        Task("a-doing", TaskState.Doing, "Ship the release", daysAgo: 3),
        Task("c-blocked", TaskState.Blocked, "Wait for access", daysAgo: 10),
        Task("d-done", TaskState.Done, "Login copy", daysAgo: 20),
        Task("e-dropped", TaskState.Dropped, "Old idea", daysAgo: 40, kind: TaskKind.Idea),
    ];

    [Fact]
    public void Nothing_written_is_the_empty_query()
    {
        ListQuery.Parse(null).Value!.IsEmpty.Should().BeTrue();
        ListQuery.Parse("   ").Value!.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Terms_words_and_sorts_are_told_apart()
    {
        var query = ListQuery.Parse("state:doing,open -project:x fix \"log in\" -old sort:-updated,id").Value!;

        query.Terms.Should().BeEquivalentTo(new[]
        {
            new ListTerm("state", ["doing", "open"], false),
            new ListTerm("project", ["x"], true),
        }, options => options.WithStrictOrdering());
        query.Words.Should().Equal(new ListWord("fix", false), new ListWord("log in", false), new ListWord("old", true));
        query.Sorts.Should().Equal(new ListSort("updated", true), new ListSort("id", false));
    }

    [Fact]
    public void A_quoted_colon_is_text_rather_than_a_field()
    {
        var query = ListQuery.Parse("\"http://example\"").Value!;

        query.Terms.Should().BeEmpty();
        query.Words.Should().Equal(new ListWord("http://example", false));
    }

    [Theory]
    [InlineData("state:doing,open -project:x fix \"log in\" sort:-updated")]
    [InlineData("-state:done,dropped")]
    [InlineData("-\"two words\" \"a:b\"")]
    public void A_query_written_out_again_reads_the_same(string text)
    {
        var once = ListQuery.Parse(text).Value!;
        var again = ListQuery.Parse(once.ToString()).Value!;

        again.ToString().Should().Be(once.ToString());
        again.Terms.Should().BeEquivalentTo(once.Terms, options => options.WithStrictOrdering());
        again.Words.Should().Equal(once.Words);
        again.Sorts.Should().Equal(once.Sorts);
    }

    [Theory]
    [InlineData(":doing", "has no field before its colon")]
    [InlineData("state:", "says no value")]
    [InlineData("-sort:id", "cannot be left out")]
    [InlineData("\"never closed", "never closed")]
    public void A_term_the_grammar_cannot_read_is_refused_with_why(string text, string said)
    {
        var parsed = ListQuery.Parse(text);

        parsed.Failed.Should().BeTrue();
        parsed.ExitCode.Should().Be(ExitCode.InvalidArguments);
        parsed.Error.Should().Contain(said);
    }

    [Fact]
    public void Removing_a_chip_removes_its_term_and_nothing_else()
    {
        var query = ListQuery.Parse("state:open project:x fix sort:id").Value!;

        query.Without(query.Terms[0]).ToString().Should().Be("project:x fix sort:id");
    }

    [Theory]
    [InlineData("colour:red", "'colour' is not something tasks can be filtered on. They can be: id, title, state")]
    [InlineData("sort:colour", "'colour' is not something tasks can be sorted on")]
    [InlineData("state:finished", "'finished' is not a state. It is one of: open, doing, blocked, done, dropped.")]
    [InlineData("updated:soon", "is not a date")]
    public void A_field_the_kind_does_not_have_is_refused_in_the_same_words_everywhere(string text, string said)
    {
        var applied = TaskFields.All.Apply(ListQuery.Parse(text).Value!, Tasks, Now);

        applied.Failed.Should().BeTrue();
        applied.ExitCode.Should().Be(ExitCode.InvalidArguments);
        applied.Error.Should().Contain(said);
    }

    [Theory]
    [InlineData("", "b-open", "a-doing", "c-blocked", "d-done", "e-dropped")]
    [InlineData(TaskFields.DefaultQuery, "b-open", "a-doing", "c-blocked")]
    [InlineData("state:doing,blocked", "a-doing", "c-blocked")]
    [InlineData("kind:idea", "e-dropped")]
    [InlineData("login", "b-open", "d-done")]
    [InlineData("login -copy", "b-open")]
    [InlineData("\"the release\"", "a-doing")]
    [InlineData("updated:<7d", "b-open", "a-doing")]
    [InlineData("updated:>7d", "c-blocked", "d-done", "e-dropped")]
    [InlineData("updated:>=2026-10-09", "b-open", "a-doing", "c-blocked")]
    [InlineData("updated:2026-10-16", "a-doing")]
    [InlineData("sort:-updated", "b-open", "a-doing", "c-blocked", "d-done", "e-dropped")]
    [InlineData("sort:updated", "e-dropped", "d-done", "c-blocked", "a-doing", "b-open")]
    [InlineData("sort:id", "a-doing", "b-open", "c-blocked", "d-done", "e-dropped")]
    [InlineData("sort:-state", "e-dropped", "d-done", "c-blocked", "a-doing", "b-open")]
    public void A_query_keeps_and_orders_tasks_as_it_says(string text, params string[] expected)
    {
        var applied = TaskFields.All.Apply(ListQuery.Parse(text).Value!, Tasks, Now);

        applied.Succeeded.Should().BeTrue(applied.Error);
        applied.Value!.Select(task => task.Id).Should().Equal(expected);
    }

    [Fact]
    public void Numbers_compare_as_numbers_and_a_missing_value_sorts_last_either_way()
    {
        var fields = new ListFields<(string Name, double? Runs)>(
            "rows",
            [
                ListField<(string Name, double? Runs)>.Text("name", "its name", row => row.Name),
                ListField<(string Name, double? Runs)>.Number("runs", "how often it ran", row => row.Runs),
            ],
            []);
        (string, double?)[] rows = [("two", 2), ("none", null), ("ten", 10), ("one", 1)];

        Names(fields.Apply(ListQuery.Parse("runs:>1").Value!, rows, Now).Value!).Should().Equal("two", "ten");
        Names(fields.Apply(ListQuery.Parse("runs:<=2,10").Value!, rows, Now).Value!).Should().Equal("two", "ten", "one");
        Names(fields.Apply(ListQuery.Parse("sort:runs").Value!, rows, Now).Value!).Should().Equal("one", "two", "ten", "none");
        Names(fields.Apply(ListQuery.Parse("sort:-runs").Value!, rows, Now).Value!).Should().Equal("ten", "two", "one", "none");
        fields.Apply(ListQuery.Parse("runs:many").Value!, rows, Now).Error.Should().Contain("is not a number");

        static IEnumerable<string> Names(IEnumerable<(string Name, double? Runs)> kept) => kept.Select(row => row.Name);
    }

    private static TaskItem Task(string id, TaskState state, string title, int daysAgo, TaskKind kind = TaskKind.Task) => new()
    {
        Id = id,
        State = state,
        Title = title,
        Kind = kind,
        DeclaredBy = "nigel",
        DeclaredUtc = Now.AddDays(-daysAgo),
    };
}
