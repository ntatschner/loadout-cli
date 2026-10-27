using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A node's permission question in words somebody can answer.
/// </summary>
/// <remarks>
/// <para>
/// The question used to be the raw command and nothing else, so whoever was
/// asked had to read the shell to know what they were agreeing to, and the
/// part the role refused was nowhere in it.
/// </para>
/// <para>
/// The description is the supervised agent's own, so these hold the line it
/// must never cross: the command is always shown beside it, and the warnings
/// come from the command whatever the description claims.
/// </para>
/// </remarks>
public sealed class AskWordingTests
{
    private const string Chained = "ls build && rm -rf build";

    private static readonly NodePolicy Builder = new(
        "20261026-1200-abcd", "implementer", "builder", ["Bash(ls:*)", "Read"], []);

    private static string Input(object input) => JsonSerializer.Serialize(input);

    // An ask as the MCP answerer writes one, from the call and the decision.
    private static PendingAsk Asked(string tool, string inputJson, PermissionDecision? decision = null) =>
        new(
            "implementer-1",
            "implementer",
            "builder",
            tool,
            NodePermissions.Target(inputJson),
            DateTimeOffset.UtcNow,
            Said: AskWording.Said(tool, inputJson),
            Blocked: decision?.Blocked,
            Flags: AskWording.Flags(tool, NodePermissions.Target(inputJson)) is { Count: > 0 } flags ? flags : null);

    [Fact]
    public void A_described_chained_command_reads_as_its_description_with_the_refused_part_named()
    {
        var input = Input(new { command = Chained, description = "List the build output" });
        var decision = NodePermissions.Decide(Builder, "Bash", input);

        decision.Allowed.Should().BeFalse();
        decision.Blocked.Should().Be("rm -rf build", "the policy already knew which part it refused");

        var view = AskWording.View(Asked("Bash", input, decision))!;

        view.Line.Should().Be("implementer (builder) wants to list the build output");
        view.Blocked.Should().Be("rm -rf build");
        view.Flags.Should().Equal(AskWording.Deletes);
        view.Command.Should().Be(Chained);

        // Set apart: on a line of its own, below the words.
        view.Text.Split('\n').Should().Contain("    " + Chained);
        view.Text.Should().Contain("Not allowed by its role: rm -rf build");
        view.Text.Should().Contain("Careful: the command deletes files.");
    }

    [Fact]
    public void The_one_sentence_question_carries_every_part()
    {
        var input = Input(new { command = Chained, description = "List the build output" });
        var ask = Asked("Bash", input, NodePermissions.Decide(Builder, "Bash", input));

        ask.Question.Should().Be(
            "implementer (builder) wants to list the build output. Its role does not allow 'rm -rf build'. "
            + "Careful: the command deletes files. The command: 'ls build && rm -rf build'. Let it");
    }

    [Fact]
    public void The_brief_question_keeps_the_line_and_the_warning_and_leaves_the_command_to_the_dialog()
    {
        var input = Input(new { command = Chained, description = "List the build output" });
        var ask = Asked("Bash", input, NodePermissions.Decide(Builder, "Bash", input));

        ask.Brief.Should().Be(
            "implementer (builder) wants to list the build output. Careful: the command deletes files. Let it?");
    }

    [Fact]
    public void An_ask_with_no_wording_is_brief_as_it_always_was()
    {
        var ask = Asked("mcp__github__create_issue", Input(new { title = "x" }));

        ask.Brief.Should().Be(ask.Asking);
    }

    [Fact]
    public void A_description_claiming_to_only_read_does_not_hide_a_delete()
    {
        var input = Input(new { command = "rm -rf src", description = "Read only: list the files" });

        var view = AskWording.View(Asked("Bash", input))!;

        view.Flags.Should().Contain(AskWording.Deletes);
        view.Command.Should().Be("rm -rf src", "the description never replaces the command");
    }

    [Theory]
    [InlineData("Edit", "file_path", "src/Foo.cs", "implementer (builder) wants to change src/Foo.cs")]
    [InlineData("MultiEdit", "file_path", "src/Foo.cs", "implementer (builder) wants to change src/Foo.cs")]
    [InlineData("Write", "file_path", "src/New.cs", "implementer (builder) wants to create or overwrite src/New.cs")]
    [InlineData("Read", "file_path", "secrets.txt", "implementer (builder) wants to read secrets.txt")]
    [InlineData("WebFetch", "url", "https://example.com/docs/page", "implementer (builder) wants to fetch a page from example.com")]
    [InlineData("WebSearch", "query", "terminal gui", "implementer (builder) wants to search the web for 'terminal gui'")]
    [InlineData("Glob", "pattern", "**/*.cs", "implementer (builder) wants to look for files matching **/*.cs")]
    [InlineData("Grep", "pattern", "TODO", "implementer (builder) wants to search files for 'TODO'")]
    public void A_built_in_tool_reads_as_a_plain_sentence(string tool, string field, string value, string line)
    {
        var ask = Asked(tool, Input(new Dictionary<string, string> { [field] = value }));

        AskWording.View(ask)!.Line.Should().Be(line);
        ask.Question.Should().StartWith(line + ". Nothing in its role allows that.");
    }

