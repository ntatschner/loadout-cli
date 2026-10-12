using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Starters;

/// <summary>Whether one prerequisite is in place.</summary>
/// <param name="Prerequisite">What was checked.</param>
/// <param name="Met">Whether it is there.</param>
/// <param name="Found">What was found when it is, such as a version, or null.</param>
public sealed record PrerequisiteStatus(Prerequisite Prerequisite, bool Met, string? Found = null);

/// <summary>What the planning session left behind.</summary>
/// <param name="Written">Whether PLAN.md exists.</param>
/// <param name="AlsoChanged">Anything else it changed, which it was asked not to.</param>
public sealed record PlanOutcome(bool Written, IReadOnlyList<string> AlsoChanged);

/// <summary>What changed since the plan, for the person to look at.</summary>
/// <param name="Saved">The summary of what was committed since the plan, or empty.</param>
/// <param name="Unsaved">Files changed and not yet committed.</param>
/// <param name="Page">The page to open in a browser, as a full path, or null when there is none.</param>
public sealed record FirstProjectReview(string Saved, IReadOnlyList<string> Unsaved, string? Page);

/// <summary>
/// The machine steps of <c>loadout start</c>: checking, creating, protecting,
/// and looking at what changed.
/// </summary>
/// <remarks>
/// The agent sessions are not here. Starting one is the command line's to do,
/// through the same launcher every other launch uses, and this layer cannot
/// see the agents in any case.
/// </remarks>
public interface IFirstProjectGuide
{
    /// <summary>Checks what a first project needs.</summary>
    /// <param name="installedAgents">The agents found on this machine, by display name.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<PrerequisiteStatus>> CheckAsync(
        IReadOnlyList<string> installedAgents,
        CancellationToken ct = default);

    /// <summary>The progress recorded on this machine, or null when there is none.</summary>
    Task<OperationResult<FirstProjectProgress?>> LoadAsync(CancellationToken ct = default);

    /// <summary>Why recorded progress can no longer be carried on from, or null when it can.</summary>
    Task<string?> StaleReasonAsync(FirstProjectProgress progress, CancellationToken ct = default);

    /// <summary>Records progress.</summary>
    Task<OperationResult> SaveAsync(FirstProjectProgress progress, CancellationToken ct = default);

    /// <summary>Forgets recorded progress. The project itself is left alone.</summary>
    OperationResult Forget();

    /// <summary>Makes the project from a starter, registers it, and records that it was done.</summary>
    Task<OperationResult<FirstProjectProgress>> CreateAsync(
        Starter starter,
        string name,
        string? destination,
        CancellationToken ct = default);

    /// <summary>Installs the hook that keeps agent files out of the project.</summary>
    Task<OperationResult> ProtectAsync(FirstProjectProgress progress, CancellationToken ct = default);

    /// <summary>Keeps PLAN.md as a commit, if the planning session wrote one.</summary>
    Task<OperationResult<PlanOutcome>> KeepPlanAsync(FirstProjectProgress progress, CancellationToken ct = default);

    /// <summary>What changed since the plan.</summary>
    Task<OperationResult<FirstProjectReview>> ReviewAsync(FirstProjectProgress progress, CancellationToken ct = default);

    /// <summary>Commits everything not yet committed. Returns false when there was nothing.</summary>
    Task<OperationResult<bool>> KeepBuildAsync(FirstProjectProgress progress, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class FirstProjectGuide : IFirstProjectGuide
{
    private readonly IProjectTemplateService _templates;
    private readonly IProjectService _projects;
    private readonly IPolicyService _policies;
    private readonly ITaskService _tasks;
    private readonly IGitManager _git;
    private readonly IWorkspaceManager _workspace;
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _clock;

    public FirstProjectGuide(
        IProjectTemplateService templates,
        IProjectService projects,
        IPolicyService policies,
        ITaskService tasks,
        IGitManager git,
        IWorkspaceManager workspace,
        IPlatformPaths paths,
        YamlStore yaml,
        TimeProvider clock)
    {
        _templates = templates;
        _projects = projects;
        _policies = policies;
        _tasks = tasks;
        _git = git;
        _workspace = workspace;
        _paths = paths;
        _yaml = yaml;
        _clock = clock;
    }

    private string ProgressFile => Path.Combine(_paths.Paths.State, "start", "progress.yaml");

    /// <inheritdoc />
    public async Task<IReadOnlyList<PrerequisiteStatus>> CheckAsync(
        IReadOnlyList<string> installedAgents,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(installedAgents);

        var version = await _git.GetVersionAsync(ct).ConfigureAwait(false);
        var git = new PrerequisiteStatus(
            Prerequisites.Git, version.Succeeded, version.Succeeded ? version.Value : null);

        // Only worth asking once Git is there to ask. Reported as missing
        // until then, which is true and puts it next in the list.
        var name = git.Met ? await _git.GetConfigValueAsync("user.name", ct: ct).ConfigureAwait(false) : null;
        var email = git.Met ? await _git.GetConfigValueAsync("user.email", ct: ct).ConfigureAwait(false) : null;

        var named = name is { Succeeded: true, Value.Length: > 0 }
            && email is { Succeeded: true, Value.Length: > 0 };

        return
        [
            git,
            new PrerequisiteStatus(
                Prerequisites.GitIdentity, named, named ? $"{name!.Value} <{email!.Value}>" : null),
            new PrerequisiteStatus(
                Prerequisites.Agent,
                installedAgents.Count > 0,
                installedAgents.Count > 0 ? string.Join(", ", installedAgents) : null),
            new PrerequisiteStatus(Prerequisites.Setup, _workspace.IsAvailable()),
        ];
    }

    /// <inheritdoc />
    public async Task<OperationResult<FirstProjectProgress?>> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(ProgressFile))
        {
            return OperationResult<FirstProjectProgress?>.Ok(null);
        }

        var loaded = await _yaml
            .LoadAsync(ProgressFile, () => new FirstProjectProgress(), ct)
            .ConfigureAwait(false);

        return loaded.Failed
            ? OperationResult<FirstProjectProgress?>.Fail(loaded.Error!, loaded.ExitCode)
            : OperationResult<FirstProjectProgress?>.Ok(
                loaded.Value!.Slug.Length == 0 ? null : loaded.Value);
    }

    /// <inheritdoc />
    public async Task<string?> StaleReasonAsync(FirstProjectProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (!Directory.Exists(progress.Path))
        {
            return $"its folder, {progress.Path}, is no longer there";
        }

        var resolved = await _projects.ResolveAsync(progress.Slug, ct).ConfigureAwait(false);

        return resolved.Failed ? $"'{progress.Slug}' is no longer a registered project" : null;
    }

    /// <inheritdoc />
    public Task<OperationResult> SaveAsync(FirstProjectProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        progress.UpdatedUtc = _clock.GetUtcNow();

        return _yaml.SaveAsync(ProgressFile, progress, restrictPermissions: false, ct);
    }

    /// <inheritdoc />
    public OperationResult Forget()
    {
        try
        {
            File.Delete(ProgressFile);

            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail($"Could not forget the guide's progress: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<FirstProjectProgress>> CreateAsync(
        Starter starter,
        string name,
        string? destination,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(starter);

        var created = await _templates
            .CreateAsync(name, templateHandle: null, destination, remote: null, ct)
            .ConfigureAwait(false);

        if (created.Failed)
        {
            return OperationResult<FirstProjectProgress>.Fail(created.Error!, created.ExitCode);
        }

        var plan = created.Value!;

        foreach (var (relative, content) in starter.Files)
        {
            var file = Path.Combine(plan.TargetPath, relative.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);

            // Line endings fixed rather than taken from however this source
            // file was checked out, so the starter is byte-for-byte the same on
            // every machine and Git has nothing to warn about.
            await File.WriteAllTextAsync(
                file,
                content(name).Replace("\r\n", "\n", StringComparison.Ordinal),
                ct).ConfigureAwait(false);
        }

        // Best effort, as project new's own first commit is: with no Git
        // identity there is no commit, and the guide checks for one before it
        // gets here, so this is the unusual case rather than the expected one.
        await _git
            .CommitAllAsync(plan.TargetPath, $"Start from Loadout's {starter.Id} starter", ct)
            .ConfigureAwait(false);

        var head = await _git.ResolveAsync(plan.TargetPath, "HEAD", ct).ConfigureAwait(false);

        // Registering queues a first-session onboarding, which reads a codebase
        // and records what it finds. There is nothing to find in three files,
        // and the guide's own planning session is that first session, so it is
        // recorded as set aside rather than left to take the session over.
        await _tasks.DeclareAsync(
            plan.Slug,
            ProjectOnboardingTask.Id,
            TaskState.Dropped,
            Environment.UserName,
            ProjectOnboardingTask.Title,
            "Made by 'loadout start', whose planning session is this project's first session.",
            ct).ConfigureAwait(false);

        var now = _clock.GetUtcNow();
        var progress = new FirstProjectProgress
        {
            Starter = starter.Id,
            Name = name,
            Slug = plan.Slug,
            Path = plan.TargetPath,
            StarterCommit = head.Succeeded ? head.Value : null,
            StartedUtc = now,
        };

        progress.Finish(GuideStep.Create);

        var saved = await SaveAsync(progress, ct).ConfigureAwait(false);

        return saved.Failed
            ? OperationResult<FirstProjectProgress>.Fail(saved.Error!, saved.ExitCode)
            : OperationResult<FirstProjectProgress>.Ok(progress);
    }

    /// <inheritdoc />
    public async Task<OperationResult> ProtectAsync(FirstProjectProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var installed = await _policies.InstallHookAsync(progress.Path, ct).ConfigureAwait(false);

        if (installed.Failed)
        {
            return installed;
        }

        progress.Finish(GuideStep.Protect);

        return await SaveAsync(progress, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<PlanOutcome>> KeepPlanAsync(
        FirstProjectProgress progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var changed = await _git.ListChangedFilesAsync(progress.Path, ct).ConfigureAwait(false);

        IReadOnlyList<string> others = changed.Succeeded
            ? [.. changed.Value!.Where(path => !IsPlan(path))]
            : [];

        if (!File.Exists(Path.Combine(progress.Path, FirstProjectBriefs.PlanFile)))
        {
            return OperationResult<PlanOutcome>.Ok(new PlanOutcome(false, others));
        }

        // Only the plan. Anything else the session changed was against what it
        // was asked, and is the person's to keep or not, not the guide's to
        // fold quietly into a commit called "plan".
        var committed = await _git
            .CommitPathsAsync(progress.Path, "Plan the first project", [FirstProjectBriefs.PlanFile], ct)
            .ConfigureAwait(false);

        if (committed.Failed)
        {
            return OperationResult<PlanOutcome>.Fail(committed.Error!, committed.ExitCode);
        }

        var head = await _git.ResolveAsync(progress.Path, "HEAD", ct).ConfigureAwait(false);

        progress.PlanCommit = head.Succeeded ? head.Value : progress.StarterCommit;
        progress.Finish(GuideStep.Plan);

        var saved = await SaveAsync(progress, ct).ConfigureAwait(false);

        return saved.Failed
            ? OperationResult<PlanOutcome>.Fail(saved.Error!, saved.ExitCode)
            : OperationResult<PlanOutcome>.Ok(new PlanOutcome(true, others));
    }

    /// <inheritdoc />
    public async Task<OperationResult<FirstProjectReview>> ReviewAsync(
        FirstProjectProgress progress,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var from = progress.PlanCommit ?? progress.StarterCommit;
        var saved = string.Empty;

        if (from is { Length: > 0 })
        {
            var diff = await _git.DiffAsync(progress.Path, from, "HEAD", summary: true, ct).ConfigureAwait(false);

            saved = diff.Succeeded ? diff.Value!.TrimEnd() : string.Empty;
        }

        var changed = await _git.ListChangedFilesAsync(progress.Path, ct).ConfigureAwait(false);

        if (changed.Failed)
        {
            return OperationResult<FirstProjectReview>.Fail(changed.Error!, changed.ExitCode);
        }

        var page = Starters.Find(progress.Starter)?.Page ?? "index.html";
        var pagePath = Path.Combine(progress.Path, page);

        return OperationResult<FirstProjectReview>.Ok(new FirstProjectReview(
            saved,
            changed.Value!,
            File.Exists(pagePath) ? pagePath : null));
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> KeepBuildAsync(FirstProjectProgress progress, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(progress);

        return _git.CommitAllAsync(progress.Path, "Build the first project from its plan", ct);
    }

    private static bool IsPlan(string path) =>
        string.Equals(path.Replace('\\', '/').Trim('"'), FirstProjectBriefs.PlanFile, StringComparison.OrdinalIgnoreCase);
}
