using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// The command 'project add' suggests is one that runs.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class ProjectAddHintContractTests
{
    [BuiltCliFact]
    public async Task The_command_suggested_for_an_unversioned_directory_is_one_the_parser_accepts()
    {
        using var loadout = new LoadoutProcess();

        var directory = Path.Combine(loadout.Home, "not-a-repository-yet");
        Directory.CreateDirectory(directory);

        var added = await loadout.RunAsync("project", "add", directory);

        added.ExitCode.Should().Be(0, added.StandardError);

        // The line as printed, run as printed. It suggested 'task list <slug>'
        // when the list takes the project as --project, so the one command
        // offered after registering failed with "Unknown command".
        var suggested = added.StandardOutput
            .Split('\n')
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("loadout task list", StringComparison.Ordinal));

        var run = await loadout.RunAsync([.. suggested["loadout ".Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries)]);

        run.ExitCode.Should().Be(0, run.StandardOutput + run.StandardError);
        run.StandardOutput.Should().Contain("setup-repository", "it lists the task registering just recorded");
    }
}
