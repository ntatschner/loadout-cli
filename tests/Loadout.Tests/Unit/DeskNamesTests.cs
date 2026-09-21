using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Somebody to call each node.
/// </summary>
/// <remarks>
/// <para>
/// Every room on a busy machine has an <c>implementer/1</c> in it, so "the
/// implementer got it wrong" is a sentence about five different agents. A
/// person's name points at exactly one node of exactly one run, which is the
/// whole reason for it - the same argument the rooms make for runs.
/// </para>
/// <para>
/// What has to hold is that it is the same person every time, on every
/// machine, without anything being stored. A name that changed when the
/// dashboard restarted would be worse than no name at all, because somebody
/// would have written the old one down.
/// </para>
/// </remarks>
public sealed class DeskNamesTests
{
    [Fact]
    public void The_same_node_is_the_same_person_every_time()
    {
        // Worked out from the identifiers rather than stored, so a second
        // process - or another machine reading the same journal - agrees
        // without anything being shared.
        DeskNames.For("20260919-0900-aaaa", "implementer/1")
            .Should().Be(DeskNames.For("20260919-0900-aaaa", "implementer/1"));

        DeskNames.Full("20260919-0900-aaaa", "implementer/1")
            .Should().Be(DeskNames.Full("20260919-0900-aaaa", "implementer/1"));
    }

    [Fact]
    public void The_same_node_of_a_different_run_is_somebody_else()
    {
        // The entire point. Two runs of the same team both have an
        // implementer/1 and they are not the same agent.
        var one = DeskNames.Full("20260919-0900-aaaa", "implementer/1");
        var other = DeskNames.Full("20260919-1100-bbbb", "implementer/1");

        one.Should().NotBe(other);
    }

    [Fact]
    public void Everybody_in_a_room_is_somebody_different()
    {
        var run = "20260919-0900-aaaa";

        var room = new[] { "lead", "planner", "implementer/1", "reviewer/1", "verifier/1" }
            .Select(node => DeskNames.Full(run, node))
            .ToList();

        // A room where two desks say the same name is a room you cannot talk
        // about, which is what this exists to prevent.
        room.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_first_name_is_short_enough_for_a_name_plate()
    {
        // These sit on a plate the width of a desk on a floor plan. Anything
        // long is clipped, and a clipped name is not a name.
        var names = new[] { "lead", "planner", "implementer/1", "reviewer/1", "verifier/1", "docs-writer/2" }
            .Select(node => DeskNames.For("20260919-0900-aaaa", node));

        names.Should().OnlyContain(name => name.Length > 1 && name.Length <= 6);
    }

    [Fact]
    public void The_full_name_is_the_first_one_and_more()
    {
        var first = DeskNames.For("20260919-0900-aaaa", "reviewer/1");
        var full = DeskNames.Full("20260919-0900-aaaa", "reviewer/1");

        full.Should().StartWith(first + " ");
        full.Should().NotBe(first);
    }

    [Fact]
    public void A_node_with_no_name_gets_no_person()
    {
        // Rather than a person called nothing, or an exception on a page.
        DeskNames.For("20260919-0900-aaaa", null).Should().BeEmpty();
        DeskNames.For("20260919-0900-aaaa", "").Should().BeEmpty();
        DeskNames.Full("20260919-0900-aaaa", " ").Should().BeEmpty();
    }

    [Fact]
    public void There_are_enough_people_that_a_repeat_is_a_coincidence()
    {
        // One list of four dozen repeats itself inside a single busy
        // afternoon, and a name two nodes share is not a name.
        DeskNames.People.Should().BeGreaterThan(1000);
    }
}
