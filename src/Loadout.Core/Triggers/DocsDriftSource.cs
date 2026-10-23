using System.Text.Json;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Models.Instructions;

namespace Loadout.Core.Triggers;

/// <summary>
/// Turns a project's documentation coming adrift from its repository into an
/// event, by running the same audit <c>loadout docs audit</c> runs.
/// </summary>
/// <remarks>
/// <para>
/// A project is audited only when a rule waits for this event on it, and only
/// when its checkout has moved since the last look: the audit reads every page
/// and every reference, and a repository that has not moved cannot have
/// drifted. Asking where the checkout is costs a git process a project, which
/// is why the daemon looks every few minutes rather than every one.
/// </para>
/// <para>
/// What counts is a finding that was not there at the last look, keyed by
/// page, kind and detail but not by line, which moves whenever anything above
/// it does. A page that was already stale is not news; a page that went stale
/// is. The first look at a project writes down what is there and posts
/// nothing, for the reason the finished-runs source gives.
/// </para>
/// <para>
/// Where it had got to is kept in <c>&lt;State&gt;/triggers/cursors/docs-drift.json</c>:
/// for each project, the commit it last audited and the findings it saw.
/// </para>
/// </remarks>
public sealed class DocsDriftSource
{
    /// <summary>What its events name as their source.</summary>
    public const string Name = "daemon";

    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly Func<string, CancellationToken, Task<DocsPolicy?>> _policy;
    private readonly string _cursor;

    /// <param name="state">The state directory.</param>
    /// <param name="projects">The registered projects.</param>
    /// <param name="git">Says where each checkout is.</param>
    /// <param name="policy">A project's documentation policy, as the audit command reads it, or null.</param>
    public DocsDriftSource(
        string state,
        IProjectService projects,
        IGitManager git,
        Func<string, CancellationToken, Task<DocsPolicy?>> policy)
    {
        _projects = projects;
        _git = git;
        _policy = policy;
        _cursor = Path.Combine(state, "triggers", "cursors", "docs-drift.json");
    }

    /// <summary>
    /// The projects whose documentation has newly drifted since the last look,
    /// as events, among those <paramref name="watched"/> says a rule waits on.
    /// </summary>
    public async Task<IReadOnlyList<TriggerEvent>> LookAsync(Func<string, bool> watched, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(watched);

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return [];
        }

        var seen = Read();
        var events = new List<TriggerEvent>();
        var changed = false;

        foreach (var project in listed.Value!)
        {
            var slug = project.Entry.Slug;

            if (!watched(slug) || project.LocalPath is not { Length: > 0 } repository || !Directory.Exists(repository))
            {
                continue;
            }

            var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

            if (state.Failed || state.Value?.HeadCommit is not { Length: > 0 } head)
            {
                continue;
            }

            seen.TryGetValue(slug, out var last);

            if (last is not null && string.Equals(last.Commit, head, StringComparison.Ordinal))
            {
                continue;
            }

            var findings = DocsAuditor.Audit(
                repository,
                DocsAuditor.Documents(repository),
                await _policy(slug, ct).ConfigureAwait(false),
                ct);

            var keys = findings.Select(Key).ToHashSet(StringComparer.Ordinal);

            seen[slug] = new Seen(head, [.. keys.Order(StringComparer.Ordinal)]);
            changed = true;

            if (last is null)
            {
                continue;
            }

            var fresh = findings.Where(one => !last.Findings.Contains(Key(one), StringComparer.Ordinal)).ToList();

            if (fresh.Count == 0)
            {
                continue;
            }

            events.Add(new TriggerEvent(
                $"docs-drift:{slug}:{head}",
                now,
                Name,
                TriggerEventKinds.DocsDrift,
                $"docs:{slug}",
                slug,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["project"] = slug,
                    ["path"] = fresh[0].Path,
                    ["count"] = fresh.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                []));
        }

        // Written before the events are handed on, so a daemon killed between
        // the two misses them rather than acting on them twice.
        if (changed)
        {
            Save(seen);
        }

        return events;
    }

    private static string Key(DocsFinding one) => $"{one.Path}|{one.Kind}|{one.Detail}";

    private Dictionary<string, Seen> Read()
    {
        try
        {
            return File.Exists(_cursor)
                ? JsonSerializer.Deserialize<Dictionary<string, Seen>>(File.ReadAllText(_cursor), TriggerJournal.Json)
                    ?? new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A cursor that cannot be read is a first look at everything.
            return new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save(Dictionary<string, Seen> seen)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_cursor)!);

        var aside = _cursor + ".tmp";
        File.WriteAllText(aside, JsonSerializer.Serialize(seen, TriggerJournal.Json));
        File.Move(aside, _cursor, overwrite: true);
    }

    private sealed record Seen(string Commit, IReadOnlyList<string> Findings);
}
