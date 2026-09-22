using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every setting the dashboard can change, run against the real parser.
/// </summary>
/// <remarks>
/// <para>
/// The pane implements nothing: each control types the command somebody would
/// have typed. That is worth exactly as much as the command lines it types are
/// ones the parser accepts, and the last time a control was added without this
/// check it passed <c>--yes</c> to a command that does not take it - Spectre
/// refused the line, the command never ran, and the page reported an exit code
/// over a typo three files away.
/// </para>
/// <para>
/// Run against the built binary, because what is asserted is what the parser
/// does with these strings, and the parser lives in a process.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class SettingsContractTests
{
    public static TheoryData<string> Settings
    {
        get
        {
            var data = new TheoryData<string>();

            foreach (var what in DashboardActions.Settings)
            {
                data.Add(what);
            }

            return data;
        }
    }

    /// <summary>
    /// One change of each kind, filled in as the pane fills them.
    /// </summary>
    /// <remarks>
    /// Every optional field is given a value, because several of them change
    /// the command line: a notify with a chat and one without are two shapes,
    /// and only one of them would be covered by leaving it null.
    /// </remarks>
    private static SettingsChange Asking(string what) => new(
        what,
        Value: what switch
        {
            "notify" => "slack",
            "office" => "open-office",
            "waiting" => "lobby",
            "listen" => "127.0.0.1",
            "webhook-teams" => "docs-crew, bug-hunt",
            "remedy" => "restart-the-daemon",
            _ => string.Empty,
        },
        Url: "https://example.invalid/hooks/not-a-real-one",
        Chat: "-1001234567890",
        Team: "iterating-project");

    [BuiltCliTheory]
    [MemberData(nameof(Settings))]
    public async Task Every_setting_the_pane_can_change_is_a_command_line_the_parser_accepts(string what)
    {
        var (command, arguments) = DashboardActions.Setting(Asking(what));

        command.Should().NotBeEmpty($"the pane can send '{what}', so something has to answer it");

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--non-interactive", "--dry-run"]);

        (run.StandardOutput + run.StandardError).Should().NotContain(
            "Unknown option",
            $"'{what}' types a line the parser would refuse before the command is reached");

        (run.StandardOutput + run.StandardError).Should().NotContain(
            "Unknown command",
            $"'{what}' names a command that does not exist");
    }

    [BuiltCliTheory]
    [MemberData(nameof(Settings))]
    public async Task And_so_is_turning_each_one_off(string what)
    {
        var (command, arguments) = DashboardActions.Setting(Asking(what) with { Off = true });

        // Not every setting has an off. Those that do must still parse; those
        // that do not fall through to the ordinary shape, which is checked
        // above, so an empty command here would be the bug.
        command.Should().NotBeEmpty();

        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. command.Split(' '), .. arguments, "--non-interactive", "--dry-run"]);

        (run.StandardOutput + run.StandardError).Should().NotContain("Unknown option");
        (run.StandardOutput + run.StandardError).Should().NotContain("Unknown command");
    }
}
