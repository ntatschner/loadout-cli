using System.Security.Cryptography;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// The second credential, for the one thing the first does not grant.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard's token is for watching and deciding: reading a run,
/// answering a gate it asked, holding it, stopping it. Every one of those is
/// something the run offered to have decided.
/// </para>
/// <para>
/// Typing at a live node is not. It puts words into a process running with the
/// file access of whoever started it, at a moment nobody chose, and the page
/// can be reached from the network. So it needs its own credential: a
/// passphrase kept in the operating system's credential store, given once, and
/// exchanged for something that stops working on its own.
/// </para>
/// <para>
/// That split is the whole of it and it is meant to be said plainly rather
/// than implied: the token is for watching and deciding; typing at a process
/// is a separate grant.
/// </para>
/// </remarks>
public sealed class Attaching
{
    private readonly ISecretProvider _secrets;
    private readonly TimeProvider _time;

    /// <summary>Grants given out and not yet expired, by their own value.</summary>
    /// <remarks>
    /// In memory only. A daemon that restarts asks again, which is right: the
    /// grant is for a person at a page, and the page has gone too.
    /// </remarks>
    private readonly Dictionary<string, DateTimeOffset> _given = new(StringComparer.Ordinal);

    /// <summary>When each recent wrong answer arrived.</summary>
    /// <remarks>
    /// Counted for the machine rather than per caller, because there is no
    /// caller to count: on loopback every request comes from the same address,
    /// and an address is not an identity anyway.
    ///
    /// That means somebody who can reach this can also keep it locked, which is
    /// a denial of service and is said out loud rather than glossed over. It
    /// needs the dashboard's token to reach at all, the lock lasts a minute,
    /// and being unable to type at a node is a great deal less bad than
    /// somebody else being able to.
    /// </remarks>
    private readonly List<DateTimeOffset> _wrong = [];

    public Attaching(ISecretProvider secrets, TimeProvider? time = null)
    {
        _secrets = secrets;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Where the passphrase lives, in the credential store's terms.</summary>
    public const string Reference = "loadout/team-attach";

    /// <summary>How many wrong answers in a row before it stops listening.</summary>
    public const int Patience = 5;

    /// <summary>How long it stops listening for.</summary>
    /// <remarks>
    /// A minute rather than an hour. The point is to make guessing cost
    /// something, not to lock the person out of their own machine: five tries
    /// a minute turns an eight-character passphrase from an afternoon's work
    /// into geological time, and a person who mistyped theirs waits a minute.
    /// </remarks>
    public static readonly TimeSpan Cools = TimeSpan.FromMinutes(1);

    /// <summary>How long a grant lasts.</summary>
    /// <remarks>
    /// Long enough to steer a node through a turn and short enough that a page
    /// left open on a laptop somebody walked away from stops being one that can
    /// type at anything.
    /// </remarks>
    public static readonly TimeSpan Lasts = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The letters and digits a made passphrase is built from.
    /// </summary>
    /// <remarks>
    /// No 0, 1, i, l or o. Somebody is going to read this off one screen and
    /// type it into another, and a passphrase that cannot be told apart from
    /// itself is one they will get wrong and blame on the machine.
    /// </remarks>
    private const string Alphabet = "23456789abcdefghjkmnpqrstuvwxyz";

    /// <summary>Makes a passphrase, in groups so a person can read it out.</summary>
    /// <remarks>
    /// Twenty characters of that alphabet is a little under a hundred bits,
    /// against the eight characters of whatever a person thinks of that the
    /// floor otherwise allows. The hyphens are part of it and are typed like
    /// anything else; they are there because four groups of five can be read
    /// off a screen and a run of twenty cannot.
    /// </remarks>
    public static string Make()
    {
        var chosen = RandomNumberGenerator.GetItems<char>(Alphabet, 20);

        return string.Join(
            '-',
            Enumerable.Range(0, 4).Select(group => new string(chosen, group * 5, 5)));
    }

    /// <summary>Keeps the passphrase, replacing any it had.</summary>
    public Task<OperationResult> RememberAsync(string passphrase, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passphrase);

        return passphrase.Trim().Length < 8
            ? Task.FromResult(OperationResult.Fail(
                "That is too short to be worth having. Eight characters at least.",
                ExitCode.InvalidArguments))
            : _secrets.SetAsync(Reference, passphrase.Trim(), ct);
    }

