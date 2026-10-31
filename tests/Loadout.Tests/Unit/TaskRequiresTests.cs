using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The requires link: how one is named, when it is met, what a loop is, and
/// how a team queue holds work behind one. Done is the only thing that
/// releases a link; anything else, a target that went away included, holds.
/// </summary>
public sealed class TaskRequiresTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 1, 0, 0, TimeSpan.Zero);

    private static TaskItem Task(string id, TaskState state = TaskState.Open, params TaskLink[] requires) => new()
    {
        Id = id,
        Title = $"Do {id}",
        State = state,
        DoneWhen = ["the suite passes"],
        Requires = [.. requires],
    };

    private static TaskLink On(string project, string id) => new() { Project = project, Id = id };

    private static TaskRequires.Lists Lists(Dictionary<string, TaskItem[]> lists) =>
        key => lists.TryGetValue(key, out var list) ? list : null;

    [Theory]
    [InlineData("notes", "website", "website", "notes")]
    [InlineData("homelab/dns", "website", "homelab", "dns")]
    [InlineData("global/renew", "website", "", "renew")]
    [InlineData("-/renew", "website", "", "renew")]
    [InlineData("notes", null, "", "notes")]
    public void A_link_is_read_from_an_id_a_project_and_id_or_the_workspace_wide_list(
        string said, string? here, string project, string id)
    {
        var (link, refusal) = TaskRequires.Parse(said, here);

        refusal.Should().BeNull();
        (link!.Project, link.Id).Should().Be((project, id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b/c")]
    [InlineData("website/")]
    public void Anything_else_is_refused_and_says_how_to_name_one(string said) =>
        TaskRequires.Parse(said, "website").Refusal.Should().Contain("project/id");

    [Fact]
    public void A_link_is_named_by_id_alone_on_its_own_list_and_in_full_elsewhere()
    {
        TaskRequires.Name(On("website", "notes"), "website").Should().Be("notes");
        TaskRequires.Name(On("homelab", "dns"), "website").Should().Be("homelab/dns");
        TaskRequires.Name(On(string.Empty, "renew")).Should().Be("global/renew");
    }

    [Fact]
    public void Only_done_meets_a_link_and_every_other_end_says_why_it_holds()
    {
        var waiting = Task("ship", TaskState.Open,
            On("website", "done"), On("website", "open"), On("website", "dropped"), On("website", "gone"), On("left", "x"));
        var lists = Lists(new()
        {
            ["website"] = [Task("done", TaskState.Done), Task("open"), Task("dropped", TaskState.Dropped)],
        });

        TaskRequires.Unmet(waiting, lists).Select(one => (one.Link.Id, one.Why)).Should().Equal(
            ("open", "is open"),
            ("dropped", "is dropped"),
            ("gone", "is no longer on its list"),
            ("x", "left is not a project in this workspace any more"));
    }

    [Fact]
    public void Unmet_requirements_are_said_in_one_line_naming_each()
    {
        var unmet = new[] { new TaskUnmet(On("website", "ci"), "is open"), new TaskUnmet(On("homelab", "dns"), "is blocked") };

        TaskRequires.Said(unmet, "website").Should().Be("waits for ci, which is open; homelab/dns, which is blocked");
        TaskRequires.Said([]).Should().BeNull();
    }

    [Fact]
    public void A_loop_through_another_project_is_found_and_named()
    {
        // website/a requires homelab/b, which requires website/c; c requiring a closes it.
        var lists = Lists(new()
        {
            ["website"] = [Task("a", TaskState.Open, On("homelab", "b")), Task("c")],
            ["homelab"] = [Task("b", TaskState.Open, On("website", "c"))],
        });

        TaskRequires.Loop(On("website", "c"), [On("website", "a")], lists)
            .Should().Be("website/c requires website/a requires homelab/b requires website/c");
        TaskRequires.Loop(On("website", "x"), [On("website", "a")], lists).Should().BeNull();
    }

    [Fact]
    public void A_queue_passes_over_a_held_item_names_what_it_waits_for_and_starts_the_next()
    {
        var tasks = new[] { Task("notes", TaskState.Open, On("website", "ci")), Task("ci"), Task("docs") };
        var queue = new TeamQueue { Items = [Item("notes"), Item("docs")] };

        var decision = QueueRules.Next(queue, Website(tasks), window: null, Lists(new() { ["website"] = tasks }));

        decision.Item!.Task.Should().Be("docs");
        decision.Skipped.Single().Reason.Should().Be("'notes' waits for ci, which is open.");
    }

    [Fact]
    public void Once_what_it_requires_is_done_the_item_starts()
    {
        var tasks = new[] { Task("notes", TaskState.Open, On("website", "ci")), Task("ci", TaskState.Done) };
        var queue = new TeamQueue { Items = [Item("notes")] };

        QueueRules.Next(queue, Website(tasks), window: null, Lists(new() { ["website"] = tasks })).Item!.Task.Should().Be("notes");
    }

    [Fact]
    public void With_the_guard_off_nothing_is_held_for_a_requirement()
    {
        var tasks = new[] { Task("notes", TaskState.Open, On("website", "ci")), Task("ci") };
        var queue = new TeamQueue { Items = [Item("notes")] };

        QueueRules.Next(queue, Website(tasks), window: null, requires: null).Item!.Task.Should().Be("notes");
    }

    [Fact]
    public void A_plan_releases_what_waits_once_the_task_it_waits_for_has_run_earlier_in_the_window()
    {
        var tasks = new[] { Task("notes", TaskState.Open, On("website", "ci")), Task("ci") };
        var queue = new TeamQueue { Items = [Item("notes"), Item("ci")] };

        var plan = TeamQueuePlanCommand.Plan(queue, Website(tasks), Now, Lists(new() { ["website"] = tasks }));

        plan.Order.Select(one => one.Task).Should().Equal("ci", "notes");
    }

    private static QueueItem Item(string task) => new() { Task = task, Project = "website" };

    private static Func<string, IReadOnlyList<TaskItem>?> Website(TaskItem[] tasks) =>
        project => project == "website" ? tasks : null;
}
