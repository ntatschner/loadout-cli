using FluentAssertions;
using Loadout.Core.Sessions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a running session looks like from the outside.
/// </summary>
/// <remarks>
/// Everything here is derived from the registry and a file's last write time.
/// Nothing attaches to a console or reads another process: doing that on this
/// machine once took out every live session on it, and a monitor that can break
/// what it watches is not one worth having.
/// </remarks>
public sealed class SessionMonitorTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static RunningSession Session(DateTimeOffset startedAt) =>
        new(
            "launch-1",
            "demo",
            "Demo",
            "claude",
            null,
            "C:/work/demo",
            1234,
            startedAt,
            startedAt);

    [Fact]
    public void A_session_that_wrote_recently_is_working()
    {
        var activity = SessionMonitor.Describe(
            Session(Now.AddHours(-1)), Now.AddMinutes(-1), Now);

        activity.State.Should().Be(SessionState.Working);
        activity.Elapsed.Should().Be(TimeSpan.FromHours(1));
        activity.Quiet.Should().Be(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void A_session_that_has_said_nothing_for_a_while_is_idle()
    {
        SessionMonitor.Describe(Session(Now.AddHours(-1)), Now.AddMinutes(-30), Now)
            .State.Should().Be(SessionState.Idle);
    }

    [Fact]
    public void The_threshold_is_reached_rather_than_passed()
    {
        SessionMonitor.Describe(
            Session(Now.AddHours(-1)), Now - SessionMonitor.IdleAfter, Now)
            .State.Should().Be(SessionState.Idle);

        SessionMonitor.Describe(
            Session(Now.AddHours(-1)), Now - SessionMonitor.IdleAfter + TimeSpan.FromSeconds(1), Now)
            .State.Should().Be(SessionState.Working);
    }

    [Fact]
    public void A_transcript_nobody_can_read_is_unknown_rather_than_idle()
    {
        var activity = SessionMonitor.Describe(Session(Now.AddHours(-1)), null, Now);

        // The distinction that matters most here. Neither agent publishes its
        // transcript format, so a session this cannot see is one it cannot
        // judge — and calling that idle would tell somebody their agent had
        // stopped when it may be working perfectly well.
        activity.State.Should().Be(SessionState.Unknown);
        activity.Quiet.Should().BeNull();
    }

    [Fact]
    public void A_session_still_reports_how_long_it_has_run_when_it_cannot_be_seen()
    {
        // The registry knows this much whatever the transcript does, and it is
        // the more useful half of the answer.
        SessionMonitor.Describe(Session(Now.AddHours(-2)), null, Now)
            .Elapsed.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void A_clock_that_went_backwards_does_not_produce_a_negative_age()
    {
        var activity = SessionMonitor.Describe(
            Session(Now.AddMinutes(5)), Now.AddMinutes(5), Now);

        // A session that started in the future is a clock problem, not a
        // session. Showing a negative age reads as a bug in the launcher,
        // which is exactly what it would be.
        activity.Elapsed.Should().Be(TimeSpan.Zero);
        activity.Quiet.Should().Be(TimeSpan.Zero);
        activity.State.Should().Be(SessionState.Working);
    }

    [Fact]
    public void A_quiet_session_that_speaks_again_is_working_again()
    {
        // Idle is a description, never a verdict. Nothing is stopped, and a
        // session quiet for an hour is working the moment it writes again.
        var quiet = SessionMonitor.Describe(Session(Now.AddHours(-2)), Now.AddHours(-1), Now);
        var awake = SessionMonitor.Describe(Session(Now.AddHours(-2)), Now, Now);

        quiet.State.Should().Be(SessionState.Idle);
        awake.State.Should().Be(SessionState.Working);
    }

    [Theory]
    [InlineData(30, "just now")]
    [InlineData(90, "1m")]
    [InlineData(60 * 64, "1h 4m")]
    [InlineData(60 * 60 * 26, "1d 2h")]
    public void A_duration_is_said_the_way_somebody_would_say_it(int seconds, string expected)
    {
        // Coarse on purpose: nobody reading a session list needs seconds, and
        // "1h 4m" is read at a glance where "01:04:37" has to be parsed.
        SessionMonitor.Spoken(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }
}
