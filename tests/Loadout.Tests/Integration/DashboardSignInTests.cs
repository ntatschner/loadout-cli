using System.Net;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Signing a browser in to the dashboard with a cookie, against the real
/// listener: what lets a request in, and what a page somewhere else on the
/// same machine cannot do with it.
/// </summary>
public sealed class DashboardSignInTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _home = Path.Combine(Path.GetTempPath(), "loadout-dash-signin-" + Guid.NewGuid().ToString("N"));
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private SignIn _signIn = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _signIn = new SignIn(Path.Combine(_home, "dashboard", "sign-in.key"));
        _server = new DashboardServer(new EmptyJournal()) { SignIn = _signIn };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";

        // Cookies sent by hand, and redirects not followed, so each test says
        // exactly what the browser would have sent.
        _client = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();

        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public async Task A_sign_in_link_gives_the_browser_a_cookie_and_sends_it_to_the_page()
    {
        var link = DashboardServer.SignInLink(_server.Address, _signIn);
        link.Should().StartWith(_root + "/signin?code=");

        using var response = await _client.GetAsync(link);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("/");

        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        cookie.Should().StartWith(SignIn.CookieName + "=")
            .And.Contain("HttpOnly")
            .And.Contain("SameSite=Strict")
            .And.Contain("Max-Age=7776000");
    }

    [Fact]
    public async Task A_link_for_studio_lands_on_studio()
    {
        var link = DashboardServer.SignInLink(_server.Address, _signIn, studio: true);
        link.Should().EndWith("&to=studio");

        using var response = await _client.GetAsync(link);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("/studio/");
    }

    [Theory]
    [InlineData("https://elsewhere.example/")]
    [InlineData("//elsewhere.example/")]
    [InlineData("classic")]
    public async Task A_link_naming_anywhere_else_lands_on_the_classic_page(string to)
    {
        var link = DashboardServer.SignInLink(_server.Address, _signIn) + "&to=" + Uri.EscapeDataString(to);

        using var response = await _client.GetAsync(link);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("/");
    }

    [Fact]
    public async Task A_sign_in_link_works_once()
    {
        var link = DashboardServer.SignInLink(_server.Address, _signIn);

        using (await _client.GetAsync(link))
        {
        }

        using var again = await _client.GetAsync(link);

        again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        again.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task The_cookie_lets_a_request_in_without_the_token()
    {
        using var refused = await SendAsync("/api/runs", cookie: null);
        using var admitted = await SendAsync("/api/runs", cookie: _signIn.IssueCookie());

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_page_sends_an_empty_token_when_it_was_opened_without_one_and_the_cookie_still_counts()
    {
        using var admitted = await SendAsync("/api/runs?token=", cookie: _signIn.IssueCookie());

        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_request_from_another_page_on_this_machine_is_refused_even_with_the_cookie()
    {
        // Cookies go to every port on a host, so a page served by something
        // else on loopback would carry this one along. It says where it came
        // from, and that is what refuses it.
        using var elsewhere = await SendAsync("/api/runs", cookie: _signIn.IssueCookie(), origin: "http://127.0.0.1:3000");
        using var ours = await SendAsync("/api/runs", cookie: _signIn.IssueCookie(), origin: _root);

        elsewhere.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        ours.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Signing_out_ends_the_cookie()
    {
        var cookie = _signIn.IssueCookie();

        new SignIn(Path.Combine(_home, "dashboard", "sign-in.key")).Rotate();

        using var refused = await SendAsync("/api/runs", cookie: cookie);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_token_still_works_for_links_already_printed()
    {
        using var admitted = await SendAsync("/api/runs?token=" + _server.Token, cookie: null);

        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_cookie_a_day_old_comes_back_fresh()
    {
        var old = new SignIn(
            Path.Combine(_home, "dashboard", "sign-in.key"),
            clock: new Shifted(TimeSpan.FromDays(-2))).IssueCookie();

        using var response = await SendAsync("/api/runs", cookie: old);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Single().Should().StartWith(SignIn.CookieName + "=");
    }

    [Fact]
    public void An_address_off_this_machine_gets_no_sign_in_link()
    {
        DashboardServer.SignInLink("http://192.168.1.20:47611/?token=abc", _signIn).Should().BeNull();
        DashboardServer.SignInLink("http://localhost:47611/?token=abc", _signIn).Should().StartWith("http://localhost:47611/signin?code=");
    }

    private Task<HttpResponseMessage> SendAsync(string path, string? cookie, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _root + path);

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{SignIn.CookieName}={cookie}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return _client.SendAsync(request);
    }

    private sealed class Shifted(TimeSpan by) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + by;
    }

    private sealed class EmptyJournal : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("no such run");

        public OperationResult<RunSummary> Summarise(string runId) => OperationResult<RunSummary>.Fail("no such run");

        public string DirectoryOf(string runId) => Path.Combine(Path.GetTempPath(), runId);

        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            OperationResult<RunForgotten>.Fail("no such run");
    }
}
