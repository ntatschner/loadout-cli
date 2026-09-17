using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Telling a run in another process to stop, hold, or read something.
/// </summary>
/// <remarks>
/// <para>
/// A run is a command the daemon started, so nothing can reach into it. The
/// channel is its own directory, in the same way its questions come out.
/// </para>
/// <para>
/// The property worth being certain about is that a message is handed over
/// exactly once. Twice reads as somebody repeating themselves crossly, and
/// nought means the thing they typed never arrived.
/// </para>
/// </remarks>
public sealed class RunControlTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-control-" + Guid.NewGuid().ToString("N"));

    public RunControlTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void A_run_nobody_has_touched_is_neither_stopped_nor_held()
    {
        RunControl.Stopped(_directory).Should().BeFalse();
        RunControl.Paused(_directory).Should().BeFalse();
        RunControl.TakeMessages(_directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Asking_it_to_stop_is_seen_by_the_run()
    {
        await RunControl.StopAsync(_directory);

        RunControl.Stopped(_directory).Should().BeTrue();
    }

    [Fact]
    public async Task A_hold_can_be_lifted_and_a_stop_cannot()
    {
        await RunControl.PauseAsync(_directory);
        RunControl.Paused(_directory).Should().BeTrue();

        RunControl.Resume(_directory);
        RunControl.Paused(_directory).Should().BeFalse();

        // Deliberately asymmetric. Letting a run carry on is undoing a hold;
        // there is no undoing a stop, and pretending otherwise would invite
        // somebody to try it on a run that had already ended.
        await RunControl.StopAsync(_directory);
        RunControl.Resume(_directory);
        RunControl.Stopped(_directory).Should().BeTrue();
    }

    [Fact]
    public async Task Letting_a_run_carry_on_when_it_was_never_held_is_not_an_error()
    {
        RunControl.Resume(_directory);

        await Task.CompletedTask;

        RunControl.Paused(_directory).Should().BeFalse();
    }

    [Fact]
    public async Task What_is_said_to_a_lead_is_handed_over_once()
    {
        await RunControl.SayAsync(_directory, "stop rewriting the tests");

        RunControl.TakeMessages(_directory).Should().ContainSingle()
            .Which.Should().Be("stop rewriting the tests");

        // Taken, not read. The lead hearing it twice would read as somebody
        // repeating themselves crossly.
        RunControl.TakeMessages(_directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Several_things_said_are_handed_over_in_the_order_they_were_said()
    {
        await RunControl.SayAsync(_directory, "first");
        await Task.Delay(5);
        await RunControl.SayAsync(_directory, "second");
        await Task.Delay(5);
        await RunControl.SayAsync(_directory, "third");

        RunControl.TakeMessages(_directory).Should().Equal("first", "second", "third");
    }

    [Fact]
    public async Task Nothing_is_handed_over_for_an_empty_message()
    {
        await RunControl.SayAsync(_directory, "   ");

        // Written and then found to be nothing: the file goes, and the lead is
        // not handed a blank line to interpret.
        RunControl.TakeMessages(_directory).Should().BeEmpty();
    }

    [Fact]
    public void A_directory_that_is_not_there_answers_rather_than_throwing()
    {
        var missing = Path.Combine(_directory, "nowhere");

        RunControl.Stopped(missing).Should().BeFalse();
        RunControl.Paused(missing).Should().BeFalse();
        RunControl.TakeMessages(missing).Should().BeEmpty();
    }
}
