using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Roles: the specialists a team run gives its nodes.
/// </summary>
/// <remarks>
/// <para>
/// A role is loaded like any other specialist and reached like no other:
/// never by evidence, only by name. The tests here pin both halves against
/// the built-in library. The first is what makes a role usable at all; the
/// second is what keeps a role out of a person's ordinary session, where
/// instructions written for a node would be wrong and expensive.
/// </para>
/// <para>
/// The coordinator reads a role's frontmatter and never its body, so the
/// frontmatter fields it depends on are pinned too.
/// </para>
/// </remarks>
public sealed class RoleLibraryTests
{
    private static SpecialistCatalogue? _catalogue;

    private static async Task<SpecialistCatalogue> LibraryAsync() =>
        _catalogue ??= await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

    private static readonly RepositoryEvidence DotNetRepository = new(
        Paths: ["src/App/Program.cs", "src/App/App.csproj", "docs/README.md"],
        Extensions: new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [".cs"] = 40, [".md"] = 3 },
        Dependencies: ["<PackageReference Include=\"Microsoft.Extensions.Hosting\" />"],
        Truncated: false);

    [Fact]
    public async Task The_member_role_ships_and_carries_the_contract_every_node_reports_against()
    {
        var member = (await LibraryAsync()).Find("role.member");

        member.Should().NotBeNull();
        member!.Kind.Should().Be(SpecialistKind.Role);
        member.Body.Should().Contain("report/1");
        member.Activation.Always.Should().BeFalse("a role is never foundation; it reaches a session by name only");
    }

    [Fact]
    public async Task Every_role_reads_its_coordinator_facing_fields_from_its_frontmatter()
    {
        var roles = (await LibraryAsync()).OfKind(SpecialistKind.Role).ToList();

        roles.Should().HaveCountGreaterThan(20, "the catalogue's seven teams need their roles");

        var reviewer = roles.Should().ContainSingle(r => r.Id == "role.reviewer").Subject;

        reviewer.Role.Should().NotBeNull();
        reviewer.Role!.Mode.Should().Be("review");
        reviewer.Role.Deliverable.Should().Be("decision");
        reviewer.Role.Contract.Should().Be("report/1");
        reviewer.Role.AllowedTools.Should().Contain("Read");
        reviewer.Role.DeniedTools.Should().Contain("Edit");

        var lead = roles.Should().ContainSingle(r => r.Id == "role.project-lead").Subject;
        lead.Role!.Mode.Should().Be("coordinate");
        lead.Role.Deliverable.Should().Be("answer");
        lead.Role.DeniedTools.Should().Contain(["Edit", "Write", "Bash(git push:*)"], "a lead never edits and never pushes");

        // The member role is the contract, not a job: it names no posture and
        // no tools, and what it does not say is empty rather than null.
        var member = roles.Should().ContainSingle(r => r.Id == "role.member").Subject;
        member.Role.Should().NotBeNull();
        member.Role!.Mode.Should().BeNull();
        member.Role.AllowedTools.Should().BeEmpty();
        member.Role.DeniedTools.Should().BeEmpty();

        // Every job has a posture, a deliverable and a deny list; nothing
        // that changes history is allowed to anyone.
        foreach (var role in roles.Where(r => r.Id != "role.member"))
        {
            role.Role.Should().NotBeNull(role.Id);
            role.Role!.Mode.Should().NotBeNullOrEmpty(role.Id);
            role.Role.Deliverable.Should().NotBeNullOrEmpty(role.Id);
            role.Role.Contract.Should().Be("report/1", role.Id);
            role.Role.DeniedTools.Should().Contain("Bash(git rebase:*)", role.Id);
            role.Role.AllowedTools.Should().NotContain("Bash(git push:*)", role.Id);
        }
    }

    [Theory]
    [InlineData("role.tool-creator")]
    [InlineData("role.tool-refiner")]
    public async Task Creator_and_refiner_roles_cannot_promote_or_trust(string id)
    {
        // They propose; promotion is the gate's and trust is a person's. A
        // blanket 'loadout tools' allow would have let both through, so this
        // goes through the same gate a node's call would.
        var role = (await LibraryAsync()).Find(id)!.Role!;
        var policy = new Loadout.Core.Teams.NodePolicy("run", "node", id, role.AllowedTools, role.DeniedTools);

        bool Allowed(string command) => Loadout.Core.Teams.NodePermissions
            .Decide(policy, "Bash", $$"""{"command":{{System.Text.Json.JsonSerializer.Serialize(command)}}}""")
            .Allowed;

        Allowed("loadout tools promote free-disk-by-cache@1.0").Should().BeFalse();
        Allowed("loadout tools trust free-disk-by-cache@1.0").Should().BeFalse();
        Allowed("loadout tools search disk cache").Should().BeTrue();
        Allowed("loadout tools submit --kind candidate --text x").Should().BeTrue();

        // Lifecycle is the Refiner's alone, and changes nothing that runs.
        Allowed("loadout tools deprecate free-disk-by-cache --reason unused")
            .Should().Be(id == "role.tool-refiner");
    }

    [Fact]
    public async Task No_role_is_reachable_by_evidence()
    {
        foreach (var role in (await LibraryAsync()).OfKind(SpecialistKind.Role))
        {
            role.Activation.GlobList.Should().BeEmpty(role.Id);
            role.Activation.DependencyList.Should().BeEmpty(role.Id);
            role.Activation.TaskPhraseList.Should().BeEmpty(role.Id);
            role.Activation.Always.Should().BeFalse(role.Id);
        }
    }

    [Fact]
    public async Task A_task_that_sounds_like_a_role_does_not_load_one()
    {
        var resolved = new SpecialistResolver().Resolve(new SpecialistRequest(
            await LibraryAsync(),
            Mode: "implement",
            Task: "implement the reviewer role for the planner and verifier, then report",
            Evidence: DotNetRepository));

        resolved.Selected.Should().NotContain(s => s.Specialist.Kind == SpecialistKind.Role,
            "a person's session must never pick up instructions written for a node");
    }

    [Fact]
    public async Task A_role_named_explicitly_is_loaded_with_the_member_contract_and_composes_last()
    {
        var resolved = new SpecialistResolver().Resolve(new SpecialistRequest(
            await LibraryAsync(),
            Mode: "review",
            Task: "review a4f21c9 against its brief",
            Explicit: ["role.reviewer"],
            Evidence: DotNetRepository));

        var ids = resolved.Selected.Select(s => s.Specialist.Id).ToList();

        ids.Should().Contain("role.reviewer");
        ids.Should().Contain("role.member", "the reviewer requires the member contract");
        ids.Should().Contain("mode.review");

        // Last, after everything that says what the code is and what the
        // task is: the role is the most specific thing in the session.
        resolved.Selected.Last().Specialist.Kind.Should().Be(SpecialistKind.Role);
        resolved.Selected.TakeWhile(s => s.Specialist.Kind != SpecialistKind.Role)
            .Should().NotBeEmpty("foundations and the mode come first");
    }

    [Fact]
    public async Task The_coordinate_mode_is_a_mode_the_resolver_knows()
    {
        var resolved = new SpecialistResolver().Resolve(new SpecialistRequest(
            await LibraryAsync(),
            Mode: "coordinate",
            Task: "reach the goal",
            Explicit: ["role.project-lead"],
            Evidence: DotNetRepository));

        resolved.Mode.Should().Be("coordinate");
        resolved.Selected.Select(s => s.Specialist.Id).Should().Contain("mode.coordinate");
    }
}
