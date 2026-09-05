using System.Text.RegularExpressions;
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
        var offenders = Commands()
            .Where(command => command.Mutates && !command.ReadsTheFlag)
            .Select(command => command.Where)
            .ToList();

        offenders.Should().BeEmpty(
            "a command that declares it changes something and never mentions DryRun cannot be "
            + "honouring it — and the failure is silent, because it reports the same words "
            + "whether or not the flag was given");
    }

    [Fact]
    public void The_search_finds_the_commands_it_is_supposed_to_be_checking()
    {
        var commands = Commands();

        // This test is the instrument's own calibration. Read per file, the
        // check passed while 'desktop' installed a Start Menu shortcut under
        // --dry-run, because another command further down the same file
        // mentioned the flag. A parser that silently matched nothing would
        // pass just as quietly.
        commands.Should().HaveCountGreaterThan(50, "the command line has dozens of commands");

        commands.Count(command => command.Mutates)
            .Should().BeGreaterThan(10, "plenty of them change something");

        commands.Select(command => command.Name).Should().Contain("DesktopCommand");
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

    /// <summary>One command, and the source that decides its answer.</summary>
    private sealed record Command(string Name, string Where, bool Mutates, bool ReadsTheFlag);

    /// <summary>
    /// Every command in the command line, read one at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per command rather than per file, which is the whole point. Ten of these
    /// files hold between three and six commands each, and a file-wide search
    /// for "DryRun" is answered by whichever of them happens to mention it —
    /// so one command in the file can be checked and the other five ride along.
    /// </para>
    /// <para>
    /// A command's settings are sometimes a nested type and sometimes a
    /// separate top-level class beside it, and the flag is usually read in the
    /// settings rather than the body — 'Apply => ApplyRequested &amp;&amp; !DryRun'.
    /// Looking only at the command's own braces reports Drift and
    /// MemoryCompress as offenders when both honour it perfectly well, so the
    /// settings named in the base list are read with it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Command> Commands()
    {
        var directory = Path.Combine(Repository(), "src", "Loadout.Cli", "Commands");

        Directory.Exists(directory).Should().BeTrue("the commands have to be findable");

        var found = new List<Command>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
        {
            // Not normalised: every check below is a substring search, and
            // CRLF and LF answer them the same.
            var text = File.ReadAllText(file);

            var classes = TopLevelClasses(text);

            foreach (var (name, span) in classes)
            {
                if (!name.EndsWith("Command", StringComparison.Ordinal))
                {
                    continue;
                }

                var source = span.WithPreamble;

                // The settings, wherever they live. A nested type is already
                // inside the braces; a sibling has to be fetched.
                var settings = Regex.Match(
                    text,
                    @"class " + Regex.Escape(name) + @"\s*:\s*\w*Command<(?<settings>[\w.]+)>");

                if (settings.Success)
                {
                    var type = settings.Groups["settings"].Value;

                    if (!type.Contains('.', StringComparison.Ordinal)
                        && classes.TryGetValue(type, out var beside))
                    {
                        source += beside.Body;
                    }
                }

                found.Add(new Command(
                    name,
                    $"{Path.GetFileName(file)}:{name}",
                    span.WithPreamble.Contains("Mutates = true", StringComparison.Ordinal),
                    source.Contains("DryRun", StringComparison.Ordinal)));
            }
        }

        return found;
    }

    /// <summary>What a top-level class covers: its own braces, and what precedes it.</summary>
    private sealed record Span(string WithPreamble, string Body);

    /// <summary>
    /// Splits a file into its top-level classes by matching braces.
    /// </summary>
    /// <remarks>
    /// The preamble runs from the end of the previous class, so the attributes
    /// and documentation above a declaration belong to it — which is where
    /// CommandMeta and its Mutates flag are written.
    /// </remarks>
    private static Dictionary<string, Span> TopLevelClasses(string text)
    {
        var classes = new Dictionary<string, Span>(StringComparer.Ordinal);
        var previous = 0;

        foreach (Match declaration in Regex.Matches(
            text,
            @"^(?:public|internal)\s+(?:sealed\s+)?(?:abstract\s+)?class (?<name>\w+)",
            RegexOptions.Multiline))
        {
            var open = text.IndexOf('{', declaration.Index);

            if (open < 0)
            {
                continue;
            }

            var depth = 0;
            var i = open;

            for (; i < text.Length; i++)
            {
                if (text[i] == '{')
                {
                    depth++;
                }
                else if (text[i] == '}' && --depth == 0)
                {
                    break;
                }
            }

            var end = Math.Min(i + 1, text.Length);

            classes[declaration.Groups["name"].Value] = new Span(
                text[previous..end], text[declaration.Index..end]);

            previous = end;
        }

        return classes;
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
