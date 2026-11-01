using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Models.Diagnostics;

namespace Loadout.Core.Delivery;

/// <summary>
/// Says whether the CLI each waiting branch's host needs is here and signed
/// in: gh, glab, az, or a project's own delivery command.
/// </summary>
/// <remarks>
/// <para>
/// Found out here rather than halfway through a merge or a release, where a
/// missing <c>glab</c> or a lapsed <c>az login</c> leaves a branch pushed for a
/// pull request that never opened.
/// </para>
/// <para>
/// Only the projects with branches waiting to land are asked about, so a
/// machine not using any of this is told nothing. Each host is asked once,
/// in the first checkout that uses it. A host that is missing or signed out
/// is a warning, not an error: nothing else Loadout does needs it.
/// </para>
/// </remarks>
internal sealed class DeliveryDiagnosticContributor : IDiagnosticContributor
{
    private const string Category = "Delivery";

    private readonly ILandingService _landings;
    private readonly IProjectService _projects;
    private readonly IRepositoryHosts _hosts;

    public DeliveryDiagnosticContributor(ILandingService landings, IProjectService projects, IRepositoryHosts hosts)
    {
        _landings = landings;
        _projects = projects;
        _hosts = hosts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DiagnosticCheck>> ContributeAsync(CancellationToken ct = default)
    {
        var checks = new List<DiagnosticCheck>();
        var asked = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        try
        {
            foreach (var project in _landings.Projects())
            {
                var resolved = await _projects.ResolveAsync(project, ct).ConfigureAwait(false);

                if (resolved.Failed || resolved.Value!.LocalPath is not { } repository)
                {
                    continue;
                }

                var host = await _hosts.ForAsync(project, repository, ct).ConfigureAwait(false);

                if (host.Failed)
                {
                    checks.Add(DiagnosticCheck.Warn(Category, project, host.Error!));

                    continue;
                }

                var chosen = host.Value!.Host;

                if (!chosen.OpensPullRequests)
                {
                    continue;
                }

                if (asked.TryGetValue(chosen.Provider, out var users))
                {
                    users.Add(project);

                    continue;
                }

                asked[chosen.Provider] = [project];

                checks.Add(await chosen.UnavailableAsync(repository, ct).ConfigureAwait(false) is { } cannot
                    ? DiagnosticCheck.Warn(Category, chosen.Provider, cannot + ". Branches waiting in " + project + " can't be opened or merged until it is.")
                    : DiagnosticCheck.Ok(Category, chosen.Provider, "signed in, for " + project));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            checks.Add(DiagnosticCheck.Warn(Category, "Hosts", "could not be checked: " + ex.Message));
        }

        return checks;
    }
}
