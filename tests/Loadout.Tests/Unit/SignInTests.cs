using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The dashboard's sign-in: a cookie that survives restarts, codes that work
/// once, and a sign-out that ends both.
/// </summary>
public sealed class SignInTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "loadout-signin-" + Guid.NewGuid().ToString("N"));
    private readonly Clock _clock = new(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));

    private string KeyFile => Path.Combine(_home, "dashboard", "sign-in.key");

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void A_new_cookie_lets_its_holder_in()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);

        signIn.Check(signIn.IssueCookie()).Should().Be(CookieStanding.Good);
    }

    [Fact]
    public void A_cookie_survives_the_daemon_restarting_because_the_key_is_kept()
    {
        var cookie = new SignIn(KeyFile, clock: _clock).IssueCookie();

        new SignIn(KeyFile, clock: _clock).Check(cookie).Should().Be(CookieStanding.Good);
    }

    [Fact]
    public void A_cookie_a_day_old_is_still_good_and_asks_to_be_issued_again()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var cookie = signIn.IssueCookie();

        _clock.Advance(TimeSpan.FromDays(2));

        signIn.Check(cookie).Should().Be(CookieStanding.Renew);
    }

    [Fact]
    public void A_cookie_older_than_ninety_days_is_refused()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var cookie = signIn.IssueCookie();

        _clock.Advance(TimeSpan.FromDays(91));

        signIn.Check(cookie).Should().Be(CookieStanding.Refused);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.notanumber.abc")]
    [InlineData("2.1760616800.abc")]
    public void Something_that_is_not_a_cookie_is_refused(string? value)
    {
        new SignIn(KeyFile, clock: _clock).Check(value).Should().Be(CookieStanding.Refused);
    }

    [Fact]
    public void A_cookie_with_its_date_changed_is_refused()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var parts = signIn.IssueCookie().Split('.');

        var moved = $"{parts[0]}.{long.Parse(parts[1]) - 86400}.{parts[2]}";

        signIn.Check(moved).Should().Be(CookieStanding.Refused);
    }

    [Fact]
    public void Signing_out_ends_every_cookie_even_in_a_daemon_that_was_already_running()
    {
        var daemon = new SignIn(KeyFile, clock: _clock);
        var cookie = daemon.IssueCookie();
        daemon.Check(cookie).Should().Be(CookieStanding.Good);

        // The sign-out command, in a process of its own.
        new SignIn(KeyFile, clock: _clock).Rotate();

        daemon.Check(cookie).Should().Be(CookieStanding.Refused);
    }

    [Fact]
    public void A_sign_out_within_the_same_tick_of_the_file_clock_is_still_noticed()
    {
        // Windows stamps files from a clock that can tick as coarsely as
        // every fifteen milliseconds, so a key replaced soon after it was
        // read can carry the very same time. Pinned here, so it happens on
        // every run rather than on a fast machine now and then.
        var daemon = new SignIn(KeyFile, clock: _clock);
        var cookie = daemon.IssueCookie();
        var written = File.GetLastWriteTimeUtc(KeyFile);

        new SignIn(KeyFile, clock: _clock).Rotate();
        File.SetLastWriteTimeUtc(KeyFile, written);

        daemon.Check(cookie).Should().Be(CookieStanding.Refused);
    }

    [Fact]
    public void A_code_works_once()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var code = signIn.MintCode();

        signIn.Redeem(code).Should().BeTrue();
        signIn.Redeem(code).Should().BeFalse();
    }

    [Fact]
    public void A_code_minted_by_the_command_line_is_redeemed_by_the_daemon()
    {
        var daemon = new SignIn(KeyFile, clock: _clock);
        daemon.IssueCookie();

        var code = new SignIn(KeyFile, clock: _clock).MintCode();

        daemon.Redeem(code).Should().BeTrue();
    }

    [Fact]
    public void A_code_left_longer_than_two_minutes_no_longer_works()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var code = signIn.MintCode();

        _clock.Advance(TimeSpan.FromMinutes(3));

        signIn.Redeem(code).Should().BeFalse();
    }

    [Fact]
    public void A_code_is_not_a_cookie_and_a_cookie_is_not_a_code()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);

        signIn.Check(signIn.MintCode()).Should().Be(CookieStanding.Refused);
        signIn.Redeem(signIn.IssueCookie()).Should().BeFalse();
    }

    [Fact]
    public void A_damaged_key_is_replaced_and_signs_everybody_out()
    {
        var signIn = new SignIn(KeyFile, clock: _clock);
        var cookie = signIn.IssueCookie();

        File.WriteAllBytes(KeyFile, [1, 2, 3]);

        var after = new SignIn(KeyFile, clock: _clock);
        after.Check(cookie).Should().Be(CookieStanding.Refused);
        after.Check(after.IssueCookie()).Should().Be(CookieStanding.Good);
        new FileInfo(KeyFile).Length.Should().Be(32);
    }

    [Fact]
    public void A_new_key_file_is_handed_over_to_be_limited_to_its_owner()
    {
        var limited = new List<string>();
        var signIn = new SignIn(KeyFile, limited.Add, _clock);

        signIn.IssueCookie();

        limited.Should().ContainSingle().Which.Should().StartWith(KeyFile);
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
