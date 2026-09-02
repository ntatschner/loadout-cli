using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Agents;
using Loadout.Platform.Abstractions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Finding Claude Code's own directory when it has been moved.
/// </summary>
/// <remarks>
/// <para>
/// The agent reads <c>CLAUDE_CONFIG_DIR</c>, and people use it to keep the
/// directory off a synced home. Six places here needed that path and two
/// honoured the variable: memory import and project attribution worked, while
/// <c>usage</c>, <c>sessions</c>, the MCP reader and <c>statusline install</c>
/// each built <c>~/.claude</c> by hand.
/// </para>
/// <para>
/// The failure was silent, which is what made it worth a test rather than a
/// fix. Each of them looked at a directory that was not there, found nothing,
/// and reported nothing as though nothing existed.
/// </para>
/// </remarks>
public sealed class AgentHomeTests
{
    /// <summary>
    /// Returns exactly what it was given, empty string included.
    /// </summary>
    /// <remarks>
    /// FakeEnvironmentProvider treats an empty variable as unset, which is
    /// right for the code that uses it and wrong here: it would answer the
    /// empty-override case before AgentHome ever saw it, and the test would be
    /// checking the fake.
    /// </remarks>
    private sealed class Literal(string home, string? value) : IEnvironmentProvider
    {
        public string HomeDirectory => home;

        public string MachineName => "TEST-MACHINE";

        public IReadOnlyList<string> PathDirectories => [];

        public IReadOnlyList<string> ExecutableExtensions => [string.Empty];

        public string? GetVariable(string name) =>
            name == AgentHome.Override ? value : null;
    }

    [Fact]
    public void The_override_is_used_when_it_is_set()
    {
        var environment = new FakeEnvironmentProvider(
            Path.Combine("C:", "Users", "someone"),
            new Dictionary<string, string>
            {
                [AgentHome.Override] = Path.Combine("D:", "agent-config"),
            });

        AgentHome.Claude(environment).Should().Be(Path.Combine("D:", "agent-config"));

        AgentHome.ClaudeProjects(environment)
            .Should().Be(Path.Combine("D:", "agent-config", "projects"));

        AgentHome.ClaudeSettings(environment)
            .Should().Be(Path.Combine("D:", "agent-config", "settings.json"));
    }

    [Fact]
    public void The_home_directory_is_used_when_it_is_not()
    {
        var home = Path.Combine("C:", "Users", "someone");

        var environment = new FakeEnvironmentProvider(home);

        AgentHome.Claude(environment).Should().Be(Path.Combine(home, ".claude"));
    }

    [Fact]
    public void An_empty_override_is_ignored_rather_than_taken_literally()
    {
        var home = Path.Combine("C:", "Users", "someone");

        // An exported-but-empty variable is common in a shell profile, and
        // taking it literally sends every lookup to the filesystem root.
        AgentHome.Claude(new Literal(home, string.Empty))
            .Should().Be(Path.Combine(home, ".claude"));
    }

    [Fact]
    public void Nothing_builds_that_path_by_hand_any_more()
    {
        // The duplication was the defect. Two of six copies honoured the
        // variable, and there was no way to notice the other four short of
        // reading all of them.
        var hardCoded = new Regex(
            @"HomeDirectory\s*,\s*""\.claude""",
            RegexOptions.Compiled);

        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Sources())
        {
            scanned++;

            if (Path.GetFileName(file) == "AgentHome.cs")
            {
                continue;
            }

            if (hardCoded.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(Path.GetFileName(file));
            }
        }

        scanned.Should().BeGreaterThan(0, "the sources have to be findable");

        offenders.Should().BeEmpty("AgentHome.Claude honours CLAUDE_CONFIG_DIR and a hand-built path does not");
    }

    private static IEnumerable<string> Sources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);

        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "src")))
        {
            root = root.Parent;
        }

        root.Should().NotBeNull("the repository has to be findable from the tests");

        return Directory
            .EnumerateFiles(Path.Combine(root!.FullName, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));
    }
}
