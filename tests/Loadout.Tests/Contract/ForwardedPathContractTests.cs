using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A command typed in one directory and run by a daemon started in another
/// finds a relative path where it was typed.
/// </summary>
/// <remarks>
/// The daemon used to make <c>--repo .</c> whole against its own directory, so
/// <c>docs audit --repo .</c> looked in the wrong place, found nothing and said
/// so in the words of a clean result. Checked on a real daemon, because the
/// directory a process runs in is exactly what a test in this one would share.
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ForwardedPathContractTests
{
    [BuiltCliFact]
    public async Task A_relative_repo_is_the_directory_the_command_was_typed_in()
    {
        using var loadout = new LoadoutProcess();

        var repository = Path.Combine(loadout.Home, "typed-here");

        await ContractDaemon.RepositoryAsync(repository);

        await using var daemon = await ContractDaemon.StartAsync(loadout);

        var (exitCode, output, error) = await daemon.TypedAsync(["docs", "audit", "--repo", ".", "--json"], repository);

        exitCode.Should().Be(0, error);
        output.TrimStart().Should().StartWith("{", $"the audit should have reported on the repository it was typed in, and said: {output}{error}");

        using var document = JsonDocument.Parse(output);

        document.RootElement.GetProperty("documents").GetInt32()
            .Should().Be(1, "the repository it was typed in holds one document, its README");
    }
}
