using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Platform;
using Loadout.Platform.Abstractions;
using Xunit;

namespace Loadout.Tests.Platform;

/// <summary>
/// A real shell in a real pseudo-terminal, carried over a real WebSocket on
/// the same listener the daemon uses, so the managed HttpListener on Linux and
/// macOS is tried as well as HTTP.SYS on Windows.
/// </summary>
/// <remarks>
/// The command typed is one whose answer does not appear in what was typed,
/// because the terminal echoes the typing back and a marker that appeared in
/// both would pass on the echo alone.
/// </remarks>
public sealed class HostedTerminalTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Typing_over_the_socket_reaches_the_shell_and_its_answer_comes_back()
    {
        await using var host = await SocketHost.StartAsync();
        using var window = await host.ConnectAsync();

        await window.SendAsync(Typed(Command), WebSocketMessageType.Binary, true, CancellationToken.None);

        (await ReadUntilAsync(window, Answer)).Should().Contain(Answer);
    }

    [Fact]
    public async Task A_window_that_closes_leaves_the_shell_running_and_the_next_one_sees_what_it_said()
    {
        await using var host = await SocketHost.StartAsync();

        using (var first = await host.ConnectAsync())
        {
            await first.SendAsync(Typed(Command), WebSocketMessageType.Binary, true, CancellationToken.None);
            await ReadUntilAsync(first, Answer);
            await first.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
        }

        await WaitAsync(() => host.Session.Viewers == 0);
        host.Session.HasEnded.Should().BeFalse();

        using var second = await host.ConnectAsync();

        (await ReadUntilAsync(second, Answer)).Should().Contain(Answer);
    }

    private static string Command => OperatingSystem.IsWindows() ? "echo %OS%-hosted" : "echo $((40+2))-hosted";

    private static string Answer => OperatingSystem.IsWindows() ? "Windows_NT-hosted" : "42-hosted";

    private static ProcessRequest Shell => OperatingSystem.IsWindows()
        ? new ProcessRequest("cmd.exe", [])
        : new ProcessRequest("/bin/sh", []);

    private static byte[] Typed(string line) => Encoding.UTF8.GetBytes(line + (char)13);

    private static async Task<string> ReadUntilAsync(WebSocket socket, string wanted)
    {
        using var patience = new CancellationTokenSource(Patience);
        var seen = new StringBuilder();
        var buffer = new byte[16 * 1024];

        while (!seen.ToString().Contains(wanted, StringComparison.Ordinal))
        {
            var received = await socket.ReceiveAsync(buffer, patience.Token);

            if (received.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (received.MessageType == WebSocketMessageType.Binary)
            {
                seen.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
            }
        }

        return seen.ToString();
    }

    private static async Task WaitAsync(Func<bool> condition)
    {
        using var patience = new CancellationTokenSource(Patience);

        while (!condition())
        {
            await Task.Delay(50, patience.Token);
        }
    }

    /// <summary>One hosted shell behind a loopback listener, served the way the daemon serves a window.</summary>
    private sealed class SocketHost : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serving;

        private SocketHost(HttpListener listener, HostedSession session, int port)
        {
            _listener = listener;
            Session = session;
            Port = port;
            _serving = ServeAsync();
        }

        public HostedSession Session { get; }

        public int Port { get; }

        public static async Task<SocketHost> StartAsync()
        {
            var started = await HostedSession.StartAsync(PlatformServices.CreatePseudoTerminal(), Shell, "s1", "shell", 100, 30);
            started.Succeeded.Should().BeTrue(started.Error);

            var port = FreePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();

            return new SocketHost(listener, started.Value!, port);
        }

        public async Task<ClientWebSocket> ConnectAsync()
        {
            var socket = new ClientWebSocket();
            using var patience = new CancellationTokenSource(Patience);
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/ws"), patience.Token);

            return socket;
        }

        private async Task ServeAsync()
        {
            var windows = new List<Task>();

            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;

                try
                {
                    context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException)
                {
                    break;
                }

                windows.Add(Task.Run(async () =>
                {
                    var accepted = await context.AcceptWebSocketAsync(null);
                    await TerminalSocket.ServeAsync(accepted.WebSocket, Session, _stop.Token);
                }));
            }

            await Task.WhenAll(windows);
        }

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            return port;
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Close();

            try
            {
                await _serving;
            }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException or WebSocketException)
            {
                // The listener closing under a window is how this host stops.
            }

            await Session.DisposeAsync();
            _stop.Dispose();
        }
    }
}
