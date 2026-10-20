using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Lists;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The pieces a task list across every project is made of: pages cut after
/// the query, a project field, the detail searched, and rows that fit the
/// terminal they are printed on.
/// </summary>
public sealed class TaskListPagingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(string id, string title = "Something", TaskState state = TaskState.Open) =>
        new() { Id = id, Title = title, State = state, DeclaredUtc = Now };

    [Fact]
    public void Without_a_limit_the_whole_list_is_one_page()
    {
        var page = ListPages.Of([1, 2, 3], 1, null).Value!;

        page.Items.Should().Equal(1, 2, 3);
        (page.Total, page.Page, page.Pages, page.More).Should().Be((3, 1, 1, false));
    }

    [Fact]
    public void A_page_holds_its_limit_and_says_whether_there_is_more()
    {
        var items = Enumerable.Range(1, 7).ToList();

        ListPages.Of(items, 1, 3).Value!.Items.Should().Equal(1, 2, 3);
        ListPages.Of(items, 2, 3).Value!.More.Should().BeTrue();

        var last = ListPages.Of(items, 3, 3).Value!;
        last.Items.Should().Equal(7);
        (last.Total, last.Pages, last.More).Should().Be((7, 3, false));
    }

    [Fact]
    public void An_empty_list_still_has_a_first_page()
    {
        var page = ListPages.Of(Array.Empty<int>(), 1, 10).Value!;

        page.Items.Should().BeEmpty();
        page.Pages.Should().Be(1);
    }

    [Theory]
    [InlineData(4, 3, "there is no page 4")]
    [InlineData(0, 3, "counts from 1")]
    [InlineData(1, 0, "1 or more")]
    [InlineData(2, null, "needs --limit")]
    public void A_page_that_cannot_be_given_is_refused_with_the_reason(int page, int? limit, string reason)
    {
        var refused = ListPages.Of(Enumerable.Range(1, 7).ToList(), page, limit);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain(reason);
    }

    [Fact]
    public void The_project_field_reads_each_task_s_own_list()
    {
        var a = Task("same-id", "On the website");
        var b = Task("same-id", "On the home lab");
        var where = new Dictionary<TaskItem, string?>(ReferenceEqualityComparer.Instance) { [a] = "website", [b] = null };
        var fields = TaskFields.For(_ => 0, _ => 0, item => where[item]);

        Keep(fields, "project:website", a, b).Should().Equal(a);
        Keep(fields, "project:global", a, b).Should().Equal(b);
    }

    [Fact]
    public void Counts_are_read_through_the_task_so_a_shared_id_is_not_counted_twice()
    {
        var a = Task("same-id");
        var b = Task("same-id");
        var fields = TaskFields.For(item => ReferenceEquals(item, a) ? 3 : 0, _ => 0, _ => null);

        Keep(fields, "runs:>0", a, b).Should().Equal(a);
    }

    [Fact]
    public void A_word_finds_a_task_by_its_detail()
    {
        var found = Task("scan");
        found.Detail = "Start with TaskBoard.svelte.";
        var fields = TaskFields.For(TaskActivity.None, "website");

        Keep(fields, "taskboard", found, Task("other")).Should().Equal(found);
    }

    [Fact]
    public void One_list_reads_its_own_project_and_not_the_workspace_wide_one()
    {
        var fields = TaskFields.For(TaskActivity.None, "website");

        Keep(fields, "project:website", Task("a")).Should().HaveCount(1);
        Keep(fields, "project:global", Task("a")).Should().BeEmpty();
    }

    [Fact]
    public void Every_list_sorts_by_project_within_each_state()
    {
        var a = Task("a");
        var b = Task("b");
        var where = new Dictionary<TaskItem, string?>(ReferenceEqualityComparer.Instance) { [a] = "website", [b] = "homelab" };
        var fields = TaskFields.For(_ => 0, _ => 0, item => where[item]);

        Keep(fields, string.Empty, a, b).Should().Equal(b, a);
    }

    [Fact]
    public void On_a_terminal_a_title_is_cut_to_fit_and_columns_line_up()
    {
        var rows = new TaskRows(70, null);
        var long_ = Task("scan", "Make idea and task lists quick to scan in Studio and the command line");

        var open = Plain(rows.Line(long_, null));
        var doing = Plain(rows.Line(Task("x", "Short", TaskState.Doing), null));

        open.Length.Should().Be(70);
        open.Should().EndWith("…");
        open.IndexOf("Make", StringComparison.Ordinal).Should().Be(doing.IndexOf("Short", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_terminal_the_title_is_left_whole()
    {
        var title = "Make idea and task lists quick to scan in Studio and the command line";

        Plain(new TaskRows(null, null).Line(Task("scan", title), null)).Should().EndWith(title);
    }

    [Fact]
    public void Rows_from_every_list_name_their_project()
    {
        var rows = new TaskRows(120, ["website", null]);

        Plain(rows.Line(Task("a"), "website")).Should().Contain(" website ");
        Plain(rows.Line(Task("b"), null)).Should().Contain(" global ");
    }

    private static List<TaskItem> Keep(ListFields<TaskItem> fields, string query, params TaskItem[] items) =>
        [.. fields.Apply(ListQuery.Parse(query).Value!, items, Now).Value!];

    private static string Plain(string markup) => Spectre.Console.Markup.Remove(markup);
}
