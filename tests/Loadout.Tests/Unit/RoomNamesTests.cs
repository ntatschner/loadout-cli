using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Giving each run a name somebody might actually remember.
/// </summary>
/// <remarks>
/// <para>
/// A run is called 20260918-1436-ed59, which is precise, sortable and
/// impossible to hold in your head. A week later "the one in the haunted
/// meeting room" is how anybody refers to it, and that has to attach to
/// something.
/// </para>
/// <para>
/// Worked out from the identifier rather than stored, so the same run is the
/// same room on every machine that reads its journal — including one reading a
/// journal it did not write.
/// </para>
/// </remarks>
public sealed class RoomNamesTests
{
    [Fact]
    public void The_same_run_is_always_the_same_room()
    {
        // The whole reason it is worked out rather than stored. A name that
        // changed when the dashboard restarted would be worse than none.
        RoomNames.For("20260918-1436-ed59").Should().Be(RoomNames.For("20260918-1436-ed59"));
    }

    [Fact]
    public void Different_runs_get_different_rooms()
    {
        var rooms = Enumerable.Range(0, 40)
            .Select(i => RoomNames.For($"20260918-14{i:00}-abcd"))
            .ToList();

        // Not all distinct - with a few hundred rooms and forty runs a repeat
        // is arithmetic rather than a bug - but nothing like one name for all
        // of them, which is what a broken hash would give.
        rooms.Distinct().Should().HaveCountGreaterThan(30);
    }

    [Fact]
    public void There_are_enough_rooms_that_a_repeat_is_a_coincidence()
    {
        // One list of two dozen names repeats by the seventh run, and a name
        // two runs share is not a name.
        RoomNames.Rooms.Should().BeGreaterThan(300);
    }

    [Fact]
    public void A_room_reads_like_somewhere_in_an_office()
    {
        var room = RoomNames.For("20260918-1436-ed59");

        room.Should().StartWith("The ");
        room.Should().EndWith(")");
        room.Should().Contain(" (");
    }

    [Fact]
    public void A_room_somebody_named_keeps_that_name()
    {
        var directory = Temporary();

        RoomNames.Rename(directory, "The Haunted Meeting Room");

        RoomNames.For(directory, "20260918-1436-ed59").Should().Be("The Haunted Meeting Room");

        // And forgetting it puts the worked-out one back rather than leaving
        // the run with no name at all.
        RoomNames.Forget(directory);

        RoomNames.For(directory, "20260918-1436-ed59")
            .Should().Be(RoomNames.For("20260918-1436-ed59"));
    }

    [Fact]
    public void Forgetting_a_name_that_was_never_given_is_not_a_failure()
    {
        // The page offers one box, and emptying a box somebody never filled in
        // is an ordinary thing to do.
        var act = () => RoomNames.Forget(Temporary());

        act.Should().NotThrow();
    }

    [Fact]
    public void A_name_longer_than_a_heading_is_cut_rather_than_taken_whole()
    {
        var directory = Temporary();

        RoomNames.Rename(directory, new string('x', RoomNames.Longest * 3));

        RoomNames.For(directory, "r").Should().HaveLength(RoomNames.Longest);
    }

    [Fact]
    public void A_name_with_a_newline_in_it_stays_one_line()
    {
        // The file has one line in it. A name carrying its own would make the
        // second line a name of its own on the next read.
        var directory = Temporary();

        RoomNames.Rename(directory, "The Broom\nCupboard");

        RoomNames.For(directory, "r").Should().Be("The Broom Cupboard");
    }

    [Fact]
    public void A_directory_that_is_not_there_falls_back_rather_than_failing()
    {
        RoomNames.For(Path.Combine(Path.GetTempPath(), "loadout-no-such-" + Guid.NewGuid()), "r")
            .Should().Be(RoomNames.For("r"));
    }

    private static string Temporary()
    {
        var directory = Path.Combine(
            Path.GetTempPath(), "loadout-room-" + Guid.NewGuid().ToString("N")[..8]);

        Directory.CreateDirectory(directory);

        return directory;
    }

    [Fact]
    public void A_run_with_no_name_is_still_somewhere()
    {
        // Nothing should ever render an empty heading, and "The Corridor" is
        // a truthful answer to "which room is this run in" when it is in none.
        RoomNames.For(null).Should().Be("The Corridor");
        RoomNames.For("   ").Should().Be("The Corridor");
    }
}
