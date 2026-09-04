using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Projects;

/// <inheritdoc />
internal sealed class ProjectService : IProjectService
{
    /// <summary>
    /// How deep discovery descends below a configured root. Two levels covers
    /// the usual ~/git/repo and ~/git/org/repo layouts without turning a scan
    /// into a full disk crawl, which spec section 64 forbids by default.
    /// </summary>
    private const int DiscoveryDepth = 3;

    private readonly IConfigurationService _configuration;
    private readonly IWorkspaceManager _workspace;
    private readonly IGitManager _git;
    private readonly IPathSemantics _paths;

    public ProjectService(
        IConfigurationService configuration,
        IWorkspaceManager workspace,
        IGitManager git,
        IPathSemantics paths)
    {
        _configuration = configuration;
        _workspace = workspace;
        _git = git;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<ProjectResolution>>> ListAsync(
        CancellationToken ct = default)
    {
        var registryResult = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);
        if (registryResult.Failed)
        {
            return OperationResult<IReadOnlyList<ProjectResolution>>.Fail(
                registryResult.Error!, registryResult.ExitCode);
        }

        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult<IReadOnlyList<ProjectResolution>>.Fail(
                machineResult.Error!, machineResult.ExitCode);
        }

        var machine = machineResult.Value!;

        var projects = registryResult.Value!.Projects
            .Select(entry => Join(entry, machine))
            .OrderByDescending(p => p.Pinned)
            .ThenByDescending(p => p.LastLaunchedUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(p => p.LaunchCount)
            .ThenBy(p => p.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return OperationResult<IReadOnlyList<ProjectResolution>>.Ok(projects);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectResolution>> ResolveAsync(
        string handle,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(handle))
        {
            return OperationResult<ProjectResolution>.Fail(
                "No project was named.", ExitCode.InvalidArguments);
        }

        var listResult = await ListAsync(ct).ConfigureAwait(false);
        if (listResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(listResult.Error!, listResult.ExitCode);
        }

        var match = listResult.Value!.FirstOrDefault(p => Matches(p.Entry, handle));

        return match is not null
            ? OperationResult<ProjectResolution>.Ok(match)
            : OperationResult<ProjectResolution>.Fail(
                $"No project matches '{handle}'.", ExitCode.ProjectNotFound);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectResolution>> ResolveFromDirectoryAsync(
        string directory,
        CancellationToken ct = default)
    {
        var rootResult = await _git.FindRepositoryRootAsync(directory, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        var root = rootResult.Value!;

        var listResult = await ListAsync(ct).ConfigureAwait(false);
        if (listResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(listResult.Error!, listResult.ExitCode);
        }

        // Path first: it is exact, and it distinguishes two clones of one
        // repository that are deliberately registered as separate projects.
        var byPath = listResult.Value!.FirstOrDefault(
            p => p.LocalPath is not null && _paths.PathsEqual(p.LocalPath, root));

        if (byPath is not null)
        {
            return OperationResult<ProjectResolution>.Ok(byPath);
        }

        // Then what the repository says about itself. This is what survives a
        // move: the recorded path no longer matches, but the repository still
        // knows which project it is, so the launcher can find it and say the
        // path needs updating rather than claiming it has never seen it.
        var markedSlug = await ReadMarkAsync(root, ct).ConfigureAwait(false);

        if (markedSlug is not null)
        {
            var byMark = listResult.Value!.FirstOrDefault(
                p => p.Entry.Slug.Equals(markedSlug, StringComparison.OrdinalIgnoreCase));

            if (byMark is not null)
            {
                return OperationResult<ProjectResolution>.Ok(byMark);
            }
        }

        var stateResult = await _git.GetStateAsync(root, ct).ConfigureAwait(false);
        if (stateResult.Succeeded && stateResult.Value!.RemoteUrl is not null)
        {
            var byRemote = listResult.Value!.FirstOrDefault(
                p => GitRemote.AreEquivalent(p.Entry.Remote, stateResult.Value!.RemoteUrl));

            if (byRemote is not null)
            {
                return OperationResult<ProjectResolution>.Ok(byRemote);
            }
        }

        return OperationResult<ProjectResolution>.Fail(
            $"'{root}' is a Git repository but is not a registered project. "
            + "Register it with: loadout project add",
            ExitCode.ProjectNotFound);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectResolution>> AddAsync(
        string repositoryPath,
        string? slug = null,
        CancellationToken ct = default)
    {
        var stateResult = await _git.GetStateAsync(repositoryPath, ct).ConfigureAwait(false);
        if (stateResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(stateResult.Error!, stateResult.ExitCode);
        }

        var state = stateResult.Value!;

        var resolvedSlug = NormaliseSlug(
            slug
            ?? GitRemote.InferRepositoryName(state.RemoteUrl)
            ?? Path.GetFileName(state.Root));

        if (resolvedSlug.Length == 0)
        {
            return OperationResult<ProjectResolution>.Fail(
                "A project slug could not be derived; pass one explicitly.", ExitCode.InvalidArguments);
        }

        var registryResult = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);
        if (registryResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(registryResult.Error!, registryResult.ExitCode);
        }

        var registry = registryResult.Value!;

        var existing = registry.Projects.FirstOrDefault(
            p => string.Equals(p.Slug, resolvedSlug, StringComparison.OrdinalIgnoreCase));

        if (existing is null && state.RemoteUrl is not null)
        {
            // A different slug pointing at the same remote is almost always a
            // re-registration from another machine rather than a new project.
            existing = registry.Projects.FirstOrDefault(
                p => GitRemote.AreEquivalent(p.Remote, state.RemoteUrl));
        }

        var entry = existing ?? new ProjectRegistryEntry
        {
            Id = Guid.NewGuid().ToString(),
            Slug = resolvedSlug,
            Name = Path.GetFileName(state.Root),
            Remote = state.RemoteUrl ?? string.Empty,
        };

        if (existing is null)
        {
            registry.Projects.Add(entry);

            var writeResult = await _workspace.WriteRegistryAsync(registry, ct).ConfigureAwait(false);
            if (writeResult.Failed)
            {
                return OperationResult<ProjectResolution>.Fail(writeResult.Error!, writeResult.ExitCode);
            }

            // A manifest may already exist: the workspace is shared, so another
            // machine may have written one, or a person may have hand-authored
            // it with context, profiles and environment bindings. Overwriting it
            // with a fresh skeleton would silently destroy all of that, which is
            // exactly the data loss spec section 47 rules out.
            var existingManifest = await _workspace.ReadProjectAsync(entry.Slug, ct)
                .ConfigureAwait(false);

            if (existingManifest.Failed)
            {
                var manifestResult = await _workspace.WriteProjectAsync(
                    new ProjectManifest
                    {
                        Id = entry.Id,
                        Slug = entry.Slug,
                        Name = entry.Name,
                        Repository = new ProjectRepository
                        {
                            Remote = entry.Remote,
                            DefaultBranch = state.Branch ?? "main",
                        },
                    },
                    ct).ConfigureAwait(false);

                if (manifestResult.Failed)
                {
                    return OperationResult<ProjectResolution>.Fail(
                        manifestResult.Error!, manifestResult.ExitCode);
                }
            }
            else
            {
                // Adopt the existing manifest's identity so the registry row and
                // the manifest agree on the project UUID.
                entry.Id = string.IsNullOrWhiteSpace(existingManifest.Value!.Id)
                    ? entry.Id
                    : existingManifest.Value.Id;

                entry.Name = existingManifest.Value.Name;
                entry.DefaultAgent = existingManifest.Value.Agents.Default;
                entry.Aliases = existingManifest.Value.Aliases;

                var registryUpdate = await _workspace.WriteRegistryAsync(registry, ct)
                    .ConfigureAwait(false);

                if (registryUpdate.Failed)
                {
                    return OperationResult<ProjectResolution>.Fail(
                        registryUpdate.Error!, registryUpdate.ExitCode);
                }
            }
        }

        var mapResult = await MapLocallyAsync(entry, state.Root, ct).ConfigureAwait(false);
        if (mapResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(mapResult.Error!, mapResult.ExitCode);
        }

        return await ResolveAsync(entry.Slug, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectRemoval>> RemoveAsync(
        string handle,
        bool fromWorkspace,
        CancellationToken ct = default)
    {
        var resolveResult = await ResolveAsync(handle, ct).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return OperationResult<ProjectRemoval>.Fail(resolveResult.Error!, resolveResult.ExitCode);
        }

        var slug = resolveResult.Value!.Entry.Slug;

        // Read and written under one lock. Loading, changing and saving
        // separately means another launcher that registered a project in the
        // meantime has its registration removed by this write — the file stays
        // valid and nothing reports a problem, which is what makes it worth
        // guarding against rather than noticing later.
        var saveResult = await _configuration
            .UpdateMachineAsync(machine => machine.Projects.Remove(slug), ct)
            .ConfigureAwait(false);

        if (saveResult.Failed)
        {
            return OperationResult<ProjectRemoval>.Fail(saveResult.Error!, saveResult.ExitCode);
        }

        if (!fromWorkspace)
        {
            // The local mapping is gone. The source repository is untouched:
            // removing a registration never deletes code.
            return OperationResult<ProjectRemoval>.Ok(Describe(slug, fromWorkspace: false));
        }

        var registryResult = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);
        if (registryResult.Failed)
        {
            return OperationResult<ProjectRemoval>.Fail(registryResult.Error!, registryResult.ExitCode);
        }

        var registry = registryResult.Value!;
        registry.Projects.RemoveAll(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

        var written = await _workspace.WriteRegistryAsync(registry, ct).ConfigureAwait(false);

        return written.Failed
            ? OperationResult<ProjectRemoval>.Fail(written.Error!, written.ExitCode)
            : OperationResult<ProjectRemoval>.Ok(Describe(slug, fromWorkspace: true));
    }

    /// <summary>
    /// What is left in the workspace after the rows have gone.
    /// </summary>
    /// <remarks>
    /// The registry row is removed; the directory holding the project's
    /// instructions, rules and memory is not. That is deliberate — it is the
    /// only copy of what an agent learned about a codebase — but it was also
    /// not said, and the option's own description implied the definition went
    /// with it. Reporting what remains is what makes the two agree.
    /// </remarks>
    private ProjectRemoval Describe(string slug, bool fromWorkspace)
    {
        if (!_workspace.IsAvailable())
        {
            return new ProjectRemoval(slug, fromWorkspace, null, 0);
        }

        var definition = Path.Combine(_workspace.LocalPath, "projects", slug);

        if (!Directory.Exists(definition))
        {
            return new ProjectRemoval(slug, fromWorkspace, null, 0);
        }

        var files = Directory.EnumerateFiles(definition, "*", SearchOption.AllDirectories).Count();

        return new ProjectRemoval(slug, fromWorkspace, definition, files);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<DiscoveredRepository>>> DiscoverAsync(
        CancellationToken ct = default)
    {
        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult<IReadOnlyList<DiscoveredRepository>>.Fail(
                machineResult.Error!, machineResult.ExitCode);
        }

        var listResult = await ListAsync(ct).ConfigureAwait(false);
        if (listResult.Failed)
        {
            return OperationResult<IReadOnlyList<DiscoveredRepository>>.Fail(
                listResult.Error!, listResult.ExitCode);
        }

        var known = listResult.Value!;
        var found = new List<DiscoveredRepository>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var root in machineResult.Value!.DiscoveryRoots)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var repository in FindRepositories(root, DiscoveryDepth))
            {
                if (!seen.Add(_paths.Canonicalise(repository)))
                {
                    continue;
                }

                var stateResult = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);
                var remote = stateResult.Succeeded ? stateResult.Value!.RemoteUrl : null;

                var match = known.FirstOrDefault(p =>
                    (p.LocalPath is not null && _paths.PathsEqual(p.LocalPath, repository))
                    || (remote is not null && GitRemote.AreEquivalent(p.Entry.Remote, remote)));

                found.Add(new DiscoveredRepository(
                    repository,
                    Path.GetFileName(repository),
                    remote,
                    match is not null,
                    match?.Entry.Slug));
            }
        }

        return OperationResult<IReadOnlyList<DiscoveredRepository>>.Ok(found);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RecordLaunchAsync(
        string slug,
        string agent,
        CancellationToken ct = default)
    {
        // The most contended write there is: it happens on every launch, and
        // launching two projects at once is ordinary. Read separately from the
        // write, two launches read the same count, both write one more than it,
        // and the count goes up by one instead of two.
        var mapped = false;

        var saved = await _configuration.UpdateMachineAsync(
            machine =>
            {
                if (!machine.Projects.TryGetValue(slug, out var entry))
                {
                    return;
                }

                mapped = true;

                entry.LastLaunchedUtc = DateTimeOffset.UtcNow;
                entry.LaunchCount++;
                entry.LastAgent = agent;
            },
            ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult.Fail(saved.Error!, saved.ExitCode);
        }

        return mapped
            ? OperationResult.Ok()
            : OperationResult.Fail(
                $"'{slug}' is not mapped on this machine.", ExitCode.ProjectNotFound);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RelocateAsync(
        string handle,
        string newPath,
        CancellationToken ct = default)
    {
        var resolveResult = await ResolveAsync(handle, ct).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return OperationResult.Fail(resolveResult.Error!, resolveResult.ExitCode);
        }

        var rootResult = await _git.FindRepositoryRootAsync(newPath, ct).ConfigureAwait(false);
        if (rootResult.Failed)
        {
            return OperationResult.Fail(rootResult.Error!, rootResult.ExitCode);
        }

        return await MapLocallyAsync(resolveResult.Value!.Entry, rootResult.Value!, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectResolution>> CloneAsync(
        string handle,
        string? destination = null,
        CancellationToken ct = default)
    {
        var resolveResult = await ResolveAsync(handle, ct).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(
                resolveResult.Error!, resolveResult.ExitCode);
        }

        var project = resolveResult.Value!;

        if (project.IsAvailableLocally)
        {
            return OperationResult<ProjectResolution>.Fail(
                $"'{project.Entry.Name}' is already present at '{project.LocalPath}'.");
        }

        if (string.IsNullOrWhiteSpace(project.Entry.Remote))
        {
            // Without a remote there is nothing to clone from, and saying so is
            // more useful than a git error about an empty URL.
            return OperationResult<ProjectResolution>.Fail(
                $"'{project.Entry.Name}' has no remote recorded, so it cannot be cloned. "
                + "Locate an existing clone instead: loadout project relocate "
                + project.Entry.Slug + " <path>",
                ExitCode.RepositoryUnavailable);
        }

        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(
                machineResult.Error!, machineResult.ExitCode);
        }

        // Every other way this method can fail says what went wrong and what to
        // do about it. Throwing here made the one case a person is most likely
        // to hit on a new machine — no clone root set yet — arrive as an
        // unhandled exception with a generic exit code, which is the least
        // useful of all of them.
        var cloneRoot = machineResult.Value!.DefaultCloneRoot;

        if (destination is null && string.IsNullOrWhiteSpace(cloneRoot))
        {
            return OperationResult<ProjectResolution>.Fail(
                "No clone root is configured on this machine, so there is nowhere to put "
                + $"'{project.Entry.Name}'. Set one with: loadout config set clone-root <path>, "
                + $"or pass a destination: loadout project clone {project.Entry.Slug} <path>",
                ExitCode.InvalidArguments);
        }

        var target = destination ?? Path.Combine(cloneRoot!, project.Entry.Slug);

        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            // Cloning into an occupied directory would either fail obscurely or
            // mix two repositories together.
            return OperationResult<ProjectResolution>.Fail(
                $"'{target}' already exists and is not empty. Pass a different destination, or "
                + $"register the existing clone: loadout project relocate {project.Entry.Slug} <path>");
        }

        var manifest = await _workspace.ReadProjectAsync(project.Entry.Slug, ct).ConfigureAwait(false);
        var branch = manifest.Succeeded ? manifest.Value!.Repository.DefaultBranch : null;

        var cloneResult = await _git.CloneAsync(project.Entry.Remote, target, branch, ct)
            .ConfigureAwait(false);

        if (cloneResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(cloneResult.Error!, cloneResult.ExitCode);
        }

        var mapResult = await MapLocallyAsync(project.Entry, target, ct).ConfigureAwait(false);
        if (mapResult.Failed)
        {
            return OperationResult<ProjectResolution>.Fail(mapResult.Error!, mapResult.ExitCode);
        }

        return await ResolveAsync(project.Entry.Slug, ct).ConfigureAwait(false);
    }

