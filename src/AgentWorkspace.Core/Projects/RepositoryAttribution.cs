using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Instructions;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Models.Results;
using AgentWorkspace.Platform.Abstractions;

namespace AgentWorkspace.Core.Projects;

/// <summary>What a directory holding agent state turned out to be.</summary>
public enum AttributionKind
{
    /// <summary>A registered project, so the state belongs to it.</summary>
    Project,

    /// <summary>
    /// A directory holding several repositories. The state was recorded while
    /// working from the parent, so which repository it describes is a question
    /// only a person can answer.
    /// </summary>
    Container,

    /// <summary>A repository on disk that has never been registered.</summary>
    Unregistered,

    /// <summary>
    /// A directory that exists and is not a repository and holds none.
    /// <para>
    /// Distinguished from an unregistered repository because the remedy is
    /// different and telling somebody to register it would send them to a
    /// command that cannot succeed.
    /// </para>
    /// </summary>
    NotARepository,

    /// <summary>Nothing is there any more.</summary>
    Missing,
}

/// <summary>
/// Agent state found outside the workspace, and what it appears to belong to.
/// </summary>
/// <param name="StatePath">Where the state was found.</param>
/// <param name="SubjectPath">The directory it was recorded against.</param>
/// <param name="Kind">What that directory turned out to be.</param>
/// <param name="Slug">The registered project, when there is one.</param>
/// <param name="Repositories">
/// Repositories inside a container, which are the candidates the state could
/// belong to.
/// </param>
/// <param name="Topics">How many memory topics were found.</param>
public sealed record StateAttribution(
    string StatePath,
    string SubjectPath,
    AttributionKind Kind,
    string? Slug,
    IReadOnlyList<string> Repositories,
    int Topics);

/// <summary>
/// Works out what agent state recorded outside the workspace belongs to.
/// <para>
/// Agents key their state by the directory they were started in, which is not
/// always a repository. Somebody working across several repositories from their
/// parent directory accumulates memory there, and it then describes all of them
/// and none of them: the launcher cannot attribute it, and importing it into one
/// project would be a guess presented as a fact.
/// </para>
/// <para>
/// Detecting that is the point. The answer is a person's to give; what the
/// launcher owes them is to notice, say which repositories are in the frame, and
/// not quietly pick one.
/// </para>
/// </summary>
public interface IRepositoryAttribution
{
    /// <summary>
    /// Agent state on this machine that is not accounted for by the workspace,
    /// with what each piece appears to belong to.
    /// </summary>
    Task<OperationResult<IReadOnlyList<StateAttribution>>> SurveyAsync(
        CancellationToken ct = default);

    /// <summary>
    /// Repositories directly inside a directory. Empty when it is a repository
    /// itself, because then its subdirectories are source code.
    /// </summary>
    IReadOnlyList<string> RepositoriesInside(string directory);
}

/// <inheritdoc />
public sealed class RepositoryAttribution : IRepositoryAttribution
{
    private readonly IEnvironmentProvider _environment;
    private readonly IProjectService _projects;
    private readonly IPathSemantics _paths;

