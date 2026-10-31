using FluentAssertions;
using Loadout.Models;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the launcher does once the About window closes: an update replaced
/// the binary this process runs from, so the session ends rather than going
/// round again.
/// </summary>
public sealed class AboutLauncherOutcomeTests
{
    [Fact]
    public void After_an_update_the_session_ends()
    {
        TerminalLauncher.AfterAbout(updated: true).Should().Be((int)ExitCode.Success);
    }

    [Fact]
    public void With_no_update_the_launcher_comes_back()
    {
        TerminalLauncher.AfterAbout(updated: false).Should().BeNull();
    }
}
