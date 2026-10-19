using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models.Results;

namespace Loadout.Core.Lists;

/// <summary>One thing global search found, in the shape every kind shares.</summary>
/// <param name="Kind">task, idea, session, run or project.</param>
/// <param name="Id">Its id, as the command that opens it takes it.</param>
/// <param name="Title">What it is called.</param>
/// <param name="Project">The project it is on, or null for none.</param>
/// <param name="State">Where it stands, in its own kind's words, or null.</param>
/// <param name="Updated">When it last changed, or null when nothing says.</param>
/// <param name="Open">The command that shows it, as somebody would type it.</param>
public sealed record SearchHit(
    string Kind,
    string Id,
    string Title,
    string? Project,
    string? State,
    DateTimeOffset? Updated,
    string Open);

/// <summary>What one kind found, best first, and how many more there were.</summary>
public sealed record SearchGroup(string Kind, IReadOnlyList<SearchHit> Hits, int More);

/// <summary>Reads one kind of thing for global search: a strategy per kind.</summary>
public interface ISearchSource
{
    /// <summary>The kind, as <c>kind:</c> names it.</summary>
    string Kind { get; }

    /// <summary>Everything of this kind there is to search, or why it could not be read.</summary>
    Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct);
}

/// <summary>
/// One search across tasks, ideas, sessions, team runs and projects, behind
/// <c>loadout find</c>, Studio's palette and the TUI's.
/// </summary>
/// <remarks>
/// <para>
/// The query is the shared one, applied with one registry over the shape all
/// the kinds share, so <c>kind:task state:open login</c> means what it means
/// on a task list and is refused in the same words. <c>kind:</c> is read
/// first and decides which kinds are read at all, so narrowing a search is
/// also what makes it quick.
/// </para>
/// <para>
/// A kind that cannot be read is left out rather than failing the search:
/// somebody looking for a task should still find it when the session history
/// is unreadable. Each group is capped, best first: a title that starts with
/// what was typed, then one containing it, then anything else, newest first
/// within each.
/// </para>
/// </remarks>
public sealed class SearchService
{
    /// <summary>The kinds, in the order the results are grouped.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["task", "idea", "session", "run", "project"];

    /// <summary>What a search can be filtered and sorted on.</summary>
    public static readonly ListFields<SearchHit> Fields = new(
        "results",
        [
            ListField<SearchHit>.Choice("kind", "what it is", Kinds, hit => hit.Kind),
            ListField<SearchHit>.Text("id", "its id", hit => hit.Id),
            ListField<SearchHit>.Text("title", "what it is called", hit => hit.Title),
            ListField<SearchHit>.Text("project", "the project it is on", hit => hit.Project),
            ListField<SearchHit>.Text("state", "where it stands", hit => hit.State),
            ListField<SearchHit>.Date("updated", "when it last changed", hit => hit.Updated),
        ],
        [new ListSort("updated", true)]);

    private readonly IReadOnlyList<ISearchSource> _sources;
    private readonly TimeProvider _time;

    public SearchService(IEnumerable<ISearchSource> sources, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = [.. sources.OrderBy(source => Kinds.ToList().IndexOf(source.Kind))];
        _time = time;
    }

    /// <summary>Searches every kind the query allows, up to this many hits each.</summary>
    public async Task<OperationResult<IReadOnlyList<SearchGroup>>> SearchAsync(string? text, int perKind, CancellationToken ct)
    {
        var parsed = ListQuery.Parse(text);

        if (parsed.Failed)
        {
            return OperationResult<IReadOnlyList<SearchGroup>>.Fail(parsed.Error!, parsed.ExitCode);
        }

        var query = parsed.Value!;
        var checkedQuery = Fields.Check(query);

        if (checkedQuery.Failed)
        {
            return OperationResult<IReadOnlyList<SearchGroup>>.Fail(checkedQuery.Error!, checkedQuery.ExitCode);
        }

        var wanted = Wanted(query);
        var reads = _sources
            .Where(source => wanted.Contains(source.Kind))
            .Select(async source => (source.Kind, Read: await source.ReadAsync(ct).ConfigureAwait(false)));
        var read = await Task.WhenAll(reads).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var groups = new List<SearchGroup>();

        foreach (var (kind, found) in read)
        {
            if (found.Failed)
            {
                continue;
            }

            var kept = Fields.Apply(query, found.Value!, now).Value!;

            // The query's own sort, when it has one, is what somebody asked
            // for; otherwise the best match first.
            var ordered = query.Sorts.Count > 0
                ? kept
                : [.. kept.OrderBy(hit => Rank(hit, query)).ThenByDescending(hit => hit.Updated ?? DateTimeOffset.MinValue)];

            if (ordered.Count > 0)
            {
                groups.Add(new SearchGroup(kind, [.. ordered.Take(perKind)], Math.Max(0, ordered.Count - perKind)));
            }
        }

        return OperationResult<IReadOnlyList<SearchGroup>>.Ok(groups);
    }

    /// <summary>The kinds the query's <c>kind:</c> terms leave, all of them when it has none.</summary>
    private static HashSet<string> Wanted(ListQuery query)
    {
        var wanted = new HashSet<string>(Kinds, StringComparer.OrdinalIgnoreCase);

        foreach (var term in query.Terms.Where(term => term.Field == "kind"))
        {
            var named = new HashSet<string>(term.Values, StringComparer.OrdinalIgnoreCase);

            wanted.RemoveWhere(kind => named.Contains(kind) == term.Negated);
        }

        return wanted;
    }

