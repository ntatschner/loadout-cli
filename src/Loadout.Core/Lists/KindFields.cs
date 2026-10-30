using Loadout.Core.Ideas;
using Loadout.Core.Teams;
using Loadout.Models.Projects;
using AgentSession = Loadout.Core.Sessions.AgentSession;

namespace Loadout.Core.Lists;

/// <summary>What an idea list can be filtered and sorted on.</summary>
public static class IdeaFields
{
    /// <summary>The stages, in the order an idea goes through them.</summary>
    public static readonly IReadOnlyList<string> Stages = ["captured", "answering", "ready", "proposed", "accepted"];

    public static readonly ListFields<IdeaSummary> All = new(
        "ideas",
        [
            ListField<IdeaSummary>.Text("id", "the idea's id", idea => idea.Place.Id),
            ListField<IdeaSummary>.Text("title", "what it asks for", idea => idea.Title),
            ListField<IdeaSummary>.Text("project", "the project it is on", idea => idea.Place.Project),
            ListField<IdeaSummary>.Choice("stage", "how far it has got", Stages, idea => idea.Stage.ToString().ToLowerInvariant()),
            ListField<IdeaSummary>.Choice("type", "what it is about", Tasks.TaskTypes.Names, idea => idea.Type is { } type ? Tasks.TaskTypes.Name(type) : null),
            ListField<IdeaSummary>.Text("by", "who dropped it in", idea => idea.DeclaredBy),
            ListField<IdeaSummary>.Date("updated", "when it was last recorded", idea => idea.DeclaredUtc),
            ListField<IdeaSummary>.Number("unanswered", "questions waiting on you", idea => idea.Unanswered),
        ],
        [new ListSort("updated", true)]);
}

/// <summary>What a project list can be filtered and sorted on.</summary>
public static class ProjectFields
{
    public static readonly ListFields<ProjectResolution> All = new(
        "projects",
        [
            ListField<ProjectResolution>.Text("id", "the project's slug", project => project.Entry.Slug),
            ListField<ProjectResolution>.Text("name", "what it is called", project => project.Entry.Name),
            ListField<ProjectResolution>.Text("agent", "the agent it launches", project => project.Entry.DefaultAgent),
            ListField<ProjectResolution>.Choice("where", "whether it is on this machine", ["here", "elsewhere"], project => project.IsAvailableLocally ? "here" : "elsewhere"),
            ListField<ProjectResolution>.Choice("pinned", "whether it is pinned", ["yes", "no"], project => project.Pinned ? "yes" : "no"),
            ListField<ProjectResolution>.Date("launched", "when it was last launched", project => project.LastLaunchedUtc),
            ListField<ProjectResolution>.Number("launches", "how often it has been launched", project => project.LaunchCount),
        ],

        // The order 'loadout list' has always had: pinned first, then the
        // most recently launched.
        [new ListSort("pinned", false), new ListSort("launched", true), new ListSort("name", false)]);
}

/// <summary>What a list of past agent sessions can be filtered and sorted on.</summary>
public static class SessionFields
{
    public static readonly ListFields<AgentSession> All = new(
        "sessions",
        [
            ListField<AgentSession>.Text("id", "the agent's id for the conversation", session => session.SessionId),
            ListField<AgentSession>.Text("title", "what it was about", session => session.Label),
            ListField<AgentSession>.Text("project", "the project it ran in", session => session.ProjectSlug),
            ListField<AgentSession>.Text("agent", "which agent it was", session => session.Agent),
            ListField<AgentSession>.Text("branch", "the branch it ran on", session => session.Branch),
            ListField<AgentSession>.Date("active", "when it last said anything", session => session.LastActive),
        ],
        [new ListSort("active", true)]);
}

/// <summary>What a list of team runs can be filtered and sorted on.</summary>
public static class RunFields
{
    /// <summary>Where a run stands, in one word a filter can name.</summary>
    public static readonly IReadOnlyList<string> States = ["waiting", "running", "ended"];

    /// <summary>The run's state as <see cref="States"/> names it.</summary>
    public static string StateOf(RunSummary run) =>
        run.WaitingForYou ? "waiting" : run.Running ? "running" : "ended";

    public static readonly ListFields<RunSummary> All = new(
        "runs",
        [
            ListField<RunSummary>.Text("id", "the run's id", run => run.RunId),
            ListField<RunSummary>.Text("team", "the team that ran", run => run.Team),
            ListField<RunSummary>.Text("project", "the project it ran on", run => run.Project),
            ListField<RunSummary>.Choice("state", "whether it waits on you, runs, or has ended", States, StateOf),
            ListField<RunSummary>.Text("goal", "what it was asked to do", run => run.Goal),
            ListField<RunSummary>.Text("ended", "how it ended", run => run.Ended),
            ListField<RunSummary>.Date("started", "when it started", run => run.Started),
            ListField<RunSummary>.Number("cost", "what it cost, in dollars", run => (double)run.CostUsd),
            ListField<RunSummary>.Number("rounds", "how many rounds it took", run => run.Rounds),
        ],
        [new ListSort("started", true)]);
}
