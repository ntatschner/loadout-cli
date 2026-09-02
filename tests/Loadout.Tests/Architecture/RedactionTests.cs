using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Credentials reaching the screen, from either surface.
/// </summary>
/// <remarks>
/// <para>
/// The command line redacts every failure it prints, at one place. The launcher
/// had no redaction anywhere: forty renders of an error or a remote went to the
/// screen intact, including the workspace remote on the settings screen and in
/// the setup wizard. Most of those errors are Git's own stderr, and Git quotes
/// the remote it could not reach when authentication fails.
/// </para>
/// <para>
/// Runtime tests cover the settings screen and every command that runs without
/// arguments. This covers the rest and everything written after them, because
/// the failure mode is not that the fix was wrong — it is that the next screen
/// is written by somebody who has not read this.
/// </para>
/// </remarks>
public sealed class RedactionTests
{
    /// <summary>
    /// A render of something ending in .Error or .Remote that escapes without
    /// redacting. <c>Shown.Safely</c> does both, in that order.
    /// </summary>
    private static readonly Regex Unredacted = new(
        @"Markup\.Escape\(([^()]*\.(?:Error|Remote)[^()]*)\)",
        RegexOptions.Compiled);

    [Fact]
    public void No_screen_escapes_an_error_or_a_remote_without_redacting_it()
    {
        var offenders = new List<string>();
        var scanned = 0;

        foreach (var file in Sources())
        {
            scanned++;

            foreach (Match match in Unredacted.Matches(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value}");
            }
        }

        scanned.Should().BeGreaterThan(0, "the launcher's sources have to be findable");

        offenders.Should().BeEmpty(
            "escaping stops a bracket being read as markup and says nothing about "
            + "what the text contains — use Shown.Safely, which redacts first");
    }

    [Fact]
    public void The_helper_redacts_before_it_escapes()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "src", "Loadout.Tui", "Shown.cs"));

        // The order is load-bearing and not obvious: the redactor's placeholder
        // is "[redacted]", which is itself markup. Escaping first would leave a
        // bracket pair for Spectre to read as a style and swallow.
        text.Should().Contain("Markup.Escape(SecretRedactor.Redact(");
    }

    private static IEnumerable<string> Sources()
    {
        // Both surfaces. The launcher was the one that had none of this, but
        // the command line had sixteen of the same renders written outside
        // CommandOutput.Fail, which is the path that redacts.
        var roots = new[] { "Loadout.Tui", "Loadout.Cli" }
            .Select(name => Path.Combine(Root(), "src", name))
            .ToList();

        roots.Should().OnlyContain(path => Directory.Exists(path),
            "both surfaces' sources have to be findable");

        return roots
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .Where(path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal));
    }

    private static string Root()
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
