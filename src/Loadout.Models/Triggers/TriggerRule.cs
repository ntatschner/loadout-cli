namespace Loadout.Models.Triggers;

/// <summary>
/// Something to do when something happens: an event to wait for, which of
/// them count, and what to start.
/// </summary>
/// <remarks>
/// <para>
/// Machine-local, for the reason a schedule is: three machines watching the
/// same repository and each starting a run on the same event is one useful run
/// and two that fight it for the branch.
/// </para>
/// <para>
/// Conditions are declared, never judged. A rule says which fields of an
/// event must hold which values, so whether it matched can be written in the
/// journal as a reason a person can check, and a rule a team suggests can be
/// read before it is accepted.
/// </para>
/// </remarks>
public sealed class TriggerRule
{
    /// <summary>Short name, unique on this machine, that the commands take.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The kind of event it waits for, such as <c>run-finished</c>.</summary>
    public string On { get; set; } = string.Empty;

    /// <summary>The projects whose events count, or empty for every project.</summary>
    public List<string> Projects { get; set; } = [];

    /// <summary>For a clock rule: how often it comes round.</summary>
    public TimeSpan? Every { get; set; }

    /// <summary>For a clock rule: the local time of day it comes round, once a day.</summary>
    /// <remarks>
    /// The two clock fields are a schedule's own, read the same way, so a
    /// schedule moved onto a rule keeps the times it had.
    /// </remarks>
    public TimeOnly? At { get; set; }

    /// <summary>
    /// Fields of the event that must hold a given value, compared without
    /// regard to case. Empty matches every event of the kind.
    /// </summary>
    public Dictionary<string, string> Where { get; set; } = [];

    /// <summary>What it starts.</summary>
    public TriggerAction Action { get; set; } = new();

    /// <summary>What each run it starts may spend, in dollars, or null for the team's own figure.</summary>
    public decimal? BudgetUsd { get; set; }

    /// <summary>The most its runs may spend inside <see cref="Window"/>, in dollars.</summary>
    /// <remarks>
    /// Required on a rule that starts anything with a model behind it, and
    /// checked when the rule is loaded however it was written: a rule with no
    /// ceiling is a loop with nothing to stop it.
    /// </remarks>
    public decimal? CapUsd { get; set; }

    /// <summary>The most runs it may start inside <see cref="Window"/>.</summary>
    /// <remarks>A run count catches a loop cheaply, before its spend adds up.</remarks>
    public int CapRuns { get; set; } = DefaultCapRuns;

    /// <summary>The rolling period both caps count over.</summary>
    public TimeSpan Window { get; set; } = DefaultWindow;

    /// <summary>
    /// How long to wait for the same subject to go quiet before acting, so a
    /// burst of events becomes one action on the latest of them.
    /// </summary>
    public TimeSpan Debounce { get; set; } = TimeSpan.Zero;

    /// <summary>Whether it acts at all. A disabled rule keeps its history.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Counts each saved change, so every decision in the journal names the
    /// version of the rule that made it.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>The team whose suggestion this was accepted from, or empty for a rule written here.</summary>
    public string SuggestedBy { get; set; } = string.Empty;

    /// <summary>
    /// Which of that team's suggestions, and a fingerprint of it as it was
    /// accepted, so a change to the suggestion is noticed rather than taken.
    /// </summary>
    public string Suggestion { get; set; } = string.Empty;

    /// <summary>Runs in a window where nothing is set.</summary>
    public const int DefaultCapRuns = 10;

    /// <summary>The window where nothing is set: the same 24 hours the rest of the design counts in.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromHours(24);
}

/// <summary>What a rule starts.</summary>
public sealed class TriggerAction
{
    /// <summary>One of <see cref="TriggerActionKinds.All"/>.</summary>
    public string Kind { get; set; } = TriggerActionKinds.Team;

    /// <summary>The team, the built-in command, or the remedy, by name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>For a team: what to ask it for, in the person's own words.</summary>
    public string Goal { get; set; } = string.Empty;

    /// <summary>For a team: supervised or autonomous. Never manual, because nobody is watching.</summary>
    public string Autonomy { get; set; } = "autonomous";
}

/// <summary>The kinds of thing a rule may start.</summary>
public static class TriggerActionKinds
{
    /// <summary>A team run.</summary>
    public const string Team = "team";

    /// <summary>A built-in Loadout command.</summary>
    public const string Builtin = "builtin";

    /// <summary>A remedy, under the trust key it already has.</summary>
    public const string Remedy = "remedy";

    public static readonly IReadOnlyList<string> All = [Team, Builtin, Remedy];
}

/// <summary>Every rule on this machine.</summary>
public sealed class TriggerRuleList
{
    public int SchemaVersion { get; set; } = 1;

    public List<TriggerRule> Items { get; set; } = [];
}
