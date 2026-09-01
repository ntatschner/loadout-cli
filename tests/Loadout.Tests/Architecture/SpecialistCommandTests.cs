using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Commands and tools the shipped specialists tell an agent to reach for.
/// </summary>
/// <remarks>
/// <para>
/// A specialist that names a command is telling an agent to run it, and an
/// agent will. A command that has been renamed produces a confident instruction
/// that fails in somebody else's session, and nothing else would catch it: the
/// specialists are prose, the parser is code, and the two are only held together
/// by whoever last remembered to look.
/// </para>
/// <para>
/// <see cref="InstructionTests"/> makes the same check over the C# sources. It
/// does not reach here, because its corpus is *.cs and the library is markdown.
/// </para>
/// </remarks>
public sealed class SpecialistCommandTests
{
    /// <summary>
    /// Only the shape used when telling somebody what to type: inside backticks,
    /// or alone on a line in a fenced block.
    /// </summary>
    /// <remarks>
    /// The bare form is not matched, for the reason
    /// <see cref="InstructionTests"/> writes down at length — "the loadout
    /// launcher" is prose, and a test that cries wolf gets deleted rather than
    /// fixed.
    /// </remarks>
    private static readonly Regex Typed = new(
        @"(?:`|^\s*)loadout ((?:[a-z][a-z-]*)(?: [a-z][a-z-]*)*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>Tool names an agent is told to call, as `loadout_remember`.</summary>
    private static readonly Regex ToolCall = new(
        @"`(loadout_[a-z_]+)`",
        RegexOptions.Compiled);

    [Fact]
    public void Every_command_a_specialist_names_is_one_that_exists()
    {
        // Registering the parser is what fills the catalogue.
        Program.CommandNames();

        var known = Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

        known.Should().NotBeEmpty("the catalogue has to be filled for this to check anything");

        var wrong = new List<string>();
        var found = 0;

        foreach (var file in Specialists())
        {
            foreach (Match match in Typed.Matches(File.ReadAllText(file)))
            {
                found++;

                var words = match.Groups[1].Value.Split(' ');

                // The longest registered prefix wins: "memory write" is a
                // command, and "memory write the fact you learned" is that
                // command with prose trailing it.
                var matched = 0;
                string? path = null;

                for (var length = Math.Min(words.Length, 3); length > 0 && path is null; length--)
                {
                    var candidate = string.Join(' ', words.Take(length));

                    if (known.Contains(candidate))
                    {
                        (path, matched) = (candidate, length);
                    }
                }

                if (path is null)
                {
                    wrong.Add($"{Path.GetFileName(file)}: loadout {match.Groups[1].Value}");
                    continue;
                }

                // Settling for a branch while a word is still unread is how a
                // renamed subcommand escapes: 'rules budgets' matches 'rules',
                // which is real, and the rename goes unnoticed. A branch is
                // only the answer when nothing followed it.
                var branch = known.Any(other =>
                    other.StartsWith(path + ' ', StringComparison.Ordinal));

                if (branch && matched < words.Length)
                {
                    wrong.Add(
                        $"{Path.GetFileName(file)}: loadout {match.Groups[1].Value} "
                        + $"— '{path}' is real but '{words[matched]}' is not one of its commands");
                }
            }
        }

        found.Should().BeGreaterThan(0, "the scan has to be finding instructions at all");

        wrong.Should().BeEmpty(
            "a specialist that names a command is telling an agent to run it, and an agent will");
    }

    [Fact]
    public void Every_tool_a_specialist_names_is_one_the_server_offers()
    {
        // Read off the attributes rather than a list kept beside them: a list
        // is one more thing to forget when a tool gets renamed, which is the
        // failure this test exists to catch.
        var offered = typeof(Program).Assembly
            .GetTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(method => method.GetCustomAttributes(inherit: false))
            .Where(attribute => attribute.GetType().Name == "McpServerToolAttribute")
            .Select(attribute => attribute.GetType()
                .GetProperty("Name")?.GetValue(attribute) as string)
            .Where(name => name is { Length: > 0 })
            .ToHashSet(StringComparer.Ordinal);

        offered.Should().NotBeEmpty("the server has to be declaring tools for this to check anything");

        var wrong = new List<string>();

        foreach (var file in Specialists())
        {
            foreach (Match match in ToolCall.Matches(File.ReadAllText(file)))
            {
                if (!offered.Contains(match.Groups[1].Value))
                {
                    wrong.Add($"{Path.GetFileName(file)}: {match.Groups[1].Value}");
                }
            }
        }

        wrong.Should().BeEmpty(
            "telling an agent to call a tool that is not served wastes its turn on an error");
    }

    private static IEnumerable<string> Specialists()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository's src directory has to be findable from the tests");

        var library = Path.Combine(root!.FullName, "src", "Loadout.Core", "Specialists");

        Directory.Exists(library).Should().BeTrue("the shipped library has to be findable");

        return Directory.EnumerateFiles(library, "*.md", SearchOption.AllDirectories);
    }
}
