using System.Text;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// Deciding what is worth saying out loud somewhere else, and saying it once.
/// </summary>
/// <remarks>
/// <para>
/// The same discipline as the rail: a notice goes out when a reason appears,
/// and a reason that clears and comes back is news again. What it must never
/// do is repeat itself every time it looks — a channel that says the same
/// thing every thirty seconds is a channel somebody mutes, and a muted channel
/// is worse than none because it looks like it is working.
/// </para>
/// <para>
/// What has been said is kept in memory only. A daemon that restarts says
/// everything still outstanding once more, which is a fair price for not
/// keeping a file nobody would ever look at — and a restart is rare enough
/// that repeating a handful of live reasons is not the failure above.
/// </para>
/// </remarks>
public sealed class Notices
{
    private readonly ISecretProvider _secrets;
    private readonly HttpClient _client;

    /// <summary>What has already gone out, by <see cref="RunAttention.Key"/>.</summary>
    /// <remarks>
    /// Keyed on what the reason is about as well as its kind, because one run
    /// can be waiting on three different questions at once and every one of
    /// them is kind "asking". Never on its words: a silence's words say how
    /// long it has lasted, and keyed on them one quiet node was news again
    /// every minute.
    /// </remarks>
    private HashSet<string> _said = new(StringComparer.Ordinal);

    public Notices(ISecretProvider secrets, HttpClient client)
    {
        _secrets = secrets;
        _client = client;
    }

    /// <summary>Where the address to post to lives, in the credential store's terms.</summary>
    /// <remarks>
    /// In the credential store rather than the config file because a Slack or
    /// Discord webhook address <em>is</em> the credential: anybody holding it
    /// can post into that channel as you.
    /// </remarks>
    public const string Reference = "loadout/team-notify";

    /// <summary>Keeps the address, replacing any it already had.</summary>
    public Task<OperationResult> RememberAsync(string url, CancellationToken ct = default) =>
        _secrets.SetAsync(Reference, url, ct);

    /// <summary>Forgets the address, which stops anything being sent.</summary>
    public Task<OperationResult> ForgetAsync(CancellationToken ct = default) =>
        _secrets.RemoveAsync(Reference, ct);

    /// <summary>Whether an address is held, never what it is.</summary>
    public async Task<bool> IsSetAsync(CancellationToken ct = default) =>
        (await _secrets.GetAsync(Reference, ct).ConfigureAwait(false)).Value is { Length: > 0 };

    /// <summary>
    /// Says anything newly worth saying about these runs, and nothing it has
    /// said before.
    /// </summary>
    /// <remarks>
    /// <c>told</c> hears each notice tried and whether it landed, so the
    /// event history can say a channel was told, or could not be.
    /// </remarks>
    /// <returns>How many notices went out.</returns>
    public async Task<int> SayAsync(
        IReadOnlyList<RunSummary> runs,
        NoticeKind kind,
        string? chat,
        string address,
        Func<RunSummary, string> linkTo,
        DateTimeOffset now,
        CancellationToken ct = default,
        Action<RunSummary, Attention, bool>? told = null)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(linkTo);

        var wanted = new List<(RunSummary Run, Attention Reason, string Key)>();

        foreach (var run in runs)
        {
            foreach (var reason in RunAttention.For(run, now))
            {
                wanted.Add((run, reason, RunAttention.Key(run, reason)));
            }
        }

        var fresh = wanted.Where(one => !_said.Contains(one.Key)).ToList();

        // Rebuilt from what is true now rather than added to, so a reason that
        // has cleared is forgotten and is news again if it returns.
        var saying = new HashSet<string>(wanted.Select(one => one.Key), StringComparer.Ordinal);

        var sent = 0;

        foreach (var (run, reason, key) in fresh)
        {
            var (detail, command) = Spoken(run, reason);

            var landed = await PostAsync(
                    kind,
                    Notifier.Title(run, reason),
                    detail,
                    linkTo(run),
                    chat,
                    address,
                    command,
                    ct).ConfigureAwait(false);

            told?.Invoke(run, reason, landed);

            if (landed)
            {
                sent++;
            }
            else
            {
                // It did not land, so it has not been said, so it is still
                // news. Recording it regardless meant one refused request -
                // a chat service restarting, a minute of no network - lost the
                // notice for good: the reason stays true, so it is never fresh
                // again, and the run sits waiting for somebody who was never
                // told. Swallowing the failure is right; forgetting what it
                // was about is not.
                saying.Remove(key);
            }
        }

        // Written at the end rather than before sending, so that a cancelled
        // pass has said nothing and starts again.
        _said = saying;

        return sent;
    }

    /// <summary>
    /// What a notice says, and the command to set apart from it where it is a
    /// permission with one.
    /// </summary>
    /// <remarks>
    /// Two parts on a phone: the plain line - who wants to do what, what was
    /// refused, what the command does - and then the command on its own. The
    /// whole question as one sentence buried the words in shell.
    /// </remarks>
    internal static (string Detail, string? Command) Spoken(RunSummary run, Attention reason)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(reason);

        if (reason.Kind == AttentionKind.Asking
            && run.Waiting.FirstOrDefault(gate => string.Equals(gate.Id, reason.About, StringComparison.Ordinal)) is { } asked
            && AskWording.View(asked) is { Command: { Length: > 0 } command } view)
        {
            return (view.Summary + " Let it?", command);
        }

        return (reason.Detail, null);
    }

    /// <summary>Sends one, and says whether it landed.</summary>
    /// <remarks>
    /// Failures are swallowed on purpose. A chat service being down is not a
    /// reason for a daemon to stop watching runs, and there is nowhere useful
    /// to report it to from inside a loop that nobody is reading.
    /// </remarks>
    public async Task<bool> PostAsync(
        NoticeKind kind,
        string title,
        string detail,
        string link,
        string? chat,
        string address,
        string? command = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        try
        {
            using var body = new StringContent(
                Notifier.Body(kind, title, detail, link, chat, command),
                Encoding.UTF8,
                "application/json");

            using var answer = await _client.PostAsync(new Uri(address), body, ct).ConfigureAwait(false);

            return answer.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            or UriFormatException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>The address held, or null.</summary>
    public async Task<string?> AddressAsync(CancellationToken ct = default)
    {
        var read = await _secrets.GetAsync(Reference, ct).ConfigureAwait(false);

        return read.Succeeded && read.Value is { Length: > 0 } url ? url : null;
    }

    /// <summary>Which shape to send, from the word somebody configured.</summary>
    public static NoticeKind? KindOf(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        "slack" => NoticeKind.Slack,
        "discord" => NoticeKind.Discord,
        "teams" => NoticeKind.Teams,
        "telegram" => NoticeKind.Telegram,
        "generic" => NoticeKind.Generic,
        _ => null,
    };
}
