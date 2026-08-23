using AgentWorkspace.Core.Backups;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models.Policies;
using AgentWorkspace.Models.Results;

namespace AgentWorkspace.Core.Policies;

/// <inheritdoc />
public sealed class MigrationService : IMigrationService
{
    /// <summary>
    /// Where each well-known agent artefact belongs in the workspace.
    /// <para>
    /// A directory moves as a unit. Listing its children separately as well
    /// would plan the same copy twice and show the user three entries where one
    /// happened, and any child mapped to a differently named destination would
    /// collide with the copy the parent already made.
    /// </para>
    /// </summary>
    private static readonly (string Source, string Destination)[] KnownMappings =
    [
        // Listed before .claude so the more specific mapping claims it first.
        //
        // Scoped instruction rules are not Claude's private business: they say
        // which instructions apply to which paths, which is true whichever
        // agent is reading them. Leaving them under agents/claude would file
        // them where only one adapter looks and where the rule loader does not
        // look at all, so a project that had carefully scoped its instructions
        // would find them silently stop being applied.
        (".claude/rules", "rules"),

        (".claude", "agents/claude"),
        (".codex", "agents/codex"),
        (".cursor", "agents/cursor"),
        (".windsurf", "agents/windsurf"),
        (".continue", "agents/continue"),
        (".roo", "agents/roo"),
        (".ai", "agents/generic"),
        (".agent", "agents/generic"),
        ("CLAUDE.local.md", "agents/claude/instructions.local.md"),
        ("CLAUDE.md", "agents/claude/instructions.md"),
        ("AGENTS.override.md", "agents/codex/AGENTS.override.md"),
    ];

    private readonly IPolicyService _policies;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly IBackupService _backups;

    public MigrationService(
        IPolicyService policies,
        IWorkspaceManager workspace,
        IGitManager git,
        IBackupService backups)
    {
        _policies = policies;
        _workspace = workspace;
        _git = git;
        _backups = backups;
    }

    /// <inheritdoc />
    public async Task<OperationResult<MigrationPlan>> PlanAsync(
        string repositoryPath,
        string slug,
        bool includeIgnored = false,
        CancellationToken ct = default)
    {
        var checkResult = await _policies.CheckAsync(repositoryPath, ct).ConfigureAwait(false);
        if (checkResult.Failed)
        {
            return OperationResult<MigrationPlan>.Fail(checkResult.Error!, checkResult.ExitCode);
        }

        var report = checkResult.Value!;
        var steps = new List<MigrationStep>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Directory-level moves first: moving .claude as a unit is clearer than
        // listing forty files inside it, and it keeps the plan readable.
        foreach (var (source, destination) in KnownMappings)
        {
            ct.ThrowIfCancellationRequested();

            var absolute = Path.Combine(report.RepositoryPath,
                source.Replace('/', Path.DirectorySeparatorChar));

            var isDirectory = Directory.Exists(absolute);

            if (!isDirectory && !File.Exists(absolute))
            {
                continue;
            }

            if (IsAlreadyCovered(source, claimed))
            {
                continue;
            }

            var kind = ClassifyKind(source, report);

            if (kind == PolicyFindingKind.Ignored && !includeIgnored)
            {
                // Already excluded from the repository, so nothing here is a
                // compliance problem. Taking it would remove a working local
                // setup for no gain.
                claimed.Add(source);
                continue;
            }

            claimed.Add(source);

            steps.Add(new MigrationStep(
                absolute,
                source,
                $"projects/{slug}/{destination}",
                kind,
                isDirectory));
        }

        return OperationResult<MigrationPlan>.Ok(
            new MigrationPlan(slug, WithExclusions(steps), false, []));
    }

    /// <inheritdoc />
    public async Task<OperationResult<MigrationPlan>> ApplyAsync(
        MigrationPlan plan,
        CancellationToken ct = default)
    {
        var trackedLeftInPlace = new List<string>();

        // Snapshot before the first copy. Migration deletes untracked files
        // from the repository and writes over whatever is already at the
        // destination, so without this the command is a one-way door and the
        // dry run is the only safety net there is.
        var captured = await _backups
            .CaptureAsync("migrate", plan.Slug, CollectAffectedPaths(plan), ct)
            .ConfigureAwait(false);

        if (captured.Failed)
        {
            return OperationResult<MigrationPlan>.Fail(
                "The migration was not started because a backup could not be taken: "
                + captured.Error);
        }

        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();

            var destination = Path.Combine(
                _workspace.LocalPath,
                step.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (step.IsDirectory)
                {
                    CopyDirectory(step.SourcePath, destination, step.Excluded);
                }
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(step.SourcePath, destination, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return OperationResult<MigrationPlan>.Fail(
                    $"Could not copy '{step.RepositoryRelativePath}' into the workspace: {ex.Message}");
            }

            if (step.Kind == PolicyFindingKind.Tracked)
            {
                // Spec section 27 is explicit: tracked files must not be
                // silently deleted. Removing them rewrites the repository's
                // contents, which is the user's decision and belongs in a
                // commit they can see and review.
                trackedLeftInPlace.Add(step.RepositoryRelativePath);
                continue;
            }

            var removed = TryRemoveFromRepository(step);

            if (removed.Failed)
            {
                return OperationResult<MigrationPlan>.Fail(removed.Error!);
            }
        }

        return OperationResult<MigrationPlan>.Ok(plan with
        {
            Applied = true,
            TrackedLeftInPlace = trackedLeftInPlace,
            BackupId = captured.Value!.Id,
        });
    }

