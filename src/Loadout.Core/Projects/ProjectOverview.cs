using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Policies;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Projects;

/// <summary>
/// What is worth knowing about a project before launching an agent at it.
/// </summary>
/// <param name="Project">The project itself.</param>
/// <param name="Branch">Checked-out branch, or null when detached or unreadable.</param>
/// <param name="IsClean">False when the working tree has uncommitted changes.</param>
/// <param name="AlwaysLoadedBytes">Instruction text every session pays for.</param>
/// <param name="ScopedRules">Rules that load only when the work touches their paths.</param>
/// <param name="MemoryTopics">Durable facts recorded for this project.</param>
/// <param name="PendingImports">Memory an agent recorded outside the workspace.</param>
/// <param name="Protected">Whether this clone has the pre-commit hook installed.</param>
/// <param name="HookNeedsUpgrade">The hook is the launcher's own but was written by an older version.</param>
/// <param name="TrackedAgentFiles">Agent files committed to the repository, which is a policy breach.</param>
/// <param name="DetectedSpecialists">
/// What the repository appears to be built from. A stable fact about the
/// project, not a judgement about any one task: a project that uses PostgreSQL
/// uses it whatever somebody is doing today, and the resolver decides
/// separately whether that matters for the work in hand.
/// </param>
/// <param name="RunningSessions">
/// How many sessions are running against this project right now.
/// </param>
/// <remarks>
/// The one line here that changes what somebody does next rather than telling
/// them how things stand. Everything else on the panel is a fact about the
/// project; this is a fact about the moment, and launching a second session
/// into a repository somebody is already working in is the mistake it exists
/// to prevent.
/// </remarks>
public sealed record ProjectOverview(
    ProjectResolution Project,
    string? Branch,
    bool IsClean,
    long AlwaysLoadedBytes,
    int ScopedRules,
    int MemoryTopics,
    int PendingImports,
    bool Protected,
    int TrackedAgentFiles,
    bool HookNeedsUpgrade = false,
    IReadOnlyList<SpecialistSelection>? DetectedSpecialists = null,
    int RunningSessions = 0)
{
    /// <summary>
    /// The point past which the always-loaded instructions are worth a look.
    /// Advisory: it prompts a question, it does not stop a launch.
    /// </summary>
    public const long ComfortableAlwaysLoadedBytes = 20 * 1024;

    public bool IsOverBudget => AlwaysLoadedBytes > ComfortableAlwaysLoadedBytes;

    /// <summary>Whether anything here deserves saying before a launch.</summary>
    public bool HasWarnings =>
        IsOverBudget || PendingImports > 0 || TrackedAgentFiles > 0 || !Protected;
}

