using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Core.Tools;
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
    public void A_held_remedy_is_put_to_a_person_as_a_remedy_and_not_as_Bash()
    {
        // The gap this closes. Nothing ever created a remedy request, so the
        // queue had no producer and `team remedy requests` could only ever be
        // empty - a reader and an approver built, and nothing that writes.
        //
        // Now the hold travels with the decision, so whoever is asked is asked
        // about the remedy. "May implementer use Bash for 'pwsh
        // ./remedies/x.ps1'" is not a question anybody can answer without going
        // and reading the script themselves.
        var standing = new RemedyStanding(
            "clear-build-cache",
            "clear-build-cache.ps1",
            "ask",
            "Nobody at this machine has agreed to this remedy.",
            What: "Deletes build output older than fourteen days.",
            Assumes: "The build box, and nothing mid-build.",
            Proves: "Free space before and after, and a build still succeeds.");

        var decided = NodePermissions.Decide(
            Policy(ruling: "ask"), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"));

        decided.Remedy.Should().NotBeNull("the hold has to reach whoever asks");

        var question = standing.Asking("implementer/1", "role.fixer");

        question.Should().Contain("implementer/1")
            .And.Contain("clear-build-cache")
            .And.Contain("deletes build output")
            .And.Contain("assumes the build box")
            .And.Contain("know it worked because free space")
            .And.Contain("Nobody at this machine has agreed");
    }

    [Fact]
    public void A_remedy_that_runs_or_is_refused_is_not_put_to_anybody()
    {
        // Only a hold is a question. One that runs needs nobody, and one this
        // machine refuses outright is not asked about again.
        NodePermissions.Decide(Policy(), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"))
            .Remedy.Should().BeNull();

        NodePermissions.Decide(Policy(ruling: "refuse"), "Bash", Calling("pwsh ./remedies/clear-build-cache.ps1"))
            .Remedy.Should().BeNull();
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
    public async Task The_remediator_is_the_one_role_that_can_reach_this_gate_at_all()
    {
        // Found by asking whether any shipped role could ever trigger it. None
        // could: every one of them has Bash(git ...) and nothing else, so a
        // call to run a script never matched an allow rule and never got as far
        // as the remedy. The whole harness gated something nothing could
        // attempt.
        var specialists = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

        string[] Allowed(string id) =>
            [.. specialists.Find(id)?.Role?.AllowedTools ?? []];

        bool CanRunAScript(string id) =>
            Allowed(id).Any(one =>
                one.StartsWith("Bash(pwsh", StringComparison.Ordinal)
                || one.StartsWith("Bash(bash", StringComparison.Ordinal)
                || string.Equals(one, "Bash", StringComparison.Ordinal));

        CanRunAScript("role.remediator").Should().BeTrue("it is the role that runs remedies");

        // And it stays the only one, because a second role with a shell is a
        // second way to run something nobody agreed to.
        foreach (var other in new[] { "role.fixer", "role.investigator", "role.reproducer", "role.verifier", "role.project-lead" })
        {
            CanRunAScript(other).Should().BeFalse($"{other} must not be able to run a script");
        }

        // It cannot reach past a remedy either: the destructive shells are
        // denied outright, so "run a remedy" does not quietly become "run
        // anything".
        var denied = specialists.Find("role.remediator")?.Role?.DeniedTools ?? [];

        denied.Should().Contain("Bash(rm:*)").And.Contain("Bash(git push:*)");

        // And it cannot change the shelf it runs from. Its role file said in
        // plain words not to rewrite a remedy mid-run, and the first real run
        // of it read one, found a genuine bug, and rewrote it anyway. A rule an
        // agent can ignore is a rule the tools should have enforced: an edited
        // remedy is one nobody has agreed to.
        Allowed("role.remediator").Should().NotContain("Write").And.NotContain("Edit");
    }

    private const string ToolScript = "param([string]$CachePath)\nGet-ChildItem $CachePath | Remove-Item\n";

    private static readonly Dictionary<string, string> TrustedKind = new() { ["unclassified"] = "trusted" };

    private static async Task<ToolRegistry> CatalogueAsync(ToolStoreFixture store)
    {
        var (registry, _) = store.Registry();
        await store.PromoteAsync(registry, ToolStoreFixture.Manifest("free-cache", "1.0"), ToolScript, ToolStoreFixture.Cases());

        return registry;
    }

    private static IReadOnlyList<Loadout.Models.Configuration.TrustedTool> AgreedTo(string version) =>
    [
        new() { Tool = "free-cache", Version = version, Fingerprint = RemedyCeiling.Fingerprint(ToolScript) },
    ];

    [Fact]
    public async Task An_active_global_tool_is_matched_by_its_versioned_file_name()
    {
        using var store = new ToolStoreFixture();
        var registry = await CatalogueAsync(store);
        var policy = new NodePolicy(
            "r", "remediator/1", ToolOffer.Remediator, ["Bash"], [], Ask: true,
            Remedies: ToolOffer.For(registry, ToolOffer.Remediator, TrustedKind, AgreedTo("1.0")));

        var one = NodePermissions.Decide(policy, "Bash", Calling("pwsh ./free-cache.v1.0.ps1"));
        var next = NodePermissions.Decide(policy, "Bash", Calling("pwsh ./free-cache.v1.1.ps1"));

        one.Allowed.Should().BeTrue();
        one.Reason.Should().Contain("tool:free-cache@1.0");
        next.Reason.Should().NotContain("tool:free-cache@1.0", "a trust given to one version names that version's file");
    }

    [Fact]
    public async Task A_candidate_tool_is_never_offered()
    {
        using var store = new ToolStoreFixture();
        var registry = await CatalogueAsync(store);
        var head = Path.Combine(registry.Root(), "free-cache", "tool.yaml");
        File.WriteAllText(head, File.ReadAllText(head).Replace("lifecycle: active", "lifecycle: candidate", StringComparison.Ordinal));

        registry.Show("free-cache").Value!.Record.Lifecycle.Should().Be("candidate");
        ToolOffer.For(registry, ToolOffer.Remediator, TrustedKind, AgreedTo("1.0")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_global_tool_is_offered_only_to_the_remediator()
    {
        using var store = new ToolStoreFixture();
        var registry = await CatalogueAsync(store);

        ToolOffer.For(registry, ToolOffer.Remediator, TrustedKind, AgreedTo("1.0")).Should().ContainSingle();

        foreach (var other in new[] { "role.fixer", "role.investigator", "role.implementer", "role.project-lead" })
        {
            ToolOffer.For(registry, other, TrustedKind, AgreedTo("1.0")).Should().BeEmpty(other + " does not run scripts");
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
