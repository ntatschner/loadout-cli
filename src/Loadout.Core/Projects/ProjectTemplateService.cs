using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Projects;

/// <summary>What creating a project from a template did, or would do.</summary>
/// <param name="Slug">Handle the new project answers to.</param>
/// <param name="Name">Display name.</param>
/// <param name="TargetPath">Where the repository was created on this machine.</param>
/// <param name="TemplateSlug">Project the definition was taken from, or null.</param>
/// <param name="Copied">
/// Workspace-relative files brought across, so somebody can see what their new
/// project starts life believing.
/// </param>
/// <param name="Skipped">
/// What was deliberately left behind, and why. Reported rather than silently
/// omitted: a template that quietly dropped half of itself would be found out
/// later, by an agent behaving unlike the project it was modelled on.
/// </param>
/// <param name="Committed">
/// Whether the first commit was made. False when Git had no identity
/// configured, which leaves a usable repository with nothing in its history and
/// is worth saying rather than leaving to be discovered at the first push.
/// </param>
public sealed record ProjectTemplatePlan(
    string Slug,
    string Name,
    string TargetPath,
    string? TemplateSlug,
    IReadOnlyList<string> Copied,
    IReadOnlyDictionary<string, string> Skipped,
    bool Committed);

/// <summary>Creates a new project, optionally modelled on one that exists.</summary>
public interface IProjectTemplateService
{
    /// <summary>
    /// Creates the repository, writes the project definition and registers it.
    /// </summary>
    /// <param name="name">Display name for the new project.</param>
    /// <param name="templateHandle">Project to copy the definition from, or null for a bare one.</param>
    /// <param name="destination">Where to create it, or null for the clone root.</param>
    /// <param name="remote">Remote to record and set on the new repository, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<OperationResult<ProjectTemplatePlan>> CreateAsync(
        string name,
        string? templateHandle = null,
        string? destination = null,
        string? remote = null,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class ProjectTemplateService : IProjectTemplateService
{
    /// <summary>
    /// Directories under a project's workspace definition that a new project
    /// inherits.
    /// <para>
    /// Instructions, rules and per-agent settings are conventions: they say how
    /// work is done here, and that is exactly what somebody starting a second
    /// service of the same shape wants to keep.
    /// </para>
    /// </summary>
    private static readonly string[] InheritedDirectories = ["agents", "rules", "context"];

    /// <summary>
    /// What a template never brings across, and the reason, which is shown.
    /// <para>
    /// Memory is the important one. It is facts an agent established about a
    /// particular codebase — where a thing lives, why a decision was taken,
    /// what broke last time — and none of it is true of a repository that does
    /// not exist yet. Copying it would furnish a new project with confident
    /// claims about code nobody has written.
    /// </para>
    /// </summary>
    private static readonly Dictionary<string, string> NeverInherited = new(StringComparer.Ordinal)
    {
        ["memory"] = "facts about the template's codebase, none of them true of a new one",
    };

    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IGitManager _git;

    public ProjectTemplateService(
        IProjectService projects,
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IGitManager git)
    {
        _projects = projects;
        _workspace = workspace;
        _configuration = configuration;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<OperationResult<ProjectTemplatePlan>> CreateAsync(
        string name,
        string? templateHandle = null,
        string? destination = null,
        string? remote = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (!_workspace.IsAvailable())
        {
            return OperationResult<ProjectTemplatePlan>.Fail(
                "There is no workspace clone on this machine, so there is nowhere to keep the "
                + "project definition. Set one up with: loadout workspace status",
                ExitCode.ConfigurationInvalid);
        }

        var slug = Slugify(name);

        if (slug.Length == 0)
        {
            return OperationResult<ProjectTemplatePlan>.Fail(
                $"'{name}' does not reduce to a usable handle. Names become slugs, so they need "
                + "some letters or digits in them.",
                ExitCode.InvalidArguments);
        }

        var registry = await _workspace.ReadRegistryAsync(ct).ConfigureAwait(false);

        if (registry.Succeeded
            && registry.Value!.Projects.Any(
                p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase)))
        {
            return OperationResult<ProjectTemplatePlan>.Fail(
                $"A project called '{slug}' is already registered. Pick another name, or open the "
                + $"one that exists: loadout code {slug}",
                ExitCode.InvalidArguments);
        }

        // Resolved before anything is created. Every failure below this point
        // would leave a half-made project behind, so the checks that can be
        // done cheaply are all done first.
        ProjectManifest? template = null;

        if (templateHandle is { Length: > 0 })
        {
            var resolved = await _projects.ResolveAsync(templateHandle, ct).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return OperationResult<ProjectTemplatePlan>.Fail(
                    $"There is no project called '{templateHandle}' to model this one on. "
                    + "See what there is with: loadout project list",
                    ExitCode.ProjectNotFound);
            }

            var read = await _workspace
                .ReadProjectAsync(resolved.Value!.Entry.Slug, ct)
                .ConfigureAwait(false);

            if (read.Failed)
            {
                return OperationResult<ProjectTemplatePlan>.Fail(
                    $"'{resolved.Value!.Entry.Slug}' has no definition in the workspace to copy.",
                    ExitCode.ProjectNotFound);
            }

            template = read.Value!;
            templateHandle = resolved.Value.Entry.Slug;
        }

        var located = await LocateAsync(slug, destination, ct).ConfigureAwait(false);

        if (located.Failed)
        {
            return OperationResult<ProjectTemplatePlan>.Fail(located.Error!, located.ExitCode);
        }

        var target = located.Value!;

        Directory.CreateDirectory(target);

        // Deliberately not taken from the template. A manifest's default branch
        // is a fact about a repository that exists — and it drifts: the project
        // this was first tried against had a feature branch recorded as its
        // default, which would have started every new repository modelled on it
        // on a branch named after somebody else's half-finished work. A
        // repository with no history has no branch to inherit.
        const string branch = "main";

        var init = await _git.InitAsync(target, branch, ct).ConfigureAwait(false);

        if (init.Failed)
        {
            return OperationResult<ProjectTemplatePlan>.Fail(init.Error!, init.ExitCode);
        }

        if (remote is { Length: > 0 })
        {
            // Recorded and set, but never pushed to. Creating something on a
            // hosting service is a different decision from creating a
            // repository here, and this makes no network call of its own.
            var set = await _git.SetRemoteAsync(target, "origin", remote, ct).ConfigureAwait(false);

            if (set.Failed)
            {
                return OperationResult<ProjectTemplatePlan>.Fail(set.Error!, set.ExitCode);
            }
        }

        // Written before the project is registered, because AddAsync adopts a
        // definition that already exists and writes a default one only when
        // there is none. Doing it in this order means the template's settings
        // are the project's from the first moment rather than something
        // applied over the top afterwards.
        var manifest = Derive(template, slug, name, remote, branch);
        var written = await _workspace.WriteProjectAsync(manifest, ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<ProjectTemplatePlan>.Fail(written.Error!, written.ExitCode);
        }

        var copied = templateHandle is { Length: > 0 }
            ? CopyDefinition(Path.Combine(_workspace.LocalPath, "projects"), templateHandle, slug)
            : [];

        // A repository with no commits has an unborn HEAD, which several things
        // downstream read as a broken repository rather than a new one: the
        // branch does not exist yet, so it cannot be pushed, branched from or
        // reported on. One commit costs nothing and makes it an ordinary
        // repository from the start.
        //
        // Best effort on purpose. Committing needs an identity configured, and
        // somebody on a fresh machine without one should still end up with
        // their project rather than an error about user.email.
        await File.WriteAllTextAsync(
            Path.Combine(target, "README.md"),
            $"# {name}{System.Environment.NewLine}",
            ct).ConfigureAwait(false);

        var committed = await _git
            .CommitAllAsync(target, "Initial commit", ct)
            .ConfigureAwait(false);

        var firstCommit = committed.Succeeded && committed.Value;

        var added = await _projects.AddAsync(target, slug, ct).ConfigureAwait(false);

        if (added.Failed)
        {
            return OperationResult<ProjectTemplatePlan>.Fail(added.Error!, added.ExitCode);
        }

        return OperationResult<ProjectTemplatePlan>.Ok(
            new ProjectTemplatePlan(
                slug,
                name,
                target,
                templateHandle,
                copied,
                template is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : NeverInherited,
                firstCommit));
    }

