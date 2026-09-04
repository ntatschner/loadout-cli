using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// No command hands a file or a folder to the desktop when nobody is watching.
/// </summary>
/// <remarks>
/// <para>
/// The same rule as never prompting where nobody can answer, in the other
/// direction. A command whose output is going down a pipe has no person in
/// front of it, and opening a window there is a surprise delivered to somebody
/// who is doing something else.
/// </para>
/// <para>
/// Found the hard way. The contract test that runs every registered command was
/// calling 'config edit' on every pass, which handed config.yaml to Windows —
/// which has no default application for .yaml, so it asked which one to use.
/// Every full run of the suite put that dialog in front of the person who owned
/// the machine, several times a day, and nothing in the suite could see it.
/// </para>
/// </remarks>
public sealed class OpensNothingUnwatchedTests
{
    [Fact]
    public void Every_command_that_opens_something_checks_that_somebody_is_there()
    {
        var commands = Directory.GetFiles(
            Path.Combine(RepositoryRoot(), "src", "Loadout.Cli", "Commands"),
            "*.cs",
            SearchOption.AllDirectories);

        var offenders = new List<string>();

        foreach (var file in commands)
        {
            var text = File.ReadAllText(file);

            if (!text.Contains("OpenInFileManagerAsync", StringComparison.Ordinal))
            {
                continue;
            }

            // The guard has to be in the same file as the call. Anything
            // cleverer here would be a test that passes on a codebase where
            // the check has drifted away from the thing it guards.
            if (!text.Contains("CanOpenAWindow", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            "a command that opens a window without checking CanOpenAWindow will do it "
            + "behind a pipe, in a test run, and in front of somebody who did not ask");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }
}
