using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Loadout.Models.Results;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Which project a command was aimed at, when it takes <c>--project</c> and
/// otherwise means the one you are standing in.
/// </summary>
/// <remarks>
/// Shared rather than written out at each call site, because the fallback is
/// the part that is easy to get subtly different: <c>--repo</c> has to win over
/// the working directory, or a command run against another checkout resolves to
/// whichever project the shell happened to be in.
/// </remarks>
internal static class ProjectHandle
{
    internal static Task<OperationResult<ProjectResolution>> ResolveAsync(
        IProjectService projects,
        string? project,
        string? repo,
        CancellationToken ct) =>
        project is { Length: > 0 } handle
            ? projects.ResolveAsync(handle, ct)
            : projects.ResolveFromDirectoryAsync(repo ?? Directory.GetCurrentDirectory(), ct);
}
