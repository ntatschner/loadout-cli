using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What the office's rooms type when a button in a popup is pressed: the
/// garbage room's and the server room's verbs, each the command a person would.
/// </summary>
/// <remarks>
/// The contract test proves the parser accepts every verb's line; this proves
/// each is the right line, which a valid command doing the wrong thing would pass.
/// </remarks>
public sealed class OfficeRoomVerbTests
{
    [Theory]
    [InlineData("restore", "team runs restore", "20260916-1200-aaaa")]
    [InlineData("purge", "team bin empty", "20260916-1200-aaaa --yes")]
    [InlineData("restore-team", "team restore", "20260916-1200-aaaa")]
    [InlineData("purge-team", "team bin empty", "20260916-1200-aaaa --yes")]
    [InlineData("daemon-pause", "team daemon pause", "")]
    [InlineData("daemon-resume", "team daemon resume", "")]
    [InlineData("daemon-restart", "team daemon restart", "")]
    public void Each_room_verb_types_the_command_that_owns_it(string verb, string command, string arguments)
    {
        var (typed, with) = DashboardActions.Maps(new RunAction("20260916-1200-aaaa", verb));

        typed.Should().Be(command);
        string.Join(' ', with).Should().Be(arguments);
    }
}
