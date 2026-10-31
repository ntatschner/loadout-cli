using Loadout.Core.Updates;
using Loadout.Tests.Fakes;
using Loadout.Tui;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The launcher's Update now runs <c>loadout update --yes --progress</c> on its
/// own binary as a piped child, and reads its steps back.
/// </summary>
public sealed class AboutUpdateRunnerTests
{
    private const string Output =
        """
        {"stage":"Checking","message":"Checking the release source"}
        not a step
        {"stage":"Installing","message":"Putting the new version in place"}
        {"stage":"Done","message":"Updated to 0.110.0"}
        """;

    private static async Task<List<UpdateProgress>> Collect(IAsyncEnumerable<UpdateProgress> steps)
    {
        var collected = new List<UpdateProgress>();

        await foreach (var step in steps)
        {
            collected.Add(step);
        }

        return collected;
    }

    [Fact]
    public async Task Runs_update_yes_progress_on_the_same_binary()
    {
        var processes = new StubProcessLauncher(Output);

        // What this launcher says it was started as: a host and the assembly
        // it ran, as a development build is.
        var runner = new AboutUpdateRunner(processes, () => ("C:/sdk/dotnet.exe", ["C:/build/loadout.dll"]));

        var steps = await Collect(runner.RunAsync());

        // Piped, which redirects every stream and opens no window; never the
        // terminal, and never a detached console.
        processes.Interactive.Should().BeNull();
        processes.Piped.Should().NotBeNull();
        processes.Piped!.Executable.Should().Be("C:/sdk/dotnet.exe");
        processes.Piped.Arguments.Should().Equal("C:/build/loadout.dll", "update", "--yes", "--progress");

        steps.Select(step => step.Stage).Should().Equal(UpdateStage.Checking, UpdateStage.Installing, UpdateStage.Done);
        steps[^1].Message.Should().Be("Updated to 0.110.0");
    }

    [Fact]
    public async Task A_failed_update_says_so()
    {
        var processes = new StubProcessLauncher(string.Empty, exitCode: 3);
        var runner = new AboutUpdateRunner(processes, () => ("loadout", []));

        var act = () => Collect(runner.RunAsync());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*code 3*");
    }
}
