using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a node may do when its agent stops to ask.
/// </summary>
/// <remarks>
/// <para>
/// Reached only when nobody is at the keyboard, which decides the shape of
/// everything here: deny wins, what nothing matches is denied, and a session
/// with no policy to answer from allows nothing at all. A launcher guessing
/// "yes" on behalf of an absent person is the worst thing this could do.
/// </para>
/// <para>
/// The rules are the role files' own spelling, which is the agent's, so the
/// examples below are lifted from the roles as they ship.
/// </para>
/// </remarks>
public sealed class NodePermissionTests
{
    private static NodePolicy Implementer() => new(
        "20260916-1200-aaaa",
        "implementer/1",
        "role.implementer",
        ["Read", "Grep", "Bash(git diff:*)", "Bash(git commit:*)", "Edit", "Write"],
        ["Bash(git push:*)", "Bash(git reset:*)"]);

    [Theory]
    [InlineData("Read", null)]
    [InlineData("Read", """{"file_path":"src/Program.cs"}""")]
    [InlineData("Bash", """{"command":"git diff --stat"}""")]
    [InlineData("Bash", """{"command":"git commit --message one"}""")]
    public void What_the_role_allows_is_allowed(string tool, string? input)
    {
        NodePermissions.Decide(Implementer(), tool, input).Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("Bash", """{"command":"git push origin main"}""")]
    [InlineData("Bash", """{"command":"git reset --hard"}""")]
    public void What_the_role_forbids_is_refused_by_name(string tool, string input)
    {
        var decision = NodePermissions.Decide(Implementer(), tool, input);

        decision.Allowed.Should().BeFalse();
        decision.Rule.Should().StartWith("Bash(git ");
        decision.Reason.Should().Contain("role.implementer").And.Contain("blocker");
    }

    [Theory]
    [InlineData("WebFetch", """{"url":"https://example.invalid"}""")]
    [InlineData("Bash", """{"command":"npm install left-pad"}""")]
    [InlineData("Bash", null)]
    public void What_nothing_names_is_refused(string tool, string? input)
    {
        // The direction that matters. With nobody to ask, silence is not
        // consent, and the reason tells the node what to do instead.
        var decision = NodePermissions.Decide(Implementer(), tool, input);

        decision.Allowed.Should().BeFalse();
        decision.Rule.Should().BeNull();
        decision.Reason.Should().Contain("Nothing in the role.implementer role allows");
    }

    [Fact]
    public void Deny_wins_over_allow()
    {
        var both = new NodePolicy("r", "n", "role.x", ["Bash(git push:*)"], ["Bash(git push:*)"]);

        NodePermissions.Decide(both, "Bash", """{"command":"git push"}""").Allowed.Should().BeFalse();
    }

    [Fact]
    public void A_session_with_no_policy_is_allowed_nothing()
    {
        // An ordinary session has a person to ask. This is only reached where
        // there is not one, so the absence of a policy is not a reason to
        // permit; it is a reason to refuse and say why.
        var decision = NodePermissions.Decide(null, "Read", null);

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain("no policy");
    }

    [Theory]
    [InlineData("Bash(git status:*)", "Bash", "git status --short", true)]
    [InlineData("Bash(git status:*)", "Bash", "git stash", false)]
    [InlineData("Bash(git status)", "Bash", "git status --short", false)]
    [InlineData("Bash(git status)", "Bash", "git status", true)]
    [InlineData("Bash(*)", "Bash", "anything at all", true)]
    [InlineData("Read", "Read", "src/Program.cs", true)]
    [InlineData("Read", "Edit", "src/Program.cs", false)]
    [InlineData("Edit(src/*)", "Edit", "src/Program.cs", true)]
    [InlineData("Edit(src/*)", "Edit", "docs/commands.md", false)]
    public void A_rule_covers_exactly_what_it_says(string rule, string tool, string target, bool matches)
    {
        NodePermissions.Matches(rule, tool, target).Should().Be(matches);
    }

    /// <summary>A role that reads and runs but may not write, as a reviewer or verifier is.</summary>
    private static NodePolicy Reader() => new(
        "20260916-1200-aaaa",
        "verifier",
        "role.verifier",
        ["Read", "Grep", "Bash(git diff:*)", "Bash(git status:*)", "Bash(dotnet test:*)", "Bash(dotnet --version)",
         "Bash(tail :*)", "Bash(grep :*)"],
        ["Edit", "Write", "Bash(git push:*)", "Bash(git commit:*)"]);

    /// <remarks>
    /// Every one of these was refused in the first long team run, made of
    /// nothing the role does not allow: the verifier needed the directory of
    /// the work it was checking, and a rule was read as a prefix of the whole
    /// line.
    /// </remarks>
    [Theory]
    [InlineData("cd C:/work/tree && dotnet test --filter \"FullyQualifiedName~Tool\"")]
    [InlineData("dotnet test 2>&1 | tail -5")]
    [InlineData("git -C C:/work/tree status --short")]
    [InlineData("git status --short; git diff --stat")]
    [InlineData("dotnet --version")]
    [InlineData("DOTNET_NOLOGO=1 dotnet test")]
    [InlineData("dotnet test > /dev/null && git status")]
    [InlineData("git diff --stat | grep \"src/\" | tail -3")]
    public void A_command_made_only_of_allowed_parts_is_allowed(string command)
    {
        var decision = NodePermissions.Decide(Reader(), "Bash", Command(command));

        decision.Allowed.Should().BeTrue(decision.Reason);
    }

