using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which specialists are reported as unreachable because nothing in a
/// repository can bring them in.
/// </summary>
public sealed class SpecialistActivationTests
{
    /// <summary>
    /// A role is named by the team that uses it and never detected, like a
    /// mode. Reporting it as unreachable put a warning in <c>doctor</c> for
    /// every role that ships - 21 on a new machine, before anything had been
    /// set up - each saying the role would only load when named, which is what
    /// a role is for.
    /// </summary>
    [Fact]
    public void A_role_with_no_evidence_is_not_reported()
    {
        Unreachable(Specialist("role.editor", SpecialistKind.Role)).Should().BeEmpty();
    }

    [Fact]
    public void Anything_else_with_no_evidence_still_is()
    {
        Unreachable(Specialist("function.caching", SpecialistKind.Function))
            .Should().ContainSingle().Which.Rule.Should().Be("function.caching");
    }

    private static SpecialistDocument Specialist(string id, SpecialistKind kind) =>
        new(id, kind, id, "summary", SpecialistActivation.None, "body", 4);

    private static IReadOnlyList<RuleFinding> Unreachable(SpecialistDocument specialist) =>
        SpecialistValidator.Validate(new Dictionary<string, SpecialistDocument> { [specialist.Id] = specialist })
            .Where(finding => finding.Kind == "specialist-unreachable")
            .ToList();
}
