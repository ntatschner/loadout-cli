namespace Loadout.Models.Ideas;

/// <summary>
/// An idea being fleshed out: what was asked, what the agent asked back, what
/// it proposed and what the person made of each piece. Kept at
/// <c>ideas/&lt;id&gt;.yaml</c> beside the task list the idea is on.
/// </summary>
/// <remarks>
/// <para>
/// The idea itself is a task of kind idea, so it is listed wherever tasks are.
/// This is the working record behind it, and it is the whole of the state: each
/// round of refinement is a fresh agent given everything here, never a session
/// resumed. A session lives on one machine and this file travels with the
/// workspace, so an idea begun on one machine can be answered on another.
/// </para>
/// <para>
/// Where it stands is worked out from what is here rather than stored beside
/// it, so the two can never disagree. See <see cref="IdeaStage"/>.
/// </para>
/// </remarks>
public sealed class IdeaRecord
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The task id this record belongs to.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What the person asked for, verbatim. Never rewritten.</summary>
    public string Ask { get; set; } = string.Empty;

    /// <summary>When it was dropped in.</summary>
    public DateTimeOffset CapturedUtc { get; set; }

    /// <summary>Every round of questions, oldest first.</summary>
    public List<IdeaRound> Rounds { get; set; } = [];

    /// <summary>The latest proposal, or null before there is one.</summary>
    public IdeaPlan? Plan { get; set; }

    /// <summary>Something the person wants changed about the whole plan, for the next round.</summary>
    public string Request { get; set; } = string.Empty;

    /// <summary>Where it went when it was accepted, or null while it is still an idea.</summary>
    public IdeaAcceptance? Accepted { get; set; }

    /// <summary>What the agent made of the last round it was given, when that round went wrong.</summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>One round of clarifying questions.</summary>
public sealed class IdeaRound
{
    public DateTimeOffset AskedUtc { get; set; }

    /// <summary>
    /// The plan revision these were asked with or after, or 0 when there was no
    /// plan yet. Answers to questions asked alongside a plan are something the
    /// plan has not seen, which is what makes the next round worth running.
    /// </summary>
    public int Revision { get; set; }

    public List<IdeaQuestion> Questions { get; set; } = [];
}

/// <summary>A question the agent needs answered before it can plan.</summary>
public sealed class IdeaQuestion
{
    /// <summary><c>Q1</c>, <c>Q2</c> and on, counted across every round so each is quotable.</summary>
    public string Id { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    /// <summary>Why the answer changes the plan.</summary>
    public string Why { get; set; } = string.Empty;

    /// <summary>Answers the agent thinks likely. The person may give any other.</summary>
    public List<string> Options { get; set; } = [];

    /// <summary>What the agent would pick, and it may be empty.</summary>
    public string Recommendation { get; set; } = string.Empty;

    /// <summary>The person's answer, or empty while it is waiting for one.</summary>
    public string Answer { get; set; } = string.Empty;
}

/// <summary>What the agent proposes, and what the person has made of each piece.</summary>
public sealed class IdeaPlan
{
    /// <summary>Which proposal this is: one for the first, counting up with each revision.</summary>
    public int Revision { get; set; }

    public DateTimeOffset ProposedUtc { get; set; }

    /// <summary>A short name for the work, fit to be a task's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The agent's understanding of what is being asked for, in its own words.</summary>
    public string Understanding { get; set; } = string.Empty;

    /// <summary>Each architectural or technical layer, with the options for it.</summary>
    public List<IdeaLayer> Layers { get; set; } = [];

    /// <summary>What the agent recommends adding that was not asked for.</summary>
    public List<IdeaAddition> Additions { get; set; } = [];

    /// <summary>Where the agent thinks this belongs.</summary>
    public IdeaPlacement Project { get; set; } = new();
}

/// <summary>What the person has said about a piece of the plan.</summary>
public enum IdeaVerdict
{
    /// <summary>Nothing yet.</summary>
    Undecided,

    /// <summary>In the plan.</summary>
    Keep,

    /// <summary>Out of the plan.</summary>
    Drop,

    /// <summary>To be reworked in the next round, as the request says.</summary>
    Improve,
}

/// <summary>One layer of the design: storage, interface, deployment and so on.</summary>
public sealed class IdeaLayer
{
    /// <summary><c>L1</c>, <c>L2</c> and on, within one revision.</summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>What this layer has to do for the idea.</summary>
    public string Purpose { get; set; } = string.Empty;

    public List<IdeaOption> Options { get; set; } = [];

    /// <summary>The option chosen, by id: the recommended one until the person picks another.</summary>
    public string Chosen { get; set; } = string.Empty;

    public IdeaVerdict Verdict { get; set; } = IdeaVerdict.Undecided;

    /// <summary>What to change, when the verdict is improve.</summary>
    public string Request { get; set; } = string.Empty;
}

/// <summary>One way of doing a layer.</summary>
public sealed class IdeaOption
{
    /// <summary>The layer's id and a letter: <c>L1a</c>, <c>L1b</c>.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Detail { get; set; } = string.Empty;

    public List<string> Pros { get; set; } = [];

    public List<string> Cons { get; set; } = [];

    public bool Recommended { get; set; }
}

/// <summary>Something the agent suggests the plan should include that was not asked for.</summary>
public sealed class IdeaAddition
{
    /// <summary><c>A1</c>, <c>A2</c> and on, within one revision.</summary>
    public string Id { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Why { get; set; } = string.Empty;

    public IdeaVerdict Verdict { get; set; } = IdeaVerdict.Undecided;

    public string Request { get; set; } = string.Empty;
}

/// <summary>Where the agent thinks the idea belongs.</summary>
public sealed class IdeaPlacement
{
    /// <summary>A registered project's slug, or empty.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>True when the agent thinks this is a project of its own.</summary>
    public bool IsNew { get; set; }

    /// <summary>A name for the new project, when it thinks it is one.</summary>
    public string Name { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;
}

/// <summary>Where an accepted idea went.</summary>
public sealed class IdeaAcceptance
{
    public DateTimeOffset AcceptedUtc { get; set; }

    public string AcceptedBy { get; set; } = string.Empty;

    /// <summary>The project the task is now on.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The plan, relative to the workspace.</summary>
    public string PlanPath { get; set; } = string.Empty;
}

/// <summary>Where an idea stands, worked out from its record.</summary>
public enum IdeaStage
{
    /// <summary>Nothing has been asked of the agent yet.</summary>
    Captured,

    /// <summary>Questions are waiting on the person.</summary>
    Answering,

    /// <summary>The person has said something the agent has not seen yet: the next round can run.</summary>
    Ready,

    /// <summary>A plan is there and the person has asked for nothing more.</summary>
    Proposed,

    /// <summary>Turned into a task on a project.</summary>
    Accepted,
}
