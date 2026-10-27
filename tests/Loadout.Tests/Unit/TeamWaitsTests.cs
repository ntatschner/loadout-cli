using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How long a run waits for a person, from what this machine has set.
/// </summary>
public sealed class TeamWaitsTests
{
    [Fact]
    public void With_nothing_set_a_permission_waits_five_minutes_and_a_question_an_hour()
    {
        var teams = new MachineTeams();

        TeamWaits.Permission(teams).Should().Be(TimeSpan.FromMinutes(5));
        TeamWaits.Question(teams).Should().Be(TimeSpan.FromHours(1));
        TeamWaits.Permission(null).Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void A_link_address_makes_the_permission_wait_longer()
    {
        // Somebody answering by phone, from a notice, needs longer than
        // somebody at the machine.
        var teams = new MachineTeams { LinkAddress = "https://desk.example.ts.net" };

        TeamWaits.Permission(teams).Should().Be(TimeSpan.FromMinutes(30));
        TeamWaits.Question(teams).Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_wait_that_is_set_is_the_wait_with_or_without_a_link_address()
    {
        var teams = new MachineTeams
        {
            LinkAddress = "https://desk.example.ts.net",
            PermissionWaitMinutes = 12,
            QuestionWaitMinutes = 90,
        };

        TeamWaits.Permission(teams).Should().Be(TimeSpan.FromMinutes(12));
        TeamWaits.Question(teams).Should().Be(TimeSpan.FromMinutes(90));
    }

    [Theory]
    [InlineData(1, "1 minute")]
    [InlineData(5, "5 minutes")]
    [InlineData(60, "1 hour")]
    [InlineData(90, "90 minutes")]
    [InlineData(120, "2 hours")]
    public void A_wait_is_said_the_way_somebody_would(int minutes, string said)
    {
        TeamWaits.Said(TimeSpan.FromMinutes(minutes)).Should().Be(said);
    }

    [Theory]
    [InlineData("team-permission-wait")]
    [InlineData("team-question-wait")]
    public void The_settings_take_whole_minutes_and_clear_with_empty(string name)
    {
        var key = ConfigKeys.Find(name)!;
        var machine = new MachineConfig();

        key.Write(new LauncherConfig(), machine, " 45 ");
        key.Read(new LauncherConfig(), machine).Should().Be("45");

        key.Write(new LauncherConfig(), machine, string.Empty);
        key.Read(new LauncherConfig(), machine).Should().BeEmpty();
    }

    [Theory]
    [InlineData("team-permission-wait", "0")]
    [InlineData("team-permission-wait", "-5")]
    [InlineData("team-permission-wait", "1441")]
    [InlineData("team-permission-wait", "half an hour")]
    [InlineData("team-question-wait", "2.5")]
    public void The_settings_refuse_what_is_not_a_wait(string name, string value)
    {
        var key = ConfigKeys.Find(name)!;
        var machine = new MachineConfig();

        var setting = () => key.Write(new LauncherConfig(), machine, value);

        setting.Should().Throw<FormatException>();
        key.Read(new LauncherConfig(), machine).Should().BeEmpty();
    }
}
