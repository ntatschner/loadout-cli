using FluentAssertions;
using Loadout.Platform.Common;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// Whether a process is still there, asked of the real operating system.
/// </summary>
/// <remarks>
/// <para>
/// The registry's tests use a process table they write, which proves the
/// registry asks the right question and nothing at all about the answer. This is
/// the other half, and it needs real processes: the only one that can be relied
/// on to exist is the test run itself, so that is what every case is built from.
/// </para>
/// <para>
/// The case worth having is the reused identifier. Nothing here can arrange a
/// genuine reuse — that needs a process to die and the number to come round
/// again — but the check that defends against it is a comparison of start times,
/// and a start time that is wrong by hours is indistinguishable to that
/// comparison from one that is wrong because the number was recycled.
/// </para>
/// </remarks>
public sealed class ProcessInspectorTests
{
    private readonly ProcessInspector _inspector = new();

    [Fact]
    public void The_process_asking_is_running()
    {
        _inspector.IsRunning(_inspector.CurrentProcessId, _inspector.CurrentProcessStartedAt)
            .Should().BeTrue();
    }

    [Fact]
    public void An_identifier_whose_process_started_at_another_time_is_not_running()
    {
        // The shape of a recycled identifier: the number is live, and the thing
        // wearing it is not what was recorded against it.
        _inspector.IsRunning(_inspector.CurrentProcessId, _inspector.CurrentProcessStartedAt.AddHours(3))
            .Should().BeFalse();
    }

    [Fact]
    public void An_identifier_nothing_bears_is_not_running()
    {
        // Far above anything an operating system hands out, so the lookup fails
        // rather than finding something. If one ever did exist, its start time
        // would not match either, and the answer would be the same.
        _inspector.IsRunning(int.MaxValue - 1, _inspector.CurrentProcessStartedAt)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_identifier_that_could_never_be_real_is_refused_without_asking(int processId)
    {
        _inspector.IsRunning(processId, _inspector.CurrentProcessStartedAt).Should().BeFalse();
    }

    [Fact]
    public void The_start_time_survives_a_round_trip_through_a_record()
    {
        // The registry writes this moment to a file and reads it back before
        // asking about it. A comparison that only held for the value in memory
        // would report every session as gone the moment it was written down.
        var written = _inspector.CurrentProcessStartedAt.ToString("O");

        var read = DateTimeOffset.Parse(
            written,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind);

        _inspector.IsRunning(_inspector.CurrentProcessId, read).Should().BeTrue();
    }
}
