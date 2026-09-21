namespace Loadout.Models.Agents;

/// <summary>
/// One exchange with a headless agent: the message went in, and this is
/// everything that came out up to and including its result.
/// </summary>
/// <param name="Events">Every event the agent produced during the turn, in order, the result last when there was one.</param>
/// <param name="Result">The agent's summary of the turn, or null if the agent's output ended before it produced one.</param>
/// <param name="CostUsd">
/// What this turn cost, worked out from the difference between this result's
/// running total and the previous one's. Zero when there was no result.
/// </param>
public sealed record HeadlessTurn(
    IReadOnlyList<HeadlessEvent> Events,
    HeadlessResult? Result,
    decimal CostUsd)
{
    /// <summary>Whether the agent finished the turn, as opposed to going away mid-way.</summary>
    public bool Completed => Result is not null;

    /// <summary>The text the main thread wrote to the person, joined in order. Subagents' text is left out.</summary>
    public string Text => string.Join(
        "\n",
        Events.OfType<HeadlessText>().Where(text => !text.FromSubagent).Select(text => text.Text));

    /// <summary>The structured answer, when a schema was asked for and the agent honoured it.</summary>
    public string? StructuredOutputJson => Result?.StructuredOutputJson;

    /// <summary>Lines the agent wrote that were not events. Empty on a healthy run.</summary>
    public IReadOnlyList<string> Unparsed => Events.OfType<HeadlessUnparsed>().Select(line => line.Raw).ToList();
}
