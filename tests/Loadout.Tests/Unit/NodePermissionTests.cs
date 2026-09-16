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
