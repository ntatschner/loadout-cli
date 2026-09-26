using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Ideas;

/// <summary>Which list a dump was dropped on, and its id there.</summary>
public sealed record DumpPlace(string? Project, string Id)
{
    public string Where => TaskPaths.Describe(Project);
}

/// <summary>What an agent answered when asked to split a dump, as <see cref="DumpSchema.Version1"/> has it.</summary>
public sealed class DumpReply
{
    [JsonPropertyName("contract")]
    public string Contract { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<ReplyItem> Items { get; set; } = [];

    public sealed class ReplyItem
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("excerpt")]
        public string Excerpt { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        public string Kind { get; set; } = "idea";

        [JsonPropertyName("project")]
        public string Project { get; set; } = string.Empty;

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;
    }
}

/// <summary>
/// Reading an agent's split of a dump, and holding it to the text it came from.
/// </summary>
public static class DumpWork
{
    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads a split, refusing it when any piece's excerpt is not in the dump
    /// word for word.
    /// </summary>
    /// <remarks>
    /// Compared with runs of whitespace collapsed, because an agent re-flowing
    /// a line break is not rewording anything, and nothing else is forgiven.
    /// </remarks>
    public static OperationResult<DumpReply> Read(string? json, string dump)
    {
        ArgumentNullException.ThrowIfNull(dump);

        if (string.IsNullOrWhiteSpace(json))
        {
            return OperationResult<DumpReply>.Fail(
                $"The agent ended its turn without a {DumpSchema.Version} answer, so nothing was recorded.");
        }

        DumpReply? reply;

        try
        {
            reply = JsonSerializer.Deserialize<DumpReply>(json, Options);
        }
        catch (JsonException ex)
        {
            return OperationResult<DumpReply>.Fail($"The agent's answer is not valid JSON: {ex.Message}");
        }

        if (reply is null || !string.Equals(reply.Contract, DumpSchema.Version, StringComparison.Ordinal))
        {
            return OperationResult<DumpReply>.Fail(
                $"The agent's answer does not follow {DumpSchema.Version}, so it was not read.");
        }

        if (reply.Items.Count == 0)
        {
            return OperationResult<DumpReply>.Fail("The agent found nothing in the dump to split out.");
        }

        var text = Flat(dump);

        var invented = reply.Items
            .Select((item, index) => (item, number: index + 1))
            .Where(pair => pair.item.Excerpt.Trim().Length == 0 || !text.Contains(Flat(pair.item.Excerpt), StringComparison.Ordinal))
            .Select(pair => pair.number.ToString(CultureInfo.InvariantCulture))
            .ToList();

        return invented.Count > 0
            ? OperationResult<DumpReply>.Fail(
                $"The excerpt for piece {string.Join(", ", invented)} is not in the dump word for word. Every "
                + "excerpt has to be copied exactly from the text, never reworded.")
            : OperationResult<DumpReply>.Ok(reply);
    }

    /// <summary>Folds a split into the dump, replacing any earlier one.</summary>
    public static void Merge(IdeaDump dump, DumpReply reply, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(dump);
        ArgumentNullException.ThrowIfNull(reply);

        dump.Items =
        [
            .. reply.Items.Select((item, index) => new DumpItem
            {
                Number = index + 1,
                Title = item.Title.Trim(),
                Excerpt = item.Excerpt.Trim(),
                Kind = string.Equals(item.Kind, "task", StringComparison.OrdinalIgnoreCase)
                    ? DumpItemKind.Task
                    : DumpItemKind.Idea,
                Project = item.Project.Trim(),
                Reason = item.Reason.Trim(),
            }),
        ];

        dump.SplitUtc = now;
        dump.LastError = string.Empty;
    }

