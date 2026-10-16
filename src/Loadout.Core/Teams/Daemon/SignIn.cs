using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Loadout.Core.Teams.Daemon;

/// <summary>Whether a sign-in cookie lets its holder in, and whether it should be issued again.</summary>
public enum CookieStanding
{
    /// <summary>Missing, altered, signed with a key since rotated, or too old.</summary>
    Refused,

    /// <summary>Good, and recent enough to leave alone.</summary>
    Good,

    /// <summary>Good, and old enough that a fresh one should go back with the answer.</summary>
    Renew,
}

/// <summary>
/// The dashboard's sign-in on this machine: a long-lived cookie and the
/// one-time codes that are exchanged for it.
/// </summary>
/// <remarks>
/// <para>
/// Both are signed with one key kept in local state, so they outlive a
/// restart of the daemon, which is what lets an installed app window keep
/// working across restarts and updates. Anything with the key can mint a
/// code, which is how the command line hands the browser a sign-in without
/// putting a lasting credential in an address bar or a history.
/// </para>
/// <para>
/// A code lasts two minutes and works once. A cookie lasts ninety days from
/// when it was issued and is issued again on use once it is a day old, so a
/// window in daily use never runs out. Rotating the key ends every cookie and
/// code at once, which is what signing out does.
/// </para>
/// <para>
/// The key is read from its file every time it is used, so a sign-out run
/// from a terminal reaches a daemon that is already running. Thirty-two bytes
/// read when a request arrives costs nothing worth saving, and the file's
/// modified time cannot stand in for it: Windows stamps files from a clock
/// that can tick as coarsely as every fifteen milliseconds, so a key replaced
/// soon after it was read could look unchanged.
/// </para>
/// </remarks>
public sealed class SignIn
{
    /// <summary>The cookie's name.</summary>
    public const string CookieName = "loadout_signin";

    /// <summary>How long a cookie lasts from when it was issued.</summary>
    public static readonly TimeSpan CookieLife = TimeSpan.FromDays(90);

    /// <summary>How old a cookie gets before a fresh one is sent back.</summary>
    public static readonly TimeSpan RenewAfter = TimeSpan.FromDays(1);

    /// <summary>How long a one-time code may wait to be used.</summary>
    public static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(2);

    private const int KeyLength = 32;
    private const string Version = "1";

    private readonly string _keyFile;
    private readonly TimeProvider _clock;
    private readonly Action<string>? _restrict;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _spent = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private byte[]? _key;

    /// <summary>Signs with the key in the file given, creating it the first time.</summary>
    /// <param name="keyFile">Where the key lives, in this machine's local state.</param>
    /// <param name="restrict">Limits a newly written key file to its owner.</param>
    /// <param name="clock">The time, for tests.</param>
    public SignIn(string keyFile, Action<string>? restrict = null, TimeProvider? clock = null)
    {
        _keyFile = keyFile;
        _restrict = restrict;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The sign-in for this machine, with its key in local state.</summary>
    /// <remarks>
    /// The state directory is already limited to its owner, which is what
    /// keeps the key from anybody else on the machine.
    /// </remarks>
    /// <param name="paths">Where this machine keeps its state.</param>
    public static SignIn ForMachine(Loadout.Platform.Abstractions.IPlatformPaths paths) =>
        new(Path.Combine(paths.Paths.State, "dashboard", "sign-in.key"));

    /// <summary>A fresh cookie value.</summary>
    public string IssueCookie()
    {
        var issued = Now().ToString(CultureInfo.InvariantCulture);

        return $"{Version}.{issued}.{Sign("cookie", issued)}";
    }

    /// <summary>Whether a cookie value lets its holder in.</summary>
    /// <param name="value">The cookie, as the browser sent it.</param>
    public CookieStanding Check(string? value)
    {
        var parts = value?.Split('.');

        if (parts is not { Length: 3 }
            || parts[0] != Version
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var issued)
            || !Matches(Sign("cookie", parts[1]), parts[2]))
        {
            return CookieStanding.Refused;
        }

        var age = TimeSpan.FromSeconds(Now() - issued);

        if (age < -TimeSpan.FromMinutes(5) || age > CookieLife)
        {
            return CookieStanding.Refused;
        }

        return age > RenewAfter ? CookieStanding.Renew : CookieStanding.Good;
    }

    /// <summary>A one-time code to exchange for a cookie.</summary>
    public string MintCode()
    {
        var issued = Now().ToString(CultureInfo.InvariantCulture);
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(12));

        return $"{issued}.{nonce}.{Sign("code", issued + "." + nonce)}";
    }

    /// <summary>
    /// Uses up a one-time code. True once for a good code within its two
    /// minutes, and false for everything else, including the same code again.
    /// </summary>
    /// <param name="code">The code, as it arrived.</param>
    public bool Redeem(string? code)
    {
        var parts = code?.Split('.');

        if (parts is not { Length: 3 }
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var issued)
            || !Matches(Sign("code", parts[0] + "." + parts[1]), parts[2]))
        {
            return false;
        }

        var age = TimeSpan.FromSeconds(Now() - issued);

        if (age < -TimeSpan.FromMinutes(1) || age > CodeLife)
        {
            return false;
        }

        Forget();

        return _spent.TryAdd(parts[1], _clock.GetUtcNow());
    }

    /// <summary>Replaces the key, which ends every cookie and code signed with the old one.</summary>
    public void Rotate()
    {
        lock (_gate)
        {
            Write(RandomNumberGenerator.GetBytes(KeyLength));
        }
    }

    private long Now() => _clock.GetUtcNow().ToUnixTimeSeconds();

    /// <summary>Drops spent codes too old to be redeemed anyway.</summary>
    private void Forget()
    {
        var cutoff = _clock.GetUtcNow() - CodeLife - TimeSpan.FromMinutes(1);

        foreach (var spent in _spent)
        {
            if (spent.Value < cutoff)
            {
                _spent.TryRemove(spent.Key, out _);
            }
        }
    }

    private string Sign(string purpose, string payload)
    {
        var mac = HMACSHA256.HashData(Key(), Encoding.UTF8.GetBytes(purpose + "|" + payload));

        return Base64Url.EncodeToString(mac);
    }

    private static bool Matches(string expected, string given) =>
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given));

    private byte[] Key()
    {
        lock (_gate)
        {
            byte[]? stored;

            try
            {
                stored = File.Exists(_keyFile) ? File.ReadAllBytes(_keyFile) : null;
            }
            catch (IOException) when (_key is not null)
            {
                // Caught mid-replace by a sign-out in another process. The
                // key it is replacing is the right answer for this moment.
                return _key;
            }

            if (stored is { Length: KeyLength })
            {
                _key = stored;

                return _key;
            }

            // None yet, or one that is not a key. Replacing a damaged one
            // signs everybody out, which is the safe direction to fail in.
            return Write(RandomNumberGenerator.GetBytes(KeyLength));
        }
    }

    /// <summary>Must be called under the lock.</summary>
    private byte[] Write(byte[] key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_keyFile)!);

        var temporary = _keyFile + ".new";
        File.WriteAllBytes(temporary, key);
        _restrict?.Invoke(temporary);
        File.Move(temporary, _keyFile, overwrite: true);

        _key = key;

        return key;
    }
}
