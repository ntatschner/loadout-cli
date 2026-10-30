using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Configuration;
using Loadout.Core.Diagnostics;
using Loadout.Core.Updates;
using Loadout.Models.Updates;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.About;

/// <summary>The releases GitHub lists, kept so a page can show them at once.</summary>
public interface IReleaseTimeline
{
    /// <summary>
    /// The releases, newest first, or why there are none. Never throws for
    /// anything the network or the cache does.
    /// </summary>
    /// <param name="refresh">Ask GitHub even when the kept list is fresh.</param>
    /// <param name="offline">Ask nobody, because the command was told not to.</param>
    /// <param name="ct">Stops the work.</param>
    Task<AboutTimeline> ReadAsync(bool refresh, bool offline, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// GitHub's release list for the repository <c>updates-source</c> names, and
/// nothing else: switched off, offline or a source that is not GitHub each make
/// no request at all and say so, which is what lets the remark on
/// <see cref="ReleaseSource"/> stay true.
/// </para>
/// <para>
/// The shared <see cref="HttpClient"/> has no User-Agent and GitHub refuses a
/// request without one, so it is set on each request here rather than on the
/// client every other caller shares. The ETag is sent back, so asking again
/// when nothing changed costs GitHub's rate limit nothing.
/// </para>
/// </remarks>
internal sealed class ReleaseTimeline : IReleaseTimeline
{
    /// <summary>How long a kept list is shown without asking again, as for the update notice.</summary>
    internal static readonly TimeSpan Freshness = UpdateNotice.Freshness;

    private static readonly JsonSerializerOptions CacheJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly string _currentVersion;

    public ReleaseTimeline(
        IConfigurationService configuration,
        IPlatformPaths paths,
        HttpClient http,
        TimeProvider time,
        string? currentVersion = null)
    {
        _configuration = configuration;
        _paths = paths;
        _http = http;
        _time = time;
        _currentVersion = currentVersion
            ?? typeof(ReleaseTimeline).Assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
    }

    /// <summary>Where the last list is kept, beside the update notice's answer.</summary>
    internal string Path => System.IO.Path.Combine(_paths.Paths.Cache, "releases.json");

    /// <inheritdoc />
    public async Task<AboutTimeline> ReadAsync(bool refresh, bool offline, CancellationToken ct = default)
    {
        var config = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);
        var configured = config.Succeeded ? config.Value!.Updates.Source : null;

        if (ReleaseSource.IsDisabled(configured))
        {
            return Nothing(TimelineEmpty.Off);
        }

        if (offline)
        {
            return Nothing(TimelineEmpty.Offline);
        }

        if (ReleaseSource.GitHubRepository(configured) is not { } repository)
        {
            return Nothing(TimelineEmpty.NotGitHub);
        }

        var cached = Read();
        var now = _time.GetUtcNow();

        if (!refresh && cached is not null && now - cached.FetchedAt < Freshness)
        {
            return From(cached, stale: false);
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{repository.Owner}/{repository.Repo}/releases?per_page=30");

            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("loadout", _currentVersion));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            if (cached?.Etag is { Length: > 0 } etag
                && EntityTagHeaderValue.TryParse(etag, out var tag))
            {
                request.Headers.IfNoneMatch.Add(tag);
            }

            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                var touched = cached with { FetchedAt = now };
                Write(touched);
                return From(touched, stale: false);
            }

            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            {
                return cached is not null ? From(cached, stale: true) : Nothing(TimelineEmpty.RateLimitedNoCache);
            }

            if (!response.IsSuccessStatusCode)
            {
                return cached is not null ? From(cached, stale: true) : Nothing(TimelineEmpty.Unreachable);
            }

            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var releases = Parse(body);

            var fresh = new Kept(now, response.Headers.ETag?.ToString(), releases);
            Write(fresh);

            return From(fresh, stale: false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested
            && ex is HttpRequestException or TaskCanceledException or JsonException or IOException)
        {
            return cached is not null ? From(cached, stale: true) : Nothing(TimelineEmpty.Unreachable);
        }
    }

    private static AboutTimeline Nothing(TimelineEmpty reason) => new([], reason, null, false);

    private AboutTimeline From(Kept kept, bool stale) => new(
        kept.Releases.Select(Release).ToList(),
        null,
        kept.FetchedAt,
        stale);

    private AboutRelease Release(KeptRelease release) => new(
        release.Version,
        release.Name,
        release.Published,
        release.Notes,
        release.Url,
        Installed: !UpdateService.IsNewer(release.Version, _currentVersion),
        Kind: Version.TryParse(release.Version, out var parsed) && parsed.Build > 0 ? "fix" : "feature");

    private static List<KeptRelease> Parse(string body)
    {
        var listed = JsonSerializer.Deserialize<List<GitHubRelease>>(body) ?? [];

        return listed
            .Where(r => !r.Draft && !string.IsNullOrWhiteSpace(r.TagName))
            .Select(r => new KeptRelease(
                r.TagName!.TrimStart('v', 'V'),
                r.Name ?? r.TagName!,
                r.PublishedAt,
                r.Body ?? string.Empty,
                r.HtmlUrl ?? string.Empty))
            .ToList();
    }

    private Kept? Read()
    {
        try
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            using var trace = ReadLedger.Begin(Path);
            return JsonSerializer.Deserialize<Kept>(File.ReadAllText(Path), CacheJson);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // An unreadable list is a missing one; the next answer replaces it.
            return null;
        }
    }

    private void Write(Kept kept)
    {
        try
        {
            Directory.CreateDirectory(_paths.Paths.Cache);
            File.WriteAllText(Path, JsonSerializer.Serialize(kept, CacheJson));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not keeping the list costs one more request next time. Nothing to report.
        }
    }

    /// <summary>What <c>releases.json</c> holds.</summary>
    internal sealed record Kept(DateTimeOffset FetchedAt, string? Etag, List<KeptRelease> Releases);

    /// <summary>One release as kept, before it is compared with what is running.</summary>
    internal sealed record KeptRelease(string Version, string Name, DateTimeOffset? Published, string Notes, string Url);

    /// <summary>The part of GitHub's release object read here.</summary>
    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }
    }
}
