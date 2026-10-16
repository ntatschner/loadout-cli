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
/// It used to report <c>InvalidArguments</c>, then <c>TerminalRequired</c>.
/// Both read as failures to a package validator, which runs the installed
/// executable with no arguments and no terminal, and the first was also
/// <c>ERROR_FILE_NOT_FOUND</c> to anything decoding it as a Win32 code. It now
/// succeeds: nothing went wrong, and what to do instead is on stderr.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class NoTerminalContractTests
{
    [Fact]
    public async Task No_arguments_and_no_terminal_succeeds_having_said_where_to_go()
    {
        using var loadout = new LoadoutProcess();

        var run = await loadout.RunAsync();

        // Nothing failed: it was asked for nothing it could do here, and said
        // so. A package validator runs exactly this, and any non-zero code -
        // 2, then 11 - held every winget release for a person to check by hand.
        run.ExitCode.Should().Be((int)ExitCode.Success, "it was asked for nothing and said where to go instead");
        run.StandardError.Should().Contain("no interactive terminal");
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