    private async Task<OperationResult> MapLocallyAsync(
        ProjectRegistryEntry entry,
        string localPath,
        CancellationToken ct)
    {
        // Registration is where losing a write costs the most: the setup that
        // ran alongside this one registered a project, and a save built on a
        // copy read before it would drop that project with nothing to say so.
        var saved = await _configuration.UpdateMachineAsync(
            machine =>
            {
                if (machine.Projects.TryGetValue(entry.Slug, out var existing))
                {
                    // Launch history is preserved across a relocation: the
                    // project is the same one, it just moved.
                    existing.Id = entry.Id;
                    existing.Path = localPath;
                }
                else
                {
                    machine.Projects[entry.Slug] = new MachineProjectEntry
                    {
                        Id = entry.Id,
                        Path = localPath,
                    };
                }
            },
            ct).ConfigureAwait(false);

        if (saved.Succeeded)
        {
            await MarkRepositoryAsync(entry.Slug, localPath, ct).ConfigureAwait(false);
        }

        return saved;
    }

    /// <summary>
    /// Records inside the repository which project it belongs to.
    /// <para>
    /// The mapping was one-directional before this: the launcher knew where a
    /// project lived, but a directory could not say what it was. That fails in
    /// the cases that matter — a repository moved on disk, a second clone, a
    /// worktree, or a directory holding several repositories where agent state
    /// was recorded against the parent and could belong to any of them.
    /// </para>
    /// <para>
    /// It goes in the repository's own Git configuration, which lives in
    /// .git/config and is never committed. Spec section 9's rule is about what
    /// a repository's contents hold, and this adds nothing to them; a tracked
    /// marker file would breach it, and the launcher's own policy check would
    /// rightly flag it.
    /// </para>
    /// <para>
    /// A failure here is not fatal. The mark is a convenience that makes
    /// resolution robust, not the source of truth, which stays in the machine
    /// configuration.
    /// </para>
    /// </summary>
    private async Task<OperationResult> MarkRepositoryAsync(
        string slug,
        string localPath,
        CancellationToken ct) =>
        await _git.SetLocalConfigValueAsync(IProjectService.ProjectMarker, slug, localPath, ct)
            .ConfigureAwait(false);

