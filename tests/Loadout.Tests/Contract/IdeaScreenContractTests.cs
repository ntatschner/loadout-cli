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
            [LauncherCommands.DumpAdd, .. new DumpNotes("- one\n- two", false, true).Arguments(null)],
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

        // The parser's refusal of a value that starts with a dash. These three
        // alone passed a line that failed on every bulleted list, because the
        // one message that line produced was none of them.
        everything.Should().NotContain("Option does not have a name");
        everything.Should().NotContain("missing required argument");
        everything.Should().NotContain("Could not match");
    }
}
