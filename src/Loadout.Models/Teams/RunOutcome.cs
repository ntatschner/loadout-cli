using System.Text.Json.Serialization;

namespace Loadout.Models.Teams;

/// <summary>
/// How a team run ended, in the few categories worth acting on.
/// </summary>
/// <remarks>
/// <para>
/// A run records its ending as a sentence, because the sentence is what
/// somebody reads: "budget spent: 4.80 of 5.00 USD" says more than any single
/// word could, and it is written to be read rather than matched. But a
/// sentence cannot be selected on, and "forget the failed ones" is what people
/// actually ask for — so the sentence now travels with one of these beside it.
/// </para>
/// <para>
/// Kept small deliberately. Every extra category is one more distinction
/// somebody deleting runs has to hold in their head, and these are the ones
/// that change what a person would do about a run.
/// </para>
/// </remarks>
public enum RunOutcome
{
    /// <summary>Still going. Nothing forgets one of these.</summary>
    [JsonStringEnumMemberName("running")] Running,

    /// <summary>The lead reported done, and accounted for what it was given.</summary>
    [JsonStringEnumMemberName("done")] Done,

    /// <summary>
    /// It did not get there: the lead failed, could not start, ended without
    /// a report, made no progress, was halted for breaking its brief, or
    /// claimed done without accounting for the goal.
    /// </summary>
    [JsonStringEnumMemberName("failed")] Failed,

    /// <summary>Somebody stopped it, or it was stopped before it began.</summary>
    [JsonStringEnumMemberName("stopped")] Stopped,

    /// <summary>It ran out of the rounds or the money it was given.</summary>
    [JsonStringEnumMemberName("limited")] Limited,

    /// <summary>The lead reported itself blocked.</summary>
    [JsonStringEnumMemberName("blocked")] Blocked,

    /// <summary>It is waiting on an answer only a person can give.</summary>
    [JsonStringEnumMemberName("needs-decision")] NeedsDecision,

    /// <summary>
    /// It recorded a finish without saying how it went.
    /// </summary>
    /// <remarks>
    /// Not the same as a run that was killed. A run whose coordinator was
    /// killed never records a finish at all, so its journal still reads as
    /// running and nothing forgets it - see RunSummary.Running, which is
    /// exactly "no finish written".
    /// </remarks>
    [JsonStringEnumMemberName("unrecorded")] Unrecorded,
}
