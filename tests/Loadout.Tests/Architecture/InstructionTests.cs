using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli;
using Xunit;

// Contracts rather than Architecture, matching the file beside it. A namespace
// called Architecture shadows System.Runtime.InteropServices.Architecture for
// every other test, and PathLayoutTests stops compiling.
namespace Loadout.Tests.Contracts;

/// <summary>
/// Commands the source tells people to run have to be commands.
/// </summary>
/// <remarks>
/// <para>
/// Three times now. The settings menu ran <c>config show</c> when the command
/// is <c>config list</c>. A resume that could not find a session said to run
/// <c>loadout session list</c>, and the command is <c>loadout sessions</c> —
/// <c>session list</c> answers "Could not match 'list' with an argument". Both
/// were written while fixing something else, and both were only ever wrong at
/// the moment somebody followed the advice.
/// </para>
/// <para>
/// LauncherCommands already guards the ones the launcher runs on your behalf.
/// This covers the other kind: the ones written into a sentence, which nothing
/// executes and nothing checked.
/// </para>
/// </remarks>
public sealed class InstructionTests
{
    /// <summary>
    /// Quoted instructions, as in <c>Run 'loadout sessions' to see what there
    /// is</c>.
    /// </summary>
    /// <remarks>
    /// Deliberately only the quoted form. Matching every "loadout" followed by
    /// a word finds seven things in this repository and all seven are prose —
    /// "loadout is run", "loadout can do", and <c>loadout starstats</c>, which
    /// is a real invocation because a bare project name launches it. A test
    /// that cries wolf seven times gets deleted, so this matches only the
    /// shape used when telling somebody what to type.
    /// </remarks>
    private static readonly Regex Quoted =
        new(@"'loadout ([a-z][a-z-]+(?: [a-z][a-z-]+)?)[^']*'", RegexOptions.Compiled);

    [Fact]
    public void Every_command_the_source_tells_you_to_run_exists()
    {
        // Registering the parser is what fills the catalogue.
        Program.CommandNames();

        var real = Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

        real.Should().NotBeEmpty("the catalogue has to be filled for this to check anything");

        var wrong = new List<string>();
        var checked_ = 0;

        foreach (var file in SourceFiles())
        {
            foreach (Match found in Quoted.Matches(File.ReadAllText(file)))
            {
                checked_++;

                var full = found.Groups[1].Value;
                var head = full.Split(' ')[0];

                if (!real.Contains(full) && !real.Contains(head))
                {
                    wrong.Add($"{Path.GetFileName(file)} says 'loadout {full}'");
                }
            }
        }

        checked_.Should().BeGreaterThan(0, "the scan has to be finding instructions at all");

        wrong.Should().BeEmpty(
            "a sentence telling somebody to run something is only useful if it exists");
    }

    /// <summary>Every C# file under src, whatever the build put where.</summary>
    private static IEnumerable<string> SourceFiles()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository's src directory has to be findable from the tests");

        return Directory
            .EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                       StringComparison.Ordinal));
    }
}
