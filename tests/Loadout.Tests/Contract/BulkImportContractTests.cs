using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Importing memory for every project at once, rather than one at a time.
/// </summary>
/// <remarks>
/// Run against the real binary and a throwaway home, because the thing worth
/// checking is what the command line does with the option — accepts it,
/// refuses the contradiction, changes nothing without being told to — and none
/// of that is visible from inside the command.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class BulkImportContractTests
{
    [Fact]
    public async Task All_is_accepted_and_reports_rather_than_failing_on_an_empty_registry()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("memory", "import", "--all");

        var everything = run.StandardOutput + run.StandardError;

        everything.Should().NotContain("Unknown option");
        everything.Should().NotContain("Unexpected option");

        // Nothing registered is not a failure. It is the answer.
        run.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task Naming_a_project_and_asking_for_all_of_them_is_refused()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("memory", "import", "somewhere", "--all");

        // Silently preferring one over the other would import something other
        // than what was asked for, which is the one outcome worth avoiding in
        // a command that writes.
        run.ExitCode.Should().Be(2, "asking for both is an argument error");
    }

    [Fact]
    public async Task Nothing_is_imported_without_being_asked()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("memory", "import", "--all", "--json");

        run.ExitCode.Should().Be(0);

        // --apply is what imports. Without it this is a report, and the whole
        // point of a command that touches sixteen repositories is that it
        // shows the whole list before it touches any of them.
        (run.StandardOutput + run.StandardError).Should().NotContain("Imported");
    }
}
