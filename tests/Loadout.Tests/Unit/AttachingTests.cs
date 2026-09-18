using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The second credential, and the one thing the first does not grant.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard's own token is for watching and deciding: reading a run,
/// answering a gate it asked, holding it, stopping it. Every one of those is
/// something the run offered to have decided.
/// </para>
/// <para>
/// Typing at a live node is not. It puts words into a process running with
/// somebody's file access, at a moment nobody chose, over a port that may be on
/// a network — so it has its own passphrase, and what the page holds afterwards
/// is not that passphrase and stops working on its own.
/// </para>
/// </remarks>
public sealed class AttachingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private const string Passphrase = "a passphrase worth having";

    private static (Attaching Attach, Clock Clock) Made(string? kept = Passphrase)
    {
        var clock = new Clock(Noon);

        return (new Attaching(new Store { Kept = kept }, clock), clock);
    }

    [Fact]
    public async Task The_passphrase_is_exchanged_for_something_else_entirely()
    {
        var (attach, _) = Made();

        var grant = await attach.GrantAsync(Passphrase);

        grant.Succeeded.Should().BeTrue(grant.Error);

        // Not the passphrase, and nothing derived from it that could be walked
        // back to it: thirty-two random bytes this process made a moment ago.
        grant.Value.Should().NotBe(Passphrase);
        grant.Value.Should().HaveLength(64);

        attach.Holds(grant.Value).Should().BeTrue();
    }

    [Fact]
    public async Task The_wrong_passphrase_gets_nothing()
    {
        var (attach, _) = Made();

        var grant = await attach.GrantAsync("not it");

        grant.Failed.Should().BeTrue();
        attach.Holds("anything at all").Should().BeFalse();
    }

    [Fact]
    public async Task A_machine_with_no_passphrase_says_how_to_set_one()
    {
        // Rather than refusing in a way that reads as "you typed it wrong",
        // which would have somebody trying passphrases they never set.
        var (attach, _) = Made(kept: null);

        var grant = await attach.GrantAsync("anything");

        grant.Failed.Should().BeTrue();
        grant.Error.Should().Contain("loadout team attach set");
    }

    [Fact]
    public async Task A_grant_stops_working_on_its_own()
    {
        // Whether or not anybody remembers to give it back. A page left open on
        // a laptop somebody walked away from stops being one that can type at
        // anything.
        var (attach, clock) = Made();

        var grant = await attach.GrantAsync(Passphrase);

        clock.Now = Noon + Attaching.Lasts - TimeSpan.FromSeconds(1);
        attach.Holds(grant.Value).Should().BeTrue();

        clock.Now = Noon + Attaching.Lasts + TimeSpan.FromSeconds(1);
        attach.Holds(grant.Value).Should().BeFalse();
    }

    [Fact]
    public async Task A_grant_can_be_given_back_before_it_runs_out()
    {
        var (attach, _) = Made();

        var grant = await attach.GrantAsync(Passphrase);

        attach.Release(grant.Value);

        attach.Holds(grant.Value).Should().BeFalse();
    }

    [Fact]
    public async Task Two_people_attaching_get_two_grants_and_neither_is_the_other()
    {
        var (attach, _) = Made();

        var one = await attach.GrantAsync(Passphrase);
        var two = await attach.GrantAsync(Passphrase);

        one.Value.Should().NotBe(two.Value);

        // And giving one back does not take the other away.
        attach.Release(one.Value);

        attach.Holds(two.Value).Should().BeTrue();
    }

    [Fact]
    public void Nothing_at_all_never_holds()
    {
        var (attach, _) = Made();

        attach.Holds(null).Should().BeFalse();
        attach.Holds(string.Empty).Should().BeFalse();
    }

    [Fact]
    public async Task A_passphrase_too_short_to_be_worth_having_is_refused()
    {
        var (attach, _) = Made();

        var kept = await attach.RememberAsync("short");

        kept.Failed.Should().BeTrue();
        kept.Error.Should().Contain("Eight characters");
    }

    [Fact]
    public async Task A_passphrase_of_nothing_is_not_a_passphrase()
    {
        var (attach, _) = Made();

        var act = async () => await attach.RememberAsync("   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A credential store holding one thing, or nothing.</summary>
    private sealed class Store : ISecretProvider
    {
        public string? Kept { get; set; }

        public string Name => "test";

        public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(Kept is { Length: > 0 }
                ? OperationResult<string>.Ok(Kept)
                : OperationResult<string>.Fail("nothing kept", ExitCode.GeneralFailure));

        public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default)
        {
            Kept = value;

            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default)
        {
            Kept = null;

            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> TestAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());
    }
}