    /// <summary>
    /// Reads the project a repository claims to belong to, under either the
    /// current key or the one used before the tool was renamed.
    /// </summary>
    private async Task<string?> ReadMarkAsync(string root, CancellationToken ct)
    {
        var current = await _git
            .GetConfigValueAsync(IProjectService.ProjectMarker, root, ct)
            .ConfigureAwait(false);

        if (current.Succeeded && current.Value is { Length: > 0 })
        {
            return current.Value;
        }

        var legacy = await _git
            .GetConfigValueAsync(IProjectService.LegacyProjectMarker, root, ct)
            .ConfigureAwait(false);

        return legacy.Succeeded && legacy.Value is { Length: > 0 } ? legacy.Value : null;
    }

    private ProjectResolution Join(ProjectRegistryEntry entry, MachineConfig machine)
    {
        if (!machine.Projects.TryGetValue(entry.Slug, out var local))
        {
            return new ProjectResolution(entry, null, null, 0, false);
        }

        // A recorded path that no longer exists is reported as unavailable
        // rather than as a broken project, so spec section 28 can offer to
        // clone or relocate it.
        var path = Directory.Exists(local.Path) ? local.Path : null;

        return new ProjectResolution(entry, path, local.LastLaunchedUtc, local.LaunchCount, local.Pinned);
    }