    /// <summary>0 when the title starts with every word, 1 when it holds them, 2 otherwise.</summary>
    private static int Rank(SearchHit hit, ListQuery query)
    {
        var words = query.Words.Where(word => !word.Negated).Select(word => word.Text).ToList();

        if (words.Count == 0)
        {
            return 0;
        }

        if (hit.Title.StartsWith(words[0], StringComparison.OrdinalIgnoreCase)
            && words.All(word => hit.Title.Contains(word, StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        return words.All(word => hit.Title.Contains(word, StringComparison.OrdinalIgnoreCase)) ? 1 : 2;
    }
}

/// <summary>Every task, on every project and the workspace-wide list.</summary>
public sealed class TaskSearchSource(ITaskService tasks, IProjectService projects) : ISearchSource
{
    public string Kind => "task";

    public async Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
    {
        var listed = await projects.ListAsync(ct).ConfigureAwait(false);
        var slugs = new List<string?> { null };

        if (listed.Succeeded)
        {
            slugs.AddRange(listed.Value!.Select(project => project.Entry.Slug));
        }

        var hits = new List<SearchHit>();

        foreach (var slug in slugs)
        {
            var read = await tasks.ListAsync(slug, ct).ConfigureAwait(false);

            if (read.Failed)
            {
                continue;
            }

            hits.AddRange(read.Value!.Select(task => new SearchHit(
                "task",
                task.Id,
                task.Title is { Length: > 0 } title ? title : task.Id,
                slug,
                task.State.ToString().ToLowerInvariant(),
                task.DeclaredUtc,
                slug is null ? "loadout task list --global --all" : $"loadout task list --project {slug} --all")));
        }

        return OperationResult<IReadOnlyList<SearchHit>>.Ok(hits);
    }
}

/// <summary>Every idea being shaped.</summary>
public sealed class IdeaSearchSource(IIdeaService ideas) : ISearchSource
{
    public string Kind => "idea";

    public async Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
    {
        var listed = await ideas.ListAsync(ct).ConfigureAwait(false);

        return listed.Failed
            ? OperationResult<IReadOnlyList<SearchHit>>.Fail(listed.Error!, listed.ExitCode)
            : OperationResult<IReadOnlyList<SearchHit>>.Ok([.. listed.Value!.Select(idea => new SearchHit(
                "idea",
                idea.Place.Id,
                idea.Title,
                idea.Place.Project,
                idea.Stage.ToString().ToLowerInvariant(),
                idea.DeclaredUtc,
                idea.Place.Project is { } slug
                    ? $"loadout idea show {idea.Place.Id} --project {slug}"
                    : $"loadout idea show {idea.Place.Id} --global"))]);
    }
}

/// <summary>Every registered project.</summary>
public sealed class ProjectSearchSource(IProjectService projects) : ISearchSource
{
    public string Kind => "project";

    public async Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
    {
        var listed = await projects.ListAsync(ct).ConfigureAwait(false);

        return listed.Failed
            ? OperationResult<IReadOnlyList<SearchHit>>.Fail(listed.Error!, listed.ExitCode)
            : OperationResult<IReadOnlyList<SearchHit>>.Ok([.. listed.Value!.Select(project => new SearchHit(
                "project",
                project.Entry.Slug,
                project.Entry.Name is { Length: > 0 } name ? name : project.Entry.Slug,
                project.Entry.Slug,
                project.IsAvailableLocally ? "here" : "elsewhere",
                project.LastLaunchedUtc,
                $"loadout project show {project.Entry.Slug}"))]);
    }
}

/// <summary>The most recent team runs.</summary>
public sealed class RunSearchSource(IRunJournal runs) : ISearchSource
{
    /// <summary>How many runs back a search looks: each one is a journal to fold.</summary>
    internal const int Recent = 100;

    public string Kind => "run";

    public Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
    {
        var hits = new List<SearchHit>();

        foreach (var id in runs.List(Recent))
        {
            if (runs.Summarise(id) is { Succeeded: true, Value: { } run })
            {
                hits.Add(new SearchHit(
                    "run",
                    run.RunId,
                    run.Goal is { Length: > 0 } goal ? goal : run.Team,
                    run.Project,
                    run.Ended ?? (run.Finished is null ? "running" : "finished"),
                    run.Finished ?? run.Started,
                    $"loadout team status {run.RunId}"));
            }
        }

        return Task.FromResult(OperationResult<IReadOnlyList<SearchHit>>.Ok(hits));
    }
}

/// <summary>Recent agent conversations, which 'loadout resume' can pick up.</summary>
public sealed class SessionSearchSource(ISessionHistoryService sessions) : ISearchSource
{
    /// <summary>How many conversations back a search looks.</summary>
    internal const int Recent = 200;

    public string Kind => "session";

    public async Task<OperationResult<IReadOnlyList<SearchHit>>> ReadAsync(CancellationToken ct)
    {
        var listed = await sessions.ListAsync(new SessionQuery(Limit: Recent), ct).ConfigureAwait(false);

        return listed.Failed
            ? OperationResult<IReadOnlyList<SearchHit>>.Fail(listed.Error!, listed.ExitCode)
            : OperationResult<IReadOnlyList<SearchHit>>.Ok([.. listed.Value!.Select(session => new SearchHit(
                "session",
                session.SessionId,
                session.Label,
                session.ProjectSlug,
                session.Agent,
                session.LastActive,
                $"loadout resume {session.SessionId}"))]);
    }
}
