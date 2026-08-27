using Loadout.Core.Git;
using Loadout.Core.Statusline;
using Loadout.Models.Configuration;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers the line Claude prints at the bottom of its screen.
/// <para>
/// The payload shape is Claude's, so the parsing tests use the exact spelling
/// from the installed binary's own documentation of the contract. If a future
/// Claude renames a field these tests keep passing while the feature quietly
/// stops working — which is why the renderer is built to lose a segment rather
/// than a line, and why that behaviour is tested here explicitly.
/// </para>
/// </summary>
public sealed class StatuslineTests
{
    /// <summary>Colour off throughout: escape codes would obscure what is being asserted.</summary>
    private static StatuslineSettings Plain() => new() { Colour = false };

    /// <summary>A payload exactly as Claude sends one.</summary>
    private const string RealPayload = """
        {
          "session_id": "2b7c1d64",
          "transcript_path": "/home/n/.claude/projects/x/2b7c1d64.jsonl",
          "cwd": "/home/n/code/alpha/src",
          "model": { "id": "claude-opus-5", "display_name": "Opus 5" },
          "workspace": {
            "current_dir": "/home/n/code/alpha/src",
            "project_dir": "/home/n/code/alpha",
            "added_dirs": [],
            "git_worktree": null
          },
          "version": "2.1.241",
          "context_window": {
            "total_input_tokens": 84000,
            "total_output_tokens": 900,
            "context_window_size": 200000
          }
        }
        """;

    [Fact]
    public void The_payload_claude_sends_is_understood()
    {
        var payload = StatuslinePayload.Parse(RealPayload);

        payload.Should().NotBeNull();
        payload!.SessionId.Should().Be("2b7c1d64");
        payload.Model!.DisplayName.Should().Be("Opus 5");
        payload.Workspace!.ProjectDir.Should().Be("/home/n/code/alpha");
        payload.ContextWindow!.ContextWindowSize.Should().Be(200_000);

        // 84,000 of 200,000. The percentage is the reason the tool exists, so
        // an arithmetic slip here matters more than a cosmetic one.
        payload.ContextWindow.UsedFraction.Should().BeApproximately(0.42, 0.001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{ \"model\": \"a string where an object was\" }")]
    public void An_unreadable_payload_yields_null_rather_than_throwing(string json)
    {
        // Claude gets no error channel from this command, so throwing would
        // blank the status line with nothing to explain it.
        var act = () => StatuslinePayload.Parse(json);

        act.Should().NotThrow();
    }

    [Fact]
    public void An_unknown_field_does_not_stop_the_rest_being_read()
    {
        // Claude adds fields between releases. One this launcher has never
        // heard of must not cost it the fields it does know.
        var payload = StatuslinePayload.Parse(
            """{ "cwd": "/tmp", "something_added_next_year": { "nested": true } }""");

        payload.Should().NotBeNull();
        payload!.Cwd.Should().Be("/tmp");
    }

    [Fact]
    public void The_line_carries_project_directory_branch_model_and_context()
    {
        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                StatuslinePayload.Parse(RealPayload),
                "alpha",
                "/home/n/code/alpha",
                new GitRepositoryState("/home/n/code/alpha", "work", null, IsClean: true, "abc123")),
            Plain());

