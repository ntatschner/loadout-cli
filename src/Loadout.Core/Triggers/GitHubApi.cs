using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Triggers;

/// <summary>What GitHub answered to one read.</summary>
/// <param name="Status">The HTTP status, or 0 where nothing came back.</param>
/// <param name="Body">The body, for a 200.</param>
/// <param name="ETag">The tag to send next time, so an unchanged answer costs no quota.</param>
/// <param name="Remaining">Requests left in the hour, where GitHub said.</param>
/// <param name="Limit">Requests allowed in the hour, where GitHub said.</param>
/// <param name="Scopes">For a classic token, the scopes it holds.</param>
/// <param name="Expires">For a token that expires, when.</param>
/// <param name="Problem">What went wrong before an answer came, where something did.</param>
public sealed record GitHubReply(
    int Status,
    string? Body = null,
    string? ETag = null,
    int? Remaining = null,
    int? Limit = null,
    string? Scopes = null,
    DateTimeOffset? Expires = null,
    string? Problem = null)
{
    /// <summary>Unchanged since the tag that was sent.</summary>
    public bool Unchanged => Status == (int)HttpStatusCode.NotModified;

    /// <summary>The credential was refused.</summary>
    public bool Refused => Status == (int)HttpStatusCode.Unauthorized;
}

/// <summary>Reads from GitHub's REST API. The daemon supplies the real one; a test scripts it.</summary>
public interface IGitHubReads
{
    /// <summary>Reads one path, sending the tag from the last read of it.</summary>
    Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct);
}

/// <summary>Which token the poller reads with, and a fingerprint that says when it changes.</summary>
/// <param name="Kind">Where it came from: <see cref="Dedicated"/>, <see cref="GhToken"/> or <see cref="None"/>.</param>
/// <param name="Token">The token. Never written anywhere.</param>
/// <param name="Fingerprint">A hash of the first few bytes of one, enough to notice a renewal and nothing more.</param>
public sealed record TriggerCredential(string Kind, string? Token, string? Fingerprint)
{
    /// <summary>A token set for triggers alone, read-only by intent.</summary>
    public const string Dedicated = "trigger token";

    /// <summary>The token the GitHub CLI is signed in with.</summary>
    public const string GhToken = "gh token";

    /// <summary>Nothing to read with.</summary>
    public const string None = "none";

    /// <summary>Where the dedicated one is kept, in the credential store's terms.</summary>
    public const string Reference = "loadout/trigger-github";

    /// <summary>Nothing to read with.</summary>
    public static readonly TriggerCredential Missing = new(None, null, null);

    /// <summary>A credential of a kind, fingerprinted.</summary>
    public static TriggerCredential Of(string kind, string token) => new(kind, token, Fingerprinted(token));

    /// <summary>
    /// Enough of a hash to tell one token from the next, and far too little to
    /// say anything about either.
    /// </summary>
    public static string Fingerprinted(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..8].ToLowerInvariant();

    /// <summary>
    /// The dedicated token where one is set, otherwise the one <c>gh</c> is
    /// signed in with, otherwise nothing.
    /// </summary>
    /// <remarks>
    /// Polling only reads, so a token kept for it alone means no write scope
    /// sits in a process that runs unattended, and your own <c>gh</c> work and
    /// the poller cannot use up each other's quota. The fallback to <c>gh</c>
    /// is for somebody who has never set one; a dedicated token that has lapsed
    /// stops polling instead, which the source decides, not this.
    /// </remarks>
    public static async Task<TriggerCredential> ResolveAsync(
        ISecretProvider secrets,
        IExecutableResolver resolver,
        IProcessLauncher processes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(processes);

        if ((await secrets.GetAsync(Reference, ct).ConfigureAwait(false)).Value is { Length: > 0 } dedicated)
        {
            return Of(Dedicated, dedicated.Trim());
        }

        if (resolver.Resolve("gh") is not { } gh)
        {
            return Missing;
        }

        var ran = await processes.RunAsync(
            new ProcessRequest(gh, ["auth", "token"], Environment.CurrentDirectory),
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);

        return ran.Succeeded && ran.Value!.Succeeded && ran.Value.StandardOutput.Trim() is { Length: > 0 } token
            ? Of(GhToken, token)
            : Missing;
    }
}

/// <summary>The real reads, over HTTPS.</summary>
public sealed class GitHubApi : IGitHubReads
{
    private static readonly Uri Root = new("https://api.github.com/");

    private readonly HttpClient _client;

    public GitHubApi(HttpClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<GitHubReply> GetAsync(TriggerCredential credential, string path, string? etag, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Root, path));

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("loadout-triggers", "1"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

        if (credential.Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (etag is { Length: > 0 })
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));

            using var response = await _client.SendAsync(request, timeout.Token).ConfigureAwait(false);

            var body = response.StatusCode == HttpStatusCode.OK
                ? await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false)
                : null;

            return new GitHubReply(
                (int)response.StatusCode,
                body,
                response.Headers.ETag?.ToString(),
                Number(response, "X-RateLimit-Remaining"),
                Number(response, "X-RateLimit-Limit"),
                Header(response, "X-OAuth-Scopes"),
                Expiry(Header(response, "GitHub-Authentication-Token-Expiration")));
        }
        catch (HttpRequestException ex)
        {
            return new GitHubReply(0, Problem: ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new GitHubReply(0, Problem: "GitHub did not answer within 30 seconds");
        }
    }

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static int? Number(HttpResponseMessage response, string name) =>
        int.TryParse(Header(response, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    /// <summary>GitHub's expiry header, which reads like <c>2026-11-01 12:00:00 UTC</c>.</summary>
    internal static DateTimeOffset? Expiry(string? said)
    {
        if (said is not { Length: > 0 })
        {
            return null;
        }

        var text = said.Replace(" UTC", "Z", StringComparison.Ordinal).Trim();

        return DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var at)
            ? at
            : null;
    }
}
