using FluentAssertions;
using Loadout.Cli;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What running <c>loadout</c> with no arguments decides to do.
/// </summary>
/// <remarks>
/// This had no tests and could not have had any. Every branch either wrote to
/// the console, ran a setup wizard or put a full-screen application up, and the
/// only branch a test could reach was the one that returns immediately when
/// output is redirected — which is the state a test always runs in. So the
/// launcher shipped opening with an empty command list, and nothing on this
/// path could have noticed.
/// </remarks>
public sealed class LauncherEntryTests
{
    [Fact]
    public void A_redirected_run_gets_no_screen()
    {
        // A pipe, a script or a CI job. Spec section 37 forbids a menu here,
        // and hanging on a prompt nobody can answer is the failure being
        // avoided.
        var entry = Program.PrepareInteractive(
            interactive: false,
            configured: () => true);

        entry.Should().Be(Program.LauncherEntry.NoTerminal);
    }

    [Fact]
    public void A_machine_that_has_never_been_configured_gets_the_wizard()
    {
        var entry = Program.PrepareInteractive(
            interactive: true,
            configured: () => false);

        entry.Should().Be(Program.LauncherEntry.Setup);
    }

    [Fact]
    public void A_configured_machine_gets_the_launcher()
    {
        var entry = Program.PrepareInteractive(
            interactive: true,
            configured: () => true);

        entry.Should().Be(Program.LauncherEntry.Launcher);
    }

    [Fact]
    public void A_redirected_run_is_not_asked_whether_the_machine_is_configured()
    {
        var asked = false;

        Program.PrepareInteractive(
            interactive: false,
            configured: () =>
            {
                asked = true;
                return true;
            });

        // Answering touches the disk, and the branch that returns immediately
        // has no use for the answer.
        asked.Should().BeFalse();
    }

    [Fact]
    public void Opening_the_launcher_leaves_the_commands_registered()
    {
        Program.PrepareInteractive(interactive: true, configured: () => true);

        // The command list the launcher shows comes from here. Without it the
        // palette opens holding nothing and explains nothing, which is exactly
        // what shipped.
        //
        // Registration happens once per process, so this cannot fail on its own
        // once anything else has triggered it. It is here to state the
        // obligation rather than to police it; what would police it is a test
        // that opens the real screen, and that needs a terminal.
        Program.RegisteredCommands().Should().NotBeEmpty();
    }
}
