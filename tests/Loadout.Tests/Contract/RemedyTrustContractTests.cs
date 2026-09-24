using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Contract;

/// <summary>
/// Trusting a team's remedy, and answering a remediation it asked for, from
/// the built command line.
/// </summary>
[Collection(ContractCollection.Name)]
public sealed class RemedyTrustContractTests
{
    /// <remarks>
    /// Refused before anything is looked up, so the remedy and the request
    /// named here need not exist: a node is told no before it learns whether
    /// they do.
    /// </remarks>
    [BuiltCliFact]
    public async Task A_node_of_a_team_run_cannot_trust_a_remedy_or_answer_for_one()
    {
        using var loadout = new LoadoutProcess();
        loadout.Environment[NodeMarker.Variable] = Path.Combine(loadout.Home, "policy-remediator.json");

        string[][] asked =
        [
            ["team", "remedy", "trust", "free-disk", "--team", "system-watch"],
            ["team", "remedy", "trust", "free-disk", "--team", "system-watch", "--revoke"],
            ["team", "remedy", "requests", "--team", "system-watch", "--approve", "req-1"],
            ["team", "remedy", "requests", "--team", "system-watch", "--refuse", "req-1"],
        ];

        foreach (var one in asked)
        {
            var run = await loadout.RunAsync(one);

            run.ExitCode.Should().NotBe(0, string.Join(' ', one));
            (run.StandardOutput + run.StandardError).Should().Contain("node of a team run", string.Join(' ', one));
        }

        // Looking is not deciding: a node may still see what is waiting.
        var listed = await loadout.RunAsync("team", "remedy", "requests", "--team", "system-watch");

        (listed.StandardOutput + listed.StandardError).Should().NotContain("node of a team run");
    }
}
