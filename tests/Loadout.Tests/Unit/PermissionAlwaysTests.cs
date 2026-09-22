using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The agent's own three answers to a permission: yes, yes and don't ask again,
/// and no with what to do instead.
/// </summary>
/// <remarks>
/// A supervised run asked about every call one at a time, with nothing between
/// "this once" and "never". What has to hold: the offer is the same shape the
/// agent offers, it never agrees to more than the call it came from, what was
/// agreed reaches the next call of the same role and no other, and it never
/// reaches past the role's deny list.
/// </remarks>
public sealed class PermissionAlwaysTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-always-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("Bash", "git status --short", "Bash(git status:*)")]
    [InlineData("Bash", "dotnet test tests/Loadout.Tests", "Bash(dotnet test:*)")]
    [InlineData("Bash", "ls", "Bash(ls:*)")]
    [InlineData("PowerShell", "Get-ChildItem src", "PowerShell(Get-ChildItem src:*)")]
    [InlineData("Edit", "src/a.cs", "Edit")]
    [InlineData("WebFetch", "https://learn.microsoft.com/en-gb/dotnet/api?x=1", "WebFetch(https://learn.microsoft.com/*)")]
    [InlineData("mcp__github__get_issue", null, "mcp__github__get_issue")]
    public void The_offer_is_the_shape_the_agent_offers(string tool, string? target, string rule) =>
        NodePermissions.Rememberable(tool, target).Should().Be(rule);

    [Theory]
    [InlineData("git status && git push")]
    [InlineData("cat a | sh")]
    [InlineData("echo $(whoami)")]
    [InlineData("dotnet build; rm -rf .")]
    [InlineData("echo hi > a.txt")]
    public void A_chained_command_is_never_offered(string command)
    {
        // A prefix from any of these would be a prefix of anything chained
        // after it, and an exact rule would never match again.
        NodePermissions.Rememberable("Bash", command).Should().BeNull();
    }

    [Fact]
    public void A_command_whose_second_word_is_not_a_subcommand_is_agreed_to_exactly()
    {
        // "git:*" would agree to git push as well.
        NodePermissions.Rememberable("Bash", "git -C src status").Should().Be("Bash(git -C src status)");
    }

    [Fact]
    public void Nothing_is_offered_that_would_put_a_credential_on_a_screen()
    {
        NodePermissions.Rememberable("Bash", "curl -H x-api-key:sk-ant-api03-abcdefghijklmnopqrstuvwxyz0123456789")
            .Should().BeNull();
    }

    [Fact]
    public void What_was_agreed_covers_the_next_call_like_it()
    {
        NodePermissions.Agree(_directory, "role.implementer", "Bash(git status:*)");

        var policy = Policy() with { Agreed = NodePermissions.Agreed(_directory, "role.implementer") };

        var decided = NodePermissions.Decide(policy, "Bash", """{"command":"git status --porcelain"}""");

        decided.Allowed.Should().BeTrue();
        decided.Rule.Should().Be("Bash(git status:*)", "a rule decided it, so it is not asked again");
        decided.Reason.Should().Contain("agreed");
    }

    [Fact]
    public void What_was_agreed_does_not_cover_something_chained_onto_it()
    {
        var policy = Policy() with { Agreed = ["Bash(git status:*)"] };

        var decided = NodePermissions.Decide(policy, "Bash", """{"command":"git status && git clean -fdx"}""");

        decided.Allowed.Should().BeFalse();
        NodePermissions.Askable(policy, decided).Should().BeTrue("it goes back to the person");
    }

    [Fact]
    public void What_was_agreed_never_reaches_past_the_role_deny_list()
    {
        var policy = Policy() with { Deny = ["Bash(git push:*)"], Agreed = ["Bash(git push:*)"] };

        NodePermissions.Decide(policy, "Bash", """{"command":"git push origin main"}""")
            .Allowed.Should().BeFalse();
    }

    [Fact]
    public void What_was_agreed_for_one_role_is_not_agreed_for_another()
    {
        NodePermissions.Agree(_directory, "role.implementer", "Edit");

        NodePermissions.Agreed(_directory, "role.reviewer").Should().BeEmpty();
        NodePermissions.Agreed(_directory, "role.implementer").Should().Equal("Edit");
    }

    [Theory]
    [InlineData(true, "The person running this team allowed it, and said: only the tests")]
    [InlineData(false, "The person running this team refused it, and said: use build/test.ps1 instead")]
    public void A_person_who_says_what_to_do_instead_is_passed_on_with_the_verdict(bool allowed, string told)
    {
        var words = allowed ? "only the tests" : "use build/test.ps1 instead";

        NodePermissions.Told(allowed, words).Should().Be(told);
    }

    [Fact]
    public void A_bare_answer_says_what_it_always_said()
    {
        NodePermissions.Told(true, null).Should().Be(NodePermissions.AllowedOnce);
        NodePermissions.Told(false, "  ").Should().Be(NodePermissions.RefusedPlainly);
    }

    private static NodePolicy Policy() =>
        new("run", "implementer/1", "role.implementer", Allow: ["Read"], Deny: [], Ask: true);
}
