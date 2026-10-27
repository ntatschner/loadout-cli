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

    /// <summary>
    /// What the person has said about the idea since dropping it in, oldest
    /// first, kept verbatim like <see cref="Ask"/>. Not the plan's additions,
    /// which are the agent's.
    /// </summary>
    public List<IdeaNote> Notes { get; set; } = [];

    /// <summary>
    /// The highest note number ever given, kept so a removed note's number is
    /// never given again: notes are removed by number, and a number reused
    /// while another screen still shows the old note would remove the wrong one.
    /// </summary>
    public int NotesNumbered { get; set; }

    /// <summary>Every round of questions, oldest first.</summary>
    public List<IdeaRound> Rounds { get; set; } = [];

    /// <summary>The latest proposal, or null before there is one.</summary>
    public IdeaPlan? Plan { get; set; }

    /// <summary>Something the person wants changed about the whole plan, for the next round.</summary>
    public string Request { get; set; } = string.Empty;

    /// <summary>Where it went when it was accepted, or null while it is still an idea.</summary>
    public IdeaAcceptance? Accepted { get; set; }

    /// <summary>
    /// The agent and model of the last round that came back with an answer,
    /// or null before there has been one. The next round asks the same,
    /// unless told otherwise.
    /// </summary>
    public IdeaAttempt? LastRound { get; set; }

    /// <summary>
    /// The latest round that went wrong, or null when the last one did not.
    /// Only the latest is kept, and the next round that comes back clears it,
    /// as does dismissing it.
    /// </summary>
    public IdeaAttempt? Failure { get; set; }

    /// <summary>
    /// What went wrong, as launchers before the agent and model were recorded
    /// wrote it. Read as a failure with neither named, and cleared whenever
    /// <see cref="Failure"/> is written.
    /// </summary>
    public string LastError { get; set; } = string.Empty;
}

/// <summary>Something the person added to an idea after dropping it in.</summary>
public sealed class IdeaNote
{
    /// <summary><c>N1</c>, <c>N2</c> and on, counted over the whole list and never reused.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>What they wrote, trimmed and otherwise verbatim.</summary>
    public string Text { get; set; } = string.Empty;

    public string By { get; set; } = string.Empty;

    public DateTimeOffset AddedUtc { get; set; }

    /// <summary>
    /// When a round that was shown it came back, or null while no round has
    /// read it. An unread note is something the plan has not taken in.
    /// </summary>
    public DateTimeOffset? TakenUtc { get; set; }
}

/// <summary>A round of refinement, as it was asked for and how it ended.</summary>
public sealed class IdeaAttempt
{
    public DateTimeOffset Utc { get; set; }

    /// <summary>The agent asked, by its configured name, or empty when that is not known.</summary>
    public string Agent { get; set; } = string.Empty;

    /// <summary>The model asked for, or empty for the agent's own default.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>What went wrong, or empty when nothing did.</summary>
    public string Error { get; set; } = string.Empty;
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

    /// <summary>
    /// What the agent took the idea to be when it asked these, kept only for a
    /// round that asked and proposed nothing: a plan carries its own, and a
    /// round with none would leave nothing to read the questions against later.
    /// </summary>
    public string Understanding { get; set; } = string.Empty;

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

    /// <summary>
    /// What the question is about, by id: <c>ask</c>, a note such as <c>N2</c>,
    /// a layer or option such as <c>L1</c> or <c>L1b</c>, an addition such as
    /// <c>A2</c> or an earlier question such as <c>Q3</c>. Empty when it is
    /// about nothing in particular, and in records written before idea/3.
    /// </summary>
    public string RelatesTo { get; set; } = string.Empty;

    /// <summary>One line saying what the question is in aid of, so it can be answered without the history.</summary>
    public string Context { get; set; } = string.Empty;

    /// <summary>
    /// What <see cref="RelatesTo"/> was when the question was asked or last
    /// answered, so a later change to it can be noticed. Null when it relates
    /// to nothing.
    /// </summary>
    public IdeaSnapshot? Snapshot { get; set; }
}

/// <summary>What a question's subject was when it was asked or last answered.</summary>
/// <remarks>
/// A layer or addition is kept by name as well as id, because ids are
/// positions within one revision and a later revision is free to reorder: a
/// layer found by name and unchanged is the same layer, wherever it now sits.
/// </remarks>
public sealed class IdeaSnapshot
{
    /// <summary>A layer's name or an addition's title; empty for the ask, a note or a question.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>An option's title, when the subject is one option of a layer.</summary>
    public string Option { get; set; } = string.Empty;

    /// <summary>Its text then: a note's words, a layer's purpose and options, a question and its answer.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The plan revision it was taken from, or 0 for something that is not in a plan.</summary>
    public int Revision { get; set; }

    /// <summary>
    /// True when the subject was already gone when this was taken: answered
    /// after a note was removed, say. Still gone later is not a change.
    /// </summary>
    public bool Missing { get; set; }
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

    /// <summary>
    /// What must be true for the work to be finished, one condition each: what
    /// the agent proposed, or, once the idea is accepted, what the person confirmed.
    /// </summary>
    /// <remarks>
    /// Carried onto the task at acceptance, so the task arrives with its own
    /// finish line. A team's queue takes only tasks that have one, and falling
    /// back to the project's standing criteria instead would let a vague idea
    /// through held to nothing more specific than "the suite passes".
    /// </remarks>
    public List<string> DoneWhen { get; set; } = [];
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

    /// <summary>
    /// The person has said something the agent has not seen yet, an answer, a
    /// request or a note: the next round can run.
    /// </summary>
    Ready,

    /// <summary>A plan is there and the person has asked for nothing more.</summary>
    Proposed,

    /// <summary>Turned into a task on a project.</summary>
    Accepted,
}
