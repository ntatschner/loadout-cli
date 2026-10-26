using System.Threading.Channels;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Sessions.Hosting;

/// <summary>Something a hosted session tells the windows watching it.</summary>
public abstract record TerminalFrame;

/// <summary>Output from the session, exactly as the pseudo-terminal produced it.</summary>
/// <param name="Data">The bytes, escape sequences and all.</param>
public sealed record OutputFrame(ReadOnlyMemory<byte> Data) : TerminalFrame;

/// <summary>Whether this window now holds the input.</summary>
/// <param name="HoldsInput">True for the one window that may type.</param>
public sealed record LeaseFrame(bool HoldsInput) : TerminalFrame;

/// <summary>The session's process has ended.</summary>
/// <param name="Code">Its exit code, or null where it could not be read.</param>
public sealed record ExitFrame(int? Code) : TerminalFrame;

/// <summary>
/// An agent session the daemon runs in a pseudo-terminal of its own, so that
/// a browser can show it and type into it.
/// </summary>
/// <remarks>
/// <para>
/// One reader drains the pseudo-terminal for as long as the child runs,
/// whether or not anybody is watching. Every chunk goes into the scrollback
/// and to each attached window, under one lock, so a window attaching sees
/// the scrollback and then everything after it, with nothing missed between
/// the two and nothing shown twice.
/// </para>
/// <para>
/// The newest window holds the input. The others still see everything, and
/// any of them can take the input back. That makes a stale tab left open
/// harmless and a second window for watching easy, which is what the Studio
/// plan asked for. Only the holder's size is applied, because two windows
/// of different sizes would otherwise fight over the child's.
/// </para>
/// <para>
/// A window that cannot keep up is closed rather than waited for. Waiting
/// would stall the reader, and a stalled reader stalls the child as soon as
/// the pseudo-terminal's own buffer fills. A closed window reattaches and
/// gets the scrollback, which is the same picture.
/// </para>
/// </remarks>
public sealed class HostedSession : IAsyncDisposable
{
    /// <summary>How much output is kept for a window that attaches later.</summary>
    public const int DefaultScrollback = 2 * 1024 * 1024;

    /// <summary>How many frames a window may fall behind by before it is closed.</summary>
    public const int DefaultQueue = 4096;

    private const int ReadSize = 16 * 1024;

    private readonly IPseudoTerminal _terminal;
    private readonly Scrollback _scrollback;
    private readonly int _queue;
    private readonly object _gate = new();
    private readonly List<TerminalAttachment> _attached = [];
    private readonly CancellationTokenSource _stopping = new();
    private TerminalAttachment? _holder;
    private Task _pump = Task.CompletedTask;
    private DateTimeOffset? _lastOutput;
    private DateTimeOffset? _lastInput;
    private bool _ended;
    private int _disposed;
    private int? _exitCode;
    private DateTimeOffset? _endedAt;
    private string? _label;
    private int _columns;
    private int _rows;

