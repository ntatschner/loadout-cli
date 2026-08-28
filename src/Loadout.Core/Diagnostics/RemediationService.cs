using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Diagnostics;
using Loadout.Models.Results;

namespace Loadout.Core.Diagnostics;

/// <summary>What a remedy did, or would do.</summary>
/// <param name="Remedy">The fix this describes.</param>
/// <param name="Applied">False when this was a preview and nothing was changed.</param>
/// <param name="Detail">What changed, or what would change, in a person's words.</param>
public sealed record RemedyOutcome(Remedy Remedy, bool Applied, string Detail);

/// <summary>Applies the fixes the doctor report knows about.</summary>
public interface IRemediationService
{
    /// <summary>
    /// Says what a remedy would do without doing it.
    /// </summary>
    Task<OperationResult<RemedyOutcome>> PreviewAsync(Remedy remedy, CancellationToken ct = default);

    /// <summary>Carries a remedy out.</summary>
    Task<OperationResult<RemedyOutcome>> ApplyAsync(Remedy remedy, CancellationToken ct = default);
}

/// <summary>
/// Turns advice into action.
/// <para>
/// Up to now the doctor told somebody which command to run and left them to it,
/// which is fine for one finding and tedious for six. This runs them — but only
/// the fixes that are unambiguous and local: installing a hook, repairing a
/// pointer, bringing memory the agent already recorded into the workspace.
/// </para>
/// <para>
/// Every remedy previews before it applies, and nothing here touches the
/// network, rewrites history, or resolves a conflict on somebody's behalf. A
/// fix that has to guess what was meant is not a fix, it is a second problem,
/// so those findings stay as advice.
/// </para>
/// </summary>
internal sealed class RemediationService : IRemediationService
{
    private readonly IPolicyService _policies;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IMemoryImporter _importer;
    private readonly IGitManager _git;

    public RemediationService(
        IPolicyService policies,
        IProjectService projects,
        IWorkspaceManager workspace,
        IMemoryImporter importer,
        IGitManager git)
    {
        _policies = policies;
        _projects = projects;
        _workspace = workspace;
        _importer = importer;
        _git = git;
    }

    /// <inheritdoc />
    public Task<OperationResult<RemedyOutcome>> PreviewAsync(
        Remedy remedy,
        CancellationToken ct = default) =>
        RunAsync(remedy, apply: false, ct);

    /// <inheritdoc />
    public Task<OperationResult<RemedyOutcome>> ApplyAsync(
        Remedy remedy,
        CancellationToken ct = default) =>
        RunAsync(remedy, apply: true, ct);

    private async Task<OperationResult<RemedyOutcome>> RunAsync(
        Remedy remedy,
        bool apply,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(remedy);

        return remedy.Kind switch
        {
            RemedyKind.InstallPreCommitHook => await HookAsync(remedy, apply, ct).ConfigureAwait(false),
            RemedyKind.RepairGlobalExcludes => await ExcludesAsync(remedy, apply, ct).ConfigureAwait(false),
            RemedyKind.ImportProjectMemory => await MemoryAsync(remedy, apply, ct).ConfigureAwait(false),
            RemedyKind.UntrackAgentFiles => await UntrackAsync(remedy, apply, ct).ConfigureAwait(false),
            _ => OperationResult<RemedyOutcome>.Fail(
                $"This build does not know how to apply '{remedy.Kind}'.",
                ExitCode.InvalidArguments),
        };
    }

    /// <summary>
    /// Installs the pre-commit hook in one clone. Hooks are per-clone and
    /// untracked, so this is the fix that comes up most: a fresh clone of a
    /// protected repository has no protection until somebody runs it.
    /// </summary>
    /// <summary>
    /// Takes committed agent files out of the index, leaving every one of them
    /// on disk.
    /// <para>
    /// This was advice for a long time, on the stated grounds that untracking
    /// "rewrites the repository". It does not. <c>git rm --cached</c> stages a
    /// removal: history is untouched, nothing is deleted, and the change is
    /// undone with <c>git reset</c> like any other staged change. Rewriting
    /// history is filter-repo, which this does not do and should not.
    /// </para>
    /// <para>
    /// The commit is left to the person. Staging is reversible and local;
    /// committing is neither, and a tool that commits on somebody's behalf is
    /// making a decision about their history that it was not asked to make.
    /// </para>
    /// </summary>
    private async Task<OperationResult<RemedyOutcome>> UntrackAsync(
        Remedy remedy,
        bool apply,
        CancellationToken ct)
    {
        if (remedy.Target is not { Length: > 0 } repository)
        {
            return OperationResult<RemedyOutcome>.Fail(
                "Untracking needs a repository, and none was recorded with the finding.",
                ExitCode.InvalidArguments);
        }

        var checkResult = await _policies.CheckAsync(repository, ct).ConfigureAwait(false);

        if (checkResult.Failed)
        {
            return OperationResult<RemedyOutcome>.Fail(checkResult.Error!, checkResult.ExitCode);
        }

        // Re-read rather than trusting what the finding said. The report may be
        // minutes old, and untracking a path that has since been dealt with
        // would stage a removal nobody asked for.
        var tracked = checkResult.Value!.Violations.Select(v => v.Path).ToList();

        if (tracked.Count == 0)
        {
            return apply
                ? Done(remedy, "Nothing is committed that should not be.")
                : Preview(remedy, "Nothing is committed that should not be.");
        }

        var listed = string.Join(Environment.NewLine, tracked.Select(path => "  " + path));

        if (!apply)
        {
            return Preview(
                remedy,
                $"Remove {tracked.Count} file(s) from the index, keeping every one on disk:"
                + Environment.NewLine + listed
                + Environment.NewLine
                + "History is not touched. Commit the staged removal when you are ready.");
        }

        var removed = await _git.UntrackAsync(repository, tracked, ct).ConfigureAwait(false);

        if (removed.Failed)
        {
            return OperationResult<RemedyOutcome>.Fail(removed.Error!, removed.ExitCode);
        }

        return Done(
            remedy,
            $"{tracked.Count} file(s) removed from the index and left on disk. "
            + "Commit the staged removal to finish.");
    }

