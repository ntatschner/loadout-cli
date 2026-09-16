namespace Loadout.Models.Teams;

/// <summary>
/// A team run somebody asked for again and again, rather than once.
/// </summary>
/// <remarks>
/// <para>
/// Machine-local, deliberately, and this is the decision worth stating. A
/// schedule in the workspace would travel to every machine that clones it, and
/// three machines waking at nine to run the same nightly sweep on the same
/// repository is not three times the work: it is one useful run and two that
/// fight it for the branch. Whoever wants a schedule wants it here.
/// </para>
/// <para>
/// It carries no schedule expression more clever than it needs. Every so
/// often, or once a day at a time, are what people actually ask for, and a
/// cron field is a language to learn, get wrong quietly, and support for ever.
/// </para>
/// </remarks>
public sealed class TeamSchedule
{
    /// <summary>Short name, unique on this machine, that the commands take.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The project the team works on.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>The team to run.</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>What to ask it for, in the person's own words.</summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>manual, supervised or autonomous.</summary>
    /// <remarks>
    /// A schedule fires when nobody is watching, so manual is refused where
    /// one is made: a run that stops at the first question, at three in the
    /// morning, has spent a session to ask something nobody will read until
    /// the morning.
    /// </remarks>
    public string Autonomy { get; set; } = "autonomous";

    /// <summary>How often, when it runs on an interval.</summary>
    public TimeSpan? Every { get; set; }

    /// <summary>The local time of day it runs at, when it runs daily.</summary>
    public TimeOnly? At { get; set; }

    /// <summary>
    /// The thing that starts it, when a clock is not what does.
    /// </summary>
    /// <remarks>
    /// One event so far: <c>commit</c>, meaning the project's repository has
    /// moved. It is here rather than in a list of its own because a person
    /// thinking "what starts runs for me" wants one list, not two, and the
    /// daemon that fires them wants one loop.
    /// </remarks>
    public string On { get; set; } = string.Empty;

    /// <summary>The commit this last saw, for an event that watches one.</summary>
    public string LastCommit { get; set; } = string.Empty;

    /// <summary>When it last started, or null when it never has.</summary>
    public DateTimeOffset? LastRun { get; set; }

    /// <summary>The run it last started, for following the trail.</summary>
    public string LastRunId { get; set; } = string.Empty;

    /// <summary>Whether it fires at all. A paused schedule keeps its history.</summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>Every schedule on this machine.</summary>
public sealed class TeamScheduleList
{
    public int SchemaVersion { get; set; } = 1;

    public List<TeamSchedule> Items { get; set; } = [];
}
