using FluentAssertions;
using Loadout.Core.Tasks;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Making and taking off requires links against real files: only between
/// tasks that exist, never in a loop, across projects, and never from a batch.
/// </summary>
public sealed class TaskRequiresServiceTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private static TaskLink On(string project, string id) => new() { Project = project, Id = id };

    private async Task<TaskItem> OneAsync(string project, string id) =>
        (await _space.Tasks.ListAsync(project)).Value!.Single(task => task.Id == id);

    private async Task SetUpAsync()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        await _space.Tasks.DeclareAsync("website", "notes", TaskState.Open, "nigel", "Write the release notes");
        await _space.Tasks.DeclareAsync("website", "ci", TaskState.Open, "nigel", "Add CI");
        await _space.Tasks.DeclareAsync("homelab", "dns", TaskState.Open, "nigel", "Fix DNS");
        await _space.Ideas.CaptureAsync("website", "A status page", "nigel", "status");
    }

    [Fact]
    public async Task A_link_is_kept_once_and_can_reach_another_project()
    {
        await SetUpAsync();

        await _space.Tasks.RequireAsync("website", "notes", [On("website", "ci"), On("homelab", "dns")], []);
        var again = await _space.Tasks.RequireAsync("website", "notes", [On("WEBSITE", "CI")], []);

        again.Succeeded.Should().BeTrue(again.Error);
        (await OneAsync("website", "notes")).Requires.Select(TaskRequires.Name).Should().Equal("website/ci", "homelab/dns");
    }

    [Fact]
    public async Task A_link_is_taken_off_and_linking_leaves_the_claim_alone()
    {
        await SetUpAsync();
        await _space.Tasks.RequireAsync("website", "notes", [On("website", "ci")], []);

        await _space.Tasks.RequireAsync("website", "notes", [], [On("website", "ci")]);

        var notes = await OneAsync("website", "notes");
        notes.Requires.Should().BeEmpty();
        notes.History.Should().ContainSingle("a link is not a claim about where the work stands");
    }

    [Theory]
    [InlineData("website", "notes", "can't require itself")]
    [InlineData("website", "nothing", "There is no task website/nothing")]
    [InlineData("website", "status", "is an idea")]
    [InlineData("elsewhere", "x", "is not a project in this workspace")]
    public async Task A_link_to_anything_but_another_task_is_refused(string project, string id, string reason)
    {
        await SetUpAsync();

        var refused = await _space.Tasks.RequireAsync("website", "notes", [On(project, id)], []);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain(reason);
        (await OneAsync("website", "notes")).Requires.Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_that_would_close_a_loop_across_projects_is_refused_and_the_loop_named()
    {
        await SetUpAsync();
        await _space.Tasks.RequireAsync("website", "notes", [On("homelab", "dns")], []);
        await _space.Tasks.RequireAsync("homelab", "dns", [On("website", "ci")], []);

        var refused = await _space.Tasks.RequireAsync("website", "ci", [On("website", "notes")], []);

        refused.Failed.Should().BeTrue();
        refused.Error.Should().Contain("website/ci requires website/notes requires homelab/dns requires website/ci");
    }

    [Fact]
    public async Task A_batch_cannot_make_a_link()
    {
        await SetUpAsync();

        var done = await _space.Tasks.ApplyAsync(
            [new TaskOperation(TaskOperation.Require, "website", "notes", Requires: [On("website", "ci")])]);

        done.Value!.Outcomes.Single().Applied.Should().BeFalse("a batch would get round the loop check");
    }
}
