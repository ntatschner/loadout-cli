using FluentAssertions;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Workspace;
using Loadout.Tui;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the end-of-session prompt tells somebody to run when nobody could
/// answer it.
/// </summary>
/// <remarks>
/// Unscoped, "loadout workspace save" commits and pushes every project, so
/// suggesting it after a session that saved only its own would hand another
/// session's unfinished work to the very command the session avoided.
/// </remarks>
public sealed class WorkspaceSavePromptTests
{
    [Fact]
    public async Task A_session_is_told_to_save_its_own_project()
    {
        var output = await HintAsync("starstats");

        output.Should().Contain("Save them with: loadout workspace save --project starstats");
    }

    [Fact]
    public async Task Without_a_project_the_hint_saves_everything()
    {
        var output = await HintAsync(null);

        output.Should().Contain("Save them with: loadout workspace save")
            .And.NotContain("--project");
    }

    private static async Task<string> HintAsync(string? projectSlug)
    {
        var console = new TestConsole();

        // The branch where nobody can answer never reaches the workspace, so
        // there is none; a call to it would throw and fail the test.
        var prompt = new WorkspaceSavePrompt(console, null!, ReadingProfile.None);

        var outcome = new LaunchOutcome(
            0,
            WorkspaceSyncOutcome.Synced,
            [],
            null,
            PendingWorkspaceChanges: ["projects/starstats/context/learned.md"],
            ProjectSlug: projectSlug);

        await prompt.HandleAsync(outcome, new GlobalSettings { NonInteractive = true });

        return console.Output;
    }
}
