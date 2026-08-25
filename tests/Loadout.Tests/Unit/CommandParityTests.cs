using Loadout.Cli;
using Loadout.Tui;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Asserts the launcher can reach everything the command line can.
/// <para>
/// The launcher offered about a fifth of the command line, and the obvious fix
/// — placing each missing command in a menu by hand — creates a second list to
/// keep in step with the first. That had already gone wrong here once: the
/// allowlist guarding bare-name launch drifted silently, and the commands it
/// missed were reported as unknown project names rather than as anything wrong.
/// </para>
/// <para>
/// So the catalogue is built while commands are registered, and these tests
/// assert the two agree. A command added tomorrow is reachable without anybody
/// remembering to make it so.
/// </para>
/// </summary>
public sealed class CommandParityTests
{
    /// <summary>Configuring the parser is what fills the catalogue.</summary>
    private static IReadOnlyList<CatalogueEntry> Catalogue()
    {
        // Building the parser is the act that records everything; the names are
        // a by-product of the same pass.
        Program.CommandNames();

        return Program.RegisteredCommands();
    }

    [Fact]
    public void Every_registered_command_is_in_the_catalogue()
    {
        var catalogue = Catalogue();

        catalogue.Should().NotBeEmpty();

        // Top-level names are recorded separately, by the rewrite guard. Both
        // lists come from the same registration, so they have to agree.
        var paths = catalogue.Select(e => e.Path).ToHashSet(StringComparer.Ordinal);

        foreach (var name in Program.CommandNames())
        {
            var reachable = paths.Contains(name)
                || paths.Any(p => p.StartsWith(name + " ", StringComparison.Ordinal));

            reachable.Should().BeTrue($"'{name}' is registered and must be reachable");
        }
    }

    [Fact]
    public void The_catalogue_covers_every_branch()
    {
        var groups = Catalogue()
            .Select(e => e.Group)
            .Where(g => g.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        // Every branch registered on the command line. Missing one means a
        // whole family of commands the launcher cannot reach.
        groups.Should().Contain(
            ["backup", "memory", "rules", "config", "mcp", "repo", "profile", "project", "workspace", "secret"]);
    }

    [Fact]
    public void Sub_commands_are_recorded_with_their_full_path()
    {
        var paths = Catalogue().Select(e => e.Path).ToList();

        // What somebody would type, so the palette can hand it straight back to
        // the parser without reassembling anything.
        paths.Should().Contain("memory compress");
        paths.Should().Contain("mcp list");
        paths.Should().Contain("backup restore");
    }

    [Fact]
    public void Every_command_either_runs_or_says_why_not()
    {
        // Hiding a command that cannot work in a menu would make parity a
        // judgement rather than something this test can check.
        foreach (var entry in Catalogue())
        {
            var accounted = entry.Runnable || entry.TerminalOnly is { Length: > 0 };

            accounted.Should().BeTrue($"'{entry.Path}' must run or explain itself");
        }
    }

    [Fact]
    public void The_commands_that_belong_on_a_terminal_are_marked()
    {
        var catalogue = Catalogue();

        string? ReasonFor(string path) =>
            catalogue.FirstOrDefault(e => e.Path == path)?.TerminalOnly;

        // completion writes a script to be piped somewhere, and the status line
        // is run by the agent several times a minute. Neither is a menu action.
        ReasonFor("completion").Should().NotBeNullOrEmpty();
        ReasonFor("statusline").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Ordinary_commands_are_not_marked_as_terminal_only()
    {
        var catalogue = Catalogue();

        foreach (var path in new[] { "drift", "doctor", "memory compress", "mcp list" })
        {
            catalogue.Single(e => e.Path == path).Runnable
                .Should().BeTrue($"'{path}' is exactly what the launcher should be able to run");
        }
    }

    [Fact]
    public void Every_command_carries_a_description()
    {
        // The palette shows this beside the name. An empty one leaves somebody
        // guessing what they are about to run.
        Catalogue().Should().OnlyContain(e => e.Description.Length > 0);
    }

    [Fact]
    public void Every_command_the_launcher_runs_for_you_exists()
    {
        var paths = Catalogue().Select(e => e.Path).ToHashSet(StringComparer.Ordinal);

        // The launcher types these on somebody's behalf, so they are a second
        // list written by hand — exactly what the catalogue exists to avoid,
        // one layer up. It had already drifted: the settings entry ran
        // "config show" and the command is "config list", which failed as
        // "Unknown command" and read like the user's mistake rather than ours.
        foreach (var command in Loadout.Tui.Terminal.LauncherCommands.All)
        {
            paths.Should().Contain(
                command,
                $"the launcher runs '{command}', so it has to be a real command");
        }
    }

    [Fact]
    public void Every_top_level_command_declares_a_category()
    {
        var catalogue = Catalogue();

        // Sub-commands take their branch's category; a branch itself must say.
        var topLevel = catalogue
            .Where(e => !e.Path.Contains(' ', StringComparison.Ordinal))
            .ToList();

        topLevel.Should().NotBeEmpty();

        var uncategorised = topLevel
            .Where(e => e.Category.Length == 0)
            .Select(e => e.Path)
            .ToList();

        // Without this the next command added is invisible to grouped help and
        // lands in whatever bucket the renderer uses for the ones it does not
        // recognise — which is how a command comes to exist and be unfindable.
        uncategorised.Should().BeEmpty(
            "these commands declare no category: " + string.Join(", ", uncategorised));
    }

    [Fact]
    public void A_category_is_one_of_the_known_ones()
    {
        var known = Loadout.Tui.CommandCategory.All.ToHashSet(StringComparer.Ordinal);

        foreach (var entry in Catalogue().Where(e => e.Category.Length > 0))
        {
            known.Should().Contain(
                entry.Category,
                $"'{entry.Path}' is filed under a category nothing else knows about");
        }
    }

    [Fact]
    public void Searching_by_intent_finds_a_command_by_what_it_is_for()
    {
        var catalogue = Catalogue();

        // The point of intent words: nobody wanting to undo a mistake searches
        // for "backup restore".
        catalogue.Where(e => e.Matches("undo")).Select(e => e.Path)
            .Should().Contain(p => p.StartsWith("backup", StringComparison.Ordinal));

        catalogue.Where(e => e.Matches("broken")).Select(e => e.Path)
            .Should().Contain("doctor");

        catalogue.Where(e => e.Matches("vscode")).Select(e => e.Path)
            .Should().Contain("code");
    }

    [Fact]
    public void Configuring_twice_does_not_duplicate_the_catalogue()
    {
        var first = Catalogue().Count;

        Program.CommandNames();

        // The palette builds a parser each time it runs a command, and a
        // catalogue that grew on every use would show each command twice, then
        // three times.
        Catalogue().Count.Should().Be(first);
    }
}
