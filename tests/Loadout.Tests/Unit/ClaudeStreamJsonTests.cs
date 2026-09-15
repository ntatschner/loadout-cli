using FluentAssertions;
using Loadout.Agents.Claude;
using Loadout.Models.Agents;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading what Claude Code writes in stream-json mode, and writing what it
/// reads.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures are the shapes a real run produced on 14 and 15 September
/// 2026 with Claude Code 2.1.270, reduced to the keys the launcher reads plus
/// enough of the rest to prove they are ignored rather than fatal. A parser
/// that only ever saw tidy examples would meet the real thing and stop at the
/// first key it did not expect.
/// </para>
/// <para>
/// Two facts about the format that cost real money to learn are pinned here:
/// the cost in a result is the session's running total, not the turn's, and
/// a structured output arrives as a tool call whose answer lands in the
/// result. Get either wrong and a run reports the wrong spend or the wrong
/// report.
/// </para>
/// </remarks>
public sealed class ClaudeStreamJsonTests
{
    private const string Init = """
        {"type":"system","subtype":"init","cwd":"C:\\work","session_id":"cb9e3890-4af6-479a-83d9-9c0a054d1507","tools":["Bash","Read"],"mcp_servers":[{"name":"perm","status":"connected"},{"name":"claude.ai n8n","status":"needs-auth"}],"model":"claude-haiku-4-5-20251001","permissionMode":"default","plugins":[],"apiKeySource":"none","uuid":"u1"}
        """;

    private const string AssistantTextAndTool = """
        {"type":"assistant","message":{"id":"msg_1","role":"assistant","content":[{"type":"thinking","thinking":"","signature":"x"},{"type":"text","text":"pong"},{"type":"tool_use","id":"toolu_01","name":"Write","input":{"file_path":"C:\\work\\hello.txt","content":"hi"}}],"model":"claude-haiku-4-5-20251001"},"parent_tool_use_id":null,"session_id":"cb9e3890","uuid":"u2"}
        """;

    private const string SubagentText = """
        {"type":"assistant","message":{"role":"assistant","content":[{"type":"text","text":"from a subagent"}]},"parent_tool_use_id":"toolu_parent","session_id":"cb9e3890"}
        """;

    private const string ThinkingOnly = """
        {"type":"assistant","message":{"role":"assistant","content":[{"type":"thinking","thinking":"","signature":"x"}]},"parent_tool_use_id":null,"session_id":"cb9e3890"}
        """;

    private const string ToolResultError = """
        {"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_01ANvs","type":"tool_result","content":"spike policy denies Bash","is_error":true}]},"parent_tool_use_id":null,"session_id":"cb9e3890"}
        """;

    private const string ToolResultBlocks = """
        {"type":"user","message":{"role":"user","content":[{"tool_use_id":"toolu_02","type":"tool_result","content":[{"type":"text","text":"line one"},{"type":"text","text":"line two"}]}]},"session_id":"cb9e3890"}
        """;

    private const string PermissionDenied = """
        {"type":"system","subtype":"permission_denied","tool_name":"Bash","tool_use_id":"toolu_01QH","session_id":"cb9e3890","uuid":"u3"}
        """;

    private const string ResultSuccess = """
        {"type":"result","subtype":"success","is_error":false,"duration_ms":3183,"duration_api_ms":3918,"num_turns":3,"result":"{\"word\":\"pong\"}","structured_output":{"word":"pong"},"stop_reason":"tool_use","session_id":"cb9e3890-4af6-479a-83d9-9c0a054d1507","total_cost_usd":0.0641688,"usage":{"input_tokens":20,"cache_creation_input_tokens":29658,"cache_read_input_tokens":29568,"output_tokens":184,"output_tokens_details":{"thinking_tokens":118},"service_tier":"standard"},"modelUsage":{},"permission_denials":[],"terminal_reason":null,"fast_mode_state":"off","subagent_stats":{},"ttft_ms":900,"uuid":"u4","queued_turn_count":0,"result_index":0}
        """;

    private const string ResultBudget = """
        {"type":"result","subtype":"error_max_budget_usd","is_error":true,"duration_ms":1125,"duration_api_ms":828,"num_turns":1,"stop_reason":"end_turn","session_id":"c068e01a","total_cost_usd":0.027069399999999997,"usage":{"input_tokens":0,"cache_creation_input_tokens":0,"cache_read_input_tokens":0,"output_tokens":0},"errors":["Reached maximum budget ($0.001)"],"permission_denials":[{"tool_name":"Bash","tool_use_id":"toolu_01QH","tool_input":{"command":"echo denied-test","description":"Run echo"}}],"uuid":"u5"}
        """;

    [Fact]
    public void The_start_event_names_the_session_the_model_the_mode_and_the_servers()
    {
        var events = ClaudeStreamJson.Parse(Init);

        var started = events.Should().ContainSingle().Which.Should().BeOfType<HeadlessStarted>().Subject;
        started.SessionId.Should().Be("cb9e3890-4af6-479a-83d9-9c0a054d1507");
        started.Model.Should().Be("claude-haiku-4-5-20251001");
        started.PermissionMode.Should().Be("default");
        started.McpServers.Should().HaveCount(2);
        started.McpServers[1].Should().Be(new HeadlessMcpServer("claude.ai n8n", "needs-auth"));
    }