    private HostedSession(IPseudoTerminal terminal, string id, string title, int columns, int rows, int scrollback, int queue)
    {
        _terminal = terminal;
        _scrollback = new Scrollback(scrollback);
        _queue = queue;
        _columns = columns;
        _rows = rows;
        Id = id;
        Title = title;
        StartedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The name the daemon knows this session by.</summary>
    public string Id { get; }

    /// <summary>What the session was launched as, as a person would call it.</summary>
    public string Title { get; }

    /// <summary>
    /// The name somebody gave it since, or null for none. Held only as long as
    /// the session is, which is no longer than the daemon.
    /// </summary>
    public string? Label
    {
        get { lock (_gate) { return _label; } }
        internal set { lock (_gate) { _label = value; } }
    }

    /// <summary>What to call it now: the name it was given, or what it was launched as.</summary>
    public string Name => Label ?? Title;

    /// <summary>When it started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>
    /// When the child last wrote anything, or null before it has. Studio calls
    /// a session idle once this is older than studio-idle-after, which is the
    /// only sign of a quiet agent a terminal gives.
    /// </summary>
    public DateTimeOffset? LastOutputAt
    {
        get { lock (_gate) { return _lastOutput; } }
    }

    /// <summary>
    /// When a window last typed into the session, or null before anybody
    /// has. An agent's report that it is waiting stops counting from here
    /// (<see cref="SessionSignals.Needs"/>).
    /// </summary>
    public DateTimeOffset? LastInputAt
    {
        get { lock (_gate) { return _lastInput; } }
    }

    /// <summary>Whether the child has ended.</summary>
    public bool HasEnded
    {
        get { lock (_gate) { return _ended; } }
    }

    /// <summary>The child's exit code once it has ended.</summary>
    public int? ExitCode
    {
        get { lock (_gate) { return _exitCode; } }
    }

    /// <summary>
    /// When the child ended, or null while it runs. Studio keeps a session
    /// that failed on its map for a day from here, as it does a failed run
    /// from when it finished.
    /// </summary>
    public DateTimeOffset? EndedAt
    {
        get { lock (_gate) { return _endedAt; } }
    }

    /// <summary>How many windows are watching.</summary>
    public int Viewers
    {
        get { lock (_gate) { return _attached.Count; } }
    }

    /// <summary>The size the child was last given.</summary>
    public (int Columns, int Rows) Size
    {
        get { lock (_gate) { return (_columns, _rows); } }
    }

    /// <summary>Starts the child in the pseudo-terminal it is given, which the session then owns.</summary>
    /// <param name="terminal">A pseudo-terminal not yet started.</param>
    /// <param name="request">What to run.</param>
    /// <param name="id">The name the daemon will know it by.</param>
    /// <param name="title">What it is, for a person.</param>
    /// <param name="columns">Width to start at.</param>
    /// <param name="rows">Height to start at.</param>
    /// <param name="scrollback">Bytes of output kept for later windows.</param>
    /// <param name="queue">Frames a window may fall behind by.</param>
    /// <param name="lifetime">
    /// Ties the child to this process, so a daemon that is killed takes its
    /// sessions with it at once. Without it they outlive the daemon until
    /// they notice their console has gone, which took five seconds in the
    /// spike and is up to the child.
    /// </param>
    /// <param name="ct">Cancels the start.</param>
    public static async Task<OperationResult<HostedSession>> StartAsync(
        IPseudoTerminal terminal,
        ProcessRequest request,
        string id,
        string title,
        int columns,
        int rows,
        int scrollback = DefaultScrollback,
        int queue = DefaultQueue,
        IChildLifetime? lifetime = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        ArgumentNullException.ThrowIfNull(request);

        var started = await terminal.StartAsync(request, columns, rows, ct).ConfigureAwait(false);

        if (!started.Succeeded)
        {
            terminal.Dispose();

            return OperationResult<HostedSession>.Fail(started.Error ?? "The session could not be started.", started.ExitCode);
        }

        if (terminal.ProcessId > 0)
        {
            lifetime?.Adopt(terminal.ProcessId);
        }

        var session = new HostedSession(terminal, id, title, columns, rows, scrollback, queue);
        session._pump = Task.Run(() => session.PumpAsync(session._stopping.Token));

        return OperationResult<HostedSession>.Ok(session);
    }

    /// <summary>The process the session started, or 0 where the terminal could not say.</summary>
    public int ProcessId => _terminal.ProcessId;

    /// <summary>Completes once the session has ended and every window has been told.</summary>
    public Task Completion => _pump;

    /// <summary>
    /// A new window watching the session. It gets the scrollback first, then
    /// everything after it, and it holds the input until another window
    /// attaches or takes it.
    /// </summary>
    /// <param name="watching">
    /// A window that may only watch, as a dashboard started with
    /// <c>--watch-only</c> serves: it never holds the input, cannot take it,
    /// and is passed over when the lease moves, so opening one does not take
    /// the keyboard from somebody typing.
    /// </param>
    public TerminalAttachment Attach(bool watching = false)
    {
        lock (_gate)
        {
            var attachment = new TerminalAttachment(this, _queue, watching);
            var kept = _scrollback.Snapshot();

            if (kept.Length > 0)
            {
                attachment.Offer(new OutputFrame(kept));
            }

            if (_ended)
            {
                attachment.Offer(new ExitFrame(_exitCode));
                attachment.Close(null);

                return attachment;
            }

            _attached.Add(attachment);

            if (watching)
            {
                attachment.Offer(new LeaseFrame(false));
            }
            else
            {
                Hand(attachment);
            }

            return attachment;
        }
    }

    /// <summary>Writes a window's input to the child, if that window holds the input.</summary>
    internal async Task<OperationResult> WriteFromAsync(TerminalAttachment attachment, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        lock (_gate)
        {
            if (_ended)
            {
                return OperationResult.Fail("The session has ended.");
            }

            if (!ReferenceEquals(_holder, attachment))
            {
                return OperationResult.Fail("Another window holds the input. Take over to type here.");
            }

            // The terminal answering the agent is not somebody answering it.
            if (!TerminalReports.IsOnlyReports(data.Span))
            {
                _lastInput = DateTimeOffset.UtcNow;
            }
        }

        return await _terminal.WriteAsync(data, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes to the child as the daemon rather than as a window: not held to
    /// the input lease, and not counted as somebody typing, so an agent's
    /// report that it is waiting still stands afterwards.
    /// </summary>
    internal async Task<OperationResult> TypeAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (HasEnded)
        {
            return OperationResult.Fail("The session has ended.");
        }

        return await _terminal.WriteAsync(data, ct).ConfigureAwait(false);
    }

    /// <summary>Applies a window's size, if that window holds the input.</summary>
    internal OperationResult ResizeFrom(TerminalAttachment attachment, int columns, int rows)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_holder, attachment))
            {
                return OperationResult.Fail("Only the window holding the input sets the size.");
            }

            if (columns == _columns && rows == _rows)
            {
                return OperationResult.Ok();
            }

