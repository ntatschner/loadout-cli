using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The harness itself, as distinct from the screens it drives.
/// </summary>
public sealed class TuiSessionTests
{
    private static ProjectResolution Project(string slug, string name) =>
        new(new ProjectRegistryEntry { Slug = slug, Name = name, DefaultAgent = "claude" }, "/repos/x", null, 0, false);

    private static TuiSession Launcher() =>
        TuiSession.Start(app => new LauncherWindow(
            [Project("alpha", "Alpha")],
            null,
            "workspace connected",
            ["claude"],
            (project, _) => Task.FromResult<ProjectOverview?>(
                new ProjectOverview(project, "main", true, 4096, 3, 2, 0, true, 0)),
            _ => { },
            [],
            app));

    /// <summary>
    /// Standing a screen up leaves the test's own synchronisation context in
    /// place. Terminal.Gui 2.5.0's Begin installs the toolkit's context on
    /// the calling thread and nothing short of Run or End restores it; a
    /// test that then awaited anything posted its continuation to a queue
    /// no loop was draining, and hung with nothing blocked to name it. This
    /// is the mechanism, asserted directly, so it fails every time rather
    /// than one run in five.
    /// </summary>
    [Fact]
    public void Starting_a_session_leaves_the_tests_synchronisation_context_alone()
    {
        var before = SynchronizationContext.Current;

        using var session = Launcher();

        SynchronizationContext.Current.Should().BeSameAs(before,
            "an await in a test must resume where xUnit expects, not on a toolkit queue nothing drains");
    }
}