    /// <summary>Where the new repository goes, and whether that is somewhere sensible.</summary>
    private async Task<OperationResult<string>> LocateAsync(
        string slug,
        string? destination,
        CancellationToken ct)
    {
        if (destination is { Length: > 0 })
        {
            var chosen = Path.GetFullPath(destination);

            return Occupied(chosen)
                ? OperationResult<string>.Fail(
                    $"'{chosen}' already exists and is not empty. Creating a repository there "
                    + "would mix it with whatever is already in it.",
                    ExitCode.InvalidArguments)
                : OperationResult<string>.Ok(chosen);
        }

        var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        if (machine.Failed)
        {
            return OperationResult<string>.Fail(machine.Error!, machine.ExitCode);
        }

        var root = machine.Value!.DefaultCloneRoot;

        if (string.IsNullOrWhiteSpace(root))
        {
            return OperationResult<string>.Fail(
                "No clone root is configured on this machine, so there is nowhere to put a new "
                + "project. Set one with: loadout config set clone-root <path>, or say where: "
                + $"loadout project new <name> --path <path>",
                ExitCode.InvalidArguments);
        }

        var target = Path.Combine(root!, slug);

        return Occupied(target)
            ? OperationResult<string>.Fail(
                $"'{target}' already exists and is not empty. Pass somewhere else with --path.",
                ExitCode.InvalidArguments)
            : OperationResult<string>.Ok(target);
    }

