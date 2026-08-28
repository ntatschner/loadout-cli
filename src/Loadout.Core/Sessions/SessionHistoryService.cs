using Loadout.Core.Projects;
using Loadout.Models.Results;

namespace Loadout.Core.Sessions;

/// <summary>How a session listing should be narrowed.</summary>
/// <param name="ProjectSlug">Only sessions belonging to this registered project.</param>
/// <param name="Agent">Only sessions of this agent.</param>
/// <param name="Directory">Only sessions that ran in this directory or below it.</param>
/// <param name="Limit">How many to return once filtered.</param>
public sealed record SessionQuery(
    string? ProjectSlug = null,
    string? Agent = null,
    string? Directory = null,
    int Limit = 20);

/// <summary>Recent agent sessions across every agent, attributed to projects.</summary>
public interface ISessionHistoryService
{
    Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(
        SessionQuery query,
        CancellationToken ct = default);
}

/// <summary>
/// Gathers what every agent remembers and says which project each session
/// belonged to.
/// <para>
/// The attribution is the part the launcher adds: an agent records the
/// directory it ran in and nothing else, so a list of raw transcripts is a list
/// of paths. Matching those against the registry turns it into a list of
/// projects, which is how somebody actually thinks about what they were doing.
/// </para>
/// </summary>
internal sealed class SessionHistoryService : ISessionHistoryService
{
    /// <summary>
    /// How many transcripts to consider before filtering. Read wide enough
    /// that filtering to one project still finds that project's sessions, but
    /// bounded so building a menu never turns into scanning a year of history.
    /// </summary>
    private const int ScanMultiplier = 10;

    private const int MaximumScan = 500;

    private readonly IReadOnlyList<ISessionHistory> _histories;
    private readonly IProjectService _projects;

    public SessionHistoryService(IEnumerable<ISessionHistory> histories, IProjectService projects)
    {
        _histories = histories.ToList();
        _projects = projects;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<AgentSession>>> ListAsync(
        SessionQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var scan = Math.Min(MaximumScan, Math.Max(query.Limit, query.Limit * ScanMultiplier));

        var gathered = new List<AgentSession>();

        foreach (var history in _histories)
        {
            ct.ThrowIfCancellationRequested();

            if (!history.IsAvailable)
            {
                continue;
            }

            if (query.Agent is { Length: > 0 } wanted
                && !string.Equals(history.Agent, wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var listed = await history.ListAsync(scan, ct).ConfigureAwait(false);

            // One agent's history being unreadable must not hide the other's.
            if (listed.Succeeded)
            {
                gathered.AddRange(listed.Value!);
            }
        }

        var attributed = await AttributeAsync(gathered, ct).ConfigureAwait(false);

        var filtered = attributed.Where(session => Matches(session, query));

        return OperationResult<IReadOnlyList<AgentSession>>.Ok(
            filtered
                .OrderByDescending(s => s.LastActive)
                .Take(query.Limit)
                .ToList());
    }

    /// <summary>
    /// Fills in the project each session belonged to.
    /// <para>
    /// Resolution is cached per directory because a busy week produces dozens
    /// of sessions in the same handful of repositories, and each resolution
    /// walks the filesystem looking for a repository root.
    /// </para>
    /// </summary>
    private async Task<List<AgentSession>> AttributeAsync(
        List<AgentSession> sessions,
        CancellationToken ct)
    {
        var resolved = new Dictionary<string, string?>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        var attributed = new List<AgentSession>(sessions.Count);

        foreach (var session in sessions)
        {
            ct.ThrowIfCancellationRequested();

            if (!resolved.TryGetValue(session.Directory, out var slug))
            {
                var result = await _projects
                    .ResolveFromDirectoryAsync(session.Directory, ct)
                    .ConfigureAwait(false);

                // Not belonging to a registered project is normal: agents get
                // run in scratch directories all the time.
                slug = result.Succeeded ? result.Value?.Entry.Slug : null;

                resolved[session.Directory] = slug;
            }

            attributed.Add(session with { ProjectSlug = slug });
        }

        return attributed;
    }

    private static bool Matches(AgentSession session, SessionQuery query)
    {
        if (query.ProjectSlug is { Length: > 0 } slug
            && !string.Equals(session.ProjectSlug, slug, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (query.Directory is { Length: > 0 } directory && !IsUnder(session.Directory, directory))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether a session ran in a directory or one below it. Compared
    /// case-insensitively only on Windows, because the filesystems this runs on
    /// genuinely differ.
    /// </summary>
    private static bool IsUnder(string path, string root)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            var full = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return full.Equals(fullRoot, comparison)
                || full.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
