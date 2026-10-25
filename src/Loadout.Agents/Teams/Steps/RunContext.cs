using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// One run's own state, made once the run knows who is watching it and handed
/// to whatever needs it, rather than kept on the runner between runs.
/// </summary>
internal sealed class RunContext
{
    public RunContext(bool asking, string? onTimeout, List<string> warnings)
    {
        Asking = asking;
        OnTimeout = onTimeout;
        Warnings = warnings;
    }

    /// <summary>
    /// Whether this run may put a node's unmatched call to the person.
    /// </summary>
    /// <remarks>
    /// Read when each node's policy is written. A fact about the run rather
    /// than about any one node.
    /// </remarks>
    public bool Asking { get; }

    /// <summary>What a timed question does when nobody answers, for this run.</summary>
    public string? OnTimeout { get; }

    /// <summary>
    /// Which questions the timer has already sent back to the lead, by
    /// <c>DecideStep.KeyOf</c>, so think-again-once sends each back only once.
    /// </summary>
    public HashSet<string> SentBackByTimer { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Everything the run has to tell the person at the end. The same list the
    /// run gathered before the context was made, so nothing said early is lost.
    /// </summary>
    public List<string> Warnings { get; }

    /// <summary>
    /// The questions a run being picked up again had left open, closed before
    /// anything watched for them, so the reopening can say it closed them.
    /// </summary>
    public IReadOnlyList<Core.Teams.PendingAsk> Retired { get; set; } = [];

    /// <summary>
    /// The questions a run being picked up again closed itself as it ended,
    /// which the lead is told of alongside <see cref="Retired"/>.
    /// </summary>
    public IReadOnlyList<Core.Teams.PendingAsk> ClosedAtEnd { get; set; } = [];

    /// <summary>What the run has spent so far, in USD.</summary>
    public decimal Cost { get; set; }

    /// <summary>How many rounds the lead has taken.</summary>
    public int Rounds { get; set; }

    /// <summary>Rounds in a row with neither a request nor a finish.</summary>
    public int QuietRounds { get; set; }

    /// <summary>Why the run ended, once it has.</summary>
    public string Ended { get; set; } = string.Empty;

    /// <summary>The budget the run was last held to.</summary>
    public UsdCap HeldTo { get; set; }

    /// <summary>The lead's last report, kept as the run's final one.</summary>
    public Report? Final { get; set; }

    /// <summary>What the run put on branches of its own, by node.</summary>
    public Dictionary<string, string> Branches { get; set; } = new(StringComparer.Ordinal);

    /// <summary>What the team's merge gate nodes decided, by node.</summary>
    public Dictionary<string, string> Decisions { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Whether the lead has already been told about an unconsulted gate.</summary>
    public bool AskedAboutTheGate { get; set; }

    /// <summary>Whether the lead has been asked to propose criteria.</summary>
    public bool Proposed { get; set; }

    /// <summary>What was agreed this round, for the feedback the lead reads next.</summary>
    public IReadOnlyList<string>? AgreedThisRound { get; set; }

    /// <summary>How the lead last said it reads each criterion.</summary>
    public Dictionary<string, string>? LastReadings { get; set; }

    /// <summary>Whether somebody has let workers start on the lead's readings.</summary>
    public bool ReadingsAccepted { get; set; }

    /// <summary>What the lead is told on its next turn.</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>What was asked for: the team, the goal and how to run it.</summary>
    public required TeamRunRequest Request { get; init; }

    /// <summary>The team being run.</summary>
    public required TeamDefinition Team { get; init; }

    /// <summary>Whoever is watching, through one voice.</summary>
    public required ITeamConsole Console { get; init; }

    /// <summary>manual, supervised or autonomous.</summary>
    public required string Autonomy { get; init; }

    /// <summary>The run's own id.</summary>
    public required string RunId { get; init; }

    /// <summary>Where the run keeps everything it writes.</summary>
    public required string Directory { get; init; }

    /// <summary>Where the team keeps what it makes, across runs.</summary>
    public required string TeamDirectory { get; init; }

    /// <summary>The team's lead node.</summary>
    public required TeamNode LeadNode { get; init; }

    /// <summary>The lead's role.</summary>
    public required Loadout.Models.Instructions.SpecialistDocument LeadRole { get; init; }

    /// <summary>The project's criteria the run is held to, frozen at its start.</summary>
    public required IReadOnlyList<string> Standing { get; init; }

    /// <summary>What a picked-up run is picked up from, or null for a new one.</summary>
    public required Core.Teams.RunResumption? Resuming { get; init; }

    /// <summary>The lead's brief, which gains the criteria once they are agreed.</summary>
    public required Brief LeadBrief { get; set; }

    /// <summary>What the run is judged on, once anybody has said.</summary>
    public required IReadOnlyList<string>? Criteria { get; set; }

    /// <summary>The project's slug, once resolved.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>The task the run was started for, if any.</summary>
    public string? ForTask { get; set; }

    /// <summary>
    /// This process's hold on the run, or null where nothing is kept. Owned
    /// by the runner, which lets it go; a step only touches it.
    /// </summary>
    public IDisposable? Owner { get; set; }

    /// <summary>The lead's session, once it has started.</summary>
    public HeadlessLaunch? Lead { get; set; }
}
