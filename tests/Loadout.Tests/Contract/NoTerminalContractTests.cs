using FluentAssertions;
using Loadout.Models;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// What running <c>loadout</c> with no arguments and nowhere to draw reports to
/// whatever ran it.
/// </summary>
/// <remarks>
/// <para>
/// Run against the real binary, because the thing worth checking is the number
/// a caller sees and that is not visible from inside the command. A test
/// harness is already a redirected run, so this is the state every automated
/// caller meets: a pipe, a script, a CI job, or somebody's package validator.
/// </para>
/// <para>
/// It used to report <c>InvalidArguments</c>, which was wrong twice over. The
/// arguments were fine — running <c>loadout</c> with none is the documented way
/// to open the launcher — and 2 is also <c>ERROR_FILE_NOT_FOUND</c>, so a
/// validator decoding it as a Win32 code reported "the system cannot find the
/// file specified" about a launcher that had started, printed and exited
/// perfectly.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class NoTerminalContractTests
{
    [Fact]
    public async Task No_arguments_and_no_terminal_says_a_terminal_is_what_is_missing()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync();

        run.ExitCode.Should().Be(
            (int)ExitCode.TerminalRequired,
            "the arguments were fine and the terminal was not there");

        // Specifically not 2. Anything reading an exit code as a Win32 one
        // turns that into a missing file, which is a claim about the package
        // rather than about the environment it was run in.
        run.ExitCode.Should().NotBe((int)ExitCode.InvalidArguments);
    }

    [Fact]
    public async Task It_says_what_to_do_instead_rather_than_failing_silently()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync();

        (run.StandardOutput + run.StandardError)
            .Should().Contain("--help", "a caller that got here needs somewhere to go");
    }

    [Fact]
    public async Task Asking_for_help_is_not_a_failure()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync("--help");

        // The guard is only for the interactive launcher having nowhere to
        // draw. Everything else still works in a pipe, and a caller checking
        // exit codes should see the difference.
        run.ExitCode.Should().Be((int)ExitCode.Success);
    }
}
