using Loadout.Models.Tasks;

namespace Loadout.Core.Projects;

/// <summary>What a launch should do about onboarding.</summary>
public enum OnboardingTurn
{
    /// <summary>Nothing is pending, or this is not a session a person started.</summary>
    None,

    /// <summary>This session onboards the project.</summary>
    Onboard,

    /// <summary>Onboarding is pending, but the session was started for something else.</summary>
    Remind,
}

/// <summary>
/// The first-session onboarding of a newly registered project, and the task
/// that records whether it happened.
/// </summary>
/// <remarks>
/// <para>
/// Recorded as a task rather than as memory. Memory holds what is true about
/// the code; "onboarding ran on 26 September" is what happened, and the task
/// record already says who declared what and when, is shared through the
/// workspace, and is shown in the launcher. Done means an agent did it; dropped
/// means a person chose to skip it.
/// </para>
/// <para>
/// Queued rather than run at registration. Registering is a moment when nobody
/// has asked to spend anything, and an agent started then would be spending on
/// the person's behalf without being asked. The first session in the project is
/// where they have.
/// </para>
/// </remarks>
public static class ProjectOnboardingTask
{
    /// <summary>The task id.</summary>
    public const string Id = "onboard-project";

    /// <summary>The task's title, which is also the task a session is given to do it.</summary>
    public const string Title = "Onboard this project";

    /// <summary>The skill that carries the procedure.</summary>
    public const string Skill = "skill.project-onboarding";

    /// <summary>
    /// The mode it runs in when none is asked for. Onboarding reads and records;
    /// it does not build features, so implement — the launcher's default — is the
    /// wrong posture for it.
    /// </summary>
    public const string Mode = "investigate";

    /// <summary>The note on the task when it is queued.</summary>
    public const string QueuedNote =
        "Registered recently, so nothing has been worked out about it yet. The next session "
        + "started without a task of its own onboards it. Skip it with "
        + "'loadout project onboard --skip'.";

    /// <summary>Whether the project is waiting to be onboarded.</summary>
    public static bool Pending(IEnumerable<TaskItem> tasks) =>
        tasks.Any(task => string.Equals(task.Id, Id, StringComparison.OrdinalIgnoreCase)
            && task.State is TaskState.Open or TaskState.Doing or TaskState.Blocked);

    /// <summary>
    /// What a launch should do, given whether onboarding is pending and what the
    /// session was started to do.
    /// </summary>
    /// <param name="pending">Whether the project is waiting to be onboarded.</param>
    /// <param name="task">The task the session was given, if any.</param>
    /// <param name="attended">
    /// Whether a person started it. A team node is somebody else's brief, and
    /// turning it into onboarding would throw that brief away.
    /// </param>
    /// <remarks>
    /// A session started for something else is reminded rather than taken over.
    /// Whoever typed "fix the upload retry" meant that; replacing it with
    /// onboarding would be the launcher deciding their session for them.
    /// </remarks>
    public static OnboardingTurn TurnFor(bool pending, string? task, bool attended)
    {
        if (!pending || !attended)
        {
            return OnboardingTurn.None;
        }

        return string.IsNullOrWhiteSpace(task)
            || string.Equals(task.Trim(), Title, StringComparison.OrdinalIgnoreCase)
            ? OnboardingTurn.Onboard
            : OnboardingTurn.Remind;
    }
}
