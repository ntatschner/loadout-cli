using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// A command that serves something has to stop when whatever started it lets
/// go.
/// </summary>
/// <remarks>
/// <para>
/// Not a rule anybody wrote down until a command broke it. The contract test
/// that runs every registered command reached <c>team dashboard</c>, which
/// served until Ctrl+C, and held the whole suite for twenty minutes with eight
/// orphaned processes behind it. The MCP server had always had this contract -
/// it reads a pipe and ends at its end - and nothing said so.
/// </para>
/// <para>
/// It is a real promise rather than a concession to a test. A script that
/// starts one of these, reads the address and closes the pipe has said it is
/// finished, and a server still listening after that is one nobody will
/// remember to stop.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class ServingCommandsContractTests
{
    /// <summary>The commands that hold a port or a pipe open.</summary>
    /// <remarks>
    /// Named rather than taken from the catalogue, because what makes one of
    /// these is that it does not return, and nothing a command declares about
    /// itself says so. A new one belongs here, and the cost of forgetting is
    /// what this exists to stop.
    /// </remarks>
    public static TheoryData<string> Serving =>
        ["team dashboard", "mcp serve"];

    [BuiltCliTheory]
    [MemberData(nameof(Serving))]
    public async Task A_serving_command_stops_when_its_input_ends(string command)
    {
        using var loadout = new LoadoutProcess();

        using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var run = loadout.RunAsync([.. command.Split(' ')]);

        var finished = await Task.WhenAny(run, Task.Delay(Timeout.Infinite, patience.Token));

        finished.Should().Be(
            (Task)run,
            $"'{command}' served for a minute after its input ended; a command that does not "
            + "return holds every test that runs every command");
    }
}
