using System.Net;
using System.Text;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// A paired phone at the link address, against the real listener: it can sign
/// in there, read runs and answer them, and reach nothing else.
/// </summary>
/// <remarks>
/// The phone is played by requests naming the link address as their Host,
/// which is all the server has to go on: through a proxy on this machine they
/// arrive from loopback like everything else.
/// </remarks>
public sealed class DashboardLinkAddressTests : IAsyncLifetime
{
    private const string Away = "desk.example.ts.net";

    private readonly CancellationTokenSource _stopping = new();
    private readonly string _home = Path.Combine(Path.GetTempPath(), "loadout-dash-link-" + Guid.NewGuid().ToString("N"));
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private SignIn _signIn = null!;
    private string _root = string.Empty;
    private readonly List<RunAction> _acted = [];

    public Task InitializeAsync()
    {
        _signIn = new SignIn(Path.Combine(_home, "dashboard", "sign-in.key"));
        _server = new DashboardServer(new EmptyJournal())
        {
            SignIn = _signIn,
            LinkAddress = $"https://{Away}",
            Act = (action, _) =>
            {
                _acted.Add(action);

                return Task.FromResult(OperationResult.Ok());
            },
        };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";

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
    public async Task A_pairing_code_signs_the_phone_in_at_the_link_address_and_lands_on_the_runs()
    {
        var link = NoticeLink.Pairing(_server.LinkAddress, _signIn.MintCode())!;
        link.Should().StartWith($"https://{Away}/signin?code=");

        using var response = await SendAsync(HttpMethod.Get, new Uri(link).PathAndQuery, cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.SeeOther);
        response.Headers.Location!.OriginalString.Should().Be("classic#list");

        // Lax so a tap from a chat app carries it; Secure because the link
        // address is https.
        response.Headers.GetValues("Set-Cookie").Single()
            .Should().StartWith(SignIn.CookieName + "=")
            .And.Contain("HttpOnly")
            .And.Contain("SameSite=Lax")
            .And.Contain("Secure");
    }

    [Fact]
    public async Task A_pairing_code_works_once()
    {
        var path = new Uri(NoticeLink.Pairing(_server.LinkAddress, _signIn.MintCode())!).PathAndQuery;

        using (await SendAsync(HttpMethod.Get, path, cookie: null))
        {
        }

        using var again = await SendAsync(HttpMethod.Get, path, cookie: null);

        again.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        again.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    [Fact]
    public async Task The_cookie_reads_runs_at_the_link_address()
    {
        using var refused = await SendAsync(HttpMethod.Get, "/api/runs", cookie: null);
        using var admitted = await SendAsync(HttpMethod.Get, "/api/runs", cookie: _signIn.IssueCookie());
        using var page = await SendAsync(HttpMethod.Get, "/classic", cookie: _signIn.IssueCookie());

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admitted.StatusCode.Should().Be(HttpStatusCode.OK);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_cookie_answers_a_gate_at_the_link_address()
    {
        using var answered = await SendAsync(
            HttpMethod.Post, "/api/runs/run-1/gate", _signIn.IssueCookie(), origin: $"https://{Away}",
            body: """{"gate":"q-1","answer":"yes"}""");

        answered.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _acted.Should().ContainSingle().Which.Verb.Should().Be("gate");
    }

    [Fact]
    public async Task An_answer_from_another_site_is_refused()
    {
        using var refused = await SendAsync(
            HttpMethod.Post, "/api/runs/run-1/gate", _signIn.IssueCookie(), origin: "https://elsewhere.example",
            body: """{"gate":"q-1","answer":"yes"}""");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _acted.Should().BeEmpty();
    }

    [Theory]
    [InlineData("POST", "/api/runs/run-1/stop")]
    [InlineData("POST", "/api/runs/run-1/say")]
    [InlineData("POST", "/api/runs/run-1/message")]
    [InlineData("POST", "/api/start")]
    [InlineData("POST", "/api/settings")]
    [InlineData("GET", "/api/settings")]
    [InlineData("GET", "/api/v1/sessions")]
    [InlineData("GET", "/api/v1/signed-in")]
    [InlineData("POST", "/api/attach")]
    [InlineData("GET", "/api/speech")]
    [InlineData("GET", "/studio/")]
    public async Task Nothing_but_runs_is_reachable_at_the_link_address(string method, string path)
    {
        using var refused = await SendAsync(new HttpMethod(method), path, _signIn.IssueCookie(), origin: $"https://{Away}", body: "{}");

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("At the link address");
        _acted.Should().BeEmpty();
    }

    [Fact]
    public async Task The_token_reaches_no_further_than_the_cookie_at_the_link_address()
    {
        using var refused = await SendAsync(HttpMethod.Get, $"/api/v1/sessions?token={_server.Token}", cookie: null);

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_address_itself_opens_the_runs_at_the_link_address()
    {
        using var response = await SendAsync(HttpMethod.Get, "/", cookie: null);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.OriginalString.Should().Be("classic#list");
    }

    [Fact]
    public async Task This_machine_is_held_to_nothing_new()
    {
        // The same request at this machine's own address, where the link
        // address's limits do not apply and the old cookie rules do.
        var request = new HttpRequestMessage(HttpMethod.Get, _root + "/api/settings");
        request.Headers.Add("Cookie", $"{SignIn.CookieName}={_signIn.IssueCookie()}");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_host_that_is_neither_gets_no_cookie_at_all()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, _root + "/api/runs");
        request.Headers.Host = "elsewhere.example";
        request.Headers.Add("Cookie", $"{SignIn.CookieName}={_signIn.IssueCookie()}");

        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/runs", true)]
    [InlineData("GET", "/api/runs/run-1", true)]
    [InlineData("GET", "/api/runs/run-1/events", true)]
    [InlineData("POST", "/api/runs/run-1/gate", true)]
    [InlineData("POST", "/api/runs/run-1/gates", true)]
    [InlineData("POST", "/api/runs/run-1/close", false)]
    [InlineData("GET", "/api/runs/run-1/close", false)]
    [InlineData("POST", "/api/runs", false)]
    [InlineData("DELETE", "/api/runs/run-1", false)]
    [InlineData("GET", "/api/ideas", false)]
    public void What_the_link_address_lets_through(string method, string path, bool let)
    {
        DashboardServer.Remotely(method, path).Should().Be(let);
    }

    /// <summary>A request as the phone would send it: naming the link address as its host.</summary>
    private Task<HttpResponseMessage> SendAsync(
        HttpMethod method, string path, string? cookie, string? origin = null, string? body = null)
    {
        var request = new HttpRequestMessage(method, _root + path);
        request.Headers.Host = Away;

        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{SignIn.CookieName}={cookie}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        if (body is not null && method != HttpMethod.Get)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return _client.SendAsync(request);
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
