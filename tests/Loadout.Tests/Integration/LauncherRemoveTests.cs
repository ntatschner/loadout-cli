using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Tui.Terminal;
using FluentAssertions;
using Terminal.Gui.Input;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Removing a project from the launcher. There was no way to: the command line
/// had one, the screen did not, and the command line's own default left the
/// project on the list.
/// </summary>
public sealed class LauncherRemoveTests
{
    private static ProjectResolution Project(string slug, string name) =>
        new(new ProjectRegistryEntry { Slug = slug, Name = name, DefaultAgent = "claude" }, "/repos/x", null, 0, false);

    private static TuiSession Launcher(
        IReadOnlyList<ProjectResolution> projects,
        Func<ProjectResolution, bool> removing,
        out Func<LauncherWindow> window)
    {
        LauncherWindow? built = null;

        var session = TuiSession.Start(app => built = new LauncherWindow(
            projects,
            null,
            "workspace connected",
            ["claude"],
            (project, _) => Task.FromResult<ProjectOverview?>(
                new ProjectOverview(project, "main", true, 4096, 3, 2, 0, true, 0)),
            _ => { },
            [],
            app,
            removing: removing));

        window = () => built!;

        return session;
    }

    [Fact]
    public void Delete_on_a_project_asks_and_then_removes_it_from_the_registry()
    {
        var asked = new List<string>();

        using var session = Launcher(
            [Project("alpha", "Alpha"), Project("beta", "Beta")],
            project =>
            {
                asked.Add(project.Entry.Slug);

                return true;
            },
            out var window);

        // An arrow from the filter moves into the list, onto the second row.
        session.Press(Key.CursorDown);
        session.Press(Key.Delete);

        asked.Should().ContainSingle("the question is put once")
            .Which.Should().Be("beta", "it names the project the cursor is on");

        window().Intent!.Action.Should().Be(LauncherAction.Command);

        // From the registry, because that is what the list is read from:
        // removing only this machine's record leaves the project on it.
        window().Intent!.CommandPath.Should().Be("project remove beta --from-workspace --non-interactive");
    }

    [Fact]
    public void Saying_no_removes_nothing()
    {
        using var session = Launcher([Project("alpha", "Alpha")], _ => false, out var window);

        session.Press(Key.CursorDown);
        session.Press(Key.Delete);

        window().Intent.Should().BeNull("a no is an answer, not a slower yes");
    }
}
