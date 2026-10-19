using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Configuration;
using Loadout.Core.Lists;
using Loadout.Models.Configuration;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A list's saved query: kept as a config key beside Studio's other settings,
/// checked before it is kept, and applied by 'task list' only where a script
/// would not be surprised by it.
/// </summary>
public sealed class SavedViewTests
{
    [Fact]
    public void A_query_is_kept_written_out_as_the_list_writes_it()
    {
        var (config, machine) = Fresh();
        var entry = ConfigKeys.Find("studio-query-tasks")!;

        entry.Write(config, machine, "  sort:-updated   state:doing,open ");

        config.Studio.Views["tasks"].Should().Be("state:doing,open sort:-updated");
        entry.Read(config, machine).Should().Be("state:doing,open sort:-updated");
        entry.IsMachineLocal.Should().BeFalse("a filter follows the person to another machine, as the theme does");
    }

    [Theory]
    [InlineData("colour:red", "'colour' is not something tasks can be filtered on")]
    [InlineData("state:finished", "'finished' is not a state")]
    [InlineData("\"never closed", "never closed")]
    public void A_query_the_list_cannot_take_is_refused_before_it_is_kept(string value, string said)
    {
        var (config, machine) = Fresh();

        var setting = () => ConfigKeys.Find("studio-query-tasks")!.Write(config, machine, value);

        setting.Should().Throw<FormatException>().WithMessage($"*{said}*");
        config.Studio.Views.Should().BeEmpty();
    }

    [Fact]
    public void An_empty_value_goes_back_to_the_default()
    {
        var (config, machine) = Fresh();
        var entry = ConfigKeys.Find("studio-query-tasks")!;
        entry.Write(config, machine, "state:doing");

        entry.Write(config, machine, "");

        config.Studio.Views.Should().NotContainKey("tasks");
        ListViews.Saved(config.Studio, "tasks").Should().BeNull();
    }

    [Theory]
    // Filter, saved, json, all, saved flag => the query.
    [InlineData(null, null, false, false, false, "-state:done,dropped")]
    [InlineData(null, "state:doing", false, false, false, "state:doing")]
    [InlineData(null, "state:doing", true, false, false, "-state:done,dropped")]
    [InlineData(null, "state:doing", true, false, true, "state:doing")]
    [InlineData(null, "state:doing", false, true, false, "")]
    [InlineData("login", "state:doing", false, false, true, "login")]
    public void Task_list_takes_the_saved_query_for_people_and_only_when_asked_for_scripts(
        string? filter, string? saved, bool json, bool all, bool savedFlag, string expected)
    {
        var settings = new TaskListCommand.Settings { Filter = filter, All = all, Saved = savedFlag };

        TaskListCommand.Query(settings, saved, json).Value!.ToString().Should().Be(expected);
    }

    [Fact]
    public void Studio_sends_a_saved_query_as_a_value_the_command_line_cannot_take_for_an_option()
    {
        var (command, arguments) = DashboardActions.Setting(new Loadout.Core.Teams.Daemon.SettingsChange("studio-query-tasks", "-state:done"));

        command.Should().Be("config set");
        arguments.Should().Equal("studio-query-tasks", "--value=-state:done");
    }

    private static (LauncherConfig Config, MachineConfig Machine) Fresh() => (new LauncherConfig(), new MachineConfig());
}
