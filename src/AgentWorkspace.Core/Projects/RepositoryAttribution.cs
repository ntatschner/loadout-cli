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
        if (!Directory.Exists(directory) || Directory.Exists(Path.Combine(directory, ".git")))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(directory)
                .Where(child => Directory.Exists(Path.Combine(child, ".git")))
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

        var inside = RepositoriesInside(subject);

        if (inside.Count > 1)
        {
            return new StateAttribution(
                memory, subject, AttributionKind.Container, null, inside, topics);
        }

        if (Directory.Exists(subject))
        {
            return new StateAttribution(
                memory, subject, AttributionKind.Unregistered, null, inside, topics);
        }

        return new StateAttribution(memory, subject, AttributionKind.Missing, null, [], topics);
    }

    /// <summary>
    /// Turns the agent's directory name back into the path it was made from.
    /// <para>
    /// The transform is lossy: every separator, colon and dot became the same
    /// hyphen, so this cannot be undone exactly. It is resolved by trying the
    /// candidates against the filesystem and taking one that exists, which
    /// answers the question that actually matters — is this a real directory,
    /// and what is in it.
    /// </para>
    /// </summary>
    internal static string RecoverPath(string slug)
    {
        // Windows first: a leading "D--" is a drive letter and two separators.
        if (slug.Length > 3 && slug[1] == '-' && slug[2] == '-' && char.IsLetter(slug[0]))
        {
            var candidate = slug[0] + ":\\" + slug[3..].Replace('-', '\\');

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            // A directory whose own name contains a hyphen cannot be told apart
            // from a separator, so each hyphen is tried as part of a name
            // working from the right, which is where compound names sit.
            var recovered = TryVariants(slug[0] + ":\\", slug[3..]);

            return recovered ?? candidate;
        }

        var posix = "/" + slug.TrimStart('-').Replace('-', '/');

        return TryVariants("/", slug.TrimStart('-')) ?? posix;
    }

    private static string? TryVariants(string prefix, string body)
    {
        var separator = prefix.EndsWith('\\') ? '\\' : '/';
        var parts = body.Split('-');

        // Rebuilt from the right: the last hyphens are the ones most likely to
        // be part of a name rather than a separator, so joining those first
        // finds "home-servers-build" before it finds three nested directories.
        for (var join = 0; join < parts.Length; join++)
        {
            for (var start = parts.Length - 1 - join; start >= 0; start--)
            {
                var rebuilt = parts.ToList();
                var merged = string.Join('-', rebuilt.Skip(start).Take(join + 1));

                rebuilt.RemoveRange(start, join + 1);
                rebuilt.Insert(start, merged);

                var candidate = prefix + string.Join(separator, rebuilt);

                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private string ClaudeHome() =>
        _environment.GetVariable("CLAUDE_CONFIG_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(_environment.HomeDirectory, ".claude");
}
