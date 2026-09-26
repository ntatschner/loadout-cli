using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Loadout.Core.Configuration;
using Loadout.Core.Security;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Ideas;

/// <summary>Which list an idea is on, and its id there.</summary>
/// <param name="Project">The project's slug, or null for the workspace-wide list.</param>
/// <param name="Id">The idea's task id.</param>
public sealed record IdeaPlace(string? Project, string Id)
{
    /// <summary>How the list is named to a person.</summary>
    public string Where => TaskPaths.Describe(Project);
}

/// <summary>One idea, as a list shows it.</summary>
public sealed record IdeaSummary(
    IdeaPlace Place,
    string Title,
    IdeaStage Stage,
    TaskState State,
    string DeclaredBy,
    DateTimeOffset DeclaredUtc,
    int Unanswered);

/// <summary>What accepting an idea did.</summary>
/// <param name="Task">The task it became.</param>
/// <param name="Project">The project it is now on.</param>
/// <param name="PlanPath">The plan, relative to the workspace.</param>
public sealed record IdeaAccepted(TaskItem Task, string Project, string PlanPath);

/// <summary>
/// Ideas and everything a person says about them: capture, answers, choices,
/// verdicts, and handing one on as work.
/// </summary>
/// <remarks>
/// Nothing here starts an agent. The agent's answers arrive through
/// <see cref="RecordReplyAsync"/>, from whichever caller ran the round, so the
/// record can be kept and tested without one.
/// </remarks>
public interface IIdeaService
{
    /// <summary>
    /// Drops an idea into a project's list, or the workspace-wide one for null,
    /// under the id given or, for null, one made from the text, and titled as
    /// given or, for null, by the text's first line.
    /// </summary>
    Task<OperationResult<IdeaRecord>> CaptureAsync(
        string? project,
        string text,
        string by,
        string? id = null,
        CancellationToken ct = default,
        string? title = null);

    /// <summary>Every idea on every list, workspace-wide first.</summary>
    Task<OperationResult<IReadOnlyList<IdeaSummary>>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Finds which list an idea is on: the one preferred first, then the
    /// workspace-wide list, then every project's.
    /// </summary>
    Task<OperationResult<IdeaPlace>> LocateAsync(string id, string? preferred, CancellationToken ct = default);

    /// <summary>An idea's working record.</summary>
    Task<OperationResult<IdeaRecord>> ReadAsync(IdeaPlace place, CancellationToken ct = default);

    /// <summary>Answers one of the agent's questions, replacing any earlier answer.</summary>
    Task<OperationResult<IdeaRecord>> AnswerAsync(
        IdeaPlace place,
        string question,
        string answer,
        CancellationToken ct = default);

    /// <summary>Picks one of a layer's options.</summary>
    Task<OperationResult<IdeaRecord>> ChooseAsync(
        IdeaPlace place,
        string layer,
        string option,
        CancellationToken ct = default);

    /// <summary>
    /// Keeps, drops or asks for a piece of the plan to be reworked. The piece
    /// <c>plan</c> is the whole of it, and only takes a request.
    /// </summary>
    Task<OperationResult<IdeaRecord>> JudgeAsync(
        IdeaPlace place,
        string piece,
        IdeaVerdict verdict,
        string? request = null,
        CancellationToken ct = default);

    /// <summary>Folds an agent's answer into the record.</summary>
    Task<OperationResult<IdeaRecord>> RecordReplyAsync(
        IdeaPlace place,
        IdeaReply reply,
        CancellationToken ct = default);

    /// <summary>Notes that a round went wrong, so the next look at the idea says so.</summary>
    Task<OperationResult<IdeaRecord>> RecordFailureAsync(
        IdeaPlace place,
        string error,
        CancellationToken ct = default);