    private static bool Occupied(string path) =>
        Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any();

    /// <summary>
    /// The new project's definition, taking from the template what describes
    /// how work is done and none of what identifies it.
    /// </summary>
    internal static ProjectManifest Derive(
        ProjectManifest? template,
        string slug,
        string name,
        string? remote,
        string branch)
    {
        var manifest = new ProjectManifest
        {
            Id = Guid.NewGuid().ToString(),
            Slug = slug,
            Name = name,
            Repository = new ProjectRepository
            {
                Remote = remote ?? string.Empty,
                DefaultBranch = branch,
            },
        };

        if (template is null)
        {
            return manifest;
        }

        // Conventions, which is the whole reason for templating: which agent
        // to launch, which shared instruction files apply, which specialists
        // are expected, how the workspace is synced.
        manifest.Agents = new ProjectAgents
        {
            Default = template.Agents.Default,
            Enabled = [.. template.Agents.Enabled],
            Settings = template.Agents.Settings.ToDictionary(
                pair => pair.Key,
                pair => new Dictionary<string, object>(pair.Value),
                StringComparer.Ordinal),
        };

        manifest.Context = new ProjectContext
        {
            Global = [.. template.Context.Global],
            Project = [.. template.Context.Project],
        };

        manifest.Profiles = template.Profiles.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        manifest.Launch = new ProjectLaunch { WorkingDirectory = template.Launch.WorkingDirectory };
        manifest.Workspace = new ProjectWorkspace
        {
            SyncOnLaunch = template.Workspace.SyncOnLaunch,
            SaveOnExit = template.Workspace.SaveOnExit,
        };
        manifest.Specialists = template.Specialists;

        // Aliases and environment bindings are deliberately not inherited.
        // An alias is a second name for one project and cannot belong to two;
        // an environment binding points at a particular project's secrets, and
        // a new project silently reading another's would be the wrong default
        // even though the binding is only a reference.
        return manifest;
    }

    /// <summary>
    /// Copies the parts of a template's definition that live as files rather
    /// than as manifest settings. Returns what was brought across.
    /// </summary>
    internal static IReadOnlyList<string> CopyDefinition(
        string projectsRoot,
        string templateSlug,
        string slug)
    {
        var from = Path.Combine(projectsRoot, templateSlug);
        var to = Path.Combine(projectsRoot, slug);
        var copied = new List<string>();

        foreach (var directory in InheritedDirectories)
        {
            var source = Path.Combine(from, directory);

            if (!Directory.Exists(source))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(from, file);
                var destination = Path.Combine(to, relative);

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, overwrite: true);

                copied.Add(relative.Replace(Path.DirectorySeparatorChar, '/'));
            }
        }

        return copied;
    }

    /// <summary>
    /// Turns a display name into a handle. Lowercase, and anything that is not
    /// a letter, digit, dot or dash becomes a dash, because a slug is typed on
    /// a command line far more often than it is read.
    /// </summary>
    internal static string Slugify(string name)
    {
        var slug = new string([.. name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '-')]);

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }
}