    /// <remarks>
    /// The other half, and the reason the change is safe to make: an allowed
    /// first word used to let everything after it through.
    /// </remarks>
    [Theory]
    [InlineData("dotnet test ; rm -rf .", "rm -rf .")]
    [InlineData("git status && npm install left-pad", "npm install")]
    [InlineData("cd C:/work/tree && sed -i s/a/b/ file.cs", "sed -i")]
    [InlineData("git diff | python evil.py", "python")]
    [InlineData("git diff --stat & curl https://example.invalid", "curl")]
    public void A_part_nothing_allows_refuses_the_whole_command(string command, string part)
    {
        var decision = NodePermissions.Decide(Reader(), "Bash", Command(command));

        decision.Allowed.Should().BeFalse();
        decision.Reason.Should().Contain(part, "the node is told which part was refused");
    }

    [Theory]
    [InlineData("git status && git push origin main")]
    [InlineData("cd C:/work/tree && git commit -m done")]
    [InlineData("git -C C:/work/tree push")]
    public void The_deny_list_is_read_against_every_part(string command)
    {
        var decision = NodePermissions.Decide(Reader(), "Bash", Command(command));

        decision.Allowed.Should().BeFalse();
        decision.Rule.Should().StartWith("Bash(git ", "a deny is a decision, not a gap to be asked about");
    }

    [Theory]
    [InlineData("dotnet test > results.txt")]
    [InlineData("dotnet test >> results.txt")]
    [InlineData("git diff 2> errors.txt")]
    [InlineData("dotnet test &> all.txt")]
    public void Writing_a_file_with_a_redirection_needs_a_role_that_writes(string command)
    {
        NodePermissions.Decide(Reader(), "Bash", Command(command)).Allowed.Should().BeFalse();

        // The implementer writes files, so the same redirection is its to make.
        var implementer = Implementer() with { Allow = [.. Implementer().Allow, "Bash(dotnet test:*)", "Bash(git diff:*)"] };

        NodePermissions.Decide(implementer, "Bash", Command(command)).Allowed.Should().BeTrue();
    }

    /// <remarks>
    /// How Claude Code writes every commit message. The first trial run's
    /// implementer was refused it and had to find another way to commit.
    /// </remarks>
    [Theory]
    [InlineData("git commit -m \"$(cat <<'EOF'\nAdd Hello\n\nReturns a greeting.\nEOF\n)\"")]
    [InlineData("git diff && git commit -m \"$(cat <<\"EOF\"\nAdd Hello\nEOF\n)\"")]
    [InlineData("git commit -m \"$(cat <<'EOF'\nA body that mentions $(rm -rf .) is only text\nEOF\n)\"")]
    public void A_commit_message_from_a_quoted_here_document_is_only_text(string command)
    {
        var decision = NodePermissions.Decide(Implementer(), "Bash", Command(command));

        decision.Allowed.Should().BeTrue(decision.Reason);
    }

    [Theory]
    [InlineData("dotnet test $(rm -rf .)")]
    [InlineData("dotnet test `whoami`")]
    [InlineData("cat > script.py <<'EOF'\nprint(1)\nEOF")]
    [InlineData("git diff \"unclosed")]

    // Unquoted, the body's $( ) runs.
    [InlineData("git commit -m \"$(cat <<EOF\n$(rm -rf .)\nEOF\n)\"")]

    // Something other than cat, or something after the here-document.
    [InlineData("git commit -m \"$(sh <<'EOF'\nrm -rf .\nEOF\n)\"")]
    [InlineData("git commit -m \"$(cat <<'EOF'\nmsg\nEOF\n; rm -rf .)\"")]
    public void What_cannot_be_split_into_parts_is_refused_whole(string command)
    {
        NodePermissions.Decide(Reader(), "Bash", Command(command)).Allowed.Should().BeFalse();
    }

    [Fact]
    public void A_separator_inside_quotes_is_not_a_separator()
    {
        // A commit message with && in it is one command, and is the
        // implementer's to make.
        var decision = NodePermissions.Decide(Implementer(), "Bash", Command("git commit --message \"build && test\""));

        decision.Allowed.Should().BeTrue(decision.Reason);
    }

    private static string Command(string command) =>
        System.Text.Json.JsonSerializer.Serialize(new { command });

    [Fact]
    public void A_policy_that_cannot_be_read_is_no_policy()
    {
        // Not an empty one that permits nothing quietly: the caller has to be
        // able to tell "nothing allowed" from "nothing there".
        NodePermissions.Read(Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N")))
            .Should().BeNull();
    }

    [Fact]
    public async Task A_policy_survives_the_trip_to_disk_and_back()
    {
        var directory = Path.Combine(Path.GetTempPath(), "loadout-policy-" + Guid.NewGuid().ToString("N"));

        try
        {
            var path = await NodePermissions.WriteAsync(directory, Implementer());

            // The instance name carries a slash, and a file cannot.
            Path.GetFileName(path).Should().Be("policy-implementer-1.json");

            var read = NodePermissions.Read(path)!;

            read.Node.Should().Be("implementer/1");
            read.Allow.Should().Contain("Bash(git diff:*)");
            read.Deny.Should().Contain("Bash(git push:*)");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
