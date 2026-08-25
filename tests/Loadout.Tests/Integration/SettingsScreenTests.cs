using System.Drawing;
using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Models.Configuration;
using Loadout.Tui.Terminal;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The settings screen, section by section.
/// </summary>
/// <remarks>
/// The screen named six of the twenty-one settings by hand. The other fifteen —
/// which terminal opens, where clones land, which directories are scanned, the
/// secrets backend, the update feed, every part of the agent status line —
/// could only be reached by typing <c>loadout config set</c>, and nothing on
/// the screen for changing settings said they existed. Nothing caught it,
/// because a hand-written list agrees with itself.
/// </remarks>
public sealed class SettingsScreenTests
{
    private const int Width = 118;
    private const int Height = 32;

    private static SettingsWindow Build(IApplication app, IReadOnlyList<string> agents)
    {
        var config = new LauncherConfig { DefaultAgent = "claude" };

        config.Workspace.Remote = "https://example.com/me/workspace.git";

        var machine = new MachineConfig
        {
            DefaultCloneRoot = "/repos",
            DiscoveryRoots = ["/repos", "/work"],
        };

        return new SettingsWindow(
            config,
            machine,
            [("Shared settings", "/config/config.yaml"), ("This machine", "/state/machines.yaml")],
            agents,
            "code",
            ["Agents"],
            app);
    }

    [Fact]
    public void Every_section_draws_the_settings_it_is_responsible_for()
    {
        var unseen = ConfigKeys.All
            .Select(entry => entry.Key)
            .Where(key => key != "editor-profiles")
            .ToHashSet(StringComparer.Ordinal);

        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        ConsoleGlyphs.MakeLegible();
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = Build(app, ["claude"]);

        app.Begin(settings);

        for (var section = 0; section < settings.Sections.Count; section++)
        {
            settings.Open(section);

            app.LayoutAndDraw();

            var screen = app.Driver?.ToString() ?? string.Empty;

            foreach (var key in unseen.ToList())
            {
                if (screen.Contains(key, StringComparison.Ordinal))
                {
                    unseen.Remove(key);
                }
            }
        }

        // Reachable is the claim, not merely constructed: a page that is built
        // and never drawn is a setting nobody can change.
        unseen.Should().BeEmpty("every setting must be reachable by opening a section");
    }

    [Fact]
    public void A_yes_or_no_setting_is_a_tick_rather_than_a_box_to_type_true_into()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        ConsoleGlyphs.MakeLegible();
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = Build(app, ["claude"]);

        app.Begin(settings);

        var statusline = settings.Sections
            .Select((name, index) => (name, index))
            .First(s => s.name == ConfigKeys.Groups.Statusline);

        settings.Open(statusline.index);

        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        screen.Should().Contain("statusline-git");
        screen.Should().NotContain("true", "a setting whose whole vocabulary is yes and no is a tick");
    }

    [Fact]
    public void The_section_list_names_every_section_at_once()
    {
        using IApplication app = Application.Create();

        app.Init(DriverRegistry.Names.ANSI);
        ConsoleGlyphs.MakeLegible();
        app.Screen = new Rectangle(0, 0, Width, Height);

        using var settings = Build(app, ["claude"]);

        app.Begin(settings);
        app.LayoutAndDraw();

        var screen = app.Driver?.ToString() ?? string.Empty;

        // What is not on the open page must still be visibly there, or the
        // screen has only moved the problem rather than fixed it.
        foreach (var section in settings.Sections)
        {
            screen.Should().Contain(section);
        }
    }
}
