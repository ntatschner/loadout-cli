using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The tools a role gets from its posture, and how its own frontmatter lays
/// over them.
/// </summary>
/// <remarks>
/// Every role file used to carry a hand copy of one list, and the copies
/// drifted: the fixer, the reproducer, the regression tester and the release
/// validator could not run the tests their own probes look for. These pin the
/// table that replaced the copies, through the same gate a node's call goes to.
/// </remarks>
public sealed class RoleToolsTests
{
    private static bool Allowed(
        (IReadOnlyList<string> Allowed, IReadOnlyList<string> Denied) lists, string tool, string? command = null) =>
        NodePermissions.Decide(
            new NodePolicy("run", "node", "role.test", lists.Allowed, lists.Denied),
            tool,
            command is null ? "{}" : $$"""{"command":{{System.Text.Json.JsonSerializer.Serialize(command)}}}""")
            .Allowed;

    [Theory]
    [InlineData("advise")]
    [InlineData("investigate")]
    [InlineData("implement")]
    [InlineData("review")]
    [InlineData("coordinate")]
    public void Every_posture_reads_and_none_rewrites_history(string mode)
    {
        var lists = RoleTools.Compose(mode, "answer", [], []);

        Allowed(lists, "Read").Should().BeTrue();
        Allowed(lists, "Bash", "git log --oneline -5").Should().BeTrue();
        Allowed(lists, "Bash", "git -C ../tree status --short").Should().BeTrue();

        foreach (var command in new[] { "git push origin main", "git push --force", "git rebase main", "git reset --hard", "git commit --amend -m x" })
        {
            Allowed(lists, "Bash", command).Should().BeFalse($"{mode} must never run '{command}'");
        }
    }

    [Theory]
    [InlineData("advise", false)]
    [InlineData("coordinate", false)]
    [InlineData("investigate", true)]
    [InlineData("implement", true)]
    [InlineData("review", true)]
    public void The_postures_that_prove_something_may_build_and_test(string mode, bool proves)
    {
        var lists = RoleTools.Compose(mode, "decision", [], []);

        foreach (var command in new[] { "dotnet test", "npm test", "cargo test", "go test ./...", "pytest -q" })
        {
            Allowed(lists, "Bash", command).Should().Be(proves, $"{mode} and '{command}'");
        }

        Allowed(lists, "Bash", "cd tree && dotnet test 2>&1 | tail -5; echo \"exit $?\"").Should().Be(proves);
    }

    [Theory]
    [InlineData("advise", false, true, false)]
    [InlineData("investigate", false, false, false)]
    [InlineData("implement", true, true, false)]
    [InlineData("review", false, false, false)]
    [InlineData("coordinate", false, false, false)]
    public void What_each_posture_may_change(string mode, bool edits, bool writes, bool commits)
    {
        var lists = RoleTools.Compose(mode, "document", [], []);

        Allowed(lists, "Edit").Should().Be(edits, mode);
        Allowed(lists, "Write").Should().Be(writes, mode);
        Allowed(lists, "Bash", "git commit -m x").Should().Be(commits, mode);
    }

    [Fact]
    public void A_commit_deliverable_brings_committing_with_it()
    {
        var lists = RoleTools.Compose("implement", "commit", [], []);

        Allowed(lists, "Bash", "git add src && git commit -m x").Should().BeTrue();
        Allowed(lists, "Bash", "git commit --amend -m x").Should().BeFalse("amending is rewriting, whatever the deliverable");
    }

    [Fact]
    public void A_role_s_own_word_beats_its_posture_in_both_directions()
    {
        // A reviewing role that writes notes, and an implementing one that
        // edits nothing: the role says so, and the posture gives way.
        var writes = RoleTools.Compose("review", "decision", ["Write"], []);
        var edits = RoleTools.Compose("implement", "document", [], ["Edit", "MultiEdit"]);

        Allowed(writes, "Write").Should().BeTrue();
        Allowed(writes, "Edit").Should().BeFalse("only what the role named gives way");
        Allowed(edits, "Edit").Should().BeFalse();
        Allowed(edits, "Write").Should().BeTrue();
    }

    [Fact]
    public void A_role_with_no_posture_is_exactly_what_it_lists()
    {
        var lists = RoleTools.Compose(null, null, ["Read"], ["Edit"]);

        lists.Allowed.Should().Equal("Read");
        lists.Denied.Should().Equal("Edit");

        RoleTools.Compose("wander", null, ["Read"], []).Allowed.Should().Equal("Read");
    }
}
