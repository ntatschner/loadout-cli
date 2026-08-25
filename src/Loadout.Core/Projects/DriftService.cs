using Loadout.Core.Git;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Projects;

/// <summary>What a project's state and its recorded configuration disagree about.</summary>
/// <param name="Slug">The project this concerns.</param>
/// <param name="Findings">
/// What was found, as ordinary diagnostic checks so that anything fixable
/// carries the same remedy the doctor report would.
/// </param>
public sealed record ProjectDrift(string Slug, IReadOnlyList<DiagnosticCheck> Findings)
{
    /// <summary>True when something here is worth acting on.</summary>
    public bool HasDrift => Findings.Any(f => f.Severity != DiagnosticSeverity.Info);

    /// <summary>The worst thing found, which decides how it is shown and the exit code.</summary>
    public DiagnosticSeverity Overall => Findings.Count == 0
        ? DiagnosticSeverity.Info
        : Findings.Max(f => f.Severity);

    /// <summary>Fixes the launcher can carry out for this project.</summary>
    public IReadOnlyList<Remedy> Remedies =>
        Findings
            .Select(f => f.Remedy)
            .OfType<Remedy>()
            .DistinctBy(r => (r.Kind, r.Target))
            .ToList();
}

/// <summary>Compares registered projects against what is actually on disk.</summary>
public interface IDriftService
{
    /// <summary>
    /// Inspects one project, or every registered one when no slug is given.
    /// </summary>
    Task<OperationResult<IReadOnlyList<ProjectDrift>>> InspectAsync(
        string? slug = null,
        CancellationToken ct = default);
}

/// <summary>
/// Finds where a project has drifted from what the workspace says about it.
/// <para>
/// The doctor report answers "is this machine set up", and it answers it for
/// wherever the shell happens to be standing. This answers a different
/// question: across every project registered, what has quietly stopped being
/// true. Those are not the same thing, and the second one is the one that goes
/// unnoticed — a hook that vanished with a re-clone, memory an agent recorded
/// that never reached the workspace, a remote that was changed on one machine.
/// </para>
/// <para>
/// Findings are ordinary diagnostic checks so a fixable one carries the same
/// remedy the doctor uses. Anything needing a person to decide something —
/// untracking committed files, reconciling two different remotes — is reported
/// without one, deliberately.
/// </para>
/// </summary>
public sealed class DriftService : IDriftService
{
    private readonly IProjectService _projects;
    private readonly IProjectOverviewService _overviews;
    private readonly IGitManager _git;

    public DriftService(
        IProjectService projects,
        IProjectOverviewService overviews,
        IGitManager git)
    {
        _projects = projects;
        _overviews = overviews;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ProjectDrift>>> InspectAsync(
        string? slug = null,
        CancellationToken ct = default)
    {
        List<ProjectResolution> projects;

        if (slug is { Length: > 0 })
        {
            var resolved = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return OperationResult<IReadOnlyList<ProjectDrift>>.Fail(
                    resolved.Error!, resolved.ExitCode);
            }

            projects = [resolved.Value!];
        }
        else
        {
            var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

            if (listed.Failed)
            {
                return OperationResult<IReadOnlyList<ProjectDrift>>.Fail(
                    listed.Error!, listed.ExitCode);
            }

            projects = [.. listed.Value!];
        }

        var results = new List<ProjectDrift>(projects.Count);

        foreach (var project in projects)
        {
            ct.ThrowIfCancellationRequested();

            results.Add(await InspectOneAsync(project, ct).ConfigureAwait(false));
        }

        return OperationResult<IReadOnlyList<ProjectDrift>>.Ok(results);
    }