    /// <summary>Forgets it, which stops anything being able to attach.</summary>
    public Task<OperationResult> ForgetAsync(CancellationToken ct = default) =>
        _secrets.RemoveAsync(Reference, ct);

    /// <summary>Whether a passphrase is set, never what it is.</summary>
    public async Task<bool> IsSetAsync(CancellationToken ct = default) =>
        (await _secrets.GetAsync(Reference, ct).ConfigureAwait(false)).Value is { Length: > 0 };

    /// <summary>
    /// Exchanges the passphrase for a grant that stops working on its own.
    /// </summary>
    /// <remarks>
    /// Compared in fixed time. The alternative leaks the passphrase one
    /// character at a time to anything that can make requests and read a clock,
    /// which over a network is anything at all.
    /// </remarks>
    public async Task<OperationResult<string>> GrantAsync(
        string? passphrase,
        CancellationToken ct = default)
    {
        // Before the secret is even read, so a locked-out caller learns
        // nothing about whether one is set.
        lock (_wrong)
        {
            Forget();

            if (_wrong.Count >= Patience)
            {
                return OperationResult<string>.Fail(
                    "Too many wrong answers. Wait a minute and try again.",
                    ExitCode.AuthenticationRequired);
            }
        }

        var kept = await _secrets.GetAsync(Reference, ct).ConfigureAwait(false);

        if (kept.Failed || kept.Value is not { Length: > 0 } wanted)
        {
            // Counted like any other refusal. Without this a machine with no
            // passphrase never locks, and whether one is set is readable from
            // the outside by whether the sixth try is turned away.
            lock (_wrong)
            {
                _wrong.Add(_time.GetUtcNow());
            }

            return OperationResult<string>.Fail(
                "Nothing on this machine can attach to a node yet. Set a passphrase with: "
                + "loadout team attach set",
                ExitCode.AuthenticationRequired);
        }

        if (passphrase is not { Length: > 0 }
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(passphrase),
                Encoding.UTF8.GetBytes(wanted)))
        {
            lock (_wrong)
            {
                _wrong.Add(_time.GetUtcNow());
            }

            return OperationResult<string>.Fail(
                "That is not the passphrase.", ExitCode.AuthenticationRequired);
        }

        // Right, so the count goes. Otherwise four mistyped attempts would
        // keep counting against somebody who has since got it right.
        lock (_wrong)
        {
            _wrong.Clear();
        }

        var grant = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

        lock (_given)
        {
            Sweep();

            _given[grant] = _time.GetUtcNow() + Lasts;
        }

        return OperationResult<string>.Ok(grant);
    }

    /// <summary>Drops wrong answers old enough to stop counting.</summary>
    private void Forget()
    {
        var since = _time.GetUtcNow() - Cools;

        _wrong.RemoveAll(one => one < since);
    }

    /// <summary>Whether a grant is one this daemon gave out and still stands.</summary>
    public bool Holds(string? grant)
    {
        if (grant is not { Length: > 0 })
        {
            return false;
        }

        lock (_given)
        {
            Sweep();

            // Looked up rather than compared one by one: a grant is thirty-two
            // random bytes this process made a moment ago, so there is nothing
            // to guess a character at a time.
            return _given.ContainsKey(grant);
        }
    }

    /// <summary>Gives a grant back before it runs out.</summary>
    public void Release(string? grant)
    {
        if (grant is { Length: > 0 })
        {
            lock (_given)
            {
                _given.Remove(grant);
            }
        }
    }

    /// <summary>Forgets whatever has run out. Called under the lock.</summary>
    private void Sweep()
    {
        var now = _time.GetUtcNow();

        foreach (var expired in _given.Where(one => one.Value <= now).Select(one => one.Key).ToList())
        {
            _given.Remove(expired);
        }
    }
}
