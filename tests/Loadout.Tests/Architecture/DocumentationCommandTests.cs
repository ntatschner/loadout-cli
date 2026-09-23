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

                if (!roots.Contains(words[0]))
                {
                    // The project-name shorthand — 'loadout starstats' launches
                    // a registered project — so an unknown first word is not by
                    // itself wrong, and the documentation's example projects
                    // exist on nobody's machine but the reader's.
                    //
                    // Unless a bare word follows it. Launching a project takes
                    // options, never a second bare word, so 'loadout reppo
                    // check' is a misspelt command rather than a project called
                    // reppo — and skipping it on the shorthand's account is how
                    // a typo would have gone out unnoticed.
                    if (words.Length > 1)
                    {
                        wrong.Add(
                            $"{Path.GetFileName(file)}: loadout {match.Groups[1].Value} "
                            + $"— '{words[0]}' is not a command, and a project takes options "
                            + "rather than a second bare word");
                    }

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

    /// <summary>
    /// Every command, by its whole path, has to be named in the reference.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The check above runs one way only: it catches a documented command that
    /// does not exist and is blind to a command nobody documented. So 'task',
    /// 'checkpoint', 'pack', 'running', 'share' and 'spend' were all shipped
    /// and absent from the page that calls itself the whole command surface —
    /// and 'commands', which exists to list everything the launcher can do, was
    /// itself unlisted.
    /// </para>
    /// <para>
    /// This then checked the first word only, which closed the hole one level
    /// deep and left the level underneath wide open: a sub-command added to a
    /// family that was already documented was covered by its parent's name and
    /// checked by nothing. Three shipped that way — 'instructions export',
    /// 'project show' and 'team capabilities' — and they were found by hand
    /// rather than by this, which is the thing this test exists to stop.
    /// </para>
    /// <para>
    /// The reference writes a family as one row, <c>loadout team schedule
    /// add|list|remove</c>, so the alternations are expanded before matching.
    /// A check that looked for the whole path literally would report two thirds
    /// of every family as missing, which is a failing test nobody can act on
    /// and therefore one somebody eventually deletes.
    /// </para>
    /// </remarks>
    [Fact]
    public void Every_command_that_exists_is_named_in_the_command_reference()
    {
        var reference = Documentation()
            .Single(f => Path.GetFileName(f).Equals("commands.md", StringComparison.Ordinal));

        var named = Named(File.ReadAllText(reference));

        // Calibration, before the assertion it guards. A matcher that silently
        // matched everything would pass this test for ever while asserting
        // nothing, and the expansion above is exactly the kind of code that
        // fails open.
        named.Should().Contain("team schedule list", "the reference names that family in one row");
        named.Should().NotContain("team reticulate", "nothing names a command that does not exist");

        var missing = Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .Distinct(StringComparer.Ordinal)
            .Where(path => !named.Contains(path))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "docs/commands.md is the command reference, so every command belongs in it — "
            + "including a sub-command of a family that is already there");
    }

    /// <summary>
    /// Every command the reference names, with <c>a|b|c</c> families expanded
    /// into one path each, and every prefix of each path recorded too.
    /// </summary>
    /// <remarks>
    /// The prefixes matter because a row reading <c>loadout team run &lt;team&gt;
    /// "&lt;goal&gt;"</c> names <c>team run</c> and <c>team</c> as well as
    /// itself, and a branch is a command somebody types.
    /// </remarks>
    private static HashSet<string> Named(string text)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);

        // A word, or an alternation of words. The pipes are backslash-escaped
        // in the table, because an unescaped one would end the cell.
        foreach (Match match in new Regex(
            @"loadout ((?:[a-z][a-z-]*)(?:(?:\\\||\|| )[a-z][a-z-]*)*)",
            RegexOptions.Compiled).Matches(text))
        {
            var words = match.Groups[1].Value.Replace("\\|", "|", StringComparison.Ordinal).Split(' ');

            for (var index = 0; index < words.Length; index++)
            {
                if (words[index].Contains('|', StringComparison.Ordinal))
                {
                    foreach (var one in words[index].Split('|'))
                    {
                        named.Add(string.Join(' ', words.Take(index).Append(one)));
                    }

                    break;
                }

                named.Add(string.Join(' ', words.Take(index + 1)));
            }
        }

        return named;
    }

    /// <summary>
    /// Every setting the documentation names is one a reader can set.
    /// </summary>
    /// <remarks>
    /// The same argument as the commands, and a sharper one: a settings page
    /// lists dozens of names at once, and a key that was renamed after the page
    /// was written fails silently when somebody types it. The accessibility
    /// keys were renamed twice before they fitted the settings screen, which is
    /// exactly how a page goes stale.
    /// </remarks>
    [Fact]
    public void Every_setting_the_documentation_names_is_one_that_exists()
    {
        var keys = Loadout.Core.Configuration.ConfigKeys.All
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Backticked words that look like a setting: lowercase, hyphenated,
        // and at least two words, so 'claude' and 'ascii' are not candidates.
        var named = new System.Text.RegularExpressions.Regex(
            @"`([a-z][a-z0-9]*(?:-[a-z0-9]+)+)`",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var wrong = new List<string>();
        var found = 0;

        foreach (var file in Documentation())
        {
            var text = File.ReadAllText(file);

            foreach (System.Text.RegularExpressions.Match match in named.Matches(text))
            {
                var word = match.Groups[1].Value;

                // Only judged where the page is talking about settings at all.
                // Hyphenated words are ordinary prose everywhere else, and
                // 'screen-reader' is a profile rather than a key.
                if (!word.StartsWith("ask-", StringComparison.Ordinal)
                    && !word.StartsWith("write-", StringComparison.Ordinal)
                    && !word.StartsWith("show-", StringComparison.Ordinal)
                    && !word.StartsWith("accessibility-", StringComparison.Ordinal))
                {
                    continue;
                }

                found++;

                if (!keys.Contains(word))
                {
                    wrong.Add($"{Path.GetFileName(file)}: {word}");
                }
            }
        }

        found.Should().BeGreaterThan(20, "the scan has to be finding settings at all");

        wrong.Should().BeEmpty(
            "somebody reading the documentation types what it says, and a setting that "
            + "does not exist is refused with a list they then have to read instead");
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
