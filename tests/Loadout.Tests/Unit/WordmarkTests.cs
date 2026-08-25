using Loadout.Tui.Terminal;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Checks the opening animation actually draws something, and moves.
/// <para>
/// An animation is exactly the sort of thing nobody tests and which then turns
/// out to render as a column of spaces on somebody else's machine, or to sit
/// perfectly still. Both of those are cheap to rule out here.
/// </para>
/// </summary>
public sealed class WordmarkTests
{
    [Fact]
    public void Every_frame_is_the_same_size()
    {
        // A frame that changed shape would make the screen jump around it.
        for (var step = 0; step < 40; step++)
        {
            var frame = Wordmark.Frame(step);

            frame.Should().HaveCount(Wordmark.Height);
            frame.Should().OnlyContain(row => row.Length == Wordmark.Width);
        }
    }

    [Fact]
    public void The_name_is_actually_drawn()
    {
        var frame = Wordmark.Frame(0);

        // Something has to be on the screen. Blank frames are the failure this
        // is really guarding against.
        string.Concat(frame).Should().Contain("█");
    }

    [Fact]
    public void The_letters_do_not_all_move_together()
    {
        // The point of the wave is that it travels along the word. If every
        // letter sat at the same height the frame would be a rectangle that
        // bobs, which is not what was asked for.
        var frame = Wordmark.Frame(0);

        var topsPerColumn = frame
            .Select(row => row.IndexOf('█', StringComparison.Ordinal))
            .Where(index => index >= 0)
            .ToList();

        topsPerColumn.Should().NotBeEmpty();

        // At least one row must start further in than the first, which can only
        // happen if the letters are at different heights.
        topsPerColumn.Distinct().Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void The_wave_moves_between_frames()
    {
        var first = string.Join("\n", Wordmark.Frame(0));

        // Somewhere in a full cycle the frame has to differ, or nothing is
        // animating at all.
        var moved = Enumerable.Range(1, 20)
            .Any(step => string.Join("\n", Wordmark.Frame(step)) != first);

        moved.Should().BeTrue();
    }

    [Fact]
    public void The_wave_comes_back_round()
    {
        // Negative steps are as valid as positive ones, because the caller
        // counts frames and nothing stops it starting anywhere.
        var frame = Wordmark.Frame(-7);

        frame.Should().HaveCount(Wordmark.Height);
        string.Concat(frame).Should().Contain("█");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(40)]
    public void The_reading_indicator_is_the_width_it_was_asked_for(int width)
    {
        // It sits in a fixed-width field; one that changed length would push
        // whatever follows it around.
        for (var step = 0; step < 12; step++)
        {
            Wordmark.Pulse(step, width).Should().HaveLength(width);
        }
    }

    [Fact]
    public void The_reading_indicator_moves()
    {
        var first = Wordmark.Pulse(0);

        Enumerable.Range(1, 12)
            .Any(step => Wordmark.Pulse(step) != first)
            .Should().BeTrue();
    }

    [Fact]
    public void The_reading_indicator_refuses_a_width_it_cannot_draw()
    {
        var act = () => Wordmark.Pulse(0, 0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
