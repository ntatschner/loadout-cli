using Loadout.Models.Agents;

namespace Loadout.Agents;

/// <summary>
/// How one agent is spoken to over its pipes: what a message from the person
/// looks like on the way in, and what an event looks like on the way out.
/// </summary>
/// <remarks>
/// The session driver is the same for every agent and knows nothing about
/// any of them. An adapter that can be driven headlessly supplies one of
/// these; one that cannot supplies null, and the launcher refuses to start
/// it as a node rather than finding out mid-run.
/// </remarks>
public interface IHeadlessProtocol
{
    /// <summary>One message from the person, as the single line the agent reads.</summary>
    string EncodeUserMessage(string text);

    /// <summary>The events one line the agent wrote carries. Never empty: an unknown line is still an event.</summary>
    IReadOnlyList<HeadlessEvent> Decode(string line);
}
