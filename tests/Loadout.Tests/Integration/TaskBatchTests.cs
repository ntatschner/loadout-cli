using System.Reflection;
using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Several changes to task lists at once, against real files: each list all
/// or nothing, a move tying its two lists together, revisions, and the lists
/// kept parsed between reads.
/// </summary>
public sealed class TaskBatchTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private string ListPath(string? slug) => TaskPaths.List(_space.Workspace.LocalPath, slug);

    private async Task<TaskBatch> ApplyAsync(params TaskOperation[] operations) =>
        (await _space.Tasks.ApplyAsync(operations)).Value!;

    [Fact]
    public async Task Operations_on_one_list_are_applied_in_order_and_each_says_what_it_did()
    {
        await _space.ProjectAsync("website");

        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Add, "website", Title: "Fix the login page", By: "nigel"),
            new TaskOperation(TaskOperation.Declare, "website", "fix-the-login-page", TaskState.Doing, By: "nigel"),
            new TaskOperation(TaskOperation.Add, "website", "docs", Title: "Write the docs", Note: "the install page", By: "nigel")));

        batch.Applied.Should().BeTrue();
        batch.Outcomes.Select(one => one.Id).Should().Equal("fix-the-login-page", "fix-the-login-page", "docs");
        batch.Outcomes[1].Task!.State.Should().Be(TaskState.Doing);
        batch.Outcomes[2].Task!.Note.Should().Be("the install page");
        batch.Lists.Should().ContainSingle().Which.Should().Match<TaskListRevision>(one => one.Project == "website" && one.Written);

        var read = (await _space.Tasks.ReadAsync("website")).Value!;
        read.Items.Select(one => (one.Id, one.State)).Should().Equal(("fix-the-login-page", TaskState.Doing), ("docs", TaskState.Open));
        read.Revision.Should().Be(batch.Lists[0].Revision);
    }

    [Fact]
    public async Task One_refused_operation_leaves_its_list_exactly_as_it_was_and_says_why_for_the_rest()
    {
        await _space.ProjectAsync("website");
        (await _space.Tasks.DeclareAsync("website", "existing", TaskState.Open, "nigel", "Already here")).Succeeded.Should().BeTrue();
        var before = await File.ReadAllBytesAsync(ListPath("website"));

        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Declare, "website", "existing", TaskState.Done, By: "nigel"),
            new TaskOperation(TaskOperation.Add, "website", "new-one", Title: "Something new", By: "nigel"),
            new TaskOperation(TaskOperation.Remove, "website", "not-there"),
            new TaskOperation(TaskOperation.Declare, "website", "another", TaskState.Open, Title: "Another", By: "nigel")));

        batch.Applied.Should().BeFalse();
        batch.Outcomes.Should().OnlyContain(one => !one.Applied);
        batch.Outcomes[2].Error.Should().Contain("'not-there' is not a task of website");
        batch.Outcomes[0].Error.Should().Contain("operation 2").And.Contain("all or nothing");
        batch.Outcomes[3].Code.Should().Be(ExitCode.InvalidArguments);
        batch.Lists.Should().ContainSingle().Which.Written.Should().BeFalse();

        (await File.ReadAllBytesAsync(ListPath("website"))).Should().Equal(before);
    }

    [Fact]
    public async Task Lists_with_nothing_in_common_stand_or_fall_on_their_own()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");

        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Declare, "website", "a", TaskState.Open, Title: "A", By: "nigel"),
            new TaskOperation(TaskOperation.Remove, "homelab", "missing"),
            new TaskOperation(TaskOperation.Declare, null, "b", TaskState.Open, Title: "B", By: "nigel")));

        batch.Outcomes.Select(one => one.Applied).Should().Equal(true, false, true);
        File.Exists(ListPath("homelab")).Should().BeFalse("a list whose only change was refused is not written");
        (await _space.Tasks.ListAsync("website")).Value!.Should().ContainSingle(one => one.Id == "a");
        (await _space.Tasks.ListAsync(null)).Value!.Should().ContainSingle(one => one.Id == "b");
    }

    [Fact]
    public async Task A_move_reports_both_lists_and_records_where_the_task_came_from()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        (await _space.Tasks.DeclareAsync("website", "dns", TaskState.Open, "nigel", "Sort the DNS")).Succeeded.Should().BeTrue();

        var batch = (await ApplyAsync(new TaskOperation(TaskOperation.Move, "website", "dns", To: "homelab")));

        batch.Applied.Should().BeTrue();
        batch.Outcomes[0].To.Should().Be("homelab");
        batch.Outcomes[0].Task!.Moved!.From.Should().Be("website");
        batch.Lists.Select(one => (one.Project, one.Written)).Should().BeEquivalentTo([("website", true), ("homelab", true)]);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
        (await _space.Tasks.ListAsync("homelab")).Value!.Should().ContainSingle(one => one.Id == "dns");
    }

    [Fact]
    public async Task A_refusal_on_either_list_of_a_move_holds_back_the_other_list_too()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        (await _space.Tasks.DeclareAsync("website", "dns", TaskState.Open, "nigel", "Sort the DNS")).Succeeded.Should().BeTrue();
        (await _space.Tasks.DeclareAsync("website", "logo", TaskState.Open, "nigel", "New logo")).Succeeded.Should().BeTrue();
        var website = await File.ReadAllBytesAsync(ListPath("website"));

        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Declare, "website", "logo", TaskState.Done, By: "nigel"),
            new TaskOperation(TaskOperation.Move, "website", "dns", To: "homelab"),
            new TaskOperation(TaskOperation.Remove, "homelab", "nothing-here")));

        batch.Outcomes.Should().OnlyContain(one => !one.Applied);
        batch.Outcomes[0].Error.Should().Contain("operation 2");
        (await File.ReadAllBytesAsync(ListPath("website"))).Should().Equal(website);
        File.Exists(ListPath("homelab")).Should().BeFalse();
    }

    [Fact]
    public async Task A_batch_will_not_move_a_task_whose_plan_would_be_left_behind()
    {
        await _space.ProjectAsync("homelab");
        (await _space.Tasks.DeclareAsync(null, "planned", TaskState.Open, "nigel", "Has a plan")).Succeeded.Should().BeTrue();
        var plans = TaskPaths.Ideas(_space.Workspace.LocalPath, null);
        Directory.CreateDirectory(plans);
        await File.WriteAllTextAsync(Path.Combine(plans, "planned.md"), "# The plan");

        var batch = await ApplyAsync(new TaskOperation(TaskOperation.Move, null, "planned", To: "homelab"));

        batch.Outcomes[0].Error.Should().Contain("loadout task move");
        (await _space.Tasks.ListAsync(null)).Value!.Should().ContainSingle(one => one.Id == "planned");
    }

    [Fact]
    public async Task A_move_onto_a_list_that_already_has_the_id_moves_nothing()
    {
        await _space.ProjectAsync("homelab");
        (await _space.Tasks.DeclareAsync(null, "same", TaskState.Open, "nigel", "Here")).Succeeded.Should().BeTrue();
        (await _space.Tasks.DeclareAsync("homelab", "same", TaskState.Open, "nigel", "There")).Succeeded.Should().BeTrue();

        var batch = (await ApplyAsync(new TaskOperation(TaskOperation.Move, null, "same", To: "homelab")));

        batch.Outcomes[0].Error.Should().Contain("already has a task called 'same'");
        (await _space.Tasks.ListAsync(null)).Value!.Should().ContainSingle(one => one.Title == "Here");
    }

    [Fact]
    public async Task A_change_made_from_an_older_revision_is_refused_and_the_list_is_not_touched()
    {
        await _space.ProjectAsync("website");
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "A")).Succeeded.Should().BeTrue();
        var old = (await _space.Tasks.ReadAsync("website")).Value!.Revision!;
        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Doing, "somebody else")).Succeeded.Should().BeTrue();
        var before = await File.ReadAllBytesAsync(ListPath("website"));

        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Declare, "website", "a", TaskState.Done, By: "nigel", Revision: old)));

        batch.Outcomes[0].Code.Should().Be(ExitCode.ChangedSinceRead);
        batch.Outcomes[0].Error.Should().Contain(old);
        (await File.ReadAllBytesAsync(ListPath("website"))).Should().Equal(before);

        var current = (await _space.Tasks.ReadAsync("website")).Value!.Revision!;
        (await ApplyAsync(new TaskOperation(TaskOperation.Declare, "website", "a", TaskState.Done, By: "nigel", Revision: current)))
            .Applied.Should().BeTrue();
    }

    [Fact]
    public async Task A_revision_is_the_text_itself_so_another_reader_of_the_same_file_agrees()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Open, "nigel", "A")).Succeeded.Should().BeTrue();

        var fresh = new TaskService(_space.Workspace, _space.Yaml, TimeProvider.System);

        (await fresh.ReadAsync(null)).Value!.Revision.Should()
            .Be((await _space.Tasks.ReadAsync(null)).Value!.Revision)
            .And.Be(YamlStore.Revision(await File.ReadAllTextAsync(ListPath(null))));
    }

    [Fact]
    public async Task A_list_changed_by_something_else_is_read_again_even_when_its_length_is_the_same()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Open, "nigel", "AAAA")).Succeeded.Should().BeTrue();
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("AAAA");

        // Another process, say, with the same length and the same stamp, as
        // two writes inside one tick of a coarse clock have.
        var stamp = File.GetLastWriteTimeUtc(ListPath(null));
        var text = await File.ReadAllTextAsync(ListPath(null));
        await File.WriteAllTextAsync(ListPath(null), text.Replace("AAAA", "BBBB", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(ListPath(null), stamp);

        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("BBBB");
    }

    [Fact]
    public async Task A_settled_list_is_kept_and_a_changed_one_read_again()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Open, "nigel", "First")).Succeeded.Should().BeTrue();
        File.SetLastWriteTimeUtc(ListPath(null), DateTime.UtcNow.AddMinutes(-5));
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("First");

        // Changed with the stamp held where it was: a kept list is used.
        var text = await File.ReadAllTextAsync(ListPath(null));
        var stamp = File.GetLastWriteTimeUtc(ListPath(null));
        await File.WriteAllTextAsync(ListPath(null), text.Replace("First", "Fir5t", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(ListPath(null), stamp);
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("First", "nothing about the file says it changed");

        // And with the stamp moved, it is read again.
        File.SetLastWriteTimeUtc(ListPath(null), stamp.AddSeconds(1));
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("Fir5t");
    }

    [Fact]
    public async Task Changing_what_a_read_returned_changes_nothing_kept()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Open, "nigel", "Kept")).Succeeded.Should().BeTrue();
        File.SetLastWriteTimeUtc(ListPath(null), DateTime.UtcNow.AddMinutes(-5));

        // The first read is what is kept; the second is from what was kept.
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title = "Changed by a caller";
        (await _space.Tasks.ListAsync(null)).Value!.Single().Title = "Changed by a caller";

        (await _space.Tasks.ListAsync(null)).Value!.Single().Title.Should().Be("Kept");
    }

    [Fact]
    public void A_copy_of_a_task_carries_every_property_it_has()
    {
        var item = new TaskItem();

        foreach (var property in typeof(TaskItem).GetProperties())
        {
            property.SetValue(item, property.PropertyType switch
            {
                var type when type == typeof(string) => "set-" + property.Name,
                var type when type == typeof(TaskState) => TaskState.Blocked,
                var type when type == typeof(TaskKind) => TaskKind.Idea,
                var type when type == typeof(TaskType?) => TaskType.Docs,
                var type when type == typeof(TaskTypeSource?) => TaskTypeSource.Agent,
                var type when type == typeof(DateTimeOffset) => DateTimeOffset.UnixEpoch.AddDays(3),
                var type when type == typeof(TaskMove) => new TaskMove { From = "elsewhere", Utc = DateTimeOffset.UnixEpoch },
                var type when type == typeof(List<string>) => new List<string> { "set-" + property.Name },
                var type when type == typeof(List<TaskEvent>) => new List<TaskEvent> { new() { State = TaskState.Doing, By = "someone", Utc = DateTimeOffset.UnixEpoch, Reason = "why" } },
                var type when type == typeof(TaskProposal) => new TaskProposal { DoneWhen = ["it works"], Why = "why", Utc = DateTimeOffset.UnixEpoch, Error = "it failed" },
                var type => throw new InvalidOperationException($"TaskItem.{property.Name} is a {type.Name}, which this test does not know how to set."),
            });
        }

        var copy = TaskService.Copy(item);

        copy.Should().BeEquivalentTo(item);
        copy.Moved.Should().NotBeSameAs(item.Moved);
        copy.History.Should().NotBeSameAs(item.History);
        copy.History[0].Should().NotBeSameAs(item.History[0]);
        copy.DoneWhen.Should().NotBeSameAs(item.DoneWhen);
        copy.Proposal.Should().NotBeSameAs(item.Proposal);
        copy.Proposal!.DoneWhen.Should().NotBeSameAs(item.Proposal!.DoneWhen);
    }

    [Theory]
    [InlineData(nameof(ITaskService.ApplyAsync))]
    [InlineData(nameof(ITaskService.HoldProposalAsync))]
    [InlineData(nameof(ITaskService.ClassifyAsync))]
    public void The_service_has_its_own_rather_than_the_default_for_the_doubles(string method)
    {
        var map = typeof(TaskService).GetInterfaceMap(typeof(ITaskService));
        var declared = typeof(ITaskService).GetMethod(method)!;

        map.TargetMethods[Array.IndexOf(map.InterfaceMethods, declared)].DeclaringType.Should().Be(typeof(TaskService));
    }

    [Fact]
    public async Task A_batch_names_only_projects_the_workspace_has_and_no_path_out_of_it()
    {
        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Declare, "nowhere", "a", TaskState.Open, Title: "A", By: "nigel"),
            new TaskOperation(TaskOperation.Declare, "../escape", "b", TaskState.Open, Title: "B", By: "nigel"),
            new TaskOperation(TaskOperation.Move, null, "c", To: "../escape")));

        batch.Outcomes[0].Code.Should().Be(ExitCode.ProjectNotFound);
        batch.Outcomes[1].Code.Should().Be(ExitCode.InvalidArguments);
        batch.Outcomes[2].Code.Should().Be(ExitCode.InvalidArguments);
        batch.Lists.Select(one => one.Project).Should().Equal(["nowhere"], "a name that cannot be a list names no file to read");
        Directory.Exists(Path.Combine(_space.Workspace.LocalPath, "projects", "nowhere")).Should().BeFalse();
        File.Exists(Path.Combine(_space.Workspace.LocalPath, "escape", "tasks.yaml")).Should().BeFalse();
    }

    [Fact]
    public async Task A_credential_in_a_batch_is_refused_by_pattern_and_the_list_is_not_written()
    {
        var batch = (await ApplyAsync(
            new TaskOperation(TaskOperation.Add, null, Title: "Rotate the key", By: "nigel"),
            new TaskOperation(TaskOperation.Declare, null, "leak", TaskState.Open, Title: "token ghp_" + new string('a', 36), By: "nigel")));

        batch.Outcomes[1].Code.Should().Be(ExitCode.PolicyViolation);
        batch.Outcomes[1].Error.Should().NotContain("ghp_aaaa");
        batch.Outcomes[0].Applied.Should().BeFalse();
        File.Exists(ListPath(null)).Should().BeFalse();
    }

    [Fact]
    public async Task A_stamp_moves_when_a_list_changes_and_names_the_list()
    {
        await _space.ProjectAsync("website");
        var before = await _space.Tasks.StampsAsync();

        (await _space.Tasks.DeclareAsync("website", "a", TaskState.Open, "nigel", "A")).Succeeded.Should().BeTrue();
        var after = await _space.Tasks.StampsAsync();

        before.Select(one => one.Project).Should().Equal(null, "website");
        after.Single(one => one.Project == "website").Stamp.Should().NotBe(before.Single(one => one.Project == "website").Stamp);
        after.Single(one => one.Project is null).Should().Be(before.Single(one => one.Project is null));
    }

    [Fact]
    public async Task A_recent_change_moves_the_stamp_even_when_the_time_and_length_do_not()
    {
        (await _space.Tasks.DeclareAsync(null, "a", TaskState.Open, "nigel", "AAAA")).Succeeded.Should().BeTrue();
        var stamp = File.GetLastWriteTimeUtc(ListPath(null));
        var before = (await _space.Tasks.StampsAsync()).Single(one => one.Project is null).Stamp;

        var text = await File.ReadAllTextAsync(ListPath(null));
        await File.WriteAllTextAsync(ListPath(null), text.Replace("AAAA", "BBBB", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(ListPath(null), stamp);

        (await _space.Tasks.StampsAsync()).Single(one => one.Project is null).Stamp.Should().NotBe(before);
    }

    [Fact]
    public void A_batch_is_read_from_the_json_every_surface_takes()
    {
        var read = TaskOperations.Parse(
            """[{"op":"declare","id":"a","state":"Done"},{"op":"move","id":"b","project":"-","to":"homelab"},{"op":"add","title":"T","by":"agent"}]""",
            "website",
            "studio");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Should().Equal(
            new TaskOperation(TaskOperation.Declare, "website", "a", TaskState.Done, By: "studio"),
            new TaskOperation(TaskOperation.Move, null, "b", By: "studio", To: "homelab"),
            new TaskOperation(TaskOperation.Add, "website", Title: "T", By: "agent"));
    }

    [Theory]
    [InlineData("""{"op":"add"}""", "JSON array")]
    [InlineData("""[{"op":"rename","id":"a"}]""", "op 'rename'")]
    [InlineData("""[{"op":"declare","id":"a"}]""", "declares no state")]
    [InlineData("""[{"op":"declare","id":"a","state":"finished"}]""", "state 'finished'")]
    [InlineData("""[{"op":"declare","id":"a","state":"1"}]""", "state '1'")]
    [InlineData("""[{"op":"move","id":"a"}]""", "without saying where to")]
    [InlineData("""[{"op":"declare","id":"a","state":"open","colour":"red"}]""", "'colour'")]
    [InlineData("""[{"op":"declare","id":"a","state":"open","title":3}]""", "'title' as something other than text")]
    [InlineData("""not json""", "not JSON")]
    public void A_request_that_is_not_a_batch_is_refused_with_what_is_wrong(string json, string said)
    {
        var read = TaskOperations.Parse(json, null, "studio");

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(said);
    }
}