    private async Task<OperationResult<RemedyOutcome>> HookAsync(
        Remedy remedy,
        bool apply,
        CancellationToken ct)
    {
        if (remedy.Target is not { Length: > 0 } repository)
        {
            return OperationResult<RemedyOutcome>.Fail(
                "Installing a hook needs a repository, and none was recorded with the finding.",
                ExitCode.InvalidArguments);
        }

        if (!apply)
        {
            return Preview(
                remedy,
                $"Write a pre-commit hook into {Path.Combine(repository, ".git", "hooks")}.");
        }

        var result = await _policies.InstallHookAsync(repository, ct).ConfigureAwait(false);

        return result.Failed
            ? OperationResult<RemedyOutcome>.Fail(result.Error!, result.ExitCode)
            : Done(remedy, $"Pre-commit hook installed in {repository}.");
    }

    /// <summary>
    /// Rewrites the global exclude file and repoints Git at it.
    /// <para>
    /// Safe to repeat: it writes the launcher's own file at the launcher's own
    /// path, and never edits a file somebody else owns.
    /// </para>
    /// </summary>
    private async Task<OperationResult<RemedyOutcome>> ExcludesAsync(
        Remedy remedy,
        bool apply,
        CancellationToken ct)
    {
        if (!apply)
        {
            return Preview(
                remedy,
                "Write the launcher's global exclude file and point core.excludesFile at it.");
        }

        var result = await _policies.InstallGlobalExcludesAsync(ct).ConfigureAwait(false);

        return result.Failed
            ? OperationResult<RemedyOutcome>.Fail(result.Error!, result.ExitCode)
            : Done(remedy, $"Global excludes written to {result.Value}.");
    }

    /// <summary>
    /// Brings memory an agent recorded on this machine into the workspace.
    /// <para>
    /// This is the drift that actually loses work: the agent wrote down what it
    /// learned, the workspace never saw it, and the next machine starts from
    /// nothing. The importer already previews, so the preview here is its own
    /// rather than a description of one.
    /// </para>
    /// </summary>
    private async Task<OperationResult<RemedyOutcome>> MemoryAsync(
        Remedy remedy,
        bool apply,
        CancellationToken ct)
    {
        if (remedy.Target is not { Length: > 0 } slug)
        {
            return OperationResult<RemedyOutcome>.Fail(
                "Importing memory needs a project, and none was recorded with the finding.",
                ExitCode.InvalidArguments);
        }

        var resolved = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        if (resolved.Failed || resolved.Value?.LocalPath is not { Length: > 0 } repository)
        {
            return OperationResult<RemedyOutcome>.Fail(
                $"{slug} is not available on this machine, so its memory cannot be read.",
                ExitCode.ProjectNotFound);
        }

        var source = _importer.Discover(repository);

        if (source is null)
        {
            // The memory may have been imported already, by hand or by an
            // earlier run. Nothing to do is a success, not a failure.
            return apply
                ? Done(remedy, $"No agent memory found outside the workspace for {slug}.")
                : Preview(remedy, $"Nothing to import for {slug}.");
        }

        var result = await _importer
            .ImportAsync(_workspace.LocalPath, slug, source, apply, ct)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return OperationResult<RemedyOutcome>.Fail(result.Error!, result.ExitCode);
        }

        var import = result.Value!;

        var summary = $"{import.Facts} fact(s) across {import.Imported.Count} topic(s) from {source}";

        if (import.Skipped.Count > 0)
        {
            // Skipped topics are the interesting half: something was there and
            // deliberately not taken.
            summary += $", {import.Skipped.Count} skipped";
        }

        return apply
            ? Done(remedy, $"Imported {summary}.")
            : Preview(remedy, $"Would import {summary}.");
    }

    private static OperationResult<RemedyOutcome> Preview(Remedy remedy, string detail) =>
        OperationResult<RemedyOutcome>.Ok(new RemedyOutcome(remedy, Applied: false, detail));

    private static OperationResult<RemedyOutcome> Done(Remedy remedy, string detail) =>
        OperationResult<RemedyOutcome>.Ok(new RemedyOutcome(remedy, Applied: true, detail));
}
