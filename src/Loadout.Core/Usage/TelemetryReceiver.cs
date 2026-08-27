using System.Net;
using System.Text;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Usage;

/// <summary>
/// Listens for the usage the agents report, and writes it down.
/// </summary>
/// <remarks>
/// <para>
/// An ordinary HTTP listener, because the agents were asked for OTLP in JSON
/// over HTTP rather than protobuf over gRPC. That keeps the whole of this to
/// the framework: no collector to install, nothing to run alongside.
/// </para>
/// <para>
/// It binds a loopback address and refuses anything else. What passes through
/// here says when somebody was working and how hard, and that is not something
/// to serve to a network by accident.
/// </para>
/// </remarks>
public sealed class TelemetryReceiver : IDisposable
{
    /// <summary>Where OTLP posts metrics.</summary>
    private const string MetricsPath = "/v1/metrics";

    /// <summary>
    /// The largest payload to accept.
    /// </summary>
    /// <remarks>
    /// A busy session posts a few kilobytes. This is far above that and far
    /// below anything that could exhaust memory, which is the only thing an
    /// unbounded read would risk on a loopback socket.
    /// </remarks>
    private const int LargestPayload = 8 * 1024 * 1024;

    private readonly ITelemetryStore _store;
    private readonly HttpListener _listener = new();

    private readonly TimeProvider _time;

    public TelemetryReceiver(ITelemetryStore store, TimeProvider? time = null)
    {
        _store = store;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>How many payloads have been accepted since it started.</summary>
    public int Accepted { get; private set; }

    /// <summary>How many numbers have been written down since it started.</summary>
    public int Written { get; private set; }

    /// <summary>
    /// Starts listening, or explains why it could not.
    /// </summary>
    public OperationResult Start(string endpoint)
    {
        if (!TelemetryEnvironment.IsLoopback(endpoint))
        {
            return OperationResult.Fail(
                $"'{endpoint}' is not an address on this machine. Usage reporting listens on "
                + "loopback only.",
                ExitCode.InvalidArguments);
        }

        // HttpListener wants a prefix with a trailing slash and a path to serve.
        var prefix = endpoint.TrimEnd('/') + "/";

        _listener.Prefixes.Add(prefix);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            return OperationResult.Fail(
                $"Could not listen on {prefix}: {ex.Message}. "
                + "Another receiver may already be running, or the port may be in use.",
                ExitCode.GeneralFailure);
        }

        return OperationResult.Ok();
    }

    /// <summary>
    /// Accepts payloads until cancelled.
    /// </summary>
    public async Task ListenAsync(CancellationToken ct)
    {
        // Stopping the listener is what unblocks GetContextAsync; without this
        // the loop would sit on an accept that never returns.
        using var registration = ct.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
                // Already gone, which is the outcome this wanted.
            }
        });

        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException
                or InvalidOperationException)
            {
                // Stopped, which is how this ends.
                return;
            }

            try
            {
                await HandleAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or HttpListenerException)
            {
                // One dropped payload is a lost count, not a reason to stop
                // listening for the rest of the session.
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;

        using var response = context.Response;

        if (!string.Equals(request.HttpMethod, "POST", StringComparison.Ordinal)
            || !string.Equals(request.Url?.AbsolutePath, MetricsPath, StringComparison.Ordinal))
        {
            // Logs and traces are deliberately not exported, so a post to
            // either is something this build did not ask for.
            response.StatusCode = (int)HttpStatusCode.NotFound;

            return;
        }

        if (request.ContentLength64 > LargestPayload)
        {
            response.StatusCode = (int)HttpStatusCode.RequestEntityTooLarge;

            return;
        }

        string body;

        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
        {
            body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }

        var samples = OtlpMetricReader.Read(body, _time.GetUtcNow());

        await _store.AppendAsync(samples, ct).ConfigureAwait(false);

        Accepted++;
        Written += samples.Count;

        // OTLP expects a JSON body on success; an empty object means everything
        // was accepted, and an agent that gets something else retries.
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/json";

        var acknowledgement = Encoding.UTF8.GetBytes("{}");

        response.ContentLength64 = acknowledgement.Length;

        await response.OutputStream.WriteAsync(acknowledgement, ct).ConfigureAwait(false);
    }

    public void Dispose() => ((IDisposable)_listener).Dispose();
}