/// <summary>
/// Gathers the state a person wants in front of them before starting a session.
/// <para>
/// It exists so the interactive launcher can render rather than orchestrate.
/// Assembling this inside the UI would put half a dozen services behind a
/// keyboard prompt, where none of it could be tested without one.
/// </para>
/// </summary>
public interface IProjectOverviewService
{
    /// <summary>
    /// Describes a project. Never fails on account of one part being
    /// unavailable: an overview that refused to render because a repository was
    /// mid-rebase would be least useful exactly when it was most wanted.
    /// </summary>
    Task<OperationResult<ProjectOverview>> DescribeAsync(
        ProjectResolution project,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class ProjectOverviewService : IProjectOverviewService
{
    private readonly IGitManager _git;
    private readonly IWorkspaceManager _workspace;
    private readonly IRuleService _rules;
    private readonly IMemoryService _memory;
    private readonly IMemoryImporter _importer;
    private readonly IPolicyService _policies;
    private readonly IInstructionService _instructions;
    private readonly Sessions.ISessionRegistry _running;

    public ProjectOverviewService(
        IGitManager git,
        IWorkspaceManager workspace,
        IRuleService rules,
        IMemoryService memory,
        IMemoryImporter importer,
        IPolicyService policies,
        IInstructionService instructions,
        Sessions.ISessionRegistry running)
    {
        _git = git;
        _workspace = workspace;
        _rules = rules;
        _memory = memory;
        _importer = importer;
        _policies = policies;
        _instructions = instructions;
        _running = running;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectOverview>> DescribeAsync(
        ProjectResolution project,
        CancellationToken ct = default)
    {
        var slug = project.Entry.Slug;
        var path = project.LocalPath;

        string? branch = null;
        var clean = true;
        var isProtected = false;
        var hookNeedsUpgrade = false;
        var tracked = 0;

        if (path is not null)
        {
            var state = await _git.GetStateAsync(path, ct).ConfigureAwait(false);

            if (state.Succeeded)
            {
                branch = state.Value!.Branch;
                clean = state.Value.IsClean;
            }

            var report = await _policies.CheckAsync(path, ct).ConfigureAwait(false);

            if (report.Succeeded)
            {
                isProtected = report.Value!.HasPreCommitHook;
                hookNeedsUpgrade = report.Value.HookNeedsUpgrade;

                tracked = report.Value!.Findings
                    .Count(f => f.Kind == Models.Policies.PolicyFindingKind.Tracked);
            }
        }

        var alwaysLoaded = 0L;
        var scoped = 0;

        var rules = await _rules.LoadAsync(_workspace.LocalPath, slug, ct).ConfigureAwait(false);

        if (rules.Succeeded)
        {
            alwaysLoaded = rules.Value!
                .Where(rule => rule.AlwaysApply || rule.IsUnscoped)
                .Sum(rule => rule.Bytes);

            scoped = rules.Value!.Count(rule => !rule.AlwaysApply && !rule.IsUnscoped);
        }

        alwaysLoaded += await CoreInstructionBytesAsync(slug, ct).ConfigureAwait(false);

        var topics = await _memory.ListAsync(_workspace.LocalPath, slug, ct).ConfigureAwait(false);

        var pending = path is null
            ? 0
            : await PendingImportsAsync(slug, path, ct).ConfigureAwait(false);

        return OperationResult<ProjectOverview>.Ok(new ProjectOverview(
            project,
            branch,
            clean,
            alwaysLoaded,
            scoped,
            topics.Succeeded ? topics.Value!.Count : 0,
            pending,
            isProtected,
            tracked,
            hookNeedsUpgrade,
            await _instructions
                .DetectAsync(path, _workspace.LocalPath, slug, ct)
                .ConfigureAwait(false),
            await RunningAsync(slug, ct).ConfigureAwait(false)));
    }

    /// <summary>How many sessions are open against a project.</summary>
    /// <remarks>
    /// Never a reason to fail the overview. The registry verifies each entry
    /// against a live process, so this is cheap, but a panel that would not
    /// draw because it could not count sessions would be worse than one that
    /// says nothing about them.
    /// </remarks>
    private async Task<int> RunningAsync(string slug, CancellationToken ct)
    {
        try
        {
            var running = await _running.ListAsync(ct).ConfigureAwait(false);

            return running.Count(session =>
                string.Equals(session.ProjectSlug, slug, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>
    /// The instruction files a launch loads whatever the task, including the
    /// per-agent one the compiler adds implicitly and a migrated CLAUDE.md
    /// lands in.
    /// </summary>
    private async Task<long> CoreInstructionBytesAsync(string slug, CancellationToken ct)
    {
        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (manifest.Failed)
        {
            return 0;
        }

        var root = _workspace.LocalPath;
        var projectRoot = Path.Combine(root, "projects", slug);

        var paths = manifest.Value!.Context.Global
            .Select(relative => Path.Combine(root, ToNative(relative)))
            .Concat(manifest.Value.Context.Project
                .Select(relative => Path.Combine(projectRoot, ToNative(relative))))
            .ToList();

        var agent = manifest.Value.Agents.Default;

        if (!string.IsNullOrWhiteSpace(agent))
        {
            paths.Add(Path.Combine(projectRoot, "agents", agent, "instructions.md"));
        }

        return paths.Where(File.Exists).Sum(path => new FileInfo(path).Length);
    }

    private async Task<int> PendingImportsAsync(string slug, string path, CancellationToken ct)
    {
        var source = _importer.Discover(path);

        if (source is null)
        {
            return 0;
        }

        var preview = await _importer
            .ImportAsync(_workspace.LocalPath, slug, source, apply: false, ct)
            .ConfigureAwait(false);

        return preview.Succeeded ? preview.Value!.Imported.Count : 0;
    }

    private static string ToNative(string relative) =>
        relative.Replace('/', Path.DirectorySeparatorChar);
}
