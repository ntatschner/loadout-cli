using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The tool a session moves a misplaced task or idea with, against real files:
/// it moves from the session's own project, and 'global' means the
/// workspace-wide list rather than a project of that name.
/// </summary>
public sealed class TaskMoveToolTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    /// <summary>Only what the move touches is real; the rest is not reached.</summary>
    private LoadoutTools Tools(string slug) =>
        new(
            instructions: null!,
            memory: null!,
            _space.Workspace,
            new FakeProjects(slug, _space.Workspace.LocalPath),
            _space.Tasks,
            git: null!,
            symbols: null!,
            runs: null!,
            TimeProvider.System,
            new LoadoutToolScope(slug),
            catalogue: null!,
            proposals: null!,
            ideas: _space.Ideas);

    [Fact]
    public async Task It_moves_from_the_session_s_project_to_the_one_named()
    {
        await _space.ProjectAsync("website");
        await _space.ProjectAsync("homelab");
        await _space.Ideas.CaptureAsync("website", "Status page", "me", "status-page");

        var said = await Tools("website").MoveTaskAsync("status-page", "homelab");

        said.Should().Contain("Moved status-page from website to homelab");
        (await _space.Tasks.ListAsync("homelab")).Value!.Single().Kind.Should().Be(TaskKind.Idea);
        (await _space.Tasks.ListAsync("website")).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Global_is_the_workspace_wide_list()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "fix-login", TaskState.Open, "me", "Fix the login");

        var said = await Tools("website").MoveTaskAsync("fix-login", "global");

        said.Should().Contain("the workspace-wide list");
        (await _space.Tasks.ListAsync(null)).Value!.Single().Id.Should().Be("fix-login");
    }

    [Fact]
    public async Task A_project_that_is_not_there_is_said_and_nothing_moves()
    {
        await _space.ProjectAsync("website");
        await _space.Tasks.DeclareAsync("website", "fix-login", TaskState.Open, "me", "Fix the login");

        var said = await Tools("website").MoveTaskAsync("fix-login", "nowhere");

        said.Should().Contain("not a project");
        (await _space.Tasks.ListAsync("website")).Value!.Should().ContainSingle();
    }
}