            _columns = columns;
            _rows = rows;
        }

        return _terminal.Resize(columns, rows);
    }

    /// <summary>Gives a window the input.</summary>
    internal void TakeOver(TerminalAttachment attachment)
    {
        lock (_gate)
        {
            if (_attached.Contains(attachment) && !attachment.Watching)
            {
                Hand(attachment);
            }
        }
    }

    /// <summary>A window has gone.</summary>
    internal void Detach(TerminalAttachment attachment)
    {
        lock (_gate)
        {
            Remove(attachment, null);
        }
    }

    /// <summary>Must be called under the lock.</summary>
    private void Hand(TerminalAttachment attachment)
    {
        if (ReferenceEquals(_holder, attachment))
        {
            return;
        }

        var previous = _holder;
        _holder = attachment;

        if (previous is not null && !previous.Offer(new LeaseFrame(false)))
        {
            Remove(previous, "fell behind");
        }

        if (!attachment.Offer(new LeaseFrame(true)))
        {
            Remove(attachment, "fell behind");
        }
    }

    /// <summary>Must be called under the lock.</summary>
    private void Remove(TerminalAttachment attachment, string? reason)
    {
        if (!_attached.Remove(attachment))
        {
            return;
        }

        attachment.Close(reason);

        if (!ReferenceEquals(_holder, attachment))
        {
            return;
        }

        _holder = null;

        // The most recent window left that may type takes the input, as it
        // would have had the one that just went never attached.
        if (_attached.LastOrDefault(one => !one.Watching) is { } next)
        {
            Hand(next);
        }
    }

    private async Task PumpAsync(CancellationToken ct)
    {
        var buffer = new byte[ReadSize];

        try
        {
            while (true)
            {
                var read = await _terminal.ReadAsync(buffer, ct).ConfigureAwait(false);

                if (!read.Succeeded || read.Value <= 0)
                {
                    break;
                }

                var chunk = buffer.AsSpan(0, read.Value).ToArray();

                lock (_gate)
                {
                    _scrollback.Append(chunk);
                    _lastOutput = DateTimeOffset.UtcNow;

                    foreach (var attachment in _attached.ToArray())
                    {
                        if (!attachment.Offer(new OutputFrame(chunk)))
                        {
                            Remove(attachment, "fell behind");
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Stopped on purpose. The exit below still reports what happened.
        }

        int? code = null;

        try
        {
            using var patience = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var exit = await _terminal.WaitForExitAsync(patience.Token).ConfigureAwait(false);

            if (exit.Succeeded)
            {
                code = exit.Value;
            }
        }
        catch (OperationCanceledException)
        {
            // Still running after its output closed. Reported without a code.
        }

        lock (_gate)
        {
            _ended = true;
            _exitCode = code;
            _endedAt = DateTimeOffset.UtcNow;
            _holder = null;

            foreach (var attachment in _attached)
            {
                attachment.Offer(new ExitFrame(code));
                attachment.Close(null);
            }

            _attached.Clear();
        }
    }

    /// <summary>Ends the session: the pseudo-terminal closes, which tells the child its terminal has gone.</summary>
    /// <remarks>
    /// Safe to call again: a session ended and kept on the list is disposed a
    /// second time when it is forgotten, and the second call only waits.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        var first = Interlocked.Exchange(ref _disposed, 1) == 0;

        if (first)
        {
            await _stopping.CancelAsync().ConfigureAwait(false);
            _terminal.Dispose();
        }

        try
        {
            await _pump.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The terminal was closed under the reader, which is how it stops.
        }

        if (first)
        {
            _stopping.Dispose();
        }
    }
}

/// <summary>One window watching a hosted session.</summary>
public sealed class TerminalAttachment : IDisposable
{
    private readonly HostedSession _session;
    private readonly Channel<TerminalFrame> _frames;

    internal TerminalAttachment(HostedSession session, int capacity, bool watching = false)
    {
        _session = session;
        Watching = watching;
        _frames = Channel.CreateBounded<TerminalFrame>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    /// <summary>Whether this window may only watch, never type.</summary>
    public bool Watching { get; }

    /// <summary>What the session has to say to this window, in order. Completes when the window is closed.</summary>
    public ChannelReader<TerminalFrame> Frames => _frames.Reader;

    /// <summary>Why the session closed this window, or null when it ended normally or the window left.</summary>
    public string? Dropped { get; private set; }

    /// <summary>Sends this window's input to the child.</summary>
    /// <param name="data">The bytes, as the terminal in the window produced them.</param>
    /// <param name="ct">Cancels the write.</param>
    public Task<OperationResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default) =>
        _session.WriteFromAsync(this, data, ct);

    /// <summary>Tells the session this window's size.</summary>
    /// <param name="columns">Width in cells.</param>
    /// <param name="rows">Height in cells.</param>
    public OperationResult Resize(int columns, int rows) => _session.ResizeFrom(this, columns, rows);

    /// <summary>Takes the input from whichever window holds it.</summary>
    public void TakeOver() => _session.TakeOver(this);

    internal bool Offer(TerminalFrame frame) => _frames.Writer.TryWrite(frame);

    internal void Close(string? reason)
    {
        Dropped ??= reason;
        _frames.Writer.TryComplete();
    }

    /// <summary>The window has gone.</summary>
    public void Dispose() => _session.Detach(this);
}
