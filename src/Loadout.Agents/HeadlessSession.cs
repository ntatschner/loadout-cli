using Loadout.Models.Agents;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents;

/// <summary>
/// A conversation with an agent that has no terminal: a message goes in,
/// events come out until the agent says the turn is over, and the caller
/// decides when there will be no more.
/// </summary>
/// <remarks>
/// <para>
/// The same for every agent. What differs, the wire format, is behind
/// <see cref="IHeadlessProtocol"/>; what is the same, holding the pipe,
/// reading line by line, knowing a turn from a stream, keeping the error
/// stream drained so the child never blocks on it, and turning a running
/// cost into a per-turn one, is here.
/// </para>
/// <para>
/// The error stream is pumped from the moment the session starts. A child
/// whose stderr nobody reads fills the pipe and stops, which looks exactly
/// like a stalled agent and is not one.
/// </para>
/// </remarks>
public sealed class HeadlessSession : IAsyncDisposable
{
    private readonly IPipedProcess _process;
    private readonly IHeadlessProtocol _protocol;
    private readonly List<string> _standardError = [];
    private readonly Task _errorPump;
    private decimal _costSoFar;

    public HeadlessSession(IPipedProcess process, IHeadlessProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(protocol);

        _process = process;
        _protocol = protocol;
        _errorPump = Task.Run(PumpErrorAsync);
    }

    /// <summary>The agent's own identifier for this conversation, once it has said it.</summary>
    public string? SessionId { get; private set; }

    /// <summary>When the agent last wrote anything, for telling a quiet agent from a stalled one.</summary>
    public DateTimeOffset LastEventAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The operating system's identifier for the agent process.</summary>
    public int ProcessId => _process.ProcessId;

    /// <summary>Completes with the exit code once the agent has exited.</summary>
    public Task<int> Exited => _process.Exited;

    /// <summary>Everything the agent has written to its error stream so far.</summary>
    public IReadOnlyList<string> StandardError
    {
        get
        {
            lock (_standardError)
            {
                return _standardError.ToList();
            }
        }
    }

    /// <summary>
    /// Sends one message and reads until the agent reports the turn over, or
    /// its output ends.
    /// </summary>
    /// <param name="message">What to say to the agent.</param>
    /// <param name="watching">
    /// Called with each event as it arrives, for anything that wants to know
    /// what the agent is doing before the turn is over. A turn can run for
    /// minutes, and until this existed the only thing anybody could see was
    /// the summary afterwards.
    /// </param>
    /// <param name="ct">Cancels the turn.</param>
    public async Task<HeadlessTurn> TurnAsync(
        string message,
        Func<HeadlessEvent, CancellationToken, Task>? watching = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _process.Input.WriteLineAsync(_protocol.EncodeUserMessage(message).AsMemory(), ct).ConfigureAwait(false);
        await _process.Input.FlushAsync(ct).ConfigureAwait(false);

        return await ReadTurnAsync(watching, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Says something to the agent without waiting for it to answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of <see cref="TurnAsync"/>: the message goes in and the
    /// caller carries on. What it is for is saying something to an agent that
    /// is already in the middle of a turn - "stop, you are in the wrong file" -
    /// which otherwise has to wait until the turn comes back, by which time it
    /// has spent the money it was going to spend.
    /// </para>
    /// <para>
    /// Whether the agent acts on it mid-turn is the agent's business and not
    /// this one's. Claude Code's stream-json input takes further user messages
    /// while a turn is running; another agent may queue it until the turn ends.
    /// What is promised here is that it was written and flushed.
    /// </para>
    /// </remarks>
    /// <param name="message">What to say.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task SayAsync(string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        await _process.Input.WriteLineAsync(_protocol.EncodeUserMessage(message).AsMemory(), ct)
            .ConfigureAwait(false);

        await _process.Input.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads until the agent reports a turn over, or its output ends. For a
    /// turn the agent started on its own, or the tail of one after a
    /// message was sent another way.
    /// </summary>
    /// <param name="watching">Called with each event as it arrives, as in <see cref="TurnAsync"/>.</param>
    /// <param name="ct">Cancels the read.</param>
    public async Task<HeadlessTurn> ReadTurnAsync(
        Func<HeadlessEvent, CancellationToken, Task>? watching = null,
        CancellationToken ct = default)
    {
        var events = new List<HeadlessEvent>();
        HeadlessResult? result = null;

        while (result is null)
        {
            var line = await _process.Output.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                // The agent closed its output: it exited, or was killed. The
                // caller gets what there was and a null result, and can tell
                // the two apart from Exited.
                break;
            }

            foreach (var evt in _protocol.Decode(line))
            {
                events.Add(evt);
                LastEventAt = DateTimeOffset.UtcNow;

                switch (evt)
                {
                    case HeadlessStarted { SessionId.Length: > 0 } started:
                        SessionId = started.SessionId;
                        break;

                    case HeadlessResult finished:
                        result = finished;
                        break;
                }

                if (watching is not null)
                {
                    // Awaited rather than left running, so the run's record
                    // stays in the order things happened. Whoever is watching
                    // is expected to be quick, and to decide for itself how
                    // much of this is worth writing down.
                    await watching(evt, ct).ConfigureAwait(false);
                }
            }
        }

        var cost = 0m;

        if (result is not null)
        {
            // The agent reports the session's running total in every result.
            // The turn's own cost is the step from the previous total, and
            // the first result's step is from zero.
            cost = result.CumulativeCostUsd - _costSoFar;
            _costSoFar = result.CumulativeCostUsd;

            if (result.SessionId is { Length: > 0 } id)
            {
                SessionId = id;
            }
        }

        return new HeadlessTurn(events, result, cost);
    }

    /// <summary>
    /// Tells the agent there are no more messages and waits for it to leave.
    /// An agent that has not gone within the grace period is killed, because
    /// a node that will not end on its own is a node the run has to end.
    /// </summary>
    /// <returns>The exit code, and whether the agent had to be killed to produce it.</returns>
    public async Task<(int ExitCode, bool Killed)> EndAsync(TimeSpan grace, CancellationToken ct = default)
    {
        await _process.CloseInputAsync().ConfigureAwait(false);

        int exitCode;
        var killed = false;

        try
        {
            exitCode = await _process.Exited.WaitAsync(grace, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _process.Kill();
            killed = true;
            exitCode = await _process.Exited.ConfigureAwait(false);
        }

        // The error stream closes when the process goes, and the pump reads
        // to the end of it. Waiting for that here, briefly, is what makes
        // StandardError complete for whoever reads it next: an agent's last
        // words are usually why it left.
        await Task.WhenAny(_errorPump, Task.Delay(TimeSpan.FromSeconds(2), ct)).ConfigureAwait(false);

        return (exitCode, killed);
    }

    /// <summary>Stops the agent now, whatever it is doing.</summary>
    public void Kill() => _process.Kill();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _process.DisposeAsync().ConfigureAwait(false);

        // Ends on its own once the process is gone and its stream closes;
        // bounded so a handle that never closes cannot hold the caller.
        await Task.WhenAny(_errorPump, Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
    }

    private async Task PumpErrorAsync()
    {
        try
        {
            string? line;

            while ((line = await _process.Error.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                lock (_standardError)
                {
                    _standardError.Add(line);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // The stream went away with the process. Nothing more to read.
        }
    }
}
