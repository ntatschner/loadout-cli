using System.Net;
using System.Text;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Saying a run wants somebody, somewhere else, once.
/// </summary>
/// <remarks>
/// <para>
/// The property worth being certain about is not that a message goes — it is
/// that the same message does not go every thirty seconds. A channel that
/// repeats itself is a channel somebody mutes, and a muted channel is worse
/// than none because it still looks like it is working.
/// </para>
/// <para>
/// Against a real listener on loopback rather than a stubbed client: what is
/// being tested includes whether a real HTTP POST comes out the other end with
/// the right body, which a fake handler would happily agree with either way.
/// </para>
/// </remarks>
public sealed class NoticesTests : IAsyncLifetime
{
    private readonly HttpListener _listener = new();
    private readonly HttpClient _client = new();
    private readonly List<string> _received = [];

    private string _address = string.Empty;
    private int _refuse;
    private CancellationTokenSource _stopping = new();
    private Task _serving = Task.CompletedTask;

    public Task InitializeAsync()
    {
        _address = $"http://127.0.0.1:{Listen()}/hook";

        _serving = Task.Run(async () =>
        {
            while (!_stopping.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException
                    or InvalidOperationException)
                {
                    return;
                }

                using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                {
                    lock (_received)
                    {
                        _received.Add(reader.ReadToEnd());
                    }
                }

                int code;

                lock (_received)
                {
                    code = _refuse > 0 ? 503 : 200;

                    if (_refuse > 0)
                    {
                        _refuse--;
                    }
                }

                context.Response.StatusCode = code;
                context.Response.Close();
            }
        });

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stopping.CancelAsync();

        try
        {
            _listener.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        await _serving;

        _listener.Close();
        _client.Dispose();
        _stopping.Dispose();
    }

    /// <summary>Starts the listener on a port nothing else is using.</summary>
    /// <remarks>
    /// Asking the operating system for a free port does not reserve it. Free()
    /// binds port zero, reads what it was given and lets go again, so between
    /// that and Start() anything else on the machine can take it. CI's macOS
    /// leg lost exactly that race - "Address already in use" - where the
    /// ephemeral range recycles fastest and the suite runs classes in parallel.
    ///
    /// The gap cannot be closed, because there is no way to hand an
    /// already-bound socket to HttpListener. It can only be tried again.
    /// </remarks>
    private int Listen()
    {
        for (var attempt = 1; ; attempt++)
        {
            var port = Free();

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                _listener.Start();

                return port;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                // Taken in between. Ask for another one.
            }
        }
    }

    private static int Free()
    {
        using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);

        probe.Start();

        var port = ((IPEndPoint)probe.LocalEndpoint).Port;

        probe.Stop();

        return port;
    }

    private IReadOnlyList<string> Received()
    {
        lock (_received)
        {
            return [.. _received];
        }
    }

    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    private static RunSummary Waiting(params string[] questions) =>
        new(
            "20260918-1200-aaaa", "d", "docs-crew", "goal", "supervised",
            Noon.AddMinutes(-5), null, null, 0.10m, 1, [], [], [],
            Gates: [.. questions.Select(q =>
                new PendingAsk(q, "implementer/1", "role.implementer", "Bash", q, Noon))]);

    private Notices Made() => new(new NoSecrets(), _client);

    private Task<int> SayAsync(params RunSummary[] runs) =>
        Made().SayAsync(runs, NoticeKind.Slack, null, _address, _ => "link", Noon);

    [Fact]
    public async Task A_run_that_wants_somebody_says_so()
    {
        var notices = Made();

        var sent = await notices.SayAsync(
            [Waiting("dotnet test")], NoticeKind.Slack, null, _address, _ => "link", Noon);

        sent.Should().Be(1);

        Received().Should().ContainSingle()
            .Which.Should().Contain("dotnet test").And.Contain("docs-crew");
    }

    [Fact]
    public async Task And_does_not_say_it_again_every_time_it_looks()
    {
        var notices = Made();
        var run = Waiting("dotnet test");

        await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon);
        await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon);
        await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon);

        // The whole point. Three looks, one message.
        Received().Should().ContainSingle();
    }

    [Fact]
    public async Task A_second_different_question_on_the_same_run_is_worth_saying()
    {
        var notices = Made();

        await notices.SayAsync(
            [Waiting("dotnet test")], NoticeKind.Slack, null, _address, _ => "link", Noon);

        await notices.SayAsync(
            [Waiting("dotnet test", "gh pr create")], NoticeKind.Slack, null, _address, _ => "link", Noon);

        // Both are kind "asking" on one run. Keyed on the kind alone, the
        // second would never have been mentioned.
        Received().Should().HaveCount(2);
        Received()[1].Should().Contain("gh pr create");
    }

    [Fact]
    public async Task Something_that_clears_and_comes_back_is_news_again()
    {
        var notices = Made();

        await notices.SayAsync(
            [Waiting("dotnet test")], NoticeKind.Slack, null, _address, _ => "link", Noon);

        // Answered: nothing is waiting.
        await notices.SayAsync(
            [Waiting()], NoticeKind.Slack, null, _address, _ => "link", Noon);

        // And asked again.
        await notices.SayAsync(
            [Waiting("dotnet test")], NoticeKind.Slack, null, _address, _ => "link", Noon);

        Received().Should().HaveCount(2, "it stopped being true, then started again");
    }

    [Fact]
    public async Task A_notice_that_did_not_land_is_said_again_next_time()
    {
        // The reason a failed send cannot simply be forgotten about: the
        // reason it was about is still true, so it is never new again, and the
        // run waits for somebody who was never told. One refused request - a
        // chat service restarting, a minute without network - lost the notice
        // for the whole run.
        _refuse = 1;

        var notices = Made();
        var run = Waiting("dotnet test");

        (await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon))
            .Should().Be(0, "the service refused it");

        (await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon))
            .Should().Be(1, "nobody has been told yet");

        // And now somebody has, so it stops - the property the whole class is
        // for still holds either side of a failure.
        (await notices.SayAsync([run], NoticeKind.Slack, null, _address, _ => "link", Noon))
            .Should().Be(0);
    }

    [Fact]
    public async Task A_run_wanting_nothing_says_nothing()
    {
        (await SayAsync(Waiting())).Should().Be(0);

        Received().Should().BeEmpty();
    }

    [Fact]
    public async Task An_address_that_answers_nothing_does_not_stop_the_daemon()
    {
        // A chat service being down is not a reason to stop watching runs, and
        // there is nowhere useful to report it from inside a loop nobody reads.
        var notices = Made();

        var act = async () => await notices.SayAsync(
            [Waiting("dotnet test")],
            NoticeKind.Slack,
            null,
            "http://127.0.0.1:1/nothing-is-here",
            _ => "link",
            Noon);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task No_address_at_all_sends_nothing_and_says_so()
    {
        (await Made().SayAsync(
            [Waiting("dotnet test")], NoticeKind.Slack, null, string.Empty, _ => "link", Noon))
            .Should().Be(0);
    }

    /// <summary>A credential store with nothing in it, which is all this needs.</summary>
    private sealed class NoSecrets : ISecretProvider
    {
        public string Name => "none";

        public Task<OperationResult> IsAvailableAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult<string>> GetAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<string>.Fail("nothing kept", ExitCode.GeneralFailure));

        public Task<OperationResult> SetAsync(string reference, string value, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> RemoveAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());

        public Task<OperationResult> TestAsync(string reference, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Ok());
    }
}
