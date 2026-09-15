using Loadout.Models.Agents;

namespace Loadout.Agents.Claude;

/// <summary>Claude Code's stream-json, as the session driver sees it.</summary>
internal sealed class ClaudeHeadlessProtocol : IHeadlessProtocol
{
    /// <summary>Stateless, so one instance serves every session.</summary>
    public static ClaudeHeadlessProtocol Instance { get; } = new();

    private ClaudeHeadlessProtocol()
    {
    }

    /// <inheritdoc />
    public string EncodeUserMessage(string text) => ClaudeStreamJson.UserMessage(text);

    /// <inheritdoc />
    public IReadOnlyList<HeadlessEvent> Decode(string line) => ClaudeStreamJson.Parse(line);
}
