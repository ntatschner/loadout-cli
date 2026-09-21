using System.Globalization;
using System.Text.RegularExpressions;
using Loadout.Core.Git;
using Loadout.Core.Security;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Core.Teams;

/// <summary>What one node changed, as something somebody can read.</summary>
/// <param name="Node">Whose work this is.</param>
/// <param name="Branch">The branch it worked on.</param>
/// <param name="From">The commit that branch started from.</param>
/// <param name="Merged">Whether the run merged that branch.</param>
/// <param name="Summary">The file-by-file summary.</param>
/// <param name="Patch">The patch itself, possibly cut short.</param>
/// <param name="Cut">Whether the patch was cut short.</param>
public sealed record NodeDiff(
    string Node,
    string Branch,
    string From,
    bool Merged,
    string Summary,
    string Patch,
    bool Cut);

/// <summary>
/// The work a node actually did, rather than its account of it.
/// </summary>
/// <remarks>
/// <para>
/// A report says "added the flag and a test for it". The diff says what was
/// added, and those are not always the same thing. This is the last of the
/// three answers a run can give about a node - what it was told, what it said,
/// and what it did - and the only one nothing can argue with.
/// </para>
/// <para>
/// Measured from the commit the branch started at, which the run wrote down at
/// the time. Not from whatever HEAD is now: once a node's branch has been
/// merged, a diff against HEAD is empty, and empty is the one answer that
/// would be read as "it did nothing" rather than as "this cannot be shown".
/// </para>
/// </remarks>
public static class RunDiff
{
    /// <summary>The most patch that will be handed to a page.</summary>
    /// <remarks>
    /// A node that reformatted a file produces a patch bigger than the browser
    /// tab has any use for. The summary above it is never cut, so what was
    /// touched is always complete even when the patch is not.
    /// </remarks>
    public const int Most = 512 * 1024;

    /// <summary>
    /// What a node changed, or why that cannot be shown.
    /// </summary>
    /// <param name="git">Whatever runs git.</param>
    /// <param name="repository">Any working tree of the run's repository.</param>
    /// <param name="node">The node, as the run recorded it.</param>
    /// <param name="merged">The branches the run merged.</param>
    /// <param name="ct">Cancellation.</param>
    public static async Task<OperationResult<NodeDiff>> ForAsync(
        IGitManager git,
        string? repository,
        RunNode node,
        IReadOnlyList<string> merged,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(git);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(merged);

        if (string.IsNullOrWhiteSpace(repository))
        {
            return OperationResult<NodeDiff>.Fail(
                "This run did not write down which repository it worked on, so there is nothing to diff against.",
                ExitCode.RepositoryUnavailable);
        }

        if (node.Branch is not { Length: > 0 } branch)
        {
            return OperationResult<NodeDiff>.Fail(
                $"{node.Node} worked in the repository itself rather than on a branch of its own.",
                ExitCode.RepositoryUnavailable);
        }

        if (node.Base is not { Length: > 0 } from)
        {
            // Said plainly rather than guessed at. A diff measured from the
            // wrong place is worse than no diff, because it looks like one.
            return OperationResult<NodeDiff>.Fail(
                $"This run did not write down where {branch} started, so what {node.Node} changed "
                + "cannot be worked out now. Runs from here on do record it.",
                ExitCode.RepositoryUnavailable);
        }

        // The branch may be long gone - merged and tidied away is the ordinary
        // ending - so the diff is measured to whatever it became rather than to
        // the name. Its own tip while it exists, and the merge that carried it
        // once it does not.
        var tip = await git.ResolveAsync(repository, branch, ct).ConfigureAwait(false);

        if (tip.Failed)
        {
            return OperationResult<NodeDiff>.Fail(
                $"{branch} is no longer in this repository, so what {node.Node} changed cannot be shown. "
                + "It was merged and tidied away, or somebody removed it.",
                ExitCode.RepositoryUnavailable);
        }

        var to = tip.Value!;

        var summary = await git.DiffAsync(repository, from, to, summary: true, ct).ConfigureAwait(false);

        if (summary.Failed)
        {
            return OperationResult<NodeDiff>.Fail(summary.Error!, ExitCode.RepositoryUnavailable);
        }

        var patch = await git.DiffAsync(repository, from, to, summary: false, ct).ConfigureAwait(false);

        if (patch.Failed)
        {
            return OperationResult<NodeDiff>.Fail(patch.Error!, ExitCode.RepositoryUnavailable);
        }

        var whole = patch.Value ?? string.Empty;
        var cut = whole.Length > Most;

        var shown = cut
            ? whole[..Most] + string.Create(
                CultureInfo.InvariantCulture,
                $"\n\n[cut here: this patch is {whole.Length:N0} characters, and the first {Most:N0} are above.\n"
                + $" The summary above it is complete. For the rest: git diff {from[..Math.Min(12, from.Length)]} {branch}]")
            : whole;

        try
        {
            return OperationResult<NodeDiff>.Ok(new NodeDiff(
                node.Node,
                branch,
                from,
                merged.Contains(branch, StringComparer.Ordinal),
                SecretRedactor.Redact(summary.Value),
                SecretRedactor.Redact(shown),
                cut));
        }
        catch (RegexMatchTimeoutException)
        {
            // The same rule the papers follow: unchecked is the one thing it
            // must not be, so what cannot be checked is not shown.
            return OperationResult<NodeDiff>.Fail(
                $"What {node.Node} changed could not be checked for credentials in a reasonable time, "
                + "so it is not being shown. Read it with git.",
                ExitCode.PolicyViolation);
        }
    }
}