    /// <summary>
    /// Every path the migration could write over or remove: each source file,
    /// and the destination it would land on.
    /// <para>
    /// Destinations are included even when nothing is there yet. The backup
    /// records them as absent, which is what lets a restore delete the copies
    /// rather than leaving them behind as debris after a rollback.
    /// </para>
    /// </summary>
    private List<string> CollectAffectedPaths(MigrationPlan plan)
    {
        var paths = new List<string>();

        foreach (var step in plan.Steps)
        {
            var destination = Path.Combine(
                _workspace.LocalPath,
                step.WorkspaceRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (!step.IsDirectory)
            {
                paths.Add(step.SourcePath);
                paths.Add(destination);
                continue;
            }

            if (!Directory.Exists(step.SourcePath))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(
                step.SourcePath, "*", SearchOption.AllDirectories))
            {
                paths.Add(file);
                paths.Add(Path.Combine(
                    destination,
                    Path.GetRelativePath(step.SourcePath, file)));
            }
        }

        return paths;
    }

    /// <summary>
    /// Tells each directory step which of its children another step is taking.
    /// <para>
    /// A more specific mapping claims a subtree first, but the parent still
    /// copies everything underneath it unless told not to. The result would be
    /// two copies of the same rules in two places, and no way for a reader to
    /// know which one is being used.
    /// </para>
    /// </summary>
    private static List<MigrationStep> WithExclusions(List<MigrationStep> steps)
    {
        var result = new List<MigrationStep>();

        foreach (var step in steps)
        {
            if (!step.IsDirectory)
            {
                result.Add(step);
                continue;
            }

            var prefix = step.RepositoryRelativePath.Replace('\\', '/') + "/";

            var excluded = steps
                .Where(other => !ReferenceEquals(other, step))
                .Select(other => other.RepositoryRelativePath.Replace('\\', '/'))
                .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(path => path[prefix.Length..])
                .ToList();

            result.Add(excluded.Count == 0 ? step : step with { Excluded = excluded });
        }

        return result;
    }

    /// <summary>
    /// Whether a longer mapping has already claimed this path, so that
    /// <c>.claude</c> does not move a subtree that <c>.claude/skills</c>
    /// already accounted for.
    /// </summary>
    private static bool IsAlreadyCovered(string source, HashSet<string> claimed) =>
        claimed.Any(existing =>
            source.StartsWith(existing + "/", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Classifies a path the way spec section 27 asks for. A directory is
    /// treated as tracked when anything inside it is, because that is what
    /// decides whether removing it needs a Git commit.
    /// </summary>
    private static PolicyFindingKind ClassifyKind(string source, PolicyReport report)
    {
        var prefix = source.Replace('\\', '/');

        var related = report.Findings
            .Where(f => f.Path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || f.Path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (related.Any(f => f.Kind == PolicyFindingKind.Tracked))
        {
            return PolicyFindingKind.Tracked;
        }

        return related.Any(f => f.Kind == PolicyFindingKind.UntrackedAndVisible)
            ? PolicyFindingKind.UntrackedAndVisible
            : PolicyFindingKind.Ignored;
    }

    private static OperationResult TryRemoveFromRepository(MigrationStep step)
    {
        try
        {
            if (step.IsDirectory)
            {
                Directory.Delete(step.SourcePath, recursive: true);
            }
            else
            {
                File.Delete(step.SourcePath);
            }

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The copy already succeeded, so nothing has been lost; the user is
            // told what is still there rather than the whole migration failing.
            return OperationResult.Fail(
                $"'{step.RepositoryRelativePath}' was copied into the workspace but could not be "
                + $"removed from the repository: {ex.Message}");
        }
    }


    /// <summary>Whether another step is taking this path out of the subtree.</summary>
    private static bool IsExcluded(string relative, IReadOnlyList<string>? excluded)
    {
        if (excluded is null || excluded.Count == 0)
        {
            return false;
        }

        var normalised = relative.Replace('\\', '/');

        return excluded.Any(path =>
            normalised.Equals(path, StringComparison.OrdinalIgnoreCase)
            || normalised.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase));
    }
    private static void CopyDirectory(
        string source,
        string destination,
        IReadOnlyList<string>? excluded = null)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            if (IsExcluded(relative, excluded))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
