using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Cli;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// Whether Studio reaches every command, which is what Studio being complete means.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="StudioCoverage"/> says where Studio offers each command. These
/// hold it to the catalogue in both directions, hold each page it names to
/// actually showing the command, and pin what is still reachable only from
/// the Commands page's generic form. That list may only shrink: a page built
/// for an area moves its commands out of it, and Studio is complete when it
/// is empty.
/// </para>
/// </remarks>
public sealed partial class StudioCoverageTests
{
    /// <summary>
    /// The commands Studio reaches only through the Commands page's generic
    /// form so far. Remove a line when a page does the command; never add
    /// one, except for a command that is new.
    /// </summary>
    private static readonly string[] NotYetOnAPage =
    [
        "backup list",
        "backup restore",
        "checkpoint create",
        "checkpoint list",
        "checkpoint remove",
        "checkpoint restore",
        "code",
        "commands",
        "config edit",
        "config get",
        "config list",
        "config set",
        "daemon autostart disable",
        "daemon autostart enable",
        "daemon autostart show",
        "daemon continue",
        "daemon pause",
        "daemon restart",
        "daemon resume",
        "daemon stop",
        "desktop",
        "docs audit",
        "docs ci",
        "docs export",
        "docs find",
        "docs refresh",
        "doctor",
        "drift",
        "handoff",
        "idea accept",
        "idea add",
        "idea answer",
        "idea choose",
        "idea dismiss",
        "idea drop",
        "idea dump add",
        "idea dump apply",
        "idea dump list",
        "idea dump show",
        "idea dump split",
        "idea improve",
        "idea keep",
        "idea list",
        "idea refine",
        "idea remove",
        "idea show",
        "instructions audit",
        "instructions explain",
        "instructions export",
        "instructions list",
        "instructions new",
        "instructions probe",
        "instructions show",
        "instructions stats",
        "instructions validate",
        "launches",
        "list",
        "mcp add",
        "mcp list",
        "mcp remove",
        "memory audit",
        "memory compress",
        "memory delete",
        "memory find",
        "memory import",
        "memory list",
        "memory reindex",
        "memory review",
        "memory show",
        "memory write",
        "migrate",
        "pack add",
        "pack approve",
        "pack list",
        "pack remove",
        "pack update",
        "profile list",
        "project add",
        "project clone",
        "project context",
        "project discover",
        "project link",
        "project list",
        "project new",
        "project onboard",
        "project open",
        "project proposal",
        "project relocate",
        "project remove",
        "project show",
        "project survey",
        "project worktrees",
        "protect",
        "repo check",
        "resume",
        "rules audit",
        "rules budget",
        "rules list",
        "rules split",
        "running",
        "secret remove",
        "secret test",
        "sessions",
        "setup",
        "share candidates",
        "share promote",
        "spend refresh",
        "start",
        "status",
        "style list",
        "style show",
        "style use",
        "task declare",
        "task list",
        "task remove",
        "team attach show",
        "team bin",
        "team bin empty",
        "team budget",
        "team capabilities",
        "team edit",
        "team list",
        "team log",
        "team message",
        "team name",
        "team new",
        "team notify clear",
        "team notify set",
        "team notify show",
        "team notify test",
        "team office check",
        "team outbox",
        "team pr",
        "team remedies",
        "team remedy requests",
        "team remedy show",
        "team remove",
        "team restore",
        "team resume",
        "team run",
        "team runs",
        "team runs prune",
        "team runs remove",
        "team runs restore",
        "team say",
        "team schedule add",
        "team schedule list",
        "team schedule remove",
        "team show",
        "team status",
        "team webhook disable",
        "team webhook enable",
        "team webhook show",
        "telemetry status",
        "tools audit",
        "tools deprecate",
        "tools health",
        "tools promote",
        "tools retire",
        "tools search",
        "tools show",
        "tools submit",
        "tools used",
        "tools verify",
        "usage",
        "voice check",
        "voice guide draft",
        "voice guide list",
        "voice guide show",
        "voice learn",
        "voice mark",
        "voice render",
        "voice runs",
        "voice show",
        "workspace open",
        "workspace save",
        "workspace status",
        "workspace sync",
    ];

