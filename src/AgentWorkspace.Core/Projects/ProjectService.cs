using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Models.Configuration;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Core.Projects;

/// <inheritdoc />
public sealed class ProjectService : IProjectService
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
            + "Register it with: agentctl project add",
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
    public async Task<OperationResult> RemoveAsync(
        string handle,
        bool fromWorkspace,
        CancellationToken ct = default)
    {
        var resolveResult = await ResolveAsync(handle, ct).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return OperationResult.Fail(resolveResult.Error!, resolveResult.ExitCode);
        }

        var slug = resolveResult.Value!.Entry.Slug;

        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult.Fail(machineResult.Error!, machineResult.ExitCode);
        }

        var machine = machineResult.Value!;
        machine.Projects.Remove(slug);

        var saveResult = await _configuration.SaveMachineAsync(machine, ct).ConfigureAwait(false);
        if (saveResult.Failed || !fromWorkspace)
        {
            // The local mapping is gone either way. The source repository is
            // untouched: removing a registration never deletes code.
            return saveResult;
        }

        var registryResult = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);
        if (registryResult.Failed)
        {
            return OperationResult.Fail(registryResult.Error!, registryResult.ExitCode);
        }

        var registry = registryResult.Value!;
        registry.Projects.RemoveAll(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase));

        return await _workspace.WriteRegistryAsync(registry, ct).ConfigureAwait(false);
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
        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult.Fail(machineResult.Error!, machineResult.ExitCode);
        }

        var machine = machineResult.Value!;

        if (!machine.Projects.TryGetValue(slug, out var entry))
        {
            return OperationResult.Fail(
                $"'{slug}' is not mapped on this machine.", ExitCode.ProjectNotFound);
        }

        entry.LastLaunchedUtc = DateTimeOffset.UtcNow;
        entry.LaunchCount++;
        entry.LastAgent = agent;

        return await _configuration.SaveMachineAsync(machine, ct).ConfigureAwait(false);
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

    private async Task<OperationResult> MapLocallyAsync(
        ProjectRegistryEntry entry,
        string localPath,
        CancellationToken ct)
    {
        var machineResult = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
        if (machineResult.Failed)
        {
            return OperationResult.Fail(machineResult.Error!, machineResult.ExitCode);
        }

        var machine = machineResult.Value!;

        if (machine.Projects.TryGetValue(entry.Slug, out var existing))
        {
            // Launch history is preserved across a relocation: the project is
            // the same one, it just moved.
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

        return await _configuration.SaveMachineAsync(machine, ct).ConfigureAwait(false);
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
