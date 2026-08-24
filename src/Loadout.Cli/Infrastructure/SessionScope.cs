using Loadout.Cli.Commands;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Decides which sessions a command is asking about.
/// <para>
/// Standing in a repository almost always means wanting that repository's
/// sessions, so that is the default rather than a flag. The alternative —
/// listing everything and making somebody scan for the right project — is the
/// behaviour both agents already have, and the reason this exists.
/// </para>
/// </summary>
public sealed class SessionScope
{
    private readonly IProjectService _projects;

    public SessionScope(IProjectService projects) => _projects = projects;

    public async Task<SessionQuery> QueryAsync(
        SessionSettings settings,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // An explicitly named project wins over where the shell happens to be.
        if (settings.Project is { Length: > 0 } named)
        {
            return new SessionQuery(named, settings.Agent, Limit: settings.Limit);
        }

        if (settings.All)
        {
            return new SessionQuery(Agent: settings.Agent, Limit: settings.Limit);
        }

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolved = await _projects.ResolveFromDirectoryAsync(directory, ct).ConfigureAwait(false);

        // Outside a registered project there is nothing to narrow to, so the
        // whole history is the honest answer rather than an empty list.
        return resolved.Succeeded && resolved.Value?.Entry.Slug is { Length: > 0 } slug
            ? new SessionQuery(slug, settings.Agent, Limit: settings.Limit)
            : new SessionQuery(Agent: settings.Agent, Limit: settings.Limit);
    }
}