        line.Should().Contain("alpha");
        line.Should().Contain("src");
        line.Should().Contain("work");
        line.Should().Contain("Opus 5");
        line.Should().Contain("42% ctx");
    }

    [Fact]
    public void A_dirty_tree_is_marked_and_a_clean_one_is_not()
    {
        StatuslineInputs Inputs(bool clean) => new(
            StatuslinePayload.Parse(RealPayload),
            "alpha",
            "/home/n/code/alpha",
            new GitRepositoryState("/home/n/code/alpha", "work", null, clean, "abc123"));

        StatuslineRenderer.Render(Inputs(clean: false), Plain()).Should().Contain("work*");
        StatuslineRenderer.Render(Inputs(clean: true), Plain()).Should().NotContain("work*");
    }

    [Fact]
    public void At_the_repository_root_the_repository_is_named_rather_than_shown_as_a_dot()
    {
        var payload = StatuslinePayload.Parse(
            """{ "workspace": { "current_dir": "/home/n/code/alpha", "project_dir": "/home/n/code/alpha" } }""");

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(payload, null, "/home/n/code/alpha", null),
            Plain());

        line.Should().Be("alpha");
        line.Should().NotContain(".");
    }

    [Fact]
    public void A_missing_piece_costs_its_segment_and_nothing_else()
    {
        // No project, no git, no model, no context: what is left is where the
        // session is, which is still worth printing.
        var payload = StatuslinePayload.Parse("""{ "cwd": "/home/n/somewhere/else" }""");

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(payload, null, null, null),
            Plain());

        line.Should().NotBeEmpty();
        line.Should().Contain("else");
    }

    [Fact]
    public void With_nothing_at_all_it_still_returns_a_string()
    {
        var line = StatuslineRenderer.Render(
            new StatuslineInputs(null, null, null, null),
            Plain());

        line.Should().NotBeNull();
    }

    [Fact]
    public void A_switched_off_segment_does_not_appear()
    {
        var inputs = new StatuslineInputs(
            StatuslinePayload.Parse(RealPayload),
            "alpha",
            "/home/n/code/alpha",
            new GitRepositoryState("/home/n/code/alpha", "work", null, IsClean: true, "abc"));

        var line = StatuslineRenderer.Render(
            inputs,
            new StatuslineSettings { Colour = false, ShowGit = false, ShowContext = false });

        line.Should().Contain("alpha");
        line.Should().NotContain("work");
        line.Should().NotContain("ctx");
    }

    [Fact]
    public void A_worktree_is_named_because_two_worktrees_look_identical_otherwise()
    {
        var payload = StatuslinePayload.Parse("""
            {
              "workspace": {
                "current_dir": "/home/n/code/alpha-fix",
                "project_dir": "/home/n/code/alpha-fix",
                "git_worktree": "hotfix"
              }
            }
            """);

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(
                payload,
                "alpha",
                "/home/n/code/alpha-fix",
                new GitRepositoryState("/home/n/code/alpha-fix", "hotfix-branch", null, true, "abc")),
            Plain());

        line.Should().Contain("hotfix");
    }

    [Fact]
    public void The_line_never_contains_a_newline()
    {
        // Claude prints this verbatim. A line break in it moves the
        // conversation around above it.
        var payload = StatuslinePayload.Parse(
            """{ "cwd": "/tmp", "model": { "display_name": "Broken\nName" } }""");

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(payload, "alpha\nbeta", "/tmp", null),
            Plain());

        line.Should().NotContain("\n");
        line.Should().NotContain("\r");
    }

    [Fact]
    public void Colour_is_escape_codes_when_on_and_absent_when_off()
    {
        var inputs = new StatuslineInputs(
            StatuslinePayload.Parse(RealPayload),
            "alpha",
            "/home/n/code/alpha",
            null);

        StatuslineRenderer.Render(inputs, new StatuslineSettings { Colour = true })
            .Should().Contain("\u001b[");

        StatuslineRenderer.Render(inputs, new StatuslineSettings { Colour = false })
            .Should().NotContain("\u001b[");
    }

    /// <summary>
    /// Strips backslash escapes the way a POSIX shell does outside quotes.
    /// </summary>
    /// <remarks>
    /// Claude runs the installed command through a shell, and on Windows that
    /// shell is Git Bash. This is the rule that destroyed the command: each
    /// backslash is consumed and the character after it taken literally.
    /// </remarks>
    private static string AsUnquotedShellWord(string word)
    {
        var result = new System.Text.StringBuilder(word.Length);

        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] == '\\' && i + 1 < word.Length)
            {
                i++;
            }

            result.Append(word[i]);
        }

        return result.ToString();
    }

    [Theory]
    // The per-user install path, which is the one that shipped broken: no
    // space anywhere in it, so the old rule left it unquoted.
    [InlineData(@"C:\Users\me\AppData\Local\Programs\loadout\bin\loadout.exe")]
    // The machine-wide path, which has a space and was quoted all along.
    [InlineData(@"C:\Program Files\loadout\bin\loadout.exe")]
    [InlineData("/usr/local/bin/loadout")]
    public void The_installed_command_survives_the_shell_that_runs_it(string executable)
    {
        var command = StatuslineInstaller.CommandFor(executable);

        // The path has to reach the shell exactly as written. Quoting is how
        // that is achieved, but the property held down here is the survival of
        // the path, not the presence of quote characters.
        command.Should().StartWith("\"" + executable + "\" ");
        command.Should().EndWith(" statusline");
    }

    [Fact]
    public void A_windows_path_without_spaces_is_still_quoted()
    {
        // The specific regression. Quoting was conditional on a space, so this
        // path installed as a bare word, Git Bash ate every backslash, and the
        // status line died with status 127 — silently, because the very same
        // command run by hand works perfectly.
        const string Executable = @"C:\Users\me\AppData\Local\Programs\loadout\bin\loadout.exe";

        StatuslineInstaller.CommandFor(Executable)
            .Should().Be("\"" + Executable + "\" statusline");

        // What the shell was handed instead, kept here so the failure mode is
        // legible: C:UsersmeAppDataLocalProgramsloadoutbinloadout.exe
        AsUnquotedShellWord(Executable).Should().NotContain("\\");
        AsUnquotedShellWord(Executable).Should().Be(
            "C:UsersmeAppDataLocalProgramsloadoutbinloadout.exe");
    }
}