    private static bool Matches(ProjectRegistryEntry entry, string handle) =>
        string.Equals(entry.Slug, handle, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Name, handle, StringComparison.OrdinalIgnoreCase)
        || string.Equals(entry.Id, handle, StringComparison.OrdinalIgnoreCase)
        || entry.Aliases.Any(a => string.Equals(a, handle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Lower-cases and strips characters that would be awkward on the command line.</summary>
    internal static string NormaliseSlug(string value)
    {
        var cleaned = new string(value
            .Trim()
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-')
            .ToArray());

        return cleaned.Trim('-').ToLowerInvariant();
    }

    /// <summary>
    /// Walks a configured root looking for repository directories, bounded by
    /// depth and never following a repository into itself.
    /// </summary>
    private static IEnumerable<string> FindRepositories(string root, int remainingDepth)
    {
        if (remainingDepth <= 0 || !Directory.Exists(root))
        {
            yield break;
        }

        if (Directory.Exists(Path.Combine(root, ".git")))
        {
            // Found a repository. Its subdirectories are source code, not more
            // projects, so the walk stops here rather than descending into
            // vendored dependencies.
            yield return root;
            yield break;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable directory is skipped rather than aborting the scan.
            // On macOS this is what a protected location looks like when the
            // launcher has not been granted access, and spec section 85 says
            // normal permission behaviour should simply apply.
            yield break;
        }

        foreach (var child in children)
        {
            var name = Path.GetFileName(child);

            // Hidden directories are dotfile stores and caches, not project
            // roots, and descending into them is how a scan becomes slow.
            if (name.StartsWith('.') || name is "node_modules" or "bin" or "obj")
            {
                continue;
            }

            foreach (var repository in FindRepositories(child, remainingDepth - 1))
            {
                yield return repository;
            }
        }
    }
}
