using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What happens when a node actually tries to run one.
/// </summary>
/// <remarks>
/// One rule decides how this composes with everything else, and it is the rule
/// the whole of teams follows: a remedy can only ever make things stricter.
/// Trust is not a way to get Bash.
/// </remarks>
public sealed class RemedyGateTests
{
    private static NodePolicy Policy(
        string ruling = "run",
        string script = "clear-build-cache.ps1",
        IReadOnlyList<string>? allow = null,
        IReadOnlyList<string>? deny = null) =>
        new(
            "20260920-1200-abcd",
            "fixer/1",
            "role.fixer",
            allow ?? ["Bash"],
            deny ?? [],
            Ask: true,
            Remedies: [new RemedyStanding("clear-build-cache", script, ruling, "Because of a reason.")]);

    private static string Calling(string command) =>
        System.Text.Json.JsonSerializer.Serialize(new { command });

    [Fact]
    public void A_trusted_remedy_the_machine_allows_runs_without_anybody_being_asked()
    {
        var decided = NodePermissions.Decide(
            Policy(), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Allowed.Should().BeTrue();
        decided.Reason.Should().Contain("clear-build-cache").And.Contain("trusted here");
    }

    [Fact]
    public void One_that_has_not_been_agreed_to_is_held_rather_than_run()
    {
        var decided = NodePermissions.Decide(
            Policy(ruling: "ask"), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Allowed.Should().BeFalse();
        decided.Reason.Should().Contain("has to be agreed to before it runs");

        // No rule named, which is what makes it a question rather than a
        // decision: a refusal that named a rule would never be put to anybody.
        decided.Rule.Should().BeNull();
    }

    [Fact]
    public void One_this_machine_refuses_is_not_put_to_anybody()
    {
        var decided = NodePermissions.Decide(
            Policy(ruling: "refuse"), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Allowed.Should().BeFalse();

        // Named as a rule on purpose. A machine that said never is not asked
        // again, and a refusal with no rule behind it would be.
        decided.Rule.Should().Be("remedy:clear-build-cache");
    }

    [Fact]
    public void Trust_is_not_a_way_to_get_a_tool_the_role_never_had()
    {
        // The rule the whole thing rests on. If a remedy could widen a role,
        // it would be a file an agent writes deciding what an agent may do.
        var decided = NodePermissions.Decide(
            Policy(allow: ["Read"]), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Allowed.Should().BeFalse();
        decided.Reason.Should().Contain("Nothing in the role.fixer role allows Bash");
    }

    [Fact]
    public void A_role_that_forbids_something_still_forbids_it()
    {
        // Deny is checked before any of this. A trusted remedy that got past a
        // deny list would make every deny list a suggestion.
        var decided = NodePermissions.Decide(
            Policy(deny: ["Bash"]), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Allowed.Should().BeFalse();
        decided.Reason.Should().Contain("forbids this");
    }

    [Fact]
    public void A_call_that_is_not_a_remedy_is_answered_the_way_it_always_was()
    {
        var decided = NodePermissions.Decide(Policy(), "Bash", Calling("dotnet test"));

        decided.Allowed.Should().BeTrue();
        decided.Reason.Should().Contain("role allows 'Bash'");
        decided.Reason.Should().NotContain("clear-build-cache");
    }

    [Fact]
    public void A_remedy_is_recognised_however_it_is_reached()
    {
        // By name rather than by path. The same script is reached by an
        // absolute path, a relative one and a shell variable, and a gate that
        // only caught the first is one somebody walks round by typing cd.
        foreach (var command in new[]
        {
            @"pwsh C:\state\teams\work\system-watch\remedies\clear-build-cache.ps1",
            "pwsh ./clear-build-cache.ps1",
            "cd remedies && pwsh clear-build-cache.ps1",
            "pwsh $HOME/remedies/CLEAR-BUILD-CACHE.PS1",
        })
        {
            NodePermissions.Decide(Policy(ruling: "refuse"), "Bash", Calling(command))
                .Rule.Should().Be("remedy:clear-build-cache", $"'{command}' names it");
        }
    }

    [Fact]
    public void A_team_with_nothing_registered_is_unaffected()
    {
        var bare = new NodePolicy("r", "n", "role.fixer", ["Bash"], []);

        NodePermissions.Decide(bare, "Bash", Calling("pwsh ./anything.ps1"))
            .Allowed.Should().BeTrue();
    }
}