    /// <summary>
    /// Writes the plan out, and turns the idea into a task on the project it
    /// belongs to, moving it there when it was somewhere else.
    /// </summary>
    Task<OperationResult<IdeaAccepted>> AcceptAsync(
        IdeaPlace place,
        string project,
        string by,
        CancellationToken ct = default);

    /// <summary>Forgets an idea: its task and its record.</summary>
    Task<OperationResult> RemoveAsync(IdeaPlace place, CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed partial class IdeaService : IIdeaService
{
    /// <summary>The piece that names the whole plan rather than one part of it.</summary>
    public const string WholePlan = IdeaWork.WholePlan;

    private const int TitleLength = 80;

    private readonly IWorkspaceManager _workspace;
    private readonly ITaskService _tasks;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;

    public IdeaService(IWorkspaceManager workspace, ITaskService tasks, YamlStore yaml, TimeProvider time)
    {
        _workspace = workspace;
        _tasks = tasks;
        _yaml = yaml;
        _time = time;
    }

    private string RecordPath(IdeaPlace place) =>
        Path.Combine(TaskPaths.Ideas(_workspace.LocalPath, place.Project), $"{place.Id}.yaml");

    /// <inheritdoc />
    public async Task<OperationResult<IdeaRecord>> CaptureAsync(
        string? project,
        string text,
        string by,
        string? id = null,
        CancellationToken ct = default,
        string? title = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return OperationResult<IdeaRecord>.Fail("An idea needs saying: the text was empty.", ExitCode.InvalidArguments);
        }

        if (Screened($"{text} {title}") is { } refused)
        {
            return OperationResult<IdeaRecord>.Fail(refused, ExitCode.PolicyViolation);
        }

        if (id is not null && TaskIds.Rejection(id) is { } rejected)
        {
            return OperationResult<IdeaRecord>.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaRecord>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        if (project is { Length: > 0 })
        {
            var manifest = await _workspace.ReadProjectAsync(project, ct).ConfigureAwait(false);

            if (manifest.Failed)
            {
                return OperationResult<IdeaRecord>.Fail(
                    $"'{project}' is not a project in this workspace.", ExitCode.ProjectNotFound);
            }
        }

        var listed = await _tasks.ListAsync(project, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<IdeaRecord>.Fail(listed.Error!, listed.ExitCode);
        }

        string chosen;

        if (id is { Length: > 0 })
        {
            chosen = id.Trim();

            if (listed.Value!.Any(item => string.Equals(item.Id, chosen, StringComparison.OrdinalIgnoreCase)))
            {
                return OperationResult<IdeaRecord>.Fail(
                    $"{TaskPaths.Describe(project)} already has a task called '{chosen}'. Choose another id.",
                    ExitCode.InvalidArguments);
            }
        }
        else
        {
            chosen = Unique(
                IdFrom(title is { Length: > 0 } ? title : text),
                listed.Value!.Select(item => item.Id),
                new IdeaPlace(project, string.Empty));
        }

        var place = new IdeaPlace(project, chosen);
        var record = new IdeaRecord { Id = chosen, Ask = text.Trim(), CapturedUtc = _time.GetUtcNow() };

        // The record first, then the task. A record with no task is invisible
        // and harmless; a task with no record would be an idea that cannot be
        // refined, listed where somebody will try.
        var saved = await _yaml.SaveAsync(RecordPath(place), record, true, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult<IdeaRecord>.Fail(saved.Error!, saved.ExitCode);
        }

        var declared = await _tasks.DeclareAsync(
            project,
            chosen,
            TaskState.Open,
            by,
            title is { Length: > 0 } ? Shorten(title) : TitleFrom(text),
            null,
            ct,
            TaskKind.Idea).ConfigureAwait(false);

        return declared.Succeeded
            ? OperationResult<IdeaRecord>.Ok(record)
            : OperationResult<IdeaRecord>.Fail(declared.Error!, declared.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<IdeaSummary>>> ListAsync(CancellationToken ct = default)
    {
        if (Unavailable() is { } missing)
        {
            return OperationResult<IReadOnlyList<IdeaSummary>>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var ideas = new List<IdeaSummary>();

        foreach (var project in Lists())
        {
            var listed = await _tasks.ListAsync(project, ct).ConfigureAwait(false);

            if (listed.Failed)
            {
                return OperationResult<IReadOnlyList<IdeaSummary>>.Fail(listed.Error!, listed.ExitCode);
            }

            foreach (var item in listed.Value!.Where(item => item.Kind == TaskKind.Idea))
            {
                var place = new IdeaPlace(project, item.Id);
                var record = await LoadAsync(place, ct).ConfigureAwait(false);

                ideas.Add(new IdeaSummary(
                    place,
                    item.Title,
                    record is null ? IdeaStage.Captured : IdeaWork.StageOf(record),
                    item.State,
                    item.DeclaredBy,
                    item.DeclaredUtc,
                    record is null ? 0 : IdeaWork.Unanswered(record).Count));
            }
        }

        return OperationResult<IReadOnlyList<IdeaSummary>>.Ok(ideas);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaPlace>> LocateAsync(string id, string? preferred, CancellationToken ct = default)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return Task.FromResult(OperationResult<IdeaPlace>.Fail(rejected, ExitCode.InvalidArguments));
        }

        if (Unavailable() is { } missing)
        {
            return Task.FromResult(OperationResult<IdeaPlace>.Fail(missing, ExitCode.WorkspaceSyncFailed));
        }

        var trimmed = id.Trim();

        IEnumerable<string?> order = preferred is { Length: > 0 }
            ? [preferred, .. Lists().Where(list => list != preferred)]
            : Lists();

        foreach (var project in order)
        {
            var place = new IdeaPlace(project, trimmed);

            if (File.Exists(RecordPath(place)))
            {
                return Task.FromResult(OperationResult<IdeaPlace>.Ok(place));
            }
        }

        return Task.FromResult(OperationResult<IdeaPlace>.Fail(
            $"There is no idea called '{trimmed}'. 'loadout idea list' shows them all.",
            ExitCode.InvalidArguments));
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdeaRecord>> ReadAsync(IdeaPlace place, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaRecord>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        return await LoadAsync(place, ct).ConfigureAwait(false) is { } record
            ? OperationResult<IdeaRecord>.Ok(record)
            : OperationResult<IdeaRecord>.Fail(NotFound(place), ExitCode.InvalidArguments);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> AnswerAsync(
        IdeaPlace place,
        string question,
        string answer,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return Task.FromResult(OperationResult<IdeaRecord>.Fail(
                "An answer cannot be empty.", ExitCode.InvalidArguments));
        }

        return ChangeAsync(place, answer, record =>
        {
            if (record.Accepted is not null)
            {
                return "That idea has been accepted, so it takes no more answers.";
            }

            var asked = record.Rounds.SelectMany(round => round.Questions)
                .FirstOrDefault(q => string.Equals(q.Id, question.Trim(), StringComparison.OrdinalIgnoreCase));

            if (asked is null)
            {
                return $"'{question}' is not one of the questions on {place.Id}.";
            }

            asked.Answer = answer.Trim();

            return null;
        }, ct);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> ChooseAsync(
        IdeaPlace place,
        string layer,
        string option,
        CancellationToken ct = default) =>
        ChangeAsync(place, null, record =>
        {
            if (Proposal(record) is { } refused)
            {
                return refused;
            }

            var found = record.Plan!.Layers
                .FirstOrDefault(l => string.Equals(l.Id, layer.Trim(), StringComparison.OrdinalIgnoreCase));

            if (found is null)
            {
                return $"'{layer}' is not a layer of the plan. {Pieces(record.Plan)}";
            }

            // Taken either whole or as the letter alone, because "L2 b" is how
            // it reads off the screen and "L2b" is how it is written.
            var wanted = option.Trim();
            var picked = found.Options.FirstOrDefault(o =>
                string.Equals(o.Id, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(o.Id, found.Id + wanted, StringComparison.OrdinalIgnoreCase));

            if (picked is null)
            {
                return $"'{option}' is not an option for {found.Id}. Its options are "
                    + $"{string.Join(", ", found.Options.Select(o => o.Id))}.";
            }

            found.Chosen = picked.Id;

            return null;
        }, ct);

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> JudgeAsync(
        IdeaPlace place,
        string piece,
        IdeaVerdict verdict,
        string? request = null,
        CancellationToken ct = default)
    {
        if (verdict == IdeaVerdict.Improve && string.IsNullOrWhiteSpace(request))
        {
            return Task.FromResult(OperationResult<IdeaRecord>.Fail(
                "Say what to improve: an improvement with no request gives the agent nothing to act on.",
                ExitCode.InvalidArguments));
        }

        return ChangeAsync(place, request, record =>
        {
            if (Proposal(record) is { } refused)
            {
                return refused;
            }

            var wanted = piece.Trim();
            var said = request?.Trim() ?? string.Empty;

            if (string.Equals(wanted, WholePlan, StringComparison.OrdinalIgnoreCase))
            {
                if (verdict != IdeaVerdict.Improve)
                {
                    return "The whole plan can only be asked to improve. Accept it, or remove the idea.";
                }

                record.Request = said;

                return null;
            }

            if (record.Plan!.Layers.FirstOrDefault(l =>
                    string.Equals(l.Id, wanted, StringComparison.OrdinalIgnoreCase)) is { } layer)
            {
                layer.Verdict = verdict;
                layer.Request = verdict == IdeaVerdict.Improve ? said : string.Empty;

                return null;
            }

            if (record.Plan.Additions.FirstOrDefault(a =>
                    string.Equals(a.Id, wanted, StringComparison.OrdinalIgnoreCase)) is { } addition)
            {
                addition.Verdict = verdict;
                addition.Request = verdict == IdeaVerdict.Improve ? said : string.Empty;

                return null;
            }

            return $"'{piece}' is not a piece of the plan. {Pieces(record.Plan)}";
        }, ct);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdeaRecord>> RecordReplyAsync(
        IdeaPlace place,
        IdeaReply reply,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reply);

        // An agent that read a repository can repeat what it found there. What
        // it says is written to a file that travels, so it is screened the same
        // as anything a person types, and refused whole rather than trimmed: a
        // plan with a hole cut in it would read as complete.
        if (Screened(JsonSerializer.Serialize(reply)) is { } refused)
        {
            await RecordFailureAsync(place, refused, ct).ConfigureAwait(false);

            return OperationResult<IdeaRecord>.Fail(refused, ExitCode.PolicyViolation);
        }

        return await ChangeAsync(place, null, record =>
        {
            if (record.Accepted is not null)
            {
                return "That idea has already been accepted.";
            }

            IdeaWork.Merge(record, reply, _time.GetUtcNow());

            return null;
        }, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> RecordFailureAsync(
        IdeaPlace place,
        string error,
        CancellationToken ct = default) =>
        ChangeAsync(place, null, record =>
        {
            record.LastError = error;

            return null;
        }, ct);

    /// <inheritdoc />
    public async Task<OperationResult<IdeaAccepted>> AcceptAsync(
        IdeaPlace place,
        string project,
        string by,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (string.IsNullOrWhiteSpace(project))
        {
            return OperationResult<IdeaAccepted>.Fail(
                "Say which project the plan belongs to.", ExitCode.InvalidArguments);
        }

        var read = await ReadAsync(place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(read.Error!, read.ExitCode);
        }

        var record = read.Value!;

        switch (IdeaWork.StageOf(record))
        {
            case IdeaStage.Accepted:
                return OperationResult<IdeaAccepted>.Fail(
                    $"{place.Id} was accepted already, onto {record.Accepted!.Project}.", ExitCode.InvalidArguments);

            case IdeaStage.Captured:
            case IdeaStage.Answering:
                return OperationResult<IdeaAccepted>.Fail(
                    $"{place.Id} has no plan to accept yet. Answer its questions and refine it first.",
                    ExitCode.InvalidArguments);

            case IdeaStage.Ready when record.Plan is null:
                return OperationResult<IdeaAccepted>.Fail(
                    $"{place.Id} has no plan to accept yet. Refine it to get one.", ExitCode.InvalidArguments);

            case IdeaStage.Ready:
                return OperationResult<IdeaAccepted>.Fail(
                    $"{place.Id} has changes the plan has not taken in yet: improvements, a request or "
                    + "new answers. Refine it first, or take the improvements back.",
                    ExitCode.InvalidArguments);
        }

        var destination = project.Trim();
        var manifest = await _workspace.ReadProjectAsync(destination, ct).ConfigureAwait(false);

        if (manifest.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(
                $"'{destination}' is not a project in this workspace.", ExitCode.ProjectNotFound);
        }

        var landed = new IdeaPlace(destination, place.Id);
        var planFile = $"ideas/{place.Id}.md";
        var planPath = TaskPaths.Relative(destination, planFile);

        // In the order that leaves the least to explain if it stops part way:
        // the plan, then the task, then the record. A plan with no task points
        // nowhere and costs nothing; a task pointing to a plan that is not
        // there would be believed.
        var document = Path.Combine(TaskPaths.Ideas(_workspace.LocalPath, destination), $"{place.Id}.md");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(document)!);
            await File.WriteAllTextAsync(document, IdeaWork.Document(record), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult<IdeaAccepted>.Fail($"The plan could not be written: {ex.Message}");
        }

        var moved = await _tasks.MoveAsync(place.Project, destination, place.Id, ct).ConfigureAwait(false);

        if (moved.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(moved.Error!, moved.ExitCode);
        }

        var declared = await _tasks.DeclareAsync(
            destination,
            place.Id,
            TaskState.Open,
            by,
            record.Plan!.Title is { Length: > 0 } title ? Shorten(title) : null,
            $"{Shorten(record.Plan.Understanding, 400)} The plan is at {planPath}.",
            ct,
            TaskKind.Task).ConfigureAwait(false);

        if (declared.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(declared.Error!, declared.ExitCode);
        }

        record.Accepted = new IdeaAcceptance
        {
            AcceptedUtc = _time.GetUtcNow(),
            AcceptedBy = by,
            Project = destination,
            PlanPath = planPath,
        };

        var saved = await _yaml.SaveAsync(RecordPath(landed), record, true, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(saved.Error!, saved.ExitCode);
        }

        if (!string.Equals(place.Project ?? string.Empty, destination, StringComparison.Ordinal))
        {
            TryDelete(RecordPath(place));
        }

        return OperationResult<IdeaAccepted>.Ok(new IdeaAccepted(declared.Value!, destination, planPath));
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(IdeaPlace place, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Unavailable() is { } missing)
        {
            return OperationResult.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return OperationResult.Fail(NotFound(place), ExitCode.InvalidArguments);
        }

        var removed = await _tasks.RemoveAsync(place.Project, place.Id, ct).ConfigureAwait(false);

        // A record whose task is already gone is still worth clearing away, so
        // only a failure other than "not there" stops it.
        if (removed.Failed && removed.ExitCode != ExitCode.InvalidArguments)
        {
            return removed;
        }

        TryDelete(path);

        return OperationResult.Ok();
    }

    private async Task<OperationResult<IdeaRecord>> ChangeAsync(
        IdeaPlace place,
        string? screen,
        Func<IdeaRecord, string?> change,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (Screened(screen) is { } refusedText)
        {
            return OperationResult<IdeaRecord>.Fail(refusedText, ExitCode.PolicyViolation);
        }

        if (Unavailable() is { } missing)
        {
            return OperationResult<IdeaRecord>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return OperationResult<IdeaRecord>.Fail(NotFound(place), ExitCode.InvalidArguments);
        }

        string? refused = null;

        var written = await _yaml.UpdateAsync<IdeaRecord>(
            path,
            () => new IdeaRecord { Id = place.Id },
            record => refused = change(record),
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult<IdeaRecord>.Fail(written.Error!, written.ExitCode);
        }

        // The change is applied to the loaded copy and written whether or not
        // it was refused. A refusal changes nothing before returning, so what
        // is written is what was read.
        return refused is null
            ? OperationResult<IdeaRecord>.Ok(written.Value!)
            : OperationResult<IdeaRecord>.Fail(refused, ExitCode.InvalidArguments);
    }

    private async Task<IdeaRecord?> LoadAsync(IdeaPlace place, CancellationToken ct)
    {
        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _yaml.LoadAsync(path, () => new IdeaRecord(), ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value : null;
    }

    /// <summary>Every list: the workspace-wide one, then each project's that has a directory.</summary>
    private IEnumerable<string?> Lists()
    {
        yield return null;

        var projects = Path.Combine(_workspace.LocalPath, "projects");

        if (!Directory.Exists(projects))
        {
            yield break;
        }

        foreach (var directory in Directory.EnumerateDirectories(projects).Order(StringComparer.Ordinal))
        {
            yield return Path.GetFileName(directory);
        }
    }

    private static string? Proposal(IdeaRecord record) =>
        record.Accepted is not null
            ? "That idea has been accepted, so its plan is settled."
            : record.Plan is null
                ? "There is no plan yet. Refine the idea to get one."
                : null;

    private static string Pieces(IdeaPlan plan) =>
        $"The plan has {string.Join(", ", plan.Layers.Select(l => l.Id).Concat(plan.Additions.Select(a => a.Id)))}"
        + $", and '{WholePlan}' for the whole of it.";

    private static string NotFound(IdeaPlace place) =>
        $"There is no idea called '{place.Id}' on {place.Where}.";

    private string? Unavailable() => _workspace.IsAvailable()
        ? null
        : "There is no workspace on this machine, so there is nowhere to keep ideas.";

    /// <summary>
    /// Refuses text carrying something that looks like a credential, naming
    /// the pattern and never the value.
    /// </summary>
    private static string? Screened(string? text)
    {
        var patterns = SecretScanner.Match(text);

        return patterns.Count == 0
            ? null
            : $"That looks like it contains a credential ({string.Join(", ", patterns)}), so nothing "
                + "was recorded. Say it without the value.";
    }

    /// <summary>An id made from the first few words of the idea.</summary>
    internal static string IdFrom(string text)
    {
        var words = Words().Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Take(5);

        var id = string.Join('-', words);

        if (id.Length > 40)
        {
            id = id[..40].TrimEnd('-');
        }

        return id.Length == 0 ? "idea" : id;
    }

    private string Unique(string stem, IEnumerable<string> taken, IdeaPlace list)
    {
        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        var candidate = stem;

        for (var n = 2; used.Contains(candidate) || File.Exists(RecordPath(list with { Id = candidate })); n++)
        {
            candidate = $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}";
        }

        return candidate;
    }

    /// <summary>The first line of the idea, short enough to be a title.</summary>
    internal static string TitleFrom(string text) =>
        Shorten(text.Trim().Split('\n', 2)[0].Trim());

    private static string Shorten(string text, int length = TitleLength)
    {
        var flat = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            flat.Append(char.IsWhiteSpace(c) ? ' ' : c);
        }

        var single = flat.ToString().Trim();

        return single.Length <= length ? single : single[..(length - 1)].TrimEnd() + "…";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind rather than failed over: the idea has already gone
            // where it was going, and a stray record is found by nothing but a
            // directory listing.
        }
    }

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex Words();
}
