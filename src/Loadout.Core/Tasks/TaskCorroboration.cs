using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>A commit, reduced to what corroboration needs of it.</summary>
/// <param name="Sha">The commit.</param>
/// <param name="WhenUtc">When it was authored.</param>
/// <param name="Subject">Its first line.</param>
public sealed record CommitSummary(string Sha, DateTimeOffset WhenUtc, string Subject);

/// <summary>Something a claim does not line up with.</summary>
/// <param name="TaskId">The task in question.</param>
/// <param name="Detail">What does not line up, said as an observation.</param>
public sealed record TaskDisagreement(string TaskId, string Detail);

/// <summary>
/// Checks what was claimed against what the record shows.
/// </summary>
/// <remarks>
/// <para>
/// The honest limit, and it is worth stating before the rules: this can say a
/// claim is <em>unsupported</em>. It can never say a claim is wrong. Work
/// happens that leaves no commit, and commits happen that name nothing. Every
/// output here is an observation somebody can dismiss in a second if they know
/// better, and none of it is a verdict.
/// </para>
/// <para>
/// Which is why nothing matches on commit messages. "Said done, and committed
/// under a message that never named the item" is the overwhelmingly common
/// case, not a problem — flagging it would make the report noise, and a report
/// that is mostly noise is one people stop reading. The only thing asked is
/// whether <em>anything</em> was committed after the claim.
/// </para>
/// </remarks>
public static class TaskCorroboration
{
    /// <summary>
    /// How long a task may sit in one state before that itself is worth saying.
    /// </summary>
    /// <remarks>
    /// Not a deadline. It says the record has gone quiet, which is a different
    /// thing from the work having stalled, and the wording keeps that
    /// difference.
    /// </remarks>
    public static readonly TimeSpan Stale = TimeSpan.FromDays(14);

    /// <summary>What the record does not back up.</summary>
    /// <param name="tasks">What was claimed.</param>
    /// <param name="commits">Commits from the project's repository.</param>
    /// <param name="now">The current time.</param>
    public static IReadOnlyList<TaskDisagreement> Check(
        IReadOnlyList<TaskItem> tasks,
        IReadOnlyList<CommitSummary> commits,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(commits);

        var found = new List<TaskDisagreement>();

        foreach (var task in tasks)
        {
            if (task.Id.Length == 0)
            {
                continue;
            }

            if (task.State == TaskState.Done)
            {
                // Anything at all, by anyone. Asking for a commit that names
                // the task would flag almost every honest one.
                var since = commits.Any(commit => commit.WhenUtc >= task.DeclaredUtc);

                if (!since)
                {
                    found.Add(new TaskDisagreement(
                        task.Id,
                        "called done, and nothing has been committed since it was said. "
                        + "That may be right — work does not always leave a commit."));
                }

                continue;
            }

            if (task.State == TaskState.Doing && now - task.DeclaredUtc >= Stale)
            {
                found.Add(new TaskDisagreement(
                    task.Id,
                    $"has been called in progress for {(int)(now - task.DeclaredUtc).TotalDays} days. "
                    + "Nothing has changed the record since."));
            }
        }

        return found;
    }
}