    [Fact]
    public void A_shell_call_with_no_description_names_what_each_part_runs()
    {
        var input = Input(new { command = "cd src && git status && dotnet test --no-build" });

        AskWording.Said("Bash", input).Should().Be("run git status, then dotnet test");
    }

    [Fact]
    public void An_MCP_tool_keeps_the_sentence_it_always_had()
    {
        var ask = Asked("mcp__github__create_issue", Input(new { title = "x" }));

        ask.Said.Should().BeNull();
        AskWording.View(ask).Should().BeNull();
        ask.Question.Should().Be(
            "implementer (builder) wants to use mcp__github__create_issue. Nothing in its role allows that. Let it");
    }

    [Fact]
    public void An_ask_written_before_this_was_carried_still_loads_and_reads_as_before()
    {
        // The shape asked files had until now: no Said, Blocked or Flags.
        const string Old = """
            {"Id":"implementer-1","Node":"implementer","Role":"builder","Tool":"Bash","Target":"ls build && rm -rf build","At":"2026-10-20T10:00:00+00:00","Kind":"permission"}
            """;

        var ask = JsonSerializer.Deserialize<PendingAsk>(Old)!;

        AskWording.View(ask).Should().BeNull();
        ask.Question.Should().Be(
            "implementer (builder) wants to use Bash for 'ls build && rm -rf build'. Nothing in its role allows that. Let it");
    }

    [Fact]
    public void A_new_ask_keeps_the_raw_target_that_dont_ask_again_rules_match()
    {
        var input = Input(new { command = "git status", description = "Check the tree" });
        var ask = Asked("Bash", input);

        var written = JsonSerializer.Deserialize<PendingAsk>(JsonSerializer.Serialize(ask))!;

        written.Target.Should().Be("git status");
        written.Said.Should().Be("check the tree");
        NodePermissions.Rememberable(written.Tool, written.Target).Should().Be("Bash(git status:*)");
    }

    [Theory]
    [InlineData("echo hi > out.txt", AskWording.Writes)]
    [InlineData("git commit -m done", AskWording.Writes)]
    [InlineData("Set-Content a.txt hi", AskWording.Writes)]
    [InlineData("sed -i s/a/b/ f", AskWording.Writes)]
    [InlineData("rm -rf build", AskWording.Deletes)]
    [InlineData("Remove-Item -Recurse build", AskWording.Deletes)]
    [InlineData("git reset --hard HEAD~1", AskWording.Deletes)]
    [InlineData("git branch -D old", AskWording.Deletes)]
    [InlineData("sudo rm x", AskWording.Deletes)]
    [InlineData("gh pr merge 12", AskWording.Pushes)]
    [InlineData("npm publish", AskWording.Pushes)]
    [InlineData("dotnet nuget push pkg.nupkg", AskWording.Pushes)]
    [InlineData("curl https://example.com", AskWording.Network)]
    [InlineData("Invoke-WebRequest https://example.com", AskWording.Network)]
    [InlineData("gh api repos/x/y", AskWording.Network)]
    [InlineData("git -C ../other push origin main", AskWording.Pushes)]
    public void A_command_is_flagged_for_what_it_does(string command, string flag)
    {
        AskWording.Flags("Bash", command).Should().Contain(flag);
    }

    [Theory]
    [InlineData("git status")]
    [InlineData("ls -la build 2>&1")]
    [InlineData("dotnet test > /dev/null")]
    [InlineData("grep -rn rm src")]
    [InlineData("git log --oneline")]
    public void A_command_that_only_looks_is_not_flagged(string command)
    {
        AskWording.Flags("Bash", command).Should().BeEmpty();
    }

    [Fact]
    public void A_push_is_flagged_as_a_push_and_as_the_network()
    {
        AskWording.Flags("Bash", "git push origin main")
            .Should().Equal(AskWording.Pushes, AskWording.Network);
    }

    [Fact]
    public void Flags_are_for_shell_commands_alone()
    {
        AskWording.Flags("Edit", "rm -rf build").Should().BeEmpty();
    }

    [Fact]
    public void A_description_is_one_short_line_with_no_backticks()
    {
        var long_ = new string('a', 300);

        AskWording.Clean("Line one\nline two\t``` closes a fence").Should().Be("Line one line two closes a fence");
        AskWording.Clean(long_)!.Length.Should().Be(AskWording.DescriptionLimit + 1, "cut, with an ellipsis");
        AskWording.Clean(" \n ").Should().BeNull();
    }

    [Fact]
    public void A_description_with_a_secret_in_it_is_redacted()
    {
        var cleaned = AskWording.Clean("Use token ghp_abcdefghijklmnopqrstuvwxyz0123456789 to push");

        cleaned.Should().NotContain("ghp_abcdefghijklmnopqrstuvwxyz0123456789");
    }

    [Theory]
    [InlineData("Bash(git status:*)", "'git status' commands")]
    [InlineData("PowerShell(Get-ChildItem:*)", "'Get-ChildItem' commands")]
    [InlineData("Bash(ls -la build)", "'ls -la build'")]
    [InlineData("WebFetch(https://example.com/*)", "pages from https://example.com")]
    [InlineData("Edit", "Edit")]
    public void Dont_ask_again_reads_in_words(string rule, string words)
    {
        NodePermissions.AlwaysOption(rule).Should().Be($"yes, and don't ask again for {words}");
    }
}