    /// <summary>The pages a command can be placed on: Studio's router's, and the Commands page.</summary>
    private static IReadOnlySet<string> Pages()
    {
        var router = File.ReadAllText(Path.Combine(RepositoryRoot(), "studio", "src", "lib", "router.svelte.ts"));
        var listed = PageList().Match(router);

        listed.Success.Should().BeTrue("the router lists its pages in a PAGES array");

        return Quoted().Matches(listed.Groups[1].Value).Select(match => match.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> Runnable()
    {
        Program.CommandNames().Should().NotBeEmpty("the catalogue is filled by registering the commands");

        return Program.RegisteredCommands()
            .Select(entry => entry.Path)
            .Where(path => Catalogue.TypeOf(path) is not null)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_command_is_placed_and_nothing_else_is()
    {
        var runnable = Runnable();
        var placed = StudioCoverage.Reach.Keys.ToHashSet(StringComparer.Ordinal);

        var missing = runnable.Except(placed).Order(StringComparer.Ordinal).ToList();
        var extra = placed.Except(runnable).Order(StringComparer.Ordinal).ToList();

        missing.Should().BeEmpty(
            "a command Studio has not been told about is missing from the page without anybody deciding it should be: "
            + string.Join(", ", missing));

        extra.Should().BeEmpty(
            "a command that no longer exists, or a branch that only groups others, has nothing to run: "
            + string.Join(", ", extra));
    }

    [Fact]
    public void Every_page_a_command_is_placed_on_exists()
    {
        var pages = Pages();

        foreach (var (path, reach) in StudioCoverage.Reach)
        {
            (reach.Page == StudioCoverage.CommandsPage || pages.Contains(reach.Page)).Should().BeTrue(
                $"'{path}' is placed on '{reach.Page}', which is not one of Studio's pages");
        }
    }

    [Fact]
    public void Only_a_terminal_only_command_says_why_and_every_one_does()
    {
        foreach (var (path, reach) in StudioCoverage.Reach)
        {
            if (reach.Effect == StudioEffect.TerminalOnly)
            {
                reach.Reason.Should().NotBeNullOrWhiteSpace($"'{path}' is left to the terminal, and the page says why");
            }
            else
            {
                reach.Reason.Should().BeNull($"'{path}' runs from Studio, so there is nothing to explain");
            }
        }
    }

    [Fact]
    public void Every_command_placed_on_a_page_is_shown_there()
    {
        // Each page shows the command it runs, as the design asks of every
        // action, so a page that does a command names it. The words are
        // looked for anywhere in Studio's sources, since a page builds its
        // commands through shared helpers; what this catches is a placement
        // that nothing on any page backs.
        var sources = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot(), "studio", "src"), "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".svelte", StringComparison.Ordinal)
                || (file.EndsWith(".ts", StringComparison.Ordinal) && !file.EndsWith(".test.ts", StringComparison.Ordinal)))
            .Select(File.ReadAllText));

        foreach (var (path, reach) in StudioCoverage.Reach.Where(one => one.Value.Page != StudioCoverage.CommandsPage))
        {
            Regex.IsMatch(sources, "loadout " + Regex.Escape(path) + "(?![a-z-])").Should().BeTrue(
                $"'{path}' is placed on the {reach.Page} page, so Studio has to show 'loadout {path}' somewhere");
        }
    }

    [Fact]
    public void What_is_left_for_the_commands_page_only_shrinks()
    {
        var left = StudioCoverage.Reach
            .Where(one => one.Value.Page == StudioCoverage.CommandsPage && one.Value.Effect != StudioEffect.TerminalOnly)
            .Select(one => one.Key)
            .ToHashSet(StringComparer.Ordinal);

        var back = left.Except(NotYetOnAPage).Order(StringComparer.Ordinal).ToList();
        var done = NotYetOnAPage.Except(left).Order(StringComparer.Ordinal).ToList();

        back.Should().BeEmpty(
            "a command that a page already did has gone back to the generic form, or a new command was not placed on a page: "
            + string.Join(", ", back));

        done.Should().BeEmpty(
            "a page now does these, so they come off the list, and Studio is complete when it is empty: "
            + string.Join(", ", done));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Path.Exists(Path.Combine(directory.FullName, ".git")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The repository root could not be found from the test output directory.");
    }

    [GeneratedRegex(@"const PAGES: PageKey\[\] = \[([^\]]*)\]")]
    private static partial Regex PageList();

    [GeneratedRegex("'([^']+)'")]
    private static partial Regex Quoted();
}
