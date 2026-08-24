using Loadout.Cli.Commands;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Results;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Works out which project an MCP command is about.
/// <para>
/// Shared so the three commands agree on what <c>--project</c> means and on
/// what happens without it. Standing in a repository almost always means
/// meaning that repository, so that is the default rather than a flag.
/// </para>
/// </summary>
public sealed class McpScopeResolver
{
    /// <summary>
    /// Stands in for a project when the servers being edited apply to all of
    /// them. The global file has no project in its path, so the value is never
    /// read — but something has to be passed.
    /// </summary>
    private const string EveryProject = "-";

    private readonly IProjectService _projects;

    public McpScopeResolver(IProjectService projects) => _projects = projects;

    public async Task<OperationResult<string>> SlugAsync(
        McpSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Project is { Length: > 0 } named)
        {
            var resolved = await _projects.ResolveAsync(named, ct).ConfigureAwait(false);

            return resolved.Failed
                ? OperationResult<string>.Fail(resolved.Error!, resolved.ExitCode)
                : OperationResult<string>.Ok(resolved.Value!.Entry.Slug);
        }

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var here = await _projects.ResolveFromDirectoryAsync(directory, ct).ConfigureAwait(false);

        if (here.Succeeded && here.Value is { } project)
        {
            return OperationResult<string>.Ok(project.Entry.Slug);
        }

        // Editing the global set does not need a project, so being outside one
        // is only a problem when the command was about a project.
        return settings.Global
            ? OperationResult<string>.Ok(EveryProject)
            : OperationResult<string>.Fail(
                $"{directory} is not a registered project. Name one with --project, "
                + "or use --global for the servers every project loads.",
                ExitCode.ProjectNotFound);
    }
}
