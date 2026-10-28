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
    private readonly Func<string, CancellationToken, Task>? _identified;
    private readonly Queue<string> _recent = new();
    private decimal _costSoFar;

    // A read of the agent's output that was started and has not come back.
    // A turn given up on as silent leaves it running rather than cancel it,
    // because a reader cancelled mid-line is not one that can be read again.
    private Task<string?>? _reading;

    /// <summary>How many of the agent's last lines are kept for <see cref="RecentOutput"/>.</summary>
    private const int RecentKept = 20;

    /// <param name="process">The agent, already started.</param>
    /// <param name="protocol">How the agent's wire format reads.</param>
    /// <param name="identified">
    /// Called with each identifier the agent gives the conversation, the
    /// first time it is seen. Called at once rather than when the session
    /// closes, so that a launcher killed mid-turn has still written down
    /// whose conversation it was.
    /// </param>
    public HeadlessSession(
        IPipedProcess process,
        IHeadlessProtocol protocol,
        Func<string, CancellationToken, Task>? identified = null)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(protocol);

        _process = process;
        _protocol = protocol;
        _identified = identified;
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

    /// <summary>
    /// The last lines the agent wrote to its output, oldest first: what it
    /// was doing when it went quiet, for the record of a turn given up on.
    /// </summary>
    public IReadOnlyList<string> RecentOutput
    {
        get
        {
            lock (_recent)
            {
                return _recent.ToList();
            }
        }
    }

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
    /// <param name="quiet">
    /// How long the agent may write nothing before the turn is given up on as
    /// silent, or null to wait for as long as it takes.
    /// </param>
    public async Task<HeadlessTurn> TurnAsync(
        string message,
        Func<HeadlessEvent, CancellationToken, Task>? watching = null,
        CancellationToken ct = default,
        QuietLimit? quiet = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        await _process.Input.WriteLineAsync(_protocol.EncodeUserMessage(message).AsMemory(), ct).ConfigureAwait(false);
        await _process.Input.FlushAsync(ct).ConfigureAwait(false);

        return await ReadTurnAsync(watching, ct, quiet).ConfigureAwait(false);
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
    /// <param name="quiet">As in <see cref="TurnAsync"/>.</param>
    /// <remarks>
    /// A turn given up on as silent comes back with <see cref="HeadlessTurn.Silent"/>
    /// set rather than as an exception, and the agent is left running: whether
    /// to kill it, and what to say to it afterwards, is the caller's to decide.
    /// </remarks>
    public async Task<HeadlessTurn> ReadTurnAsync(
        Func<HeadlessEvent, CancellationToken, Task>? watching = null,
        CancellationToken ct = default,
        QuietLimit? quiet = null)
    {
        var events = new List<HeadlessEvent>();
        HeadlessResult? result = null;

        while (result is null)
        {
            var (line, silent) = await NextLineAsync(quiet, ct).ConfigureAwait(false);

            if (silent)
            {
                return new HeadlessTurn(events, null, 0m) { Silent = true };
            }

            if (line is null)
            {
                // The agent closed its output: it exited, or was killed. The
                // caller gets what there was and a null result, and can tell
                // the two apart from Exited.
                break;
            }

            lock (_recent)
            {
                _recent.Enqueue(line.Length > 500 ? line[..500] + "…" : line);

                while (_recent.Count > RecentKept)
                {
                    _recent.Dequeue();
                }
            }

            foreach (var evt in _protocol.Decode(line))
            {
                events.Add(evt);
                LastEventAt = DateTimeOffset.UtcNow;

                switch (evt)
                {
                    case HeadlessStarted { SessionId.Length: > 0 } started:
                        await IdentifyAsync(started.SessionId, ct).ConfigureAwait(false);
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
                await IdentifyAsync(id, ct).ConfigureAwait(false);
            }
        }

        return new HeadlessTurn(events, result, cost);
    }

    /// <summary>
    /// The agent's next line, or word that it has been silent for longer than
    /// it was allowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With no limit, the read is the plain one it always was, so a caller
    /// that never asked for one is not changed by it.
    /// </para>
    /// <para>
    /// With one, the read races a timer that every line starts again. The
    /// timer glances at the clock rather than sleeping the whole limit, so
    /// that a question put to a person is noticed within a glance and the
    /// clock stopped for it.
    /// </para>
    /// </remarks>
    private async Task<(string? Line, bool Silent)> NextLineAsync(QuietLimit? quiet, CancellationToken ct)
    {
        if (quiet is null)
        {
            var plain = _reading is { } left
                ? await left.WaitAsync(ct).ConfigureAwait(false)
                : await _process.Output.ReadLineAsync(ct).ConfigureAwait(false);

            _reading = null;

            return (plain, false);
        }

        _reading ??= _process.Output.ReadLineAsync(CancellationToken.None).AsTask();

        // Started on the first glance rather than here, so a line that was
        // already waiting is read without the clock being looked at at all.
        DateTimeOffset? deadline = null;

        while (!_reading.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();

            var now = quiet.Time.GetUtcNow();

            if (quiet.Held?.Invoke() == true)
            {
                // Waiting on a person. The clock stops, and starts again at
                // its full length once they have answered.
                deadline = null;
            }
            else
            {
                deadline ??= now + quiet.Limit;

                if (now >= deadline)
                {
                    return (null, true);
                }
            }

            var left = deadline is { } due && due - now < quiet.Glance ? due - now : quiet.Glance;

            using var glance = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var waited = quiet.WaitAsync(left, glance.Token);

            await Task.WhenAny(_reading, waited).ConfigureAwait(false);
            await glance.CancelAsync().ConfigureAwait(false);
        }

        var line = await _reading.ConfigureAwait(false);
        _reading = null;

        return (line, false);
    }

    /// <summary>
    /// Takes the agent's identifier for the conversation, and passes on one
    /// not seen before. A resumed session can be given a new identifier, so
    /// a change is passed on as well as the first.
    /// </summary>
    private async Task IdentifyAsync(string id, CancellationToken ct)
    {
        if (string.Equals(SessionId, id, StringComparison.Ordinal))
        {
            return;
        }

        SessionId = id;

        if (_identified is not null)
        {
            await _identified(id, ct).ConfigureAwait(false);
        }
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
