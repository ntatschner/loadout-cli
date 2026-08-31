using FluentAssertions;
using Xunit;

// Loadout.Tests.Contracts, matching this folder's neighbours rather than the
// folder's own name. A namespace called Architecture shadows
// System.Runtime.InteropServices.Architecture, which PathLayoutTests uses
// unqualified — the mismatch looks like untidiness and is load-bearing.
namespace Loadout.Tests.Contracts;

/// <summary>
/// Every command that can change something has to read <c>--dry-run</c>.
/// </summary>
/// <remarks>
/// <para>
/// The contract test that enumerates mutating commands asserts only that the
/// option is <em>accepted</em>. That is what can be checked from outside a
/// process, and it is not enough: `workspace save` accepted the flag for its
/// whole life while committing the workspace and pushing it, and `launch`
/// accepted it while starting an agent — which on an interactive terminal is a
/// session opening in front of somebody who asked for a description of one.
/// </para>
/// <para>
/// Reading the source is cruder than running the command, and it catches a
/// different thing: a command that never mentions the flag cannot be honouring
/// it. Both were found this way after two had already been found by hand, and
/// three more with them — policy, project link, handoff, update and setup, none
/// of which referred to it anywhere.
/// </para>
/// <para>
/// A mention is not proof of correct handling. This is a floor, not a ceiling,
/// and it is worth having because the failure it catches is silent: a command
/// asked what it would do, doing it, and saying the same words either way.
/// </para>
/// </remarks>
public sealed class DryRunTests
{
    [Fact]
    public void Every_command_that_says_it_mutates_reads_the_flag()
    {
        var commands = Path.Combine(Repository(), "src", "Loadout.Cli", "Commands");

        Directory.Exists(commands).Should().BeTrue("the commands have to be findable");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(commands, "*.cs"))
        {
            var text = File.ReadAllText(file);

            if (!text.Contains("Mutates = true", StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.Contains("DryRun", StringComparison.Ordinal))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        offenders.Should().BeEmpty(
            "a command that declares it changes something and never mentions DryRun cannot be "
            + "honouring it — and the failure is silent, because it reports the same words "
            + "whether or not the flag was given");
    }

    [Fact]
    public void The_launcher_reads_it_too_although_it_changes_no_files()
    {
        // 'launch' does not declare that it mutates, and by the letter of it
        // that is right: it writes nothing. It starts an agent, which is a
        // larger thing to do by surprise than writing a file, and the test
        // above would never have covered it.
        var launch = Path.Combine(
            Repository(), "src", "Loadout.Cli", "Commands", "LaunchCommand.cs");

        File.ReadAllText(launch).Should().Contain("DryRun");
    }

    private static string Repository()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return root!.FullName;
    }
}