    /// <summary>What the agent is told when asked to split a dump.</summary>
    public static string Prompt(IdeaDump dump, IReadOnlyList<IdeaProjectHint> projects)
    {
        ArgumentNullException.ThrowIfNull(dump);
        ArgumentNullException.ThrowIfNull(projects);

        var text = new StringBuilder();

        text.AppendLine(
            "Somebody has dropped in notes they kept elsewhere. Split them into separate pieces of "
            + "work, one per distinct idea or task. Do not write code or change any file, and do not "
            + $"plan anything: give your answer in the {DumpSchema.Version} shape you have been given.");
        text.AppendLine();
        text.AppendLine(
            "For each piece: a short title in your words; the excerpt, which is the part of the notes "
            + "it is, copied exactly and never reworded or summarised; kind 'task' when it is clear "
            + "enough to be worked on as it stands, or 'idea' when it needs thinking through first; "
            + "the slug of the project below it belongs to, or empty when none fits or you cannot "
            + "tell; and a reason in a sentence. Keep the pieces in the order they appear. Leave out "
            + "anything that is not an idea or a task, such as a heading or a date.");
        text.AppendLine();
        text.AppendLine("## Projects they already have");
        text.AppendLine();

        if (projects.Count == 0)
        {
            text.AppendLine("None registered.");
        }

        foreach (var project in projects)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {project.Slug}: {project.Name}");
        }

        text.AppendLine();
        text.AppendLine("## The notes");
        text.AppendLine();
        text.AppendLine(dump.Text);

        return text.ToString();
    }

    private static string Flat(string text)
    {
        var flat = new StringBuilder(text.Length);
        var space = false;

        foreach (var c in text.Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                space = true;
                continue;
            }

            if (space && flat.Length > 0)
            {
                flat.Append(' ');
            }

            space = false;
            flat.Append(c);
        }

        return flat.ToString();
    }
}

/// <summary>What applying a split recorded.</summary>
/// <param name="Item">The piece.</param>
/// <param name="List">The list it went on: a project's slug, or null for the workspace-wide one.</param>
/// <param name="Id">Its id there.</param>
public sealed record DumpApplied(DumpItem Item, string? List, string Id);

/// <summary>
/// Notes dropped in from elsewhere: kept verbatim, split by an agent, and
/// recorded as ideas and tasks once the person says which.
/// </summary>
public interface IIdeaDumps
{
    /// <summary>Keeps a dump on a project's list, or the workspace-wide one for null.</summary>
    Task<OperationResult<IdeaDump>> KeepAsync(string? project, string text, string source, CancellationToken ct = default);

    /// <summary>Every dump on every list.</summary>
    Task<OperationResult<IReadOnlyList<(DumpPlace Place, IdeaDump Dump)>>> ListAsync(CancellationToken ct = default);

    /// <summary>Finds which list a dump is on, the preferred one first.</summary>
    Task<OperationResult<DumpPlace>> LocateAsync(string id, string? preferred, CancellationToken ct = default);

    Task<OperationResult<IdeaDump>> ReadAsync(DumpPlace place, CancellationToken ct = default);

    /// <summary>Folds an agent's split into the dump, replacing any earlier one.</summary>
    Task<OperationResult<IdeaDump>> RecordSplitAsync(DumpPlace place, DumpReply reply, CancellationToken ct = default);

    /// <summary>Notes that a split went wrong.</summary>
    Task<OperationResult<IdeaDump>> RecordFailureAsync(DumpPlace place, string error, CancellationToken ct = default);

