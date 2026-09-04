namespace Loadout.Core.Workspace;

/// <summary>A file in the workspace, as the candidate search sees it.</summary>
/// <param name="RelativePath">Path from the workspace root, with forward slashes.</param>
/// <param name="Text">What it says.</param>
public sealed record WorkspaceFile(string RelativePath, string Text);

/// <summary>Something that could be shared, and why it looks that way.</summary>
/// <param name="RelativePath">Where it is now.</param>
/// <param name="Reason">What suggests it, said so somebody can disagree.</param>
public sealed record ShareCandidate(string RelativePath, string Reason);

/// <summary>
/// Spots guidance that is written as though it belonged to one project but
/// never mentions it.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here shares anything. It finds things worth asking about, because
/// "publish deliberately" turns into "publish never" if nobody is ever
/// prompted — a rule that depends on somebody remembering is a rule that
/// decays.
/// </para>
/// <para>
/// The signal is deliberately weak and stated as such: a specialist filed under
/// a project that never names the project, its slug or its paths is <em>often</em>
/// general guidance somebody put in the nearest folder. It is sometimes not, so
/// this offers rather than decides, and the reason is printed so it can be
/// disagreed with in a second.
/// </para>
/// <para>
/// What it will never offer is the private half. A workspace holds handoffs,
/// memory and decisions, and publishing those is the irreversible disclosure
/// this whole feature is designed around — so those directories are not
/// searched at all rather than filtered afterwards. A filter is a place a
/// mistake can be made; not looking is not.
/// </para>
/// </remarks>
public static class ShareCandidates
{
    /// <summary>
    /// The only places a candidate may come from.
    /// </summary>
    /// <remarks>
    /// An allow list rather than a deny list. A new directory added to the
    /// workspace later is not searched until somebody says it should be, which
    /// is the safe direction for a store that also holds handoffs.
    /// </remarks>
    private static readonly string[] Searched = ["specialists/", "rules/", "instructions/"];

    /// <summary>Directories that are never searched, whatever else changes.</summary>
    /// <remarks>
    /// Named explicitly as well as excluded by the allow list, so that widening
    /// the allow list one day cannot quietly widen this too. Handoffs and
    /// memory are the reason a workspace is created private.
    /// </remarks>
    public static readonly string[] NeverSearched = ["handoffs/", "memory/", "state/"];

    /// <summary>What looks general enough to be worth asking about.</summary>
    /// <param name="files">Files under the project, relative to the workspace root.</param>
    /// <param name="slug">The project's slug.</param>
    /// <param name="projectName">The project's name.</param>
    public static IReadOnlyList<ShareCandidate> Find(
        IReadOnlyList<WorkspaceFile> files,
        string slug,
        string? projectName = null)
    {
        ArgumentNullException.ThrowIfNull(files);

        var found = new List<ShareCandidate>();

        foreach (var file in files)
        {
            var path = file.RelativePath.Replace('\\', '/');

            if (NeverSearched.Any(directory =>
                path.Contains(directory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!Searched.Any(directory =>
                path.Contains(directory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (Mentions(file.Text, slug) || Mentions(file.Text, projectName))
            {
                continue;
            }

            found.Add(new ShareCandidate(
                path,
                $"never mentions {slug}, so it may be guidance that belongs to everybody"));
        }

        return [.. found.OrderBy(candidate => candidate.RelativePath, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Whether a file names the project.
    /// </summary>
    /// <remarks>
    /// A plain contains, because the question is only whether the project is
    /// spoken about at all. Anything cleverer would be a judgement, and this
    /// deliberately makes none — it decides what to ask about, not what to do.
    /// </remarks>
    private static bool Mentions(string text, string? name) =>
        name is { Length: > 2 } && text.Contains(name, StringComparison.OrdinalIgnoreCase);
}
