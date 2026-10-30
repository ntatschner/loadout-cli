using System.Net;
using FluentAssertions;
using Loadout.Core.About;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The About page's release list: when GitHub is asked, how, and what is shown
/// when it cannot be.
/// </summary>
/// <remarks>
/// Every test counts requests through a fake handler, because the promise that
/// matters most here is the one about not asking: switched off or pointed
/// elsewhere, nothing goes to GitHub at all.
/// </remarks>
public sealed class ReleaseTimelineTests : IDisposable
{
    private const string Releases = """
        [
          { "tag_name": "v0.110.0", "name": "0.110.0", "published_at": "2026-10-29T10:00:00Z", "body": "New.", "html_url": "https://github.com/ntatschner/loadout-cli/releases/tag/v0.110.0", "draft": false },
          { "tag_name": "v0.109.5", "name": "0.109.5", "published_at": "2026-10-28T10:00:00Z", "body": "Fixed.", "html_url": "https://github.com/ntatschner/loadout-cli/releases/tag/v0.109.5", "draft": false }
        ]
        """;

    private readonly AboutHome _home = new();
    private readonly Handler _handler = new();
    private readonly MovingTime _time = new();

    public void Dispose() => _home.Dispose();

    private ReleaseTimeline Timeline(string? source = null) =>
        new(new SourceConfiguration(source), _home.Paths, new HttpClient(_handler), _time, "0.109.5");

    [Fact]
    public async Task Off_makes_no_request_and_says_off()
    {
        var timeline = await Timeline("off").ReadAsync(refresh: true, offline: false);

        _handler.Requests.Should().BeEmpty();
        timeline.Empty.Should().Be(TimelineEmpty.Off);
    }

    [Fact]
    public async Task Non_github_source_makes_no_request_to_github()
    {
        var timeline = await Timeline("https://mirror.example/loadout/feed.json").ReadAsync(refresh: true, offline: false);

        _handler.Requests.Should().BeEmpty();
        timeline.Empty.Should().Be(TimelineEmpty.NotGitHub);
    }

    [Fact]
    public async Task Newer_release_is_marked_not_installed()
    {
        _handler.Answer = _ => Ok(Releases, "\"one\"");

        var timeline = await Timeline().ReadAsync(refresh: true, offline: false);

        timeline.Releases.Select(r => (r.Version, r.Installed)).Should().Equal(("0.110.0", false), ("0.109.5", true));
    }

    [Fact]
    public async Task Not_modified_serves_the_cache()
    {
        _handler.Answer = _ => Ok(Releases, "\"one\"");
        await Timeline().ReadAsync(refresh: true, offline: false);

        _handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.NotModified);
        _time.Now += TimeSpan.FromHours(25);

        var timeline = await Timeline().ReadAsync(refresh: false, offline: false);

        _handler.Requests.Should().HaveCount(2);
        _handler.Requests[1].IfNoneMatch.Should().Be("\"one\"");
        timeline.Releases.Should().HaveCount(2);
        timeline.Stale.Should().BeFalse();
        timeline.FetchedAt.Should().Be(_time.Now);
    }

    [Fact]
    public async Task Rate_limit_with_cache_serves_stale()
    {
        _handler.Answer = _ => Ok(Releases, "\"one\"");
        await Timeline().ReadAsync(refresh: true, offline: false);

        _handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);

        var timeline = await Timeline().ReadAsync(refresh: true, offline: false);

        timeline.Stale.Should().BeTrue();
        timeline.Releases.Should().HaveCount(2);
        timeline.Empty.Should().BeNull();
    }

    [Fact]
    public async Task Rate_limit_without_cache_says_why()
    {
        _handler.Answer = _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        var timeline = await Timeline().ReadAsync(refresh: true, offline: false);

        timeline.Releases.Should().BeEmpty();
        timeline.Empty.Should().Be(TimelineEmpty.RateLimitedNoCache);
    }

    [Fact]
    public async Task Sends_a_user_agent()
    {
        _handler.Answer = _ => Ok(Releases, "\"one\"");

        await Timeline().ReadAsync(refresh: true, offline: false);

        // GitHub refuses a request without one, and the shared client has none.
        _handler.Requests.Should().ContainSingle().Which.UserAgent.Should().Be("loadout/0.109.5");
    }

    private static HttpResponseMessage Ok(string body, string etag)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        response.Headers.ETag = System.Net.Http.Headers.EntityTagHeaderValue.Parse(etag);
        return response;
    }

    /// <summary>What a request carried, read before it is disposed.</summary>
    private sealed record Seen(string Uri, string UserAgent, string IfNoneMatch);

    /// <summary>Answers however the test says and remembers every request.</summary>
    private sealed class Handler : HttpMessageHandler
    {
        public List<Seen> Requests { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } =
            _ => throw new HttpRequestException("no network in tests");

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new Seen(
                request.RequestUri!.ToString(),
                request.Headers.UserAgent.ToString(),
                request.Headers.IfNoneMatch.ToString()));

            return Task.FromResult(Answer(request));
        }
    }

    private sealed class MovingTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 30, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
