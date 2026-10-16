using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>Carries a hosted session to a browser over a WebSocket.</summary>
/// <remarks>
/// <para>
/// Binary messages are terminal bytes in both directions: output to the
/// window, keystrokes to the child. Text messages are JSON control messages.
/// From the session: <c>{"type":"lease","holds":true}</c>,
/// <c>{"type":"exit","code":0}</c> and <c>{"type":"dropped","reason":"..."}</c>.
/// From the window: <c>{"type":"resize","columns":120,"rows":30}</c> and
/// <c>{"type":"takeover"}</c>.
/// </para>
/// <para>
/// Only one loop sends. A WebSocket allows one send at a time, so a refusal
/// to type is not answered on the socket; the window already knows from the
/// last lease message whether it may.
/// </para>
/// </remarks>
public static class TerminalSocket
{
    private const int ReceiveSize = 16 * 1024;

    /// <summary>The largest single message accepted from a window.</summary>
    private const int Largest = 1024 * 1024;

    /// <summary>Serves one window until it closes, the session ends, or the token is cancelled.</summary>
    /// <param name="socket">An open WebSocket.</param>
    /// <param name="session">The session to show.</param>
    /// <param name="ct">Ends the window, as the daemon stopping does.</param>
    public static async Task ServeAsync(WebSocket socket, HostedSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(session);

        using var attachment = session.Attach();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var sending = SendAsync(socket, attachment, linked.Token);
        var receiving = ReceiveAsync(socket, attachment, linked.Token);

        await Task.WhenAny(sending, receiving).ConfigureAwait(false);
        await linked.CancelAsync().ConfigureAwait(false);

        try
        {
            await Task.WhenAll(sending, receiving).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Either end going away is how a window ends.
        }

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, patience.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
            {
                // Gone already.
            }
        }
    }

    private static async Task SendAsync(WebSocket socket, TerminalAttachment attachment, CancellationToken ct)
    {
        await foreach (var frame in attachment.Frames.ReadAllAsync(ct).ConfigureAwait(false))
        {
            switch (frame)
            {
                case OutputFrame output:
                    await socket.SendAsync(output.Data, WebSocketMessageType.Binary, endOfMessage: true, ct).ConfigureAwait(false);
                    break;

                case LeaseFrame lease:
                    await SendTextAsync(socket, new { type = "lease", holds = lease.HoldsInput }, ct).ConfigureAwait(false);
                    break;

                case ExitFrame exit:
                    await SendTextAsync(socket, new { type = "exit", code = exit.Code }, ct).ConfigureAwait(false);
                    break;
            }
        }

        if (attachment.Dropped is { } reason)
        {
            await SendTextAsync(socket, new { type = "dropped", reason }, ct).ConfigureAwait(false);
        }
    }

    private static Task SendTextAsync(WebSocket socket, object message, CancellationToken ct) =>
        socket.SendAsync(
            new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(message)),
            WebSocketMessageType.Text,
            endOfMessage: true,
            ct);

    private static async Task ReceiveAsync(WebSocket socket, TerminalAttachment attachment, CancellationToken ct)
    {
        var buffer = new byte[ReceiveSize];
        using var message = new MemoryStream();

        while (socket.State == WebSocketState.Open)
        {
            var received = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);

            if (received.MessageType == WebSocketMessageType.Close)
            {
                return;
            }

            message.Write(buffer, 0, received.Count);

            if (message.Length > Largest)
            {
                // Nothing a person types is this long. Ending the window is
                // kinder than holding an unbounded message in memory.
                return;
            }

            if (!received.EndOfMessage)
            {
                continue;
            }

            var data = message.ToArray();
            message.SetLength(0);

            if (received.MessageType == WebSocketMessageType.Binary)
            {
                await attachment.WriteAsync(data, ct).ConfigureAwait(false);
            }
            else
            {
                Control(attachment, data);
            }
        }
    }

    /// <summary>Acts on a control message from the window. Anything unreadable is ignored.</summary>
    internal static void Control(TerminalAttachment attachment, byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String)
            {
                return;
            }

            switch (type.GetString())
            {
                case "resize"
                    when root.TryGetProperty("columns", out var columns)
                        && root.TryGetProperty("rows", out var rows)
                        && columns.TryGetInt32(out var width)
                        && rows.TryGetInt32(out var height)
                        && width is > 0 and <= 1000
                        && height is > 0 and <= 1000:
                    attachment.Resize(width, height);
                    break;

                case "takeover":
                    attachment.TakeOver();
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or DecoderFallbackException)
        {
            // A window sending nonsense gets nothing for it.
        }
    }
}
