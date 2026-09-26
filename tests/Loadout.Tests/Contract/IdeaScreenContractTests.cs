using FluentAssertions;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The lines the launcher's dump screens hand to the command, put through the
/// real parser. A screen that builds an option the command does not declare
/// fails only when somebody uses it, as "Unknown option", which reads like
/// their mistake.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class IdeaScreenContractTests
{
    public static TheoryData<string[]> Lines()
    {
        string[][] lines =
        [
            [LauncherCommands.DumpAdd, .. new DumpNotes("- a \"quoted\" note\n- another", true, true).Arguments("website")],
            [LauncherCommands.DumpAdd, .. new DumpNotes("x", false, false).Arguments(null)],
            [LauncherCommands.DumpApply, .. DumpPiecesDialog.Arguments("dump-1", "website", [1, 3], offered: 4)],
            [LauncherCommands.DumpApply, .. DumpPiecesDialog.Arguments("dump-1", null, [1], offered: 1)],
        ];

        var data = new TheoryData<string[]>();

        foreach (var line in lines)
        {
            data.Add(line);
        }

        return data;
    }

    [BuiltCliTheory]
    [MemberData(nameof(Lines))]
    public async Task Every_line_the_dump_screens_build_is_one_the_parser_accepts(string[] line)
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync([.. line[0].Split(' '), .. line[1..], "--non-interactive", "--dry-run"]);

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain("Unknown option");
        everything.Should().NotContain("Unexpected option");
        everything.Should().NotContain("Unknown command");
    }
}