    [Fact]
    public void An_assistant_message_yields_one_event_per_block_and_skips_thinking()
    {
        var events = ClaudeStreamJson.Parse(AssistantTextAndTool);

        events.Should().HaveCount(2);
        events[0].Should().BeOfType<HeadlessText>().Which.Text.Should().Be("pong");

        var tool = events[1].Should().BeOfType<HeadlessToolUse>().Subject;
        tool.Tool.Should().Be("Write");
        tool.InputJson.Should().Contain("hello.txt");
        tool.FromSubagent.Should().BeFalse();
    }

    [Fact]
    public void A_parent_tool_use_id_marks_the_event_as_a_subagents()
    {
        ClaudeStreamJson.Parse(SubagentText).Single()
            .Should().BeOfType<HeadlessText>().Which.FromSubagent.Should().BeTrue();
    }

    [Fact]
    public void A_message_with_only_thinking_is_still_an_event_rather_than_nothing()
    {
        // Nothing is dropped quietly: the run can count it, even if it has
        // nothing to say about it.
        ClaudeStreamJson.Parse(ThinkingOnly).Single()
            .Should().BeOfType<HeadlessOther>().Which.Subtype.Should().Be("thinking");
    }

    [Fact]
    public void A_refused_tool_comes_back_as_an_error_result_with_the_message()
    {
        var result = ClaudeStreamJson.Parse(ToolResultError).Single()
            .Should().BeOfType<HeadlessToolResult>().Subject;

        result.IsError.Should().BeTrue();
        result.ToolUseId.Should().Be("toolu_01ANvs");
        result.Content.Should().Be("spike policy denies Bash");
    }

    [Fact]
    public void A_tool_result_made_of_blocks_is_read_as_their_text()
    {
        ClaudeStreamJson.Parse(ToolResultBlocks).Single()
            .Should().BeOfType<HeadlessToolResult>().Which.Content.Should().Be("line one\nline two");
    }

    [Fact]
    public void The_permission_denied_event_names_the_tool()
    {
        ClaudeStreamJson.Parse(PermissionDenied).Single()
            .Should().BeOfType<HeadlessPermissionDenied>().Which.Tool.Should().Be("Bash");
    }

    [Fact]
    public void A_successful_result_carries_the_running_cost_the_usage_and_the_structured_output()
    {
        var result = ClaudeStreamJson.Parse(ResultSuccess).Single()
            .Should().BeOfType<HeadlessResult>().Subject;

        result.Subtype.Should().Be("success");
        result.IsError.Should().BeFalse();
        result.Turns.Should().Be(3);
        result.Duration.Should().Be(TimeSpan.FromMilliseconds(3183));

        // Parsed from the number's text: through a double this would not be
        // the figure the agent wrote.
        result.CumulativeCostUsd.Should().Be(0.0641688m);

        result.Usage.Should().Be(new HeadlessUsage(20, 29658, 29568, 184));
        result.StructuredOutputJson.Should().Be("""{"word":"pong"}""");
        result.Denials.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
        result.SessionId.Should().Be("cb9e3890-4af6-479a-83d9-9c0a054d1507");
    }

    [Fact]
    public void A_budget_result_is_an_error_with_its_reason_and_its_denials()
    {
        var result = ClaudeStreamJson.Parse(ResultBudget).Single()
            .Should().BeOfType<HeadlessResult>().Subject;

        result.Subtype.Should().Be("error_max_budget_usd");
        result.IsError.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Should().Be("Reached maximum budget ($0.001)");
        result.StructuredOutputJson.Should().BeNull();

        var denial = result.Denials.Should().ContainSingle().Subject;
        denial.Tool.Should().Be("Bash");
        denial.InputJson.Should().Contain("echo denied-test");
    }

    [Theory]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{}}""", "rate_limit_event", null)]
    [InlineData("""{"type":"system","subtype":"hook_started","hook_name":"x"}""", "system", "hook_started")]
    [InlineData("""{"type":"system","subtype":"thinking_tokens","count":12}""", "system", "thinking_tokens")]
    public void An_event_the_launcher_has_no_use_for_is_kept_with_its_type(string line, string type, string? subtype)
    {
        var other = ClaudeStreamJson.Parse(line).Single().Should().BeOfType<HeadlessOther>().Subject;

        other.Type.Should().Be(type);
        other.Subtype.Should().Be(subtype);
        other.Raw.Should().Be(line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not logged in · Please run /login")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    public void A_line_that_is_not_an_event_is_kept_rather_than_thrown_away(string line)
    {
        ClaudeStreamJson.Parse(line).Single()
            .Should().BeOfType<HeadlessUnparsed>().Which.Raw.Should().Be(line);
    }

    [Fact]
    public void A_user_message_is_one_line_however_many_the_text_has()
    {
        var line = ClaudeStreamJson.UserMessage("first line\nsecond line\r\n```\ncode\n```");

        line.Should().NotContain("\n");
        line.Should().StartWith("""{"type":"user","message":{"role":"user","content":""");
        line.Should().Contain("first line\\nsecond line");
    }
}
