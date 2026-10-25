using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Every command that reads what is piped into it is one the command line
/// keeps for itself while something is.
/// </summary>
/// <remarks>
/// A command handed to the daemon reads an empty standard input, because the
/// terminal's stays in the terminal. One that reads it and is not listed would
/// be handed over from a pipe, find nothing there, and act on nothing without
/// a word.
/// </remarks>
public sealed partial class ForwardedInputTests
{
    [GeneratedRegex(@"\bConsole\.In\b|\bBatchInput\.ReadAsync\(")]
    private static partial Regex ReadsInput();

    [GeneratedRegex(@"\bclass\s+(\w+Command)\b")]
    private static partial Regex Command();

    [Fact]
    public void Every_command_reading_standard_input_stays_in_the_terminal_while_it_is_piped()
    {
        Program.CommandNames().Should().NotBeEmpty("the catalogue is filled by registering the commands");

        var paths = Program.RegisteredCommands()
            .Select(entry => (entry.Path, Type: Catalogue.TypeOf(entry.Path)))
            .Where(one => one.Type is not null)
            .ToLookup(one => one.Type!.Name, one => one.Path);

        var commands = Path.Combine(RepositoryRoot(), "src", "Loadout.Cli", "Commands");
        var unlisted = new List<string>();

        foreach (var file in Directory.GetFiles(commands, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            string? owner = null;

            for (var i = 0; i < lines.Length; i++)
            {
                if (Command().Match(lines[i]) is { Success: true } declared)
                {
                    owner = declared.Groups[1].Value;
                }

                if (lines[i].TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || !ReadsInput().IsMatch(lines[i])
                    || owner is null)
                {
                    continue;
                }

                foreach (var path in paths[owner])
                {
                    // Kept here already, whatever is piped.
                    if (!Forwarding.Forwardable(Program.Rewrite(path.Split(' ')), inputRedirected: false, local: null))
                    {
                        continue;
                    }

                    if (!Forwarding.ReadsInput.Contains(path))
                    {
                        unlisted.Add($"loadout {path} ({Path.GetFileName(file)}:{i + 1})");
                    }
                }
            }
        }

        unlisted.Distinct().Should().BeEmpty(
            "a command that reads standard input belongs in Forwarding.ReadsInput, or a daemon runs it with nothing to read");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        // A directory in an ordinary checkout, a file in a git worktree.
        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }
}
