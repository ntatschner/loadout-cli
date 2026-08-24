using Loadout.Cli.Commands;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Cli.Infrastructure;

/// <summary>A settings file a status line command applies to.</summary>
/// <param name="Description">How to name it to a person, for example the project slug.</param>
/// <param name="SettingsPath">The Claude settings file itself.</param>
public sealed record StatuslineTarget(string Description, string SettingsPath);

/// <summary>
/// Works out which Claude settings files a command should touch.
/// <para>
/// Shared by install, uninstall and show so all three agree on what
/// <c>--project</c>, <c>--all</c> and <c>--global</c> mean. Getting that
/// consistent matters more than it looks: uninstalling from a different place
/// than install wrote to would leave a status line nobody can get rid of.
/// </para>
/// </summary>
public sealed class StatuslineTargets
{
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IEnvironmentProvider _environment;

    public StatuslineTargets(
        IProjectService projects,
        IWorkspaceManager workspace,
        IEnvironmentProvider environment)
    {
        _projects = projects;
        _workspace = workspace;
        _environment = environment;
    }

    /// <summary>
    /// Where this launcher is on disk, which is what gets written into the
    /// settings file as the command to run.
    /// </summary>
    public static string? ExecutablePath() => Environment.ProcessPath;

    /// <summary>
    /// The Claude user settings file. Claude reads this for every session on
    /// the machine, whoever started it.
    /// </summary>
    public string GlobalSettingsPath() =>
        Path.Combine(_environment.HomeDirectory, ".claude", "settings.json");

    /// <summary>
    /// The settings file the launcher hands Claude for a project, which is the
    /// path the Claude adapter already passes with --settings.
    /// </summary>
    public string ProjectSettingsPath(string slug) =>
        Path.Combine(_workspace.LocalPath, "projects", slug, "agents", "claude", "settings.json");

    public async Task<OperationResult<IReadOnlyList<StatuslineTarget>>> ResolveAsync(
        StatuslineTargetSettings settings,
        CancellationToken ct = default)
    {
        var targets = new List<StatuslineTarget>();

        if (settings.Global)
        {
            targets.Add(new StatuslineTarget("every session on this machine", GlobalSettingsPath()));
        }

        if (settings.All)
        {
            var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

            if (listed.Failed)
            {
                return OperationResult<IReadOnlyList<StatuslineTarget>>.Fail(
                    listed.Error!, listed.ExitCode);
            }

            foreach (var project in listed.Value!)
            {
                targets.Add(new StatuslineTarget(
                    project.Entry.Slug,
                    ProjectSettingsPath(project.Entry.Slug)));
            }

            if (targets.Count == 0)
            {
                return OperationResult<IReadOnlyList<StatuslineTarget>>.Fail(
                    "No projects are registered, so there is nothing to install into.",
                    ExitCode.ProjectNotFound);
            }

            return OperationResult<IReadOnlyList<StatuslineTarget>>.Ok(targets);
        }

        if (settings.Project is { Length: > 0 } slug)
        {
            targets.Add(new StatuslineTarget(slug, ProjectSettingsPath(slug)));

            return OperationResult<IReadOnlyList<StatuslineTarget>>.Ok(targets);
        }

        if (targets.Count > 0)
        {
            // --global on its own is a complete instruction; there is no need
            // to also find a project.
            return OperationResult<IReadOnlyList<StatuslineTarget>>.Ok(targets);
        }

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolved = await _projects.ResolveFromDirectoryAsync(directory, ct).ConfigureAwait(false);

        if (resolved.Failed || resolved.Value is null)
        {
            return OperationResult<IReadOnlyList<StatuslineTarget>>.Fail(
                $"{directory} is not a registered project. Name one with --project, use --all "
                + "for every project, or use --global to apply it to every session on this machine.",
                ExitCode.ProjectNotFound);
        }

        targets.Add(new StatuslineTarget(
            resolved.Value.Entry.Slug,
            ProjectSettingsPath(resolved.Value.Entry.Slug)));

        return OperationResult<IReadOnlyList<StatuslineTarget>>.Ok(targets);
    }
}
