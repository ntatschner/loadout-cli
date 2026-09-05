using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Every command line the launcher can build actually parses.
/// </summary>
/// <remarks>
/// <para>
/// The seam test next door checks that the command a menu entry names exists,
/// and its own comment says it was written to stop "the menu entry that fails
/// only when somebody picks it". A wrong option walked straight through it: the
/// launcher offered <c>launches --project &lt;slug&gt;</c>, the command takes the
/// project as a positional argument, and picking the entry printed "Unknown
/// option 'project'" over the launcher.
/// </para>
/// <para>
/// So this checks the whole line rather than the command at the front of it. It
/// runs the real binary, because the parser is the only thing that knows
/// whether a line parses and reimplementing its rules here would test the
/// reimplementation.
/// </para>
/// <para>
/// Most of these fail in a throwaway home — no project is registered — and that
/// is fine. A failure is an answer; "Unknown option" is the parser refusing to
/// get as far as one.
/// </para>
/// </remarks>
[Collection(ContractCollection.Name)]
public sealed class LauncherInvocationTests
{
    [BuiltCliFact]
    public async Task Every_command_line_the_launcher_builds_is_one_the_parser_accepts()
    {
        var invocations = Invocations();

        invocations.Should().NotBeEmpty(
            "the launcher builds command lines, and this test is worthless if it found none");

        using var loadout = new LoadoutProcess();

        var refused = new List<string>();

        foreach (var invocation in invocations)
        {
            var run = await loadout.RunAsync([.. invocation.Split(' ',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]);

            var said = run.StandardOutput + run.StandardError;

            if (said.Contains("Unknown option", StringComparison.OrdinalIgnoreCase)
                || said.Contains("Unexpected option", StringComparison.OrdinalIgnoreCase)
                || said.Contains("Unknown command", StringComparison.OrdinalIgnoreCase))
            {
                refused.Add($"{invocation}: {said.Split('\n')[0].Trim()}");
            }
        }

        refused.Should().BeEmpty(
            "a command line the launcher offers has to parse, or picking the entry prints a "
            + "parser error over the launcher and there is nothing the person can do about it");
    }

    /// <summary>
    /// The command lines the launcher builds, read out of its own source.
    /// </summary>
    /// <remarks>
    /// Read rather than listed, so that adding a menu entry adds a case here
    /// without anybody remembering to. The interpolations are filled with
    /// something plausible: what is being checked is the shape of the line, and
    /// a slug that resolves to nothing still parses or fails to.
    /// </remarks>
    private static IReadOnlyList<string> Invocations()
    {
        var source = Path.Combine(
            RepositoryRoot(), "src", "Loadout.Tui", "Terminal", "LauncherWindow.cs");

        if (!File.Exists(source))
        {
            return [];
        }

        var text = File.ReadAllText(source);
        var constants = Constants();
        var found = new List<string>();

        foreach (Match match in Regex.Matches(text, @"RunCommand\(\$""(?<line>[^""]+)""\)"))
        {
            var line = match.Groups["line"].Value;

            // {LauncherCommands.Usage} and friends become what they are; every
            // other hole becomes a plausible project name.
            line = Regex.Replace(
                line,
                @"\{LauncherCommands\.(?<name>\w+)\}",
                m => constants.TryGetValue(m.Groups["name"].Value, out var value) ? value : "?");

            line = Regex.Replace(line, @"\{[^}]+\}", "some-project").Replace("\\\"", string.Empty);

            if (!line.Contains('?', StringComparison.Ordinal))
            {
                found.Add(line.Trim());
            }
        }

        return found;
    }

    private static Dictionary<string, string> Constants() =>
        typeof(Loadout.Tui.Terminal.LauncherWindow).Assembly
            .GetType("Loadout.Tui.Terminal.LauncherCommands")!
            .GetFields(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .ToDictionary(field => field.Name, field => (string)field.GetRawConstantValue()!);

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
