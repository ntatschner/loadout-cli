using Loadout.Models.Projects;
using Loadout.Models.Teams;

namespace Loadout.Core.Delivery;

/// <summary>What a queued run that met its done-when does about a pull request.</summary>
public enum PullRequestMode
{
    /// <summary>Opens none; the branch is kept on this machine.</summary>
    None,

    /// <summary>Pushes the branch and opens a draft pull request.</summary>
    Draft,

    /// <summary>Pushes the branch and opens a pull request ready for review.</summary>
    Ready,
}

/// <summary>Where the pull request mode a run follows was set.</summary>
public enum PullRequestModeLevel
{
    /// <summary>Nothing set one, so none is opened.</summary>
    Default,

    /// <summary>The project's manifest.</summary>
    Project,

    /// <summary>The team's queue.</summary>
    Queue,

    /// <summary>The queued task itself.</summary>
    Item,
}

/// <summary>The pull request mode a run follows, and which level set it.</summary>
/// <param name="Mode">What it does.</param>
/// <param name="Level">Where that was set.</param>
public sealed record PullRequestModeChoice(PullRequestMode Mode, PullRequestModeLevel Level)
{
    /// <summary>Where it was set, in words: "the queued task", "the queue", "the project", or "nothing".</summary>
    public string From => Level switch
    {
        PullRequestModeLevel.Item => "the queued task",
        PullRequestModeLevel.Queue => "the queue",
        PullRequestModeLevel.Project => "the project",
        _ => "nothing",
    };
}

/// <summary>
/// Reads the pull request mode words, and works out which one a run follows.
/// </summary>
/// <remarks>
/// The narrowest level that says anything wins: the queued task, then the
/// team's queue, then the project. Nothing set means none, so no pull request
/// is opened unless a person has asked for one somewhere.
/// </remarks>
public static class PullRequestModes
{
    /// <summary>The words a mode is written as, in the order they are offered.</summary>
    public static IReadOnlyList<string> Words { get; } = ["draft", "ready", "none"];

    /// <summary>
    /// Reads a mode word. Empty or blank reads as null, meaning "not set here";
    /// anything else that is not a mode word is refused.
    /// </summary>
    public static bool TryParse(string? word, out PullRequestMode? mode)
    {
        mode = null;

        switch (word?.Trim().ToLowerInvariant())
        {
            case null or "":
                return true;
            case "draft":
                mode = PullRequestMode.Draft;
                return true;
            case "ready":
                mode = PullRequestMode.Ready;
                return true;
            case "none":
                mode = PullRequestMode.None;
                return true;
            default:
                return false;
        }
    }

    /// <summary>The word a mode is written as.</summary>
    public static string Word(PullRequestMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>The refusal for a word that is not a mode.</summary>
    public static string Refusal(string word) =>
        $"'{word}' is not a pull request mode. Give draft, ready or none.";

    /// <summary>
    /// The queue's own mode, reading the draft grant queues had before modes
    /// as <c>draft</c>. Null when the queue leaves it to the project, or holds
    /// a word that is not a mode.
    /// </summary>
    public static PullRequestMode? OfQueue(QueueSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (TryParse(settings.PrMode, out var mode) && mode is not null)
        {
            return mode;
        }

        return settings.DraftPullRequests ? PullRequestMode.Draft : null;
    }

    /// <summary>
    /// The mode a run follows, given what its queued task, its queue and its
    /// project say. A word that is not a mode is passed over, as if unset.
    /// </summary>
    /// <param name="item">The mode the queued task asked for, or empty.</param>
    /// <param name="queue">The queue's settings.</param>
    /// <param name="project">The project's delivery settings, or null when its manifest could not be read.</param>
    public static PullRequestModeChoice Resolve(string? item, QueueSettings queue, ProjectDelivery? project)
    {
        if (TryParse(item, out var own) && own is { } fromItem)
        {
            return new PullRequestModeChoice(fromItem, PullRequestModeLevel.Item);
        }

        if (OfQueue(queue) is { } fromQueue)
        {
            return new PullRequestModeChoice(fromQueue, PullRequestModeLevel.Queue);
        }

        if (TryParse(project?.PrMode, out var set) && set is { } fromProject)
        {
            return new PullRequestModeChoice(fromProject, PullRequestModeLevel.Project);
        }

        return new PullRequestModeChoice(PullRequestMode.None, PullRequestModeLevel.Default);
    }
}
