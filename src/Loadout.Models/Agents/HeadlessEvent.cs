namespace Loadout.Models.Agents;

/// <summary>
/// One thing an agent said while being driven without a terminal, in the
/// launcher's own vocabulary rather than any agent's.
/// </summary>
/// <remarks>
/// <para>
/// Every adapter translates its agent's event stream into these, so the code
/// that runs a team never reads an agent's JSON and never names an agent.
/// Claude Code writes one JSON object per line in its stream-json mode;
/// Codex writes a different one; a generic agent may write plain text. What
/// they have in common is here, and what they do not is kept as the raw
/// line on <see cref="Raw"/> so nothing is thrown away.
/// </para>
/// <para>
/// Nothing is dropped quietly. A line that is not an event the adapter knows
/// comes through as <see cref="HeadlessOther"/> or, if it was not even JSON,
/// <see cref="HeadlessUnparsed"/>, because a log line an agent prints to
/// stdout is a fact about the run and a parser that swallowed it would hide
/// the moment the agent's format changed.
/// </para>
/// </remarks>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
public abstract record HeadlessEvent(string Raw);

/// <summary>The agent has started and named its session.</summary>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
/// <param name="SessionId">The agent's own identifier for the conversation, the key to resuming it later.</param>
/// <param name="Model">The model the agent reports it is using, or null if it did not say.</param>
/// <param name="PermissionMode">The permission mode the agent reports it is in, or null if it did not say.</param>
/// <param name="McpServers">Names of the MCP servers the agent connected, with their reported status.</param>
public sealed record HeadlessStarted(
    string Raw,
    string SessionId,
    string? Model,
    string? PermissionMode,
    IReadOnlyList<HeadlessMcpServer> McpServers) : HeadlessEvent(Raw);

/// <summary>An MCP server as the agent reported it at start.</summary>
public sealed record HeadlessMcpServer(string Name, string Status);

/// <summary>Text the agent wrote to the person.</summary>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
/// <param name="Text">The text itself.</param>
/// <param name="FromSubagent">True when a subagent inside the session wrote it rather than the main thread.</param>
public sealed record HeadlessText(string Raw, string Text, bool FromSubagent) : HeadlessEvent(Raw);

/// <summary>The agent called a tool.</summary>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
/// <param name="Tool">The tool's name as the agent knows it.</param>
/// <param name="InputJson">The call's input as JSON, kept as text because its shape is the tool's business.</param>
/// <param name="FromSubagent">True when a subagent made the call.</param>
public sealed record HeadlessToolUse(string Raw, string Tool, string InputJson, bool FromSubagent) : HeadlessEvent(Raw);

/// <summary>A tool answered.</summary>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
/// <param name="ToolUseId">Which call this answers, in the agent's own identifiers.</param>
/// <param name="IsError">True when the tool reported failure, including a permission refusal.</param>
/// <param name="Content">What the tool returned, as text.</param>
public sealed record HeadlessToolResult(string Raw, string ToolUseId, bool IsError, string Content) : HeadlessEvent(Raw);

/// <summary>The agent was refused a tool by its own permission rules.</summary>
public sealed record HeadlessPermissionDenied(string Raw, string? Tool) : HeadlessEvent(Raw);

/// <summary>Token counts for one result, by kind, because each kind is priced differently.</summary>
public sealed record HeadlessUsage(
    long InputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long OutputTokens);

/// <summary>A tool call the agent's permission rules refused, as the result lists it.</summary>
public sealed record HeadlessDenial(string Tool, string ToolUseId, string InputJson);

/// <summary>The agent finished a turn and summed it up.</summary>
/// <param name="Raw">The line exactly as the agent wrote it.</param>
/// <param name="Subtype">The agent's own word for how it ended: success, or an error kind such as a budget being reached.</param>
/// <param name="IsError">True when the turn did not complete normally.</param>
/// <param name="Turns">How many exchanges with the model the turn took.</param>
/// <param name="Duration">Wall-clock time the turn took.</param>
/// <param name="CumulativeCostUsd">
/// What the whole session has cost so far, not this turn. Claude Code reports
/// the running total in every result, so the cost of one turn is the
/// difference between consecutive results, and anything that records cost
/// per turn has to subtract.
/// </param>
/// <param name="Usage">Token counts for this turn.</param>
/// <param name="StructuredOutputJson">The structured output the turn produced, when a schema was asked for, as JSON text.</param>
/// <param name="Denials">Tool calls the agent's permission rules refused during the turn.</param>
/// <param name="Errors">Error messages the agent attached to the result.</param>
/// <param name="SessionId">The session this result belongs to.</param>
public sealed record HeadlessResult(
    string Raw,
    string Subtype,
    bool IsError,
    int Turns,
    TimeSpan Duration,
    decimal CumulativeCostUsd,
    HeadlessUsage Usage,
    string? StructuredOutputJson,
    IReadOnlyList<HeadlessDenial> Denials,
    IReadOnlyList<string> Errors,
    string? SessionId) : HeadlessEvent(Raw);

/// <summary>An event the adapter recognised as the agent's but has no use for: a hook running, a rate limit notice, a token count.</summary>
public sealed record HeadlessOther(string Raw, string Type, string? Subtype) : HeadlessEvent(Raw);

/// <summary>A line that was not an event at all. Kept because an agent printing something unexpected is worth knowing about.</summary>
public sealed record HeadlessUnparsed(string Raw) : HeadlessEvent(Raw);
