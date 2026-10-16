using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Studio's page and its files, served from the bundle built into the
/// assembly, against the real listener.
/// </summary>
/// <remarks>
/// Skipped where the build had no Node and so no bundle. CI builds with
/// StudioBuild=required, where a missing bundle fails the build instead.
/// </remarks>
public sealed partial class DashboardStudioTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stopping = new();
    private DashboardServer _server = null!;
    private HttpClient _client = null!;
    private string _root = string.Empty;

    public Task InitializeAsync()
    {
        _server = new DashboardServer(new EmptyJournal());

        var started = _server.Start(0);
        started.Succeeded.Should().BeTrue(started.Error);

        _root = $"http://127.0.0.1:{_server.Port}";
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };

        _ = _server.ListenAsync(_stopping.Token);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();
        _client.Dispose();
        _server.Dispose();
        _stopping.Dispose();
    }

    [StudioBuiltFact]
    public async Task Studio_without_its_slash_is_sent_to_it()
    {
        using var response = await _client.GetAsync(_root + "/studio");

        response.StatusCode.Should().Be(HttpStatusCode.MovedPermanently);
        response.Headers.Location!.OriginalString.Should().Be("/studio/");
    }

    [StudioBuiltFact]
    public async Task The_page_is_served_without_the_token_and_may_run_only_its_own_scripts()
    {
        using var response = await _client.GetAsync(_root + "/studio/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Headers.CacheControl!.NoCache.Should().BeTrue();

        var policy = response.Headers.GetValues("Content-Security-Policy").Single();
        policy.Should().Contain("script-src 'self';").And.Contain("default-src 'none'").And.NotContain("unsafe-eval");
        policy.Should().NotContain("script-src 'self' 'unsafe-inline'");
    }

    [StudioBuiltFact]
    public async Task The_files_the_page_names_are_served_and_kept_for_good()
    {
        var page = await _client.GetStringAsync(_root + "/studio/");
        var script = ScriptPath().Match(page);
        script.Success.Should().BeTrue("the page names its script: " + page);

        using var response = await _client.GetAsync(_root + script.Groups[1].Value);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/javascript");
        response.Headers.CacheControl!.ToString().Should().Contain("immutable");
    }

    [StudioBuiltFact]
    public async Task The_manifest_is_served_as_one()
    {
        using var response = await _client.GetAsync(_root + "/studio/manifest.webmanifest");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/manifest+json");
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"start_url\": \"/studio/\"");
    }

    [StudioBuiltFact]
    public async Task A_file_that_is_not_in_the_bundle_is_not_found()
    {
        using var missing = await _client.GetAsync(_root + "/studio/assets/nothing.js");
        using var climbing = await _client.GetAsync(_root + "/studio/..%2F..%2Fdashboard.html");

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        // The listener resolves the climb before this server sees it, and
        // what it climbs to is behind the token. Either way nothing is served.
        climbing.StatusCode.Should().NotBe(HttpStatusCode.OK);
    }

    [GeneratedRegex("src=\"(/studio/assets/[^\"]+[.]js)\"")]
    private static partial Regex ScriptPath();

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

/// <summary>A test that needs Studio's bundle, skipped where the build left it out.</summary>
public sealed class StudioBuiltFactAttribute : FactAttribute
{
    public StudioBuiltFactAttribute()
    {
        if (!StudioAssets.Built)
        {
            Skip = "Studio was not built into this build (no Node).";
        }
    }
}