    public RepositoryAttribution(
        IEnvironmentProvider environment,
        IProjectService projects,
        IPathSemantics paths)
    {
        _environment = environment;
        _projects = projects;
        _paths = paths;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> RepositoriesInside(string directory)
    {
        if (!Directory.Exists(directory) || IsRepository(directory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(directory)
                .Where(IsRepository)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable directory tells us nothing, which is different from
            // telling us there is nothing there.
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<StateAttribution>>> SurveyAsync(
        CancellationToken ct = default)
    {
        var projectsRoot = Path.Combine(ClaudeHome(), "projects");

        if (!Directory.Exists(projectsRoot))
        {
            return OperationResult<IReadOnlyList<StateAttribution>>.Ok([]);
        }

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IReadOnlyList<StateAttribution>>.Fail(
                listed.Error!, listed.ExitCode);
        }

        var found = new List<StateAttribution>();

        foreach (var directory in Directory.EnumerateDirectories(projectsRoot))
        {
            ct.ThrowIfCancellationRequested();

            var memory = Path.Combine(directory, "memory");

            if (!Directory.Exists(memory))
            {
                continue;
            }

            var topics = Directory.EnumerateFiles(memory, "*.md")
                .Count(file => !Path.GetFileName(file)
                    .Equals("MEMORY.md", StringComparison.OrdinalIgnoreCase));

            if (topics == 0)
            {
                continue;
            }

            found.Add(Attribute(directory, memory, topics, listed.Value!));
        }

        return OperationResult<IReadOnlyList<StateAttribution>>.Ok(
            found.OrderBy(a => a.Kind).ThenBy(a => a.SubjectPath, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    /// <summary>
    /// Whether a directory is a working tree.
    /// <para>
    /// A linked worktree has a .git file rather than a directory, so checking
    /// only for the directory would report every worktree as not a repository
    /// at all.
    /// </para>
    /// </summary>
    private static bool IsRepository(string directory)
    {
        var git = Path.Combine(directory, ".git");

        return Directory.Exists(git) || File.Exists(git);
    }

    private StateAttribution Attribute(
        string stateDirectory,
        string memory,
        int topics,
        IReadOnlyList<ProjectResolution> projects)
    {
        var subject = RecoverPath(Path.GetFileName(stateDirectory));

        var project = projects.FirstOrDefault(
            p => p.LocalPath is not null && _paths.PathsEqual(p.LocalPath, subject));

        if (project is not null)
        {
            return new StateAttribution(
                memory, subject, AttributionKind.Project, project.Entry.Slug, [], topics);
        }

        if (!Directory.Exists(subject))
        {
            return new StateAttribution(memory, subject, AttributionKind.Missing, null, [], topics);
        }

        // Asked before looking inside: a repository's own subdirectories are
        // source code, and one of them being a repository in its own right does
        // not make the parent a container.
        if (IsRepository(subject))
        {
            return new StateAttribution(
                memory, subject, AttributionKind.Unregistered, null, [], topics);
        }

        var inside = RepositoriesInside(subject);

        return inside.Count > 0
            ? new StateAttribution(memory, subject, AttributionKind.Container, null, inside, topics)
            : new StateAttribution(
                memory, subject, AttributionKind.NotARepository, null, [], topics);
    }

    /// <summary>
    /// Turns the agent's directory name back into the path it was made from.
    /// <para>
    /// The transform is lossy: every separator, colon and dot became the same
    /// hyphen, so it cannot be undone by reading the name. It is undone by
    /// walking the filesystem instead — at each level, the longest run of parts
    /// that names a directory that actually exists is taken, backtracking when
    /// a choice leads nowhere. That answers the question that matters, which is
    /// not how the name was built but whether a real directory is there.
    /// </para>
    /// </summary>
    internal static string RecoverPath(string slug)
    {
        // A leading "D--" is a drive letter followed by two separators.
        if (slug.Length > 3 && slug[1] == '-' && slug[2] == '-' && char.IsLetter(slug[0]))
        {
            var root = slug[0] + ":" + Path.DirectorySeparatorChar;

            return Walk(root, slug[3..].Split('-'), 0)
                ?? root + slug[3..].Replace('-', Path.DirectorySeparatorChar);
        }

        var body = slug.TrimStart('-');

        return Walk("/", body.Split('-'), 0) ?? "/" + body.Replace('-', '/');
    }

    /// <summary>
    /// Rebuilds a path one directory at a time, longest candidate first.
    /// <para>
    /// Longest first because a hyphen inside a name is more common than a
    /// directory named after a single fragment of one, and backtracking because
    /// "more common" is not "always": where both a directory and a longer name
    /// beginning with it exist, the wrong branch has to be abandoned rather
    /// than committed to.
    /// </para>
    /// </summary>
    private static string? Walk(string current, string[] parts, int index)
    {
        if (index >= parts.Length)
        {
            return current;
        }

        for (var take = parts.Length - index; take >= 1; take--)
        {
            var candidate = Path.Combine(current, string.Join('-', parts.Skip(index).Take(take)));

            if (!Directory.Exists(candidate))
            {
                continue;
            }

            var resolved = Walk(candidate, parts, index + take);

            if (resolved is not null)
            {
                return resolved;
            }
        }

        return null;
    }

    private string ClaudeHome() =>
        _environment.GetVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(_environment.HomeDirectory, ".claude");
}
