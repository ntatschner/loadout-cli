using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Platform.Abstractions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A headless turn given a limit on how long the agent may write nothing.
/// </summary>
/// <remarks>
/// The clock is the test's own and moves only when the timer waits, so twenty
/// minutes of silence pass in a moment and land exactly where the test says.
/// The pipe writes a line only when the test pushes one, at a time it chooses.
/// What is pinned: a turn silent past its limit comes back silent with the
/// agent left running; a line starts the timer again; a question waiting on a
/// person stops it however long they take, and it starts again at full length
/// once they have answered.
/// </remarks>
public sealed class HeadlessSessionQuietTests
{
    private const string Init = """{"type":"system","subtype":"init","session_id":"sess-1","model":"m","mcp_servers":[]}""";
    private const string Pong = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"pong"}]},"parent_tool_use_id":null}""";

    private static readonly TimeSpan Twenty = TimeSpan.FromMinutes(20);

    [Fact]
    public async Task A_turn_silent_for_longer_than_its_limit_comes_back_silent_with_the_agent_still_running()
    {
        var clock = new Clock();
        var pipe = new QuietPipe();
        pipe.Push(Init);
        await using var session = new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance);

        var turn = await session.TurnAsync("go", quiet: clock.Limit(Twenty));

        turn.Silent.Should().BeTrue();
        turn.Completed.Should().BeFalse();
        clock.Elapsed.Should().Be(Twenty, "given up on at the limit, and not a glance later");
        pipe.Killed.Should().BeFalse("what to do with a silent agent is the caller's to decide");
        session.RecentOutput.Should().Equal(Init);
    }

    [Fact]
    public async Task A_line_from_the_agent_starts_the_timer_again()
    {
        var clock = new Clock();
        var pipe = new QuietPipe();
        await using var session = new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance);

        // Fifteen minutes in, the agent says something; it then says nothing.
        clock.At(TimeSpan.FromMinutes(15), () => pipe.Push(Pong));

        var turn = await session.TurnAsync("go", quiet: clock.Limit(Twenty));

        turn.Silent.Should().BeTrue();
        turn.Text.Should().Be("pong");
        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(35));
    }

    [Fact]
    public async Task A_question_waiting_on_a_person_stops_the_timer_however_long_they_take()
    {
        var clock = new Clock();
        var pipe = new QuietPipe();
        await using var session = new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance);

        // A question open from five minutes in until an hour in: three times
        // the limit, and the turn must not be given up on while it is.
        var asking = false;
        clock.At(TimeSpan.FromMinutes(5), () => asking = true);
        clock.At(TimeSpan.FromMinutes(60), () => asking = false);

        var quiet = clock.Limit(Twenty) with { Held = () => asking };

        var turn = await session.TurnAsync("go", quiet: quiet);

        turn.Silent.Should().BeTrue();
        clock.Elapsed.Should().Be(TimeSpan.FromMinutes(80), "the full twenty minutes again from the answer");
    }

    [Fact]
    public async Task A_turn_that_finishes_inside_its_limit_is_an_ordinary_turn()
    {
        var clock = new Clock();
        var pipe = new QuietPipe();
        await using var session = new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance);

        clock.At(TimeSpan.FromMinutes(19), () =>
        {
            pipe.Push(Pong);
            pipe.Push("""{"type":"result","subtype":"success","is_error":false,"num_turns":1,"duration_ms":10,"total_cost_usd":0.06,"usage":{},"session_id":"sess-1"}""");
        });

        var turn = await session.TurnAsync("go", quiet: clock.Limit(Twenty));

        turn.Silent.Should().BeFalse();
        turn.Completed.Should().BeTrue();
        turn.Text.Should().Be("pong");
    }

    /// <summary>A clock that moves only when the timer waits on it, and runs what was due on the way.</summary>
    private sealed class Clock : TimeProvider
    {
        private static readonly DateTimeOffset Start = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
        private readonly List<(TimeSpan At, Action Then)> _due = [];

        public TimeSpan Elapsed { get; private set; }

        public override DateTimeOffset GetUtcNow() => Start + Elapsed;

        public void At(TimeSpan at, Action then) => _due.Add((at, then));

        public QuietLimit Limit(TimeSpan limit) => new(limit, this)
        {
            Glance = TimeSpan.FromMinutes(1),
            Wait = (span, _) =>
            {
                Elapsed += span;

                foreach (var one in _due.Where(one => one.At <= Elapsed).ToList())
                {
                    _due.Remove(one);
                    one.Then();
                }

                return Task.CompletedTask;
            },
        };
    }

    /// <summary>An agent that writes a line only when the test pushes one, and otherwise says nothing for ever.</summary>
    private sealed class QuietPipe : IPipedProcess
    {
        private readonly Lines _output = new();

        public void Push(string line) => _output.Push(line);

        public bool Killed { get; private set; }

        public int ProcessId => 4242;

        public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;

        public TextWriter Input { get; } = new StringWriter();

        public TextReader Output => _output;

        public TextReader Error { get; } = new StringReader(string.Empty);

        public Task<int> Exited { get; } = new TaskCompletionSource<int>().Task;

        public Task CloseInputAsync() => Task.CompletedTask;

        public void Kill() => Killed = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>
        /// Hands a waiting read its line the moment it is pushed, so a read
        /// is complete by the next time the timer looks.
        /// </summary>
        private sealed class Lines : TextReader
        {
            private readonly Queue<string> _queued = new();
            private TaskCompletionSource<string?>? _waiting;

            public void Push(string line)
            {
                if (_waiting is { } waiting)
                {
                    _waiting = null;
                    waiting.SetResult(line);

                    return;
                }

                _queued.Enqueue(line);
            }

            public override ValueTask<string?> ReadLineAsync(CancellationToken ct)
            {
                if (_queued.TryDequeue(out var line))
                {
                    return ValueTask.FromResult<string?>(line);
                }

                _waiting = new TaskCompletionSource<string?>();

                return new ValueTask<string?>(_waiting.Task);
            }
        }
    }
}
