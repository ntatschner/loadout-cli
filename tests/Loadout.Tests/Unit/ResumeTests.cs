using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Models.Projects;
using Spectre.Console;
using Loadout.Tui.Terminal;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which session <c>resume</c> is asked for, and what happens when there is no
/// such thing.
/// </summary>
/// <remarks>
/// Reported from use: Resume did not work. Two defects, one on each side of the
/// call. The launcher handed a project's slug to an argument that takes a
/// session id, and a session id that matched nothing returned the same null the
/// picker returns when somebody backs out of it — so the command printed
/// nothing and exited zero. Silence and success are the same thing from
/// outside, which is why it looked like the button was dead.
/// </remarks>
public sealed class ResumeTests
{
    private static AgentSession Session(string id, string slug = "alpha") =>
        new("claude", id, "Some conversation", "/repos/alpha", "main",
            DateTimeOffset.UnixEpoch, "/transcripts/" + id, slug);

    private static ProjectResolution Project(string slug) =>
        new(new ProjectRegistryEntry { Slug = slug, Name = slug, DefaultAgent = "claude" },
            "/repos/" + slug, null, 0, false);

    [Fact]
    public void A_session_id_that_matches_nothing_is_an_error_rather_than_silence()
    {
        var found = ResumeCommand.Match([Session("abc123"), Session("def456")], "zzz");

        found.Failed.Should().BeTrue("naming a session that does not exist cannot be a success");
        found.Error.Should().Contain("zzz");

        // The message names a command, so the command has to exist. The first
        // draft said 'loadout session list' — the command is 'loadout
        // sessions', and 'session list' answers "Could not match 'list' with
        // an argument", which is the same defect this whole fix is about:
        // telling somebody to run something that is not there.
        found.Error.Should().Contain("loadout sessions");
        found.ExitCode.Should().Be(ExitCode.ProjectNotFound);
    }

    [Fact]
    public void An_ambiguous_prefix_says_so_rather_than_picking_one()
    {
        var found = ResumeCommand.Match([Session("abc123"), Session("abc999")], "abc");

        found.Failed.Should().BeTrue();
        found.Error.Should().Contain("more of the id");
        found.ExitCode.Should().Be(ExitCode.InvalidArguments);
    }

    [Fact]
    public void A_prefix_is_enough_when_it_names_one_session()
    {
        // Nobody types a whole UUID.
        var found = ResumeCommand.Match([Session("abc123"), Session("def456")], "def");

        found.Succeeded.Should().BeTrue();
        found.Value!.SessionId.Should().Be("def456");
    }

    [Fact]
    public void A_chosen_session_is_resumed_by_its_own_id()
    {
        var arguments = TerminalLauncher.ResumeArguments(
            new LauncherIntent(LauncherAction.Resume, Project("alpha"), SessionId: "abc123"));

        arguments.Should().Equal("abc123");
    }

    [Fact]
    public void Resuming_a_project_scopes_the_list_rather_than_naming_a_session()
    {
        var arguments = TerminalLauncher.ResumeArguments(
            new LauncherIntent(LauncherAction.Resume, Project("alpha")));

        // The bug, exactly: the slug went in as the session id, matched no
        // session, and the command said nothing at all. --project is what the
        // option is for, and it puts that project's sessions in the picker.
        arguments.Should().Equal("--project", "alpha");
    }

    [Fact]
    public void Resuming_with_nothing_chosen_reaches_the_picker()
    {
        var arguments = TerminalLauncher.ResumeArguments(
            new LauncherIntent(LauncherAction.Resume));

        arguments.Should().BeEmpty();
    }

    [Fact]
    public void A_title_with_brackets_does_not_break_the_picker()
    {
        // Reported from use: choosing Resume in the launcher printed "Could not
        // find color or style 'SYSTEM'" and dropped back to it. Spectre parses
        // what a prompt's converter returns as markup, and this title is a real
        // one — the opening line of a background-task notification, which is
        // what a session gets named when that is the first thing in it.
        var session = new AgentSession(
            "claude",
            "00000000-0000-4000-8000-000000000001",
            "[SYSTEM NOTIFICATION - NOT USER INPUT] This is an automated",
            "/repos/alpha",
            "main",
            DateTimeOffset.UtcNow,
            "/transcripts/x.jsonl",
            "alpha");

        var rendered = new SessionChoice(session).Render(100);

        // Parsing is the assertion: an unescaped '[SYSTEM' throws here exactly
        // as it did in the launcher, and no amount of checking the string for
        // brackets would prove the renderer is happy with it.
        var parse = () => new Markup(rendered);

        parse.Should().NotThrow();

        // Escaped for the renderer, not mangled for the reader. Asserted on what
        // the renderer would actually print, because deleting the brackets also
        // stops the throw and a test checking only the words passes against it —
        // that mutation was run, and did pass, before this line was written.
        Markup.Remove(rendered).Should().Contain("[SYSTEM NOTIFICATION");
    }

    [Fact]
    public void An_ordinary_title_is_left_alone()
    {
        var session = new AgentSession(
            "claude",
            "00000000-0000-4000-8000-000000000002",
            "Fix the importer",
            "/repos/alpha",
            "main",
            DateTimeOffset.UtcNow,
            "/transcripts/y.jsonl",
            "alpha");

        // Escaping doubles a bracket and nothing else, so a title without one
        // comes through untouched. Without this, escaping everything twice
        // would pass the test above and quietly litter the list with '[['.
        new SessionChoice(session).Render(100).Should().Contain("Fix the importer");
    }
}