    /// <summary>
    /// Records the pieces asked for, or every piece not yet recorded when none
    /// are named, as ideas and tasks.
    /// </summary>
    /// <param name="place">The dump.</param>
    /// <param name="numbers">Which pieces, or empty for all of them.</param>
    /// <param name="to">
    /// Where to put them all, overriding where the agent placed each: a slug,
    /// the empty string for the workspace-wide list, or null to follow the
    /// agent and then the list the dump is on.
    /// </param>
    /// <param name="by">Who is recording them.</param>
    /// <param name="ct">Cancels the work.</param>
    Task<OperationResult<IReadOnlyList<DumpApplied>>> ApplyAsync(
        DumpPlace place,
        IReadOnlyCollection<int> numbers,
        string? to,
        string by,
        CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed class IdeaDumps : IIdeaDumps
{
    private readonly IWorkspaceManager _workspace;
    private readonly ITaskService _tasks;
    private readonly IIdeaService _ideas;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public IdeaDumps(
        IWorkspaceManager workspace,
        ITaskService tasks,
        IIdeaService ideas,
        YamlStore yaml,
        TimeProvider time)
    {
        _workspace = workspace;
        _tasks = tasks;
        _ideas = ideas;
        _yaml = yaml;
        _time = time;
    }

    private string Directory(string? project) =>
        Path.Combine(TaskPaths.Ideas(_workspace.LocalPath, project), "dumps");

    private string PathOf(DumpPlace place) => Path.Combine(Directory(place.Project), $"{place.Id}.yaml");

    /// <inheritdoc />
    public async Task<OperationResult<IdeaDump>> KeepAsync(
        string? project,
        string text,
        string source,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult<IdeaDump>.Fail("There was nothing in it to keep.", ExitCode.InvalidArguments);
        }

        if (Screened(text) is { } refused)
        {
            return OperationResult<IdeaDump>.Fail(refused, ExitCode.PolicyViolation);
        }

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaDump>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        if (project is { Length: > 0 }
            && (await _workspace.ReadProjectAsync(project, ct).ConfigureAwait(false)).Failed)
        {
            return OperationResult<IdeaDump>.Fail($"'{project}' is not a project in this workspace.", ExitCode.ProjectNotFound);
        }

        var now = _time.GetUtcNow();
        var stem = $"dump-{now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture)}";
        var id = stem;

        for (var n = 2; File.Exists(PathOf(new DumpPlace(project, id))); n++)
        {
            id = $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        var dump = new IdeaDump { Id = id, Text = text, Source = source, CapturedUtc = now };
        var saved = await _yaml.SaveAsync(PathOf(new DumpPlace(project, id)), dump, true, ct).ConfigureAwait(false);

        return saved.Succeeded
            ? OperationResult<IdeaDump>.Ok(dump)
            : OperationResult<IdeaDump>.Fail(saved.Error!, saved.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<(DumpPlace Place, IdeaDump Dump)>>> ListAsync(CancellationToken ct = default)
    {
        if (Unavailable() is { } missing)
        {
            return OperationResult<IReadOnlyList<(DumpPlace, IdeaDump)>>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var found = new List<(DumpPlace, IdeaDump)>();

        foreach (var project in Lists())
        {
            var directory = Directory(project);

            if (!System.IO.Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.yaml").Order(StringComparer.Ordinal))
            {
                var place = new DumpPlace(project, Path.GetFileNameWithoutExtension(file));
                var loaded = await _yaml.LoadAsync(file, () => new IdeaDump(), ct).ConfigureAwait(false);

                if (loaded.Succeeded)
                {
                    found.Add((place, loaded.Value!));
                }
            }
        }

        return OperationResult<IReadOnlyList<(DumpPlace, IdeaDump)>>.Ok(found);
    }

    /// <inheritdoc />
    public Task<OperationResult<DumpPlace>> LocateAsync(string id, string? preferred, CancellationToken ct = default)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return Task.FromResult(OperationResult<DumpPlace>.Fail(rejected, ExitCode.InvalidArguments));
        }

        if (Unavailable() is { } missing)
        {
            return Task.FromResult(OperationResult<DumpPlace>.Fail(missing, ExitCode.WorkspaceSyncFailed));
        }

        IEnumerable<string?> order = preferred is { Length: > 0 }
            ? [preferred, .. Lists().Where(list => list != preferred)]
            : Lists();

        foreach (var project in order)
        {
            var place = new DumpPlace(project, id.Trim());

            if (File.Exists(PathOf(place)))
            {
                return Task.FromResult(OperationResult<DumpPlace>.Ok(place));
            }
        }

        return Task.FromResult(OperationResult<DumpPlace>.Fail(
            $"There is no dump called '{id.Trim()}'. 'loadout idea dump list' shows them.", ExitCode.InvalidArguments));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdeaDump>> ReadAsync(DumpPlace place, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaDump>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        if (!File.Exists(PathOf(place)))
        {
            return OperationResult<IdeaDump>.Fail($"There is no dump called '{place.Id}' on {place.Where}.", ExitCode.InvalidArguments);
        }

        return await _yaml.LoadAsync(PathOf(place), () => new IdeaDump(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdeaDump>> RecordSplitAsync(DumpPlace place, DumpReply reply, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        // Excerpts are the person's own words and were screened on the way in;
        // the titles and reasons are the agent's, and are what this is for.
        if (Screened(JsonSerializer.Serialize(reply)) is { } refused)
        {
            await RecordFailureAsync(place, refused, ct).ConfigureAwait(false);

            return OperationResult<IdeaDump>.Fail(refused, ExitCode.PolicyViolation);
        }

        return await ChangeAsync(place, dump =>
        {
            if (dump.Items.Any(item => item.Recorded.Length > 0))
            {
                return "Some of this dump has been recorded already, so it is not split again.";
            }

            DumpWork.Merge(dump, reply, _time.GetUtcNow());

            return null;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaDump>> RecordFailureAsync(DumpPlace place, string error, CancellationToken ct = default) =>
        ChangeAsync(place, dump =>
        {
            dump.LastError = error;

            return null;
        }, ct);

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<DumpApplied>>> ApplyAsync(
        DumpPlace place,
        IReadOnlyCollection<int> numbers,
        string? to,
        string by,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);

        var read = await ReadAsync(place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<IReadOnlyList<DumpApplied>>.Fail(read.Error!, read.ExitCode);
        }

        var dump = read.Value!;

        if (dump.Items.Count == 0)
        {
            return OperationResult<IReadOnlyList<DumpApplied>>.Fail(
                $"{place.Id} has not been split yet. Split it first.", ExitCode.InvalidArguments);
        }

        var unknown = numbers.Where(n => dump.Items.All(item => item.Number != n)).ToList();

        if (unknown.Count > 0)
        {
            return OperationResult<IReadOnlyList<DumpApplied>>.Fail(
                $"{string.Join(", ", unknown)} {(unknown.Count == 1 ? "is not a piece" : "are not pieces")} of "
                + $"{place.Id}. It has 1 to {dump.Items.Count}. Nothing was recorded.",
                ExitCode.InvalidArguments);
        }

        var chosen = dump.Items
            .Where(item => numbers.Count == 0 || numbers.Contains(item.Number))
            .Where(item => item.Recorded.Length == 0)
            .ToList();

        if (chosen.Count == 0)
        {
            return OperationResult<IReadOnlyList<DumpApplied>>.Fail(
                "Everything asked for has been recorded already.", ExitCode.InvalidArguments);
        }

        if (to is { Length: > 0 } && (await _workspace.ReadProjectAsync(to, ct).ConfigureAwait(false)).Failed)
        {
            return OperationResult<IReadOnlyList<DumpApplied>>.Fail(
                $"'{to}' is not a project in this workspace.", ExitCode.ProjectNotFound);
        }

        var applied = new List<DumpApplied>();

        foreach (var item in chosen)
        {
            var list = await ListForAsync(item, to, place.Project, ct).ConfigureAwait(false);

            OperationResult<string> recorded = item.Kind == DumpItemKind.Idea
                ? await IdeaAsync(list, item, by, ct).ConfigureAwait(false)
                : await TaskAsync(list, item, by, ct).ConfigureAwait(false);

            if (recorded.Failed)
            {
                // What was recorded before this stays recorded, and says so on
                // the dump, so applying again carries on rather than doubling.
                return OperationResult<IReadOnlyList<DumpApplied>>.Fail(
                    $"Piece {item.Number} could not be recorded: {recorded.Error} "
                    + (applied.Count > 0 ? $"{applied.Count} before it were." : "Nothing was recorded."),
                    recorded.ExitCode);
            }

            var id = recorded.Value!;
            var marked = await ChangeAsync(place, d =>
            {
                d.Items.First(i => i.Number == item.Number).Recorded = $"{TaskPaths.Describe(list)}/{id}";

                return null;
            }, ct).ConfigureAwait(false);

            if (marked.Failed)
            {
                return OperationResult<IReadOnlyList<DumpApplied>>.Fail(marked.Error!, marked.ExitCode);
            }

            applied.Add(new DumpApplied(item, list, id));
        }

        return OperationResult<IReadOnlyList<DumpApplied>>.Ok(applied);
    }

    /// <summary>
    /// Where a piece goes: where the person said, else the project the agent
    /// placed it on when that project exists, else the list the dump is on.
    /// </summary>
    private async Task<string?> ListForAsync(DumpItem item, string? to, string? dumpedOn, CancellationToken ct)
    {
        if (to is not null)
        {
            return to.Length > 0 ? to : null;
        }

        if (item.Project.Length > 0
            && (await _workspace.ReadProjectAsync(item.Project, ct).ConfigureAwait(false)).Succeeded)
        {
            return item.Project;
        }

        return dumpedOn;
    }

    private async Task<OperationResult<string>> IdeaAsync(string? list, DumpItem item, string by, CancellationToken ct)
    {
        var captured = await _ideas.CaptureAsync(list, item.Excerpt, by, null, ct, item.Title).ConfigureAwait(false);

        return captured.Succeeded
            ? OperationResult<string>.Ok(captured.Value!.Id)
            : OperationResult<string>.Fail(captured.Error!, captured.ExitCode);
    }

    private async Task<OperationResult<string>> TaskAsync(string? list, DumpItem item, string by, CancellationToken ct)
    {
        var listed = await _tasks.ListAsync(list, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<string>.Fail(listed.Error!, listed.ExitCode);
        }

        var taken = new HashSet<string>(listed.Value!.Select(t => t.Id), StringComparer.OrdinalIgnoreCase);
        var stem = IdeaService.IdFrom(item.Title.Length > 0 ? item.Title : item.Excerpt);
        var id = stem;

        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        var declared = await _tasks.DeclareAsync(
            list, id, TaskState.Open, by,
            item.Title.Length > 0 ? item.Title : IdeaService.TitleFrom(item.Excerpt),
            item.Excerpt,
            ct,
            TaskKind.Task).ConfigureAwait(false);

        return declared.Succeeded
            ? OperationResult<string>.Ok(id)
            : OperationResult<string>.Fail(declared.Error!, declared.ExitCode);
    }

    private async Task<OperationResult<IdeaDump>> ChangeAsync(DumpPlace place, Func<IdeaDump, string?> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaDump>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        if (!File.Exists(PathOf(place)))
        {
            return OperationResult<IdeaDump>.Fail($"There is no dump called '{place.Id}' on {place.Where}.", ExitCode.InvalidArguments);
        }

        string? refused = null;

        var written = await _yaml.UpdateAsync<IdeaDump>(
            PathOf(place), () => new IdeaDump { Id = place.Id }, dump => refused = change(dump), true, ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<IdeaDump>.Fail(written.Error!, written.ExitCode);
        }

        return refused is null
            ? OperationResult<IdeaDump>.Ok(written.Value!)
            : OperationResult<IdeaDump>.Fail(refused, ExitCode.InvalidArguments);
    }

    private IEnumerable<string?> Lists()
    {
        yield return null;

        var projects = Path.Combine(_workspace.LocalPath, "projects");

        if (!System.IO.Directory.Exists(projects))
        {
            yield break;
        }

        foreach (var directory in System.IO.Directory.EnumerateDirectories(projects).Order(StringComparer.Ordinal))
        {
            yield return Path.GetFileName(directory);
        }
    }

    private string? Unavailable() => _workspace.IsAvailable()
        ? null
        : "There is no workspace on this machine, so there is nowhere to keep ideas.";

    private static string? Screened(string? text)
    {
        var patterns = SecretScanner.Match(text);

        return patterns.Count == 0
            ? null
            : $"That looks like it contains a credential ({string.Join(", ", patterns)}), so nothing "
                + "was recorded. Take the value out and drop it in again.";
    }
}