    private async Task<ProjectDrift> InspectOneAsync(ProjectResolution project, CancellationToken ct)
    {
        var slug = project.Entry.Slug;
        var findings = new List<DiagnosticCheck>();

        if (project.LocalPath is not { Length: > 0 } path)
        {
            // Registered but absent is not a fault — a workspace is shared
            // across machines and no machine has every project — so it is said
            // plainly and nothing else is claimed about it.
            findings.Add(DiagnosticCheck.Ok(
                slug, "Clone", "registered but not present on this machine"));

            return new ProjectDrift(slug, findings);
        }

        await AddRemoteFindingAsync(project, path, slug, findings, ct).ConfigureAwait(false);

        var described = await _overviews.DescribeAsync(project, ct).ConfigureAwait(false);

        if (described.Failed || described.Value is not { } overview)
        {
            findings.Add(DiagnosticCheck.Warn(
                slug, "Inspection", described.Error ?? "could not be inspected"));

            return new ProjectDrift(slug, findings);
        }

        findings.Add(overview.TrackedAgentFiles == 0
            ? DiagnosticCheck.Ok(slug, "Agent files", "none committed")
            : DiagnosticCheck.Error(
                slug,
                "Agent files",
                $"{overview.TrackedAgentFiles} agent file(s) are committed to this repository. "
                + "They belong in the workspace. Removing them from the index leaves them on "
                + "disk and does not touch history; the commit is yours to make.",

                // This carried no fix for a long time, because the message here
                // claimed untracking "rewrites the repository". It does not:
                // git rm --cached stages a removal, history is untouched, and
                // git reset undoes it. That one wrong sentence left the most
                // common finding in the tool as advice rather than an action.
                overview.Project.LocalPath is { Length: > 0 } repository
                    ? new Remedy(
                        RemedyKind.UntrackAgentFiles,
                        $"Take {overview.TrackedAgentFiles} committed agent file(s) out of the index",
                        repository)
                    : null));

        findings.Add(overview.Protected && !overview.HookNeedsUpgrade
            ? DiagnosticCheck.Ok(slug, "Pre-commit protection", "installed")
            : overview.HookNeedsUpgrade
            ? DiagnosticCheck.Warn(
                slug,
                "Pre-commit protection",
                "installed, but written by an older version of the launcher, so it still names "
                + "commands that no longer exist",
                new Remedy(
                    RemedyKind.InstallPreCommitHook,
                    $"Replace the outdated pre-commit hook in {slug}",
                    path))
            : DiagnosticCheck.Warn(
                slug,
                "Pre-commit protection",
                "not installed in this clone; hooks are per-clone, so a fresh clone has none",
                new Remedy(
                    RemedyKind.InstallPreCommitHook,
                    $"Install the pre-commit hook in {slug}",
                    path)));

        findings.Add(overview.PendingImports == 0
            ? DiagnosticCheck.Ok(slug, "Memory", $"{overview.MemoryTopics} topic(s) in the workspace")
            : DiagnosticCheck.Warn(
                slug,
                "Memory",
                $"{overview.PendingImports} topic(s) recorded by an agent on this machine that the "
                + "workspace does not hold",
                new Remedy(
                    RemedyKind.ImportProjectMemory,
                    $"Import {overview.PendingImports} memory topic(s) for {slug}",
                    slug)));

        findings.Add(overview.IsOverBudget
            ? DiagnosticCheck.Warn(
                slug,
                "Instruction budget",
                $"{overview.AlwaysLoadedBytes / 1024} KB loads on every session regardless of the "
                + $"task. Splitting it into scoped rules is a judgement call: loadout rules budget {slug}")
            : DiagnosticCheck.Ok(
                slug,
                "Instruction budget",
                $"{overview.AlwaysLoadedBytes / 1024} KB always loaded, {overview.ScopedRules} scoped rule(s)"));

        return new ProjectDrift(slug, findings);
    }

    /// <summary>
    /// Compares the remote the registry recorded against the one the clone
    /// actually points at.
    /// <para>
    /// Reported, never corrected. Either side could be the right one — the
    /// repository may have moved, or this clone may be pointed somewhere
    /// deliberately — and guessing would send somebody's work to the wrong
    /// place.
    /// </para>
    /// </summary>
    private async Task AddRemoteFindingAsync(
        ProjectResolution project,
        string path,
        string slug,
        List<DiagnosticCheck> findings,
        CancellationToken ct)
    {
        var recorded = project.Entry.Remote;

        if (string.IsNullOrWhiteSpace(recorded))
        {
            return;
        }

        var state = await _git.GetStateAsync(path, ct).ConfigureAwait(false);

        if (state.Failed || state.Value?.RemoteUrl is not { Length: > 0 } actual)
        {
            return;
        }

        findings.Add(GitRemote.AreEquivalent(recorded, actual)
            ? DiagnosticCheck.Ok(slug, "Remote", actual)
            : DiagnosticCheck.Warn(
                slug,
                "Remote",
                $"the registry records '{recorded}' but this clone points at '{actual}'. "
                + "Either could be right, so nothing was changed"));
    }
}
