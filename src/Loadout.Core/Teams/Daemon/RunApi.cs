using System.Net;
using System.Text;
using System.Text.Json;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Teams.Daemon;

/// <summary>
/// The terminal a forwarded command is drawn for: where it was typed and how
/// that terminal shows things.
/// </summary>
/// <param name="Directory">The directory it was typed in.</param>
/// <param name="Width">How many columns the terminal has.</param>
/// <param name="Ansi">Whether the terminal takes escape sequences.</param>
/// <param name="Colours">How many colours it shows: 0, 3, 4, 8 or 24 bits.</param>
public sealed record ForwardedTerminal(string Directory, int Width, bool Ansi, int Colours);

/// <summary>
/// The command line's own commands, handed to a running daemon to run instead
/// of a new launcher doing it, under <c>/api/v1/run</c>.
/// </summary>
/// <remarks>
/// <para>
/// A launcher typed in a terminal spends most of a short command starting:
/// the runtime, then every service it might need, then the parser. A daemon
/// of the same version has done all of that already, so the command line
/// sends it the words, the directory and the shape of the terminal, and
/// writes back what comes. The answer is streamed as it is written, one JSON
/// object per line: <c>{"out":…}</c>, <c>{"err":…}</c>, and last
/// <c>{"exit":n}</c>.
/// </para>
/// <para>
/// It runs anything, so it is held tighter than the page's routes: the token
/// itself in a header and never the browser's cookie, so a page cannot reach
/// it; never on a dashboard that only watches or listens beyond this machine;
/// and only from a launcher of exactly this version, because a command that
/// changed between the two would otherwise answer as neither does. Anything
/// refused here is run by the command line itself, as it always was.
/// </para>
/// </remarks>
public static class RunApi
{
    public const string Root = "/api/v1/run";

    /// <summary>Said in the refusal of a launcher of another version, which runs the command itself.</summary>
    public const int OtherVersion = 409;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record Asked(string? Version, List<string>? Words, string? Directory, int Width, bool Ansi, int Colours);

    /// <summary>Runs what was asked and streams back what it says.</summary>
    /// <param name="context">The request.</param>
    /// <param name="commands">What runs a command in the daemon.</param>
    /// <param name="version">This daemon's version.</param>
    /// <param name="ran">Told each forwarded command's name, for the daemon's log; never the words after it.</param>
    /// <param name="ct">Stops the command when the daemon stops.</param>
    public static async Task HandleAsync(
        HttpListenerContext context, IStudioCommands commands, string version, Action<string>? ran, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(commands);

        Asked? asked;

        try
        {
            asked = await JsonSerializer.DeserializeAsync<Asked>(context.Request.InputStream, Json, ct).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            asked = null;
        }

        if (asked is not { Words: { Count: > 0 } words, Directory: { Length: > 0 } directory } || !Path.IsPathFullyQualified(directory))
        {
            await DashboardServer.WriteAsync(context, 400, "text/plain; charset=utf-8",
                "The body should be JSON with the launcher's 'version', its 'words', and the 'directory' they were typed in.").ConfigureAwait(false);

            return;
        }

        if (!string.Equals(asked.Version, version, StringComparison.Ordinal))
        {
            await DashboardServer.WriteAsync(context, OtherVersion, "text/plain; charset=utf-8",
                $"This daemon is version {version}, so the command runs where it was typed.").ConfigureAwait(false);

            return;
        }

        var terminal = new ForwardedTerminal(directory, Math.Clamp(asked.Width, 20, 1000), asked.Ansi, asked.Colours);

        if (commands.Forward(words, terminal) is not { } process)
        {
            await DashboardServer.WriteAsync(context, 501, "text/plain; charset=utf-8",
                "This daemon runs no commands for the command line.").ConfigureAwait(false);

            return;
        }

        ran?.Invoke(Named(words));

        var response = context.Response;

        response.StatusCode = 200;
        response.ContentType = "application/x-ndjson; charset=utf-8";
        response.SendChunked = true;

        var gate = new SemaphoreSlim(1, 1);
        var stream = response.OutputStream;

        await using (process.ConfigureAwait(false))
        {
            try
            {
                await Task.WhenAll(
                    PumpAsync(process.Output, "out", stream, gate, ct),
                    PumpAsync(process.Error, "err", stream, gate, ct)).ConfigureAwait(false);

                var code = await process.Exited.ConfigureAwait(false);

                await FrameAsync(stream, gate, new Dictionary<string, object> { ["exit"] = code }, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException)
            {
                // The terminal went, Ctrl+C or a closed window: stop what it asked for.
                process.Kill();
            }
        }

        try
        {
            response.Close();
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException)
        {
        }
    }

    /// <summary>The command's name, as far as its words name one, for the log.</summary>
    private static string Named(IReadOnlyList<string> words) =>
        string.Join(' ', words.TakeWhile(word => word.Length > 0 && word[0] != '-').Take(3));

    /// <summary>Sends what a stream says a line at a time, as it is written.</summary>
    private static async Task PumpAsync(TextReader reader, string name, Stream stream, SemaphoreSlim gate, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            await FrameAsync(stream, gate, new Dictionary<string, object> { [name] = line + "\n" }, ct).ConfigureAwait(false);
        }
    }

    private static async Task FrameAsync(Stream stream, SemaphoreSlim gate, Dictionary<string, object> frame, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(frame) + "\n");

        await gate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
