using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The routes Studio added beside the classic dashboard's: whether a browser
/// is signed in, signing every browser out, its settings, the launch sheet,
/// and what a watch-only dashboard and a page from elsewhere are refused.
/// Against the real listener.
/// </summary>
public sealed class DashboardStudioRoutesTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "loadout-signin-" + Guid.NewGuid().ToString("N"));
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;
    private readonly List<SettingsChange> _settled = [];

    public Task InitializeAsync()
    {
        _server = new DashboardServer(new NoRuns())
        {
            SignIn = new SignIn(_keys),
            Settle = (change, _) =>
            {
                _settled.Add(change);

                return Task.FromResult(OperationResult.Ok());
            },
            Launch = new ScriptedLaunch(),
        };

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();

        if (File.Exists(_keys))
        {
            File.Delete(_keys);
        }
    }

    [Fact]
    public async Task Whether_a_browser_is_signed_in_is_answered_without_refusing_it()
    {
        var stranger = await ReadAsync(new HttpRequestMessage(HttpMethod.Get, _root + "/api/v1/signed-in"));
        var signed = await ReadAsync(Signed(HttpMethod.Get, "/api/v1/signed-in"));

        stranger.Status.Should().Be(HttpStatusCode.OK, "a browser counts a refusal as an error in its console");
        stranger.Json.GetProperty("signedIn").GetBoolean().Should().BeFalse();
        signed.Json.GetProperty("signedIn").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Signing_out_stops_every_cookie_working_and_clears_this_one()
    {
        var cookie = _server.SignIn!.IssueCookie();
        _server.SignIn.Check(cookie).Should().Be(CookieStanding.Good);

        using var request = Signed(HttpMethod.Post, "/api/v1/sign-out");
        using var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        _server.SignIn.Check(cookie).Should().Be(CookieStanding.Refused);
        response.Headers.GetValues("Set-Cookie").Single().Should().StartWith(SignIn.CookieName + "=;").And.Contain("Max-Age=0");
    }

    [Fact]
    public async Task A_setting_from_studio_runs_config_set_for_its_own_keys_only()
    {
        var studio = await ReadAsync(Signed(HttpMethod.Put, "/api/v1/preferences", "{\"key\":\"studio-halo\",\"value\":\"gyroscope\"}"));
        var other = await ReadAsync(Signed(HttpMethod.Put, "/api/v1/preferences", "{\"key\":\"default-agent\",\"value\":\"codex\"}"));

        studio.Status.Should().Be(HttpStatusCode.OK);
        other.Status.Should().Be(HttpStatusCode.BadRequest, "the rest of the machine's settings have their own screens");
        _settled.Should().ContainSingle().Which.Should().Be(new SettingsChange("studio-halo", "gyroscope"));
    }

    [Theory]
    [InlineData("PUT", "/api/v1/preferences", "{\"key\":\"studio-halo\",\"value\":\"orbit\"}")]
    [InlineData("POST", "/api/v1/sign-out", "")]
    public async Task Watching_only_refuses_a_change_and_says_what_to_type_instead(string method, string path, string body)
    {
        _server.WatchOnly = true;

        var refused = await ReadAsync(Signed(new HttpMethod(method), path, body));

        refused.Status.Should().Be(HttpStatusCode.Forbidden);
        refused.Text.Should().Contain("only watching").And.Contain("in a terminal");
        _settled.Should().BeEmpty();
        _server.SignIn!.Check(_server.SignIn.IssueCookie()).Should().Be(CookieStanding.Good, "nothing was rotated");
    }

    [Fact]
    public async Task About_says_whether_it_is_only_watching()
    {
        _server.WatchOnly = true;

        var about = await ReadAsync(Signed(HttpMethod.Get, "/api/v1/about"));

        about.Json.GetProperty("watchOnly").GetBoolean().Should().BeTrue();
        about.Json.GetProperty("startsSessions").GetBoolean().Should().BeFalse();
        about.Json.GetProperty("attachPassphrase").GetBoolean().Should().BeFalse();
        about.Json.GetProperty("triggers").GetBoolean().Should().BeFalse("triggers are off unless the daemon said so");
    }

    [Fact]
    public async Task About_says_when_the_daemon_runs_triggers_so_the_approvals_inbox_is_shown()
    {
        _server.Triggers = true;

        var about = await ReadAsync(Signed(HttpMethod.Get, "/api/v1/about"));

        about.Json.GetProperty("triggers").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("PUT", "/api/v1/preferences", "{\"key\":\"studio-halo\",\"value\":\"orbit\"}")]
    [InlineData("POST", "/api/v1/sign-out", "")]
    [InlineData("POST", "/api/v1/launch/preview", "{\"project\":\"storefront\"}")]
    public async Task A_change_from_another_page_is_refused_even_with_the_token(string method, string path, string body)
    {
        using var request = Signed(new HttpMethod(method), path, body);
        request.Headers.Add("Origin", "http://elsewhere.example");

        var refused = await ReadAsync(request);

        refused.Status.Should().Be(HttpStatusCode.Forbidden);
        refused.Text.Should().Contain("another page");
        _settled.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_from_studios_own_page_is_taken()
    {
        using var request = Signed(HttpMethod.Put, "/api/v1/preferences", "{\"key\":\"studio-motion\",\"value\":\"reduced\"}");
        request.Headers.Add("Origin", _root);

        (await ReadAsync(request)).Status.Should().Be(HttpStatusCode.OK);
        _settled.Should().ContainSingle();
    }

    [Fact]
    public async Task The_launch_sheet_is_read_for_a_project_and_previewed_from_what_was_picked()
    {
        var sheet = await ReadAsync(Signed(HttpMethod.Get, "/api/v1/launch?project=storefront&agent=shell"));
        var preview = await ReadAsync(Signed(HttpMethod.Post, "/api/v1/launch/preview",
            "{\"project\":\"storefront\",\"taskId\":\"upload-retries\",\"agent\":\"shell\",\"offline\":true}"));

        sheet.Json.GetProperty("asked").GetString().Should().Be("storefront/shell");
        preview.Json.GetProperty("command").GetString().Should().Be("loadout launch storefront --task-id=upload-retries --agent=shell --offline");
    }

    [Fact]
    public async Task Without_signing_in_nothing_of_studios_is_read()
    {
        foreach (var path in new[] { "/api/v1/about", "/api/v1/preferences", "/api/v1/launch?project=storefront", "/api/v1/events" })
        {
            var refused = await ReadAsync(new HttpRequestMessage(HttpMethod.Get, _root + path));

            refused.Status.Should().Be(HttpStatusCode.Forbidden, path);
        }
    }

    private HttpRequestMessage Signed(HttpMethod method, string path, string body = "")
    {
        var request = new HttpRequestMessage(method, _root + path);
        request.Headers.Add("X-Loadout-Token", _server.Token);

        if (method != HttpMethod.Get)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    private async Task<(HttpStatusCode Status, string Text, JsonElement Json)> ReadAsync(HttpRequestMessage request)
    {
        using (request)
        using (var response = await _client.SendAsync(request))
        {
            var text = await response.Content.ReadAsStringAsync();
            var json = text.StartsWith('{') ? JsonDocument.Parse(text).RootElement.Clone() : default;

            return (response.StatusCode, text, json);
        }
    }

    /// <summary>The sheet and preview, as plain as the route needs: it only carries them.</summary>
    private sealed class ScriptedLaunch : IStudioLaunch
    {
        public Task<object> SheetAsync(string? project, string? agent, CancellationToken ct) =>
            Task.FromResult<object>(new { asked = $"{project}/{agent}" });

        public Task<object> PreviewAsync(HostedLaunch launch, CancellationToken ct) =>
            Task.FromResult<object>(new { command = HostedSessions.CommandLine(launch).Value });
    }

    private sealed class NoRuns : IRunJournal
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
