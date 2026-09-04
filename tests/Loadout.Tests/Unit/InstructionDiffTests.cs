using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What changes between two ways of asking the same question.
/// </summary>
/// <remarks>
/// <c>instructions explain</c> says what one configuration loads. Reading two
/// full listings side by side to see what a change costs is exactly the
/// comparison a person does badly: forty lines are identical and the three that
/// differ are the whole question.
/// </remarks>
public sealed class InstructionDiffTests
{
    [Fact]
    public void What_only_the_second_composes_is_added()
    {
        var diff = InstructionDiff.Between(
            Resolution(2000, "foundation.change-safety"),
            Resolution(2200, "foundation.change-safety", "function.security"));

        diff.Added.Should().ContainSingle().Which.Id.Should().Be("function.security");
        diff.Removed.Should().BeEmpty();
    }

    [Fact]
    public void What_only_the_first_composes_is_removed()
    {
        var diff = InstructionDiff.Between(
            Resolution(2200, "foundation.change-safety", "function.security"),
            Resolution(2000, "foundation.change-safety"));

        diff.Removed.Should().ContainSingle().Which.Id.Should().Be("function.security");
        diff.Added.Should().BeEmpty();
    }

    [Fact]
    public void What_both_compose_is_counted_and_not_listed()
    {
        // The point of asking for a comparison is that the shared lines are not
        // the question. Listing them would put the reader back where they were.
        //
        // Something is dropped as well as added, deliberately: where the second
        // is a superset of the first, "how many both had" and "how many the
        // first had" are the same number, and a fixture like that cannot tell
        // the two apart.
        var diff = InstructionDiff.Between(
            Resolution(2000, "a.one", "a.two", "a.three"),
            Resolution(2100, "a.one", "a.two", "b.four"));

        diff.Kept.Should().Be(2);
        diff.Added.Should().ContainSingle().Which.Id.Should().Be("b.four");
        diff.Removed.Should().ContainSingle().Which.Id.Should().Be("a.three");
    }

    [Fact]
    public void The_expensive_change_is_shown_first()
    {
        // Somebody diffing configurations is usually trying to get under a
        // budget, so the specialist worth looking at first is the costly one.
        var diff = InstructionDiff.Between(
            Resolution(1000),
            Sized(3000, ("cheap.one", 40), ("dear.two", 400), ("middling.three", 200)));

        diff.Added.Select(change => change.Id)
            .Should().Equal("dear.two", "middling.three", "cheap.one");
    }

    [Fact]
    public void The_reason_the_second_reached_for_it_comes_with_it()
    {
        var diff = InstructionDiff.Between(
            Resolution(1000),
            Resolution(1200, "function.security"));

        diff.Added.Should().ContainSingle()
            .Which.Reason.Should().Be("because of function.security");
    }

    [Fact]
    public void The_cost_of_the_change_is_reported_as_a_delta()
    {
        var diff = InstructionDiff.Between(Resolution(2403), Resolution(1655));

        diff.TokensBefore.Should().Be(2403);
        diff.TokensAfter.Should().Be(1655);
        diff.TokenDelta.Should().Be(-748);
    }

    [Fact]
    public void Two_configurations_composing_the_same_thing_say_so()
    {
        var diff = InstructionDiff.Between(
            Resolution(2000, "a.one"),
            Resolution(2000, "a.one"));

        diff.IsSame.Should().BeTrue();
    }

    [Fact]
    public void A_specialist_named_differently_in_case_is_the_same_specialist()
    {
        var diff = InstructionDiff.Between(
            Resolution(2000, "Function.Security"),
            Resolution(2000, "function.security"));

        diff.IsSame.Should().BeTrue("identifiers are matched the way the library matches them");
    }

    private static EffectiveInstructions Resolution(int tokens, params string[] ids) =>
        Sized(tokens, ids.Select(id => (id, 100)).ToArray());

    private static EffectiveInstructions Sized(
        int tokens,
        params (string Id, int Bytes)[] specialists) =>
        new(
            "implement",
            specialists.Select(entry => Selection(entry.Id, entry.Bytes)).ToList(),
            [],
            [],
            new InstructionContextBudget(tokens * 4, tokens, 12000, 80));

    private static SpecialistSelection Selection(string id, int bytes) =>
        new(
            new SpecialistDocument(
                id,
                SpecialistKind.Function,
                id,
                "summary",
                SpecialistActivation.None,
                "body",
                Bytes: bytes),
            SpecialistTrigger.Mode,
            $"because of {id}",
            Confidence: 100);
}
