using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Models.Agents;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A conversation with a headless agent, over a scripted pipe.
/// </summary>
/// <remarks>
/// The pipe is a stub carrying the lines a real Claude Code session wrote,
/// so the tests cover the driver's reading of them and nothing about whether
/// an agent is installed. What is pinned: a turn ends at the result and not
/// before; two turns over one pipe are two messages written and two results
/// read; the cost of a turn is the step in the running total; a stream that
/// ends without a result is reported as such rather than as a turn.
/// </remarks>
public sealed class HeadlessSessionTests
{
    private const string Init = """{"type":"system","subtype":"init","session_id":"sess-1","model":"m","mcp_servers":[]}""";
    private const string Pong = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"pong"}]},"parent_tool_use_id":null}""";
    private const string Ping = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"ping"}]},"parent_tool_use_id":null}""";
    private const string Aside = """{"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"aside"}]},"parent_tool_use_id":"toolu_sub"}""";
    private const string ResultOne = """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"duration_ms":10,"total_cost_usd":0.06,"usage":{},"session_id":"sess-1","structured_output":{"word":"pong"}}""";
    private const string ResultTwo = """{"type":"result","subtype":"success","is_error":false,"num_turns":1,"duration_ms":10,"total_cost_usd":0.10,"usage":{},"session_id":"sess-1"}""";

    private static (HeadlessSession Session, StubProcessLauncher.StubPipedProcess Pipe) Open(params string[] lines)
    {
        var pipe = new StubProcessLauncher.StubPipedProcess(string.Join("\n", lines) + "\n", 0);

        return (new HeadlessSession(pipe, ClaudeHeadlessProtocol.Instance), pipe);
    }

    [Fact]
    public async Task A_turn_reads_up_to_its_result_and_stops_there()
    {
        var (session, pipe) = Open(Init, Pong, ResultOne, Ping, ResultTwo);
        await using var _ = session;

        var turn = await session.TurnAsync("say pong");

        turn.Completed.Should().BeTrue();
        turn.Text.Should().Be("pong");
        turn.StructuredOutputJson.Should().Be("""{"word":"pong"}""");
        turn.Events.Should().HaveCount(3, "init, the text and the result, and nothing from the next turn");
        turn.Events[^1].Should().BeOfType<HeadlessResult>();
        session.SessionId.Should().Be("sess-1");

        pipe.Written.ToString().Should().StartWith("""{"type":"user","message":{"role":"user","content":"say pong"}}""");
    }

    [Fact]
    public async Task Two_turns_over_one_pipe_cost_the_step_in_the_running_total()
    {
        var (session, pipe) = Open(Init, Pong, ResultOne, Ping, ResultTwo);
        await using var _ = session;

        var first = await session.TurnAsync("say pong");
        var second = await session.TurnAsync("say ping");

        first.CostUsd.Should().Be(0.06m);
        second.CostUsd.Should().Be(0.04m, "the agent reports 0.10 as the running total, not the turn");
        second.Text.Should().Be("ping");

        pipe.Written.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }

    [Fact]
    public async Task A_stream_that_ends_before_a_result_is_not_a_completed_turn()
    {
        var (session, _) = Open(Init, Pong);
        await using var __ = session;

        var turn = await session.TurnAsync("say pong");

        turn.Completed.Should().BeFalse();
        turn.Result.Should().BeNull();
        turn.CostUsd.Should().Be(0m);
        turn.Text.Should().Be("pong", "what did arrive is still handed over");
    }

    [Fact]
    public async Task Text_from_a_subagent_is_kept_as_an_event_and_left_out_of_the_reply()
    {
        var (session, _) = Open(Init, Aside, Pong, ResultOne);
        await using var __ = session;

        var turn = await session.TurnAsync("go");

        turn.Text.Should().Be("pong");
        turn.Events.OfType<HeadlessText>().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_line_that_is_not_an_event_is_reported_with_the_turn()
    {
        var (session, _) = Open(Init, "Not logged in · Please run /login", Pong, ResultOne);
        await using var __ = session;

        var turn = await session.TurnAsync("go");

        turn.Unparsed.Should().ContainSingle().Which.Should().Be("Not logged in · Please run /login");
    }

    [Fact]
    public async Task Ending_the_session_closes_the_input_and_reports_the_exit()
    {
        var (session, pipe) = Open(Init, Pong, ResultOne);
        await using var _ = session;

        await session.TurnAsync("go");
        var (code, killed) = await session.EndAsync(TimeSpan.FromSeconds(5));

        pipe.InputClosed.Should().BeTrue();
        code.Should().Be(0);
        killed.Should().BeFalse();
        pipe.Killed.Should().BeFalse();
    }
}
