namespace Loadout.Models.Delivery;

/// <summary>Where a branch stands on its way into the repository.</summary>
public enum LandingState
{
    /// <summary>Kept on this machine with no pull request; one can be opened for it.</summary>
    Pending,

    /// <summary>Its pull request is open as a draft.</summary>
    Draft,

    /// <summary>Its pull request is open and ready for review.</summary>
    Ready,

    /// <summary>Its pull request has been merged.</summary>
    Merged,

    /// <summary>A person decided it is not going in.</summary>
    Abandoned,
}

/// <summary>
/// One branch of work on its way into the repository: where it came from, and
/// how far it has got.
/// </summary>
/// <remarks>
/// Kept apart from the run that made it, because a run's journal can be
/// forgotten and its queue record trimmed while the branch is still waiting
/// for somebody to decide what to do with it.
/// </remarks>
public sealed class BranchLanding
{
    /// <summary>The branch. One landing per branch in a project.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>The team whose run made it, or empty when no team did.</summary>
    public string Team { get; set; } = string.Empty;

    /// <summary>The run that made it, or empty when no run did.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>
    /// The node whose branch it is, or empty for a run's own branch, which is
    /// where the run's merged work ends up and so what it delivers.
    /// </summary>
    public string Node { get; set; } = string.Empty;

    /// <summary>The task it was done for, or empty.</summary>
    public string Task { get; set; } = string.Empty;

    public LandingState State { get; set; } = LandingState.Pending;

    /// <summary>Its pull request's address, once it has one.</summary>
    public string PullRequest { get; set; } = string.Empty;

    /// <summary>Why it stands where it does, in a line.</summary>
    public string Why { get; set; } = string.Empty;

    /// <summary>
    /// When a person asked for its pull request to be squash-merged once its
    /// checks pass, or null when nobody has. It stays ready until the merge
    /// is seen, since the host does the merging, not Loadout.
    /// </summary>
    public DateTimeOffset? MergeRequestedUtc { get; set; }

    /// <summary>When its pull request was last looked for among the merged, or null when it never has been.</summary>
    public DateTimeOffset? MergeChecked { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }
}

/// <summary>A project's landings, as kept on this machine.</summary>
public sealed class ProjectLandings
{
    public List<BranchLanding> Landings { get; set; } = [];
}
