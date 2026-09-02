using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Commands the documentation tells a reader to run.
/// </summary>
/// <remarks>
/// <para>
/// The same rule already holds for the C# sources and for the specialist
/// library, and the documentation was the one corpus nothing checked — which is
/// where it matters most, because a reader who types a command that does not
/// exist concludes the tool is broken rather than that the page is old. It had
/// already drifted twice: a table naming the instruction sub-commands after one
/// was added, and a count of the specialist library left at 71.
/// </para>
/// <para>
/// A first word the parser does not know is a project name, not a mistake:
/// <c>loadout starstats</c> launches a registered project, and that is the
/// documented shorthand. So only phrases beginning with a real command are
/// judged.
/// </para>
/// </remarks>
public sealed class DocumentationCommandTests
{
    /// <summary>
    /// Only the form used when telling somebody what to type: inside backticks,
    /// or alone on a line in a fenced block.
    /// </summary>
    private static readonly Regex Typed = new(
        @"(?:`|^\s*)loadout ((?:[a-z][a-z-]*)(?: [a-z][a-z-]*)*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    [Fact]
    public void Every_command_the_documentation_names_is_one_that_exists()
    {
        // Registering the parser is what fills the catalogue.
        var roots = Program.CommandNames();

        var paths = Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

        paths.Should().NotBeEmpty("the catalogue has to be filled for this to check anything");

        var wrong = new List<string>();
        var found = 0;

        foreach (var file in Documentation())
        {
            foreach (Match match in Typed.Matches(File.ReadAllText(file)))
            {
                var words = match.Groups[1].Value.Split(' ');

                // Not a command at all: this is the project-name shorthand.
                if (!roots.Contains(words[0]))
                {
                    continue;
                }

                found++;

                var matched = 0;
                string? path = null;

                for (var length = Math.Min(words.Length, 3); length > 0 && path is null; length--)
                {
                    var candidate = string.Join(' ', words.Take(length));

                    if (paths.Contains(candidate))
                    {
                        (path, matched) = (candidate, length);
                    }
                }

                if (path is null)
                {
                    wrong.Add($"{Path.GetFileName(file)}: loadout {match.Groups[1].Value}");

                    continue;
                }

                // A branch with a word still unread is how a renamed
                // sub-command escapes: 'rules budgets' matches 'rules', which
                // is real, and the rename goes unnoticed.
                var branch = paths.Any(other =>
                    other.StartsWith(path + ' ', StringComparison.Ordinal));

                if (branch && matched < words.Length)
                {
                    wrong.Add(
                        $"{Path.GetFileName(file)}: loadout {match.Groups[1].Value} "
                        + $"— '{path}' is real but '{words[matched]}' is not one of its commands");
                }
            }
        }

        found.Should().BeGreaterThan(20, "the scan has to be finding instructions at all");

        wrong.Should().BeEmpty(
            "somebody reading the documentation types what it says, and a command that "
            + "does not exist reads as a broken tool rather than an old page");
    }

    private static IEnumerable<string> Documentation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        var docs = Path.Combine(root!.FullName, "docs");

        Directory.Exists(docs).Should().BeTrue("the documentation has to be findable");

        return Directory
            .EnumerateFiles(docs, "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(root.FullName, "README.md"))
            .Where(File.Exists);
    }
}
