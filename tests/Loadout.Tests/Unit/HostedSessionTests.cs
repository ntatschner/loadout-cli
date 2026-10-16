using System.Text;
using System.Threading.Channels;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A session the daemon hosts for the browser: what a window sees when it
/// attaches, which window may type, and what happens to one that falls behind.
/// </summary>
public sealed class HostedSessionTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task A_window_attaching_later_gets_what_was_said_then_what_follows()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var first = session.Attach();
        (await NextAsync(first)).Should().BeOfType<LeaseFrame>();

        terminal.Say("one");
        Text(await NextAsync(first)).Should().Be("one");

        using var second = session.Attach();
        Text(await NextAsync(second)).Should().Be("one");
        (await NextAsync(second)).Should().Be(new LeaseFrame(true));

        terminal.Say("two");
        Text(await NextAsync(second)).Should().Be("two");
    }

    [Fact]
    public async Task The_newest_window_holds_the_input_and_the_older_one_cannot_type()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var older = session.Attach();
        using var newer = session.Attach();

        (await NextAsync(older)).Should().Be(new LeaseFrame(true));
        (await NextAsync(older)).Should().Be(new LeaseFrame(false));

        (await older.WriteAsync(Bytes("x"))).Succeeded.Should().BeFalse();
        (await newer.WriteAsync(Bytes("y"))).Succeeded.Should().BeTrue();

        terminal.Typed.Should().Be("y");
    }

    [Fact]
    public async Task Taking_over_moves_the_input_to_the_window_that_asked()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var older = session.Attach();
        using var newer = session.Attach();
        await NextAsync(newer);

        older.TakeOver();

        (await NextAsync(newer)).Should().Be(new LeaseFrame(false));
        (await newer.WriteAsync(Bytes("y"))).Succeeded.Should().BeFalse();
        (await older.WriteAsync(Bytes("x"))).Succeeded.Should().BeTrue();

        terminal.Typed.Should().Be("x");
    }

    [Fact]
    public async Task When_the_window_holding_the_input_leaves_the_most_recent_one_left_takes_it()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var first = session.Attach();
        using var second = session.Attach();
        var third = session.Attach();

        // Second was given the input when it attached and lost it to third.
        (await NextAsync(second)).Should().Be(new LeaseFrame(true));
        (await NextAsync(second)).Should().Be(new LeaseFrame(false));

        third.Dispose();

        (await NextAsync(second)).Should().Be(new LeaseFrame(true));
        (await second.WriteAsync(Bytes("z"))).Succeeded.Should().BeTrue();
        (await first.WriteAsync(Bytes("q"))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_window_holding_the_input_sets_the_size()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var older = session.Attach();
        using var newer = session.Attach();

        older.Resize(200, 60).Succeeded.Should().BeFalse();
        newer.Resize(100, 40).Succeeded.Should().BeTrue();

        terminal.Sizes.Should().Equal((100, 40));
        session.Size.Should().Be((100, 40));
    }

    [Fact]
    public async Task The_end_reaches_every_window_and_a_window_attaching_afterwards_still_sees_what_was_said()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var window = session.Attach();
        await NextAsync(window);

        terminal.Say("bye");
        terminal.Exit(3);

        Text(await NextAsync(window)).Should().Be("bye");
        (await NextAsync(window)).Should().Be(new ExitFrame(3));
        (await EndsAsync(window)).Should().BeTrue();

        session.HasEnded.Should().BeTrue();
        session.ExitCode.Should().Be(3);

        using var late = session.Attach();
        Text(await NextAsync(late)).Should().Be("bye");
        (await NextAsync(late)).Should().Be(new ExitFrame(3));
        (await EndsAsync(late)).Should().BeTrue();
        (await late.WriteAsync(Bytes("x"))).Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task A_window_that_falls_behind_is_closed_and_the_others_miss_nothing()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal, queue: 4);

        using var stalled = session.Attach();
        using var keeping = session.Attach();
        await NextAsync(keeping);

        var heard = new List<string>();

        for (var i = 0; i < 10; i++)
        {
            terminal.Say($"line {i}");
            heard.Add(Text(await NextAsync(keeping)));
        }

        heard.Should().Equal(Enumerable.Range(0, 10).Select(i => $"line {i}"));
        stalled.Dropped.Should().Be("fell behind");
        session.Viewers.Should().Be(1);
    }

    [Fact]
    public async Task Typing_reaches_the_child_byte_for_byte()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);

        using var window = session.Attach();

        // Arrow up, then a carriage return: what a terminal in a page sends.
        var keys = new byte[] { 27, (byte)'[', (byte)'A', 13 };
        (await window.WriteAsync(keys)).Succeeded.Should().BeTrue();

        terminal.Written.Should().Equal(keys);
    }

    [Fact]
    public async Task Ending_the_session_closes_the_pseudo_terminal()
    {
        var terminal = new ScriptedTerminal();
        var session = await StartAsync(terminal);

        await session.DisposeAsync();

        terminal.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task The_child_is_tied_to_the_daemon_so_a_killed_daemon_takes_it_along()
    {
        var terminal = new ScriptedTerminal();
        var lifetime = new RecordingLifetime();

        var started = await HostedSession.StartAsync(terminal, Request, "s1", "stand-in", 80, 24, lifetime: lifetime);
        await using var session = started.Value!;

        lifetime.Adopted.Should().Equal(ScriptedTerminal.ChildId);
    }

    [Fact]
    public async Task A_pseudo_terminal_that_would_not_start_is_reported_and_closed()
    {
        var terminal = new ScriptedTerminal { Refuse = "no console" };

        var started = await HostedSession.StartAsync(terminal, Request, "s1", "stand-in", 80, 24);

        started.Succeeded.Should().BeFalse();
        started.Error.Should().Be("no console");
        terminal.Disposed.Should().BeTrue();
    }

    [Fact]
    public void Kept_output_starts_after_a_line_feed_once_the_front_has_been_dropped()
    {
        var kept = new Scrollback(12);

        kept.Append(Bytes("first line" + (char)10));
        kept.Append(Bytes("second" + (char)10 + "third"));

        // Twenty-three bytes into twelve: the front of "second" went, so what is
        // handed out starts at the next whole line.
        kept.Trimmed.Should().BeTrue();
        Encoding.UTF8.GetString(kept.Snapshot()).Should().Be("third");
    }

    [Fact]
    public void Kept_output_is_handed_back_whole_while_nothing_has_been_dropped()
    {
        var kept = new Scrollback(64);

        kept.Append(Bytes("abc"));
        kept.Append(Bytes("def"));

        kept.Trimmed.Should().BeFalse();
        Encoding.UTF8.GetString(kept.Snapshot()).Should().Be("abcdef");
    }

    [Fact]
    public void Kept_output_keeps_the_newest_bytes_in_order_across_the_wrap()
    {
        var kept = new Scrollback(8);
        var all = new StringBuilder();

        for (var i = 0; i < 7; i++)
        {
            var piece = (char)10 + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ab";
            kept.Append(Bytes(piece));
            all.Append(piece);
        }

        var newest = all.ToString()[^8..];
        var expected = newest[(newest.IndexOf((char)10, StringComparison.Ordinal) + 1)..];

        Encoding.UTF8.GetString(kept.Snapshot()).Should().Be(expected);
    }

    [Fact]
    public void A_chunk_larger_than_the_buffer_keeps_its_own_end()
    {
        var kept = new Scrollback(6);

        kept.Append(Bytes("abcdefgh" + (char)10 + "ijk"));

        Encoding.UTF8.GetString(kept.Snapshot()).Should().Be("ijk");
    }

    [Fact]
    public async Task A_resize_message_with_a_size_out_of_range_is_ignored()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);
        using var window = session.Attach();

        TerminalSocket.Control(window, Bytes("""{"type":"resize","columns":0,"rows":30}"""));
        TerminalSocket.Control(window, Bytes("""{"type":"resize","columns":"wide","rows":30}"""));
        TerminalSocket.Control(window, Bytes("not json"));
        TerminalSocket.Control(window, Bytes("""{"type":"resize","columns":132,"rows":43}"""));

        terminal.Sizes.Should().Equal((132, 43));
    }

    [Fact]
    public async Task A_takeover_message_takes_the_input()
    {
        var terminal = new ScriptedTerminal();
        await using var session = await StartAsync(terminal);
        using var older = session.Attach();
        using var newer = session.Attach();

        TerminalSocket.Control(older, Bytes("""{"type":"takeover"}"""));

        (await older.WriteAsync(Bytes("x"))).Succeeded.Should().BeTrue();
        (await newer.WriteAsync(Bytes("y"))).Succeeded.Should().BeFalse();
    }

    private static readonly ProcessRequest Request = new("stand-in", []);

    private static async Task<HostedSession> StartAsync(ScriptedTerminal terminal, int queue = HostedSession.DefaultQueue)
    {
        var started = await HostedSession.StartAsync(terminal, Request, "s1", "stand-in", 80, 24, queue: queue);
        started.Succeeded.Should().BeTrue(started.Error);

        return started.Value!;
    }

    private static async Task<TerminalFrame> NextAsync(TerminalAttachment attachment)
    {
        using var patience = new CancellationTokenSource(Patience);

        return await attachment.Frames.ReadAsync(patience.Token);
    }

    private static async Task<bool> EndsAsync(TerminalAttachment attachment)
    {
        using var patience = new CancellationTokenSource(Patience);

        return !await attachment.Frames.WaitToReadAsync(patience.Token);
    }

    private static string Text(TerminalFrame frame) =>
        Encoding.UTF8.GetString(frame.Should().BeOfType<OutputFrame>().Subject.Data.Span);

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private sealed class RecordingLifetime : IChildLifetime
    {
        public List<int> Adopted { get; } = [];

        public bool IsEnforced => true;

        public string Detail => "recorded";

        public void Adopt(int processId) => Adopted.Add(processId);
    }

    /// <summary>A pseudo-terminal whose output the test writes and whose input the test reads.</summary>
    private sealed class ScriptedTerminal : IPseudoTerminal
    {
        private readonly Channel<byte[]> _output = Channel.CreateUnbounded<byte[]>();
        private readonly TaskCompletionSource<int> _exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<byte> _written = [];

        public const int ChildId = 4242;

        public int ProcessId { get; private set; }

        public string? Refuse { get; init; }

        public List<(int, int)> Sizes { get; } = [];

        public bool Disposed { get; private set; }

        public byte[] Written
        {
            get { lock (_written) { return [.. _written]; } }
        }

        public string Typed => Encoding.UTF8.GetString(Written);

        public void Say(string text) => _output.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

        public void Exit(int code)
        {
            _output.Writer.TryComplete();
            _exit.TrySetResult(code);
        }

        public Task<OperationResult> StartAsync(ProcessRequest request, int columns, int rows, CancellationToken ct = default) =>
            Task.FromResult(Refuse is null ? Started() : OperationResult.Fail(Refuse));

        private OperationResult Started()
        {
            ProcessId = ChildId;

            return OperationResult.Ok();
        }

        public Task<OperationResult> WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            lock (_written)
            {
                _written.AddRange(data.ToArray());
            }

            return Task.FromResult(OperationResult.Ok());
        }

        public async Task<OperationResult<int>> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            try
            {
                var chunk = await _output.Reader.ReadAsync(ct);
                chunk.CopyTo(buffer);

                return OperationResult<int>.Ok(chunk.Length);
            }
            catch (ChannelClosedException)
            {
                return OperationResult<int>.Ok(0);
            }
        }

        public OperationResult Resize(int columns, int rows)
        {
            Sizes.Add((columns, rows));

            return OperationResult.Ok();
        }

        public async Task<OperationResult<int>> WaitForExitAsync(CancellationToken ct = default) =>
            OperationResult<int>.Ok(await _exit.Task.WaitAsync(ct));

        public void Dispose()
        {
            Disposed = true;
            Exit(-1);
        }
    }
}
