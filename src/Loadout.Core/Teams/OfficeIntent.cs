namespace Loadout.Core.Teams;

/// <summary>The light on a person's desk in the office.</summary>
public enum OfficeLamp
{
    /// <summary>Nothing to report: between turns, or waiting on another node.</summary>
    Quiet,

    /// <summary>Taking a turn.</summary>
    Working,

    /// <summary>Stopped on a question somebody has to answer.</summary>
    Waiting,

    /// <summary>Failed, and still sitting where it failed.</summary>
    Failed,

    /// <summary>Finished and gone home.</summary>
    Done,
}

/// <summary>Where a person in the office should be.</summary>
public enum OfficePlace
{
    /// <summary>At their own desk.</summary>
    Desk,

    /// <summary>Anywhere in the room: nothing is asking anything of them.</summary>
    Free,

    /// <summary>Out of the room.</summary>
    Gone,
}

/// <summary>How a person in the office should be standing or sitting.</summary>
public enum OfficePose
{
    /// <summary>Standing, or at rest.</summary>
    Idle,

    /// <summary>Seated, watching somebody else's work.</summary>
    Sit,

    /// <summary>Seated and typing.</summary>
    Type,

    /// <summary>Slumped over the desk.</summary>
    Slump,
}

/// <summary>What the office should show for one node.</summary>
/// <param name="Lamp">The light on their desk.</param>
/// <param name="Place">Where they should be.</param>
/// <param name="Pose">How they should be.</param>
/// <param name="Bubble">A mark over their head, or null for none.</param>
/// <remarks>
/// The words beside a node come from <see cref="RunSummary.Activity"/>; this is
/// the picture of the same thing, and is built from it so that the two cannot
/// disagree. It used to be worked out on the page, twice: the desks read
/// "waiting" from the activity word while the badges read it from the run's
/// questions, so a node with a question on it could be a waiting badge beside
/// a failed desk, and a node last seen working in a run that had ended kept
/// typing at its desk for ever.
/// </remarks>
public sealed record OfficeIntent(OfficeLamp Lamp, OfficePlace Place, OfficePose Pose, string? Bubble)
{
    /// <summary>What the office should show for a node of a run.</summary>
    /// <remarks>
    /// In order of what must not be hidden: a question first, because it is
    /// the one state where the next move is somebody's at the screen; then a
    /// failure; then work. Only a node nothing is asking anything of is free
    /// to be anywhere, which is what lets the office wander without lying.
    /// </remarks>
    public static OfficeIntent For(RunSummary run, RunNode node)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(node);

        var activity = run.Activity(node);

        if (activity == "waiting for you")
        {
            return new(OfficeLamp.Waiting, OfficePlace.Desk, OfficePose.Idle, "?");
        }

        // Kept at the desk after the run ends, because a failure is the one
        // thing about a finished run somebody still has to go and look at.
        if (node.State is "failed")
        {
            return new(OfficeLamp.Failed, OfficePlace.Desk, OfficePose.Slump, "!");
        }

        if (!run.Running || node.State is "done" or "ended")
        {
            return new(OfficeLamp.Done, OfficePlace.Gone, OfficePose.Idle, null);
        }

        if (node.State is "working")
        {
            return new(OfficeLamp.Working, OfficePlace.Desk, OfficePose.Type, null);
        }

        if (activity.StartsWith("waiting on ", StringComparison.Ordinal))
        {
            return new(OfficeLamp.Quiet, OfficePlace.Desk, OfficePose.Sit, null);
        }

        return new(OfficeLamp.Quiet, OfficePlace.Free, OfficePose.Idle, null);
    }
}
