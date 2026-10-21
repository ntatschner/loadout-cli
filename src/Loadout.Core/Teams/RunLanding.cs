using Loadout.Core.Git;

namespace Loadout.Core.Teams;

/// <summary>Where a run's merged work ended up.</summary>
/// <param name="Target">The branch it was merged into.</param>
/// <param name="Branches">The run's branches that were merged, in order.</param>
/// <param name="Commit">What the target pointed at after the last merge, when the journal recorded it.</param>
/// <param name="Repository">The repository it was merged in, when the run wrote it down.</param>
/// <param name="Standing">Whether that commit has been pushed, when it could be asked.</param>
public sealed record Landing(
    string Target,
    IReadOnlyList<string> Branches,
    string? Commit,
    string? Repository,
    GitPushStanding? Standing);

/// <summary>
/// Reads where a finished run's work went, and whether it has left this
/// machine.
/// </summary>
/// <remarks>
/// <para>
/// A run that merges does it into whatever branch the repository had checked
/// out, and it never pushes. Before this, <c>team status</c> showed each
/// branch as "merged" and nothing else, so a person reading a finished run
/// could not tell into what, whether anybody else could see it yet, or what
/// to do next. The one line naming the target sat in the decisions at the
/// bottom of several hundred.
/// </para>
/// <para>
/// Whether it was pushed is asked of git each time rather than recorded,
/// because it stops being true the moment somebody pushes.
/// </para>
/// </remarks>
public static class RunLanding
{
    /// <summary>Where the run's merges went, or null when it merged nothing.</summary>
    /// <param name="run">The run, for the repository it worked on.</param>
    /// <param name="events">Its events, for the merges.</param>
    /// <param name="git">Git, to ask whether the merge has been pushed; null to not ask.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<Landing?> OfAsync(
        RunSummary run,
        IReadOnlyList<RunEvent> events,
        IGitManager? git,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(events);

        var merges = events.Where(one => one.Kind == "merge.done").ToList();

        if (merges.Count == 0)
        {
            return null;
        }

        var last = merges[^1];
        var branches = merges
            .Select(one => one.Word("branch"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // MergeStep says "the current branch" when the head was detached,
        // and that names nothing git could look up.
        var target = last.Word("target") ?? "the current branch";
        var commit = last.Word("commit");
        var named = !target.Contains(' ', StringComparison.Ordinal);

        GitPushStanding? standing = null;

        if (git is not null && named && commit is not null && run.Path is { Length: > 0 } repository)
        {
            var asked = await git.GetPushStandingAsync(repository, target, commit, ct).ConfigureAwait(false);
            standing = asked.Succeeded ? asked.Value : null;
        }

        return new Landing(target, branches, commit, run.Path, standing);
    }
}
