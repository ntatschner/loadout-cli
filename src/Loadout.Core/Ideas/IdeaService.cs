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
/// <param name="Place">Which list it is on, and its id.</param>
/// <param name="Title">Its task's title.</param>
/// <param name="Stage">Where it stands, from its record.</param>
/// <param name="State">Its task's state.</param>
/// <param name="DeclaredBy">Who last declared the task.</param>
/// <param name="DeclaredUtc">When.</param>
/// <param name="Unanswered">How many of the agent's questions wait on an answer.</param>
/// <param name="Failure">The latest round that went wrong, or null.</param>
/// <param name="Running">True while a round on it is running on this machine.</param>
/// <param name="Moved">Where it was moved from, or null when it has stayed where it was dropped in.</param>
/// <param name="Outdated">How many of its questions may have been overtaken by a change since they were asked.</param>
/// <param name="Mark">Whether its plan lacks something the current contract asks for.</param>
/// <param name="Type">Its task's type, or null when it was recorded before there were types.</param>
/// <param name="TypeBy">Who gave it <paramref name="Type"/>, or null when it has none.</param>
public sealed record IdeaSummary(
    IdeaPlace Place,
    string Title,
    IdeaStage Stage,
    TaskState State,
    string DeclaredBy,
    DateTimeOffset DeclaredUtc,
    int Unanswered,
    IdeaAttempt? Failure = null,
    bool Running = false,
    TaskMove? Moved = null,
    int Outdated = 0,
    Backfill.ContractMark Mark = Backfill.ContractMark.None,
    TaskType? Type = null,
    TaskTypeSource? TypeBy = null);

/// <summary>What accepting an idea did.</summary>
/// <param name="Task">The task it became.</param>
/// <param name="Project">The project it is now on.</param>
/// <param name="PlanPath">The plan, relative to the workspace.</param>
/// <param name="From">The list it was on, a slug or null for the workspace-wide one.</param>
public sealed record IdeaAccepted(TaskItem Task, string Project, string PlanPath, string? From = null)
{
    /// <summary>Whether accepting it took it off the list it was on.</summary>
    public bool Moved => !string.Equals(From, Project, StringComparison.Ordinal);
}

/// <summary>An idea that became a task on another list, as the list it left shows it.</summary>
/// <param name="Id">Its id, the same on both lists.</param>
/// <param name="Title">Its task's title.</param>
/// <param name="From">The list it left, a slug or null for the workspace-wide one.</param>
/// <param name="Where">The list it is a task on now, the same way.</param>
/// <param name="Utc">When it moved.</param>
public sealed record IdeaMovedOn(string Id, string Title, string? From, string? Where, DateTimeOffset Utc);

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
    /// Every idea that is a task on another list now, by accepting or by a move
    /// after it, so the list it left can say where it went.
    /// </summary>
    /// <remarks>
    /// Worked out from the task's own record of where it came from, and an
    /// idea's record beside it, so it lasts as long as they do and nothing is
    /// kept on the list it left.
    /// </remarks>
    Task<OperationResult<IReadOnlyList<IdeaMovedOn>>> MovedOnAsync(CancellationToken ct = default);

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

    /// <summary>
    /// Folds an agent's answer into the record, and remembers the agent and
    /// model it came from as the next round's default. The notes named in
    /// <paramref name="seen"/>, the ones the round was shown, are marked taken
    /// in; null marks none.
    /// </summary>
    /// <remarks>
    /// A type in the answer revises the idea's task's type, unless the person
    /// chose that type, which no answer changes.
    /// </remarks>
    Task<OperationResult<IdeaRecord>> RecordReplyAsync(
        IdeaPlace place,
        IdeaReply reply,
        CancellationToken ct = default,
        IdeaAttempt? asked = null,
        IReadOnlyCollection<string>? seen = null);

    /// <summary>The task an idea is, or null when its list has none by that id.</summary>
    Task<TaskItem?> TaskOfAsync(IdeaPlace place, CancellationToken ct = default);

    /// <summary>
    /// Sets an idea's type as the person's choice, or with null hands it back
    /// to the agent: the rules' guess from everything that was asked stands
    /// until the next round revises it.
    /// </summary>
    Task<OperationResult<TaskItem>> RetypeAsync(
        IdeaPlace place,
        TaskType? type,
        string by,
        CancellationToken ct = default);

    /// <summary>
    /// Adds something the person wants the plan to take in, kept verbatim with
    /// who said it and when, for the next round to read. Refused once the idea
    /// has been accepted.
    /// </summary>
    Task<OperationResult<IdeaRecord>> AddToAsync(
        IdeaPlace place,
        string text,
        string by,
        CancellationToken ct = default);

    /// <summary>
    /// What <see cref="AddToAsync"/> would make of the record, refused for the
    /// same reasons, with nothing written. For a dry run.
    /// </summary>
    Task<OperationResult<IdeaRecord>> PreviewAddToAsync(
        IdeaPlace place,
        string text,
        string by,
        CancellationToken ct = default);

    /// <summary>
    /// Takes off a note no round has read, named by its number as
    /// <c>idea show</c> lists it (<c>2</c> or <c>N2</c>). Refused once a round
    /// has read it, once the idea is accepted, and while a round on the idea
    /// is running on this machine, which may have read it without saying so
    /// yet. Its number is never given to another note.
    /// </summary>
    Task<OperationResult<IdeaNote>> RemoveNoteAsync(IdeaPlace place, string note, CancellationToken ct = default);

    /// <summary>
    /// What <see cref="RemoveNoteAsync"/> would take off, refused for the
    /// same reasons, with nothing written. For a dry run.
    /// </summary>
    Task<OperationResult<IdeaNote>> PreviewRemoveNoteAsync(IdeaPlace place, string note, CancellationToken ct = default);

    /// <summary>
    /// Notes that a round went wrong, and with which agent and model, so the
    /// next look at the idea says so. Replaces any earlier failure.
    /// </summary>
    Task<OperationResult<IdeaRecord>> RecordFailureAsync(
        IdeaPlace place,
        string error,
        CancellationToken ct = default,
        IdeaAttempt? asked = null);

    /// <summary>
    /// Forgets the latest failure without running another round, for an idea
    /// somebody has stopped refining. Answers and the plan stay as they are.
    /// </summary>
    Task<OperationResult<IdeaRecord>> DismissFailureAsync(IdeaPlace place, CancellationToken ct = default);

    /// <summary>
    /// Writes a backfill pass's done-when into an idea's plan as a new
    /// revision, marked as gap-filled, changing nothing else in it.
    /// </summary>
    /// <param name="place">The idea.</param>
    /// <param name="revision">The plan revision the pass read, so a plan changed since is refused rather than written over.</param>
    /// <param name="doneWhen">The conditions.</param>
    /// <param name="ct">Cancellation.</param>
    Task<OperationResult<IdeaRecord>> FillGapsAsync(
        IdeaPlace place,
        int revision,
        IReadOnlyList<string> doneWhen,
        CancellationToken ct = default);

    /// <summary>
    /// Notes that a backfill pass could not fill an idea's plan, replacing any
    /// earlier note of it. Rounds and their failures are left alone.
    /// </summary>
    Task<OperationResult<IdeaRecord>> RecordGapFillFailureAsync(
        IdeaPlace place,
        IdeaAttempt attempt,
        CancellationToken ct = default);

    /// <summary>
    /// Writes the plan out, and turns the idea into a task on the project it
    /// belongs to, moving it there when it was somewhere else.
    /// </summary>
    /// <param name="place">The idea.</param>
    /// <param name="project">The project it goes on.</param>
    /// <param name="by">Who is accepting it.</param>
    /// <param name="ct">Cancellation.</param>
    /// <param name="doneWhen">
    /// The done-when the person confirmed, replacing what the plan proposed, or
    /// null to confirm the plan's own by accepting it.
    /// </param>
    Task<OperationResult<IdeaAccepted>> AcceptAsync(
        IdeaPlace place,
        string project,
        string by,
        CancellationToken ct = default,
        IReadOnlyList<string>? doneWhen = null);

    /// <summary>Forgets an idea: its task and its record.</summary>
    Task<OperationResult> RemoveAsync(IdeaPlace place, CancellationToken ct = default);

    /// <summary>
    /// Moves a task or an idea to another list, keeping its id, and takes what
    /// is kept beside it along: an idea's working record, and the plan of one
    /// that was accepted. For something put on the wrong project.
    /// </summary>
    /// <param name="from">The list it is on: a project, or null for the workspace-wide one.</param>
    /// <param name="to">The list it belongs on, the same way.</param>
    /// <param name="id">Its id.</param>
    /// <param name="ct">Cancellation.</param>
    Task<OperationResult<TaskItem>> MoveAsync(
        string? from,
        string? to,
        string id,
        CancellationToken ct = default);

    /// <summary>An idea's working record, with the revision a change made from it should name.</summary>
    Task<OperationResult<IdeaRead>> ReadWithRevisionAsync(IdeaPlace place, CancellationToken ct = default);

    /// <summary>
    /// Makes several changes to ideas, each idea's all or nothing, and says
    /// what became of every one.
    /// </summary>
    Task<OperationResult<IdeaBatch>> ApplyAsync(IReadOnlyList<IdeaOperation> operations, CancellationToken ct = default);

    /// <summary>Every idea's record as it stands on disk, cheap enough to ask for twice a second.</summary>
    Task<IReadOnlyList<IdeaStamp>> StampsAsync(CancellationToken ct = default);
}

/// <inheritdoc />
internal sealed partial class IdeaService : IIdeaService
{
    /// <summary>The piece that names the whole plan rather than one part of it.</summary>
    public const string WholePlan = IdeaWork.WholePlan;

    private const int TitleLength = TaskTitles.Length;

    private readonly IWorkspaceManager _workspace;
    private readonly ITaskService _tasks;
    private readonly YamlStore _yaml;
    private readonly TimeProvider _time;
    private readonly IIdeaLocks _locks;
    private readonly KeptFiles<IdeaRecord> _kept;

    public IdeaService(
        IWorkspaceManager workspace,
        ITaskService tasks,
        YamlStore yaml,
        TimeProvider time,
        IIdeaLocks locks)
    {
        _workspace = workspace;
        _tasks = tasks;
        _yaml = yaml;
        _time = time;
        _locks = locks;
        _kept = new KeptFiles<IdeaRecord>(yaml, () => new IdeaRecord(), Copy);
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
            TaskKind.Idea,
            // Guessed here rather than by the list, which sees only the
            // title: what was asked says far more about what it is.
            type: TaskTypes.Guess($"{title}\n{text}", TaskKind.Idea, project),
            typeBy: TaskTypeSource.Rule).ConfigureAwait(false);

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
                    record is null ? 0 : IdeaWork.Unanswered(record).Count,
                    record is null ? null : IdeaWork.FailureOf(record),
                    _locks.Held(place),
                    item.Moved,
                    record is null ? 0 : IdeaWork.OutdatedCount(record),
                    Backfill.ContractBackfill.MarkOf(record),
                    item.Type,
                    item.TypeBy));
            }
        }

        return OperationResult<IReadOnlyList<IdeaSummary>>.Ok(ideas);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<IdeaMovedOn>>> MovedOnAsync(CancellationToken ct = default)
    {
        if (Unavailable() is { } missing)
        {
            return OperationResult<IReadOnlyList<IdeaMovedOn>>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        var moved = await _tasks.MovedAsync(ct).ConfigureAwait(false);

        if (moved.Failed)
        {
            return OperationResult<IReadOnlyList<IdeaMovedOn>>.Fail(moved.Error!, moved.ExitCode);
        }

        // Work, not ideas: an idea that moved is still on the ideas list, on
        // the list it went to. One that became work is on no ideas list at
        // all, and only its record says it was ever an idea.
        var gone = moved.Value!
            .Where(one => one.Task.Kind == TaskKind.Task && one.Left(one.From))
            .Where(one => File.Exists(RecordPath(new IdeaPlace(one.Where, one.Task.Id))))
            .Select(one => new IdeaMovedOn(one.Task.Id, one.Task.Title, one.From, one.Where, one.Task.Moved!.Utc))
            .ToList();

        return OperationResult<IReadOnlyList<IdeaMovedOn>>.Ok(gone);
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
        CancellationToken ct = default) =>
        EditAsync(place, [Answer(place, question, answer)], null, ct);

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> ChooseAsync(
        IdeaPlace place,
        string layer,
        string option,
        CancellationToken ct = default) =>
        EditAsync(place, [Choose(layer, option)], null, ct);

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> JudgeAsync(
        IdeaPlace place,
        string piece,
        IdeaVerdict verdict,
        string? request = null,
        CancellationToken ct = default) =>
        EditAsync(place, [Judge(piece, verdict, request)], null, ct);

    /// <summary>Answering one of the agent's questions, replacing any earlier answer.</summary>
    private static IdeaEdit Answer(IdeaPlace place, string question, string answer) => new(
        string.IsNullOrWhiteSpace(answer) ? "An answer cannot be empty." : null,
        answer,
        record =>
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

            // Answered against what is there now, so what it relates to is
            // taken again: giving the same answer is how one says it still stands.
            IdeaWork.Retake(record, asked);

            return null;
        });

    /// <summary>Picking one of a layer's options.</summary>
    private static IdeaEdit Choose(string layer, string option) => new(
        null,
        null,
        record =>
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
        });

    /// <summary>Keeping, dropping or asking for a piece of the plan to be reworked.</summary>
    private static IdeaEdit Judge(string piece, IdeaVerdict verdict, string? request) => new(
        verdict == IdeaVerdict.Improve && string.IsNullOrWhiteSpace(request)
            ? "Say what to improve: an improvement with no request gives the agent nothing to act on."
            : null,
        request,
        record =>
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
        });

    /// <inheritdoc />
    public async Task<OperationResult<IdeaRecord>> RecordReplyAsync(
        IdeaPlace place,
        IdeaReply reply,
        CancellationToken ct = default,
        IdeaAttempt? asked = null,
        IReadOnlyCollection<string>? seen = null)
    {
        ArgumentNullException.ThrowIfNull(reply);

        // An agent that read a repository can repeat what it found there. What
        // it says is written to a file that travels, so it is screened the same
        // as anything a person types, and refused whole rather than trimmed: a
        // plan with a hole cut in it would read as complete.
        if (Screened(JsonSerializer.Serialize(reply)) is { } refused)
        {
            await RecordFailureAsync(place, refused, ct, asked).ConfigureAwait(false);

            return OperationResult<IdeaRecord>.Fail(refused, ExitCode.PolicyViolation);
        }

        var recorded = await ChangeAsync(place, null, record =>
        {
            if (record.Accepted is not null)
            {
                return "That idea has already been accepted.";
            }

            var now = _time.GetUtcNow();

            IdeaWork.Merge(record, reply, now, seen);

            if (asked is not null)
            {
                record.LastRound = new IdeaAttempt { Utc = now, Agent = asked.Agent, Model = asked.Model };
            }

            return null;
        }, ct).ConfigureAwait(false);

        // After the round is kept, and not a reason to say it failed: the
        // plan is what the round was for, and a type it could not set is
        // still the guess it was before.
        if (recorded.Succeeded && TaskTypes.Parse(reply.Type) is { } type)
        {
            await _tasks.RetypeAsync(place.Project, place.Id, type, TaskTypeSource.Agent, "agent", ct).ConfigureAwait(false);
        }

        return recorded;
    }

    /// <inheritdoc />
    public async Task<TaskItem?> TaskOfAsync(IdeaPlace place, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        var listed = await _tasks.ListAsync(place.Project, ct).ConfigureAwait(false);

        return listed.Succeeded
            ? listed.Value!.FirstOrDefault(item => string.Equals(item.Id, place.Id, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> RetypeAsync(
        IdeaPlace place,
        TaskType? type,
        string by,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (type is { } chosen)
        {
            return await _tasks.RetypeAsync(place.Project, place.Id, chosen, TaskTypeSource.Person, by, ct).ConfigureAwait(false);
        }

        // Handed back: the rules' guess from what was asked, as at capture,
        // which any round may revise because it is no longer a choice.
        var read = await ReadAsync(place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<TaskItem>.Fail(read.Error!, read.ExitCode);
        }

        var task = await TaskOfAsync(place, ct).ConfigureAwait(false);
        var guess = TaskTypes.Guess(
            string.Join(' ', task?.Title ?? string.Empty, read.Value!.Ask, string.Join(' ', read.Value.Notes.Select(note => note.Text))),
            TaskKind.Idea,
            place.Project);

        return await _tasks.RetypeAsync(place.Project, place.Id, guess, TaskTypeSource.Rule, by, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> AddToAsync(
        IdeaPlace place,
        string text,
        string by,
        CancellationToken ct = default) =>
        EditAsync(place, [AddNote(place, text, by, _time.GetUtcNow())], null, ct);

    /// <inheritdoc />
    public async Task<OperationResult<IdeaRecord>> PreviewAddToAsync(
        IdeaPlace place,
        string text,
        string by,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);

        var edit = AddNote(place, text, by, _time.GetUtcNow());

        if (edit.Refusal is { } refusal)
        {
            return OperationResult<IdeaRecord>.Fail(refusal, ExitCode.InvalidArguments);
        }

        if (Screened(edit.Screen) is { } screened)
        {
            return OperationResult<IdeaRecord>.Fail(screened, ExitCode.PolicyViolation);
        }

        // ReadAsync hands out a copy, so the change made to it is made to nothing kept.
        var read = await ReadAsync(place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return read;
        }

        return edit.Change(read.Value!) is { } refused
            ? OperationResult<IdeaRecord>.Fail(refused, ExitCode.InvalidArguments)
            : read;
    }

    /// <summary>Adding a note to an idea, verbatim, for the next round to read.</summary>
    private static IdeaEdit AddNote(IdeaPlace place, string text, string by, DateTimeOffset now) => new(
        string.IsNullOrWhiteSpace(text) ? "A note needs saying: the text was empty." : null,
        text,
        record =>
        {
            if (IdeaWork.NoteRefusal(place, record) is { } refused)
            {
                return refused;
            }

            IdeaWork.AddNote(record, text, by, now);

            return null;
        });

    /// <inheritdoc />
    public async Task<OperationResult<IdeaNote>> RemoveNoteAsync(IdeaPlace place, string note, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(note);

        if (Running(place) is { } running)
        {
            return OperationResult<IdeaNote>.Fail(running, ExitCode.InvalidArguments);
        }

        IdeaNote? removed = null;
        string? refusal = null;
        var code = ExitCode.InvalidArguments;

        var edited = await EditAsync(place, [new IdeaEdit(null, null, record =>
        {
            var taken = IdeaWork.RemoveNote(place, record, note);
            removed = taken.Value;
            refusal = taken.Error;
            code = taken.ExitCode;

            return taken.Error;
        })], null, ct).ConfigureAwait(false);

        return edited.Succeeded
            ? OperationResult<IdeaNote>.Ok(removed!)
            : OperationResult<IdeaNote>.Fail(refusal ?? edited.Error!, refusal is null ? edited.ExitCode : code);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IdeaNote>> PreviewRemoveNoteAsync(IdeaPlace place, string note, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentNullException.ThrowIfNull(note);

        if (Running(place) is { } running)
        {
            return OperationResult<IdeaNote>.Fail(running, ExitCode.InvalidArguments);
        }

        // ReadAsync hands out a copy, so the change made to it is made to nothing kept.
        var read = await ReadAsync(place, ct).ConfigureAwait(false);

        return read.Failed
            ? OperationResult<IdeaNote>.Fail(read.Error!, read.ExitCode)
            : IdeaWork.RemoveNote(place, read.Value!, note);
    }

    private string? Running(IdeaPlace place) =>
        _locks.Held(place)
            ? $"A round on {place.Id} is running on this machine, and may have read the note already. "
                + "Remove it when the round has finished. Nothing was removed."
            : null;

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> RecordFailureAsync(
        IdeaPlace place,
        string error,
        CancellationToken ct = default,
        IdeaAttempt? asked = null) =>
        ChangeAsync(place, null, record =>
        {
            IdeaWork.Failed(record, new IdeaAttempt
            {
                Utc = _time.GetUtcNow(),
                Agent = asked?.Agent ?? string.Empty,
                Model = asked?.Model ?? string.Empty,
                Error = error,
            });

            return null;
        }, ct);

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> DismissFailureAsync(IdeaPlace place, CancellationToken ct = default) =>
        EditAsync(place, [Dismiss(place)], null, ct);

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> FillGapsAsync(
        IdeaPlace place,
        int revision,
        IReadOnlyList<string> doneWhen,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(doneWhen);

        var conditions = doneWhen.Select(one => one.Trim()).Where(one => one.Length > 0).ToList();

        return ChangeAsync(place, string.Join(' ', conditions), record =>
        {
            // Asked again under the lock: somebody may have answered, asked or
            // refined since the pass looked, and a revision written over that
            // would make what they said look taken in.
            if (record.Plan is not { } plan || plan.Revision != revision)
            {
                return $"{place.Id}'s plan has changed since the pass read it, so nothing was filled.";
            }

            if (Backfill.ContractBackfill.ForIdea(place.Project, new TaskItem { Id = place.Id, Kind = TaskKind.Idea }, record) is not { } finding
                || !finding.Missing.Contains(IdeaContract.DoneWhen, StringComparer.Ordinal))
            {
                return $"{place.Id} no longer has an unaccepted plan missing its done-when, so nothing was filled.";
            }

            if (finding.Skipped is { } skipped)
            {
                return $"Nothing was filled for {place.Id}. {skipped}";
            }

            if (conditions.Count == 0)
            {
                return "There are no conditions to fill it with.";
            }

            plan.Revision++;
            plan.ProposedUtc = _time.GetUtcNow();
            plan.DoneWhen = conditions;
            plan.GapFilled = [.. plan.GapFilled.Append(IdeaContract.DoneWhen).Distinct(StringComparer.Ordinal)];
            record.GapFillFailure = null;

            return null;
        }, ct);
    }

    /// <inheritdoc />
    public Task<OperationResult<IdeaRecord>> RecordGapFillFailureAsync(
        IdeaPlace place,
        IdeaAttempt attempt,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        return ChangeAsync(place, attempt.Error, record =>
        {
            record.GapFillFailure = new IdeaAttempt
            {
                Utc = _time.GetUtcNow(),
                Agent = attempt.Agent,
                Model = attempt.Model,
                Error = attempt.Error,
            };

            return null;
        }, ct);
    }

    /// <summary>Forgetting the latest failure without running another round.</summary>
    private static IdeaEdit Dismiss(IdeaPlace place) => new(
        null,
        null,
        record =>
        {
            if (IdeaWork.FailureOf(record) is null)
            {
                return $"{place.Id} has no failed round to dismiss.";
            }

            IdeaWork.ClearFailure(record);

            return null;
        });

    /// <inheritdoc />
    public async Task<OperationResult<IdeaAccepted>> AcceptAsync(
        IdeaPlace place,
        string project,
        string by,
        CancellationToken ct = default,
        IReadOnlyList<string>? doneWhen = null)
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
                    $"{place.Id} has changes the plan has not taken in yet: improvements, a request, "
                    + "new answers or something you added. Refine it first, or take the improvements back.",
                    ExitCode.InvalidArguments);
        }

        var destination = project.Trim();
        var manifest = await _workspace.ReadProjectAsync(destination, ct).ConfigureAwait(false);

        if (manifest.Failed)
        {
            return OperationResult<IdeaAccepted>.Fail(
                $"'{destination}' is not a project in this workspace.", ExitCode.ProjectNotFound);
        }

        // What was confirmed goes into the plan before it is written, so the
        // document, the record and the task all say the same finish line.
        if (doneWhen is not null)
        {
            record.Plan!.DoneWhen = [.. doneWhen.Select(one => one.Trim()).Where(one => one.Length > 0)];
        }

        // Accepting is where the person confirms what a pass filled in, so
        // nothing in the plan is unseen any more.
        record.Plan!.GapFilled = [];

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
            $"The plan is at {planPath}.",
            ct,
            TaskKind.Task,
            record.Plan.Understanding.Trim(),
            record.Plan.DoneWhen).ConfigureAwait(false);

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

        return OperationResult<IdeaAccepted>.Ok(new IdeaAccepted(declared.Value!, destination, planPath, place.Project));
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

    /// <inheritdoc />
    public async Task<OperationResult<TaskItem>> MoveAsync(
        string? from,
        string? to,
        string id,
        CancellationToken ct = default)
    {
        if (TaskIds.Rejection(id) is { } rejected)
        {
            return OperationResult<TaskItem>.Fail(rejected, ExitCode.InvalidArguments);
        }

        if (Unavailable() is { } missing)
        {
            return OperationResult<TaskItem>.Fail(missing, ExitCode.WorkspaceSyncFailed);
        }

        from = from is { Length: > 0 } ? from.Trim() : null;
        to = to is { Length: > 0 } ? to.Trim() : null;

        if (to is not null && (await _workspace.ReadProjectAsync(to, ct).ConfigureAwait(false)).Failed)
        {
            return OperationResult<TaskItem>.Fail(
                $"'{to}' is not a project in this workspace.", ExitCode.ProjectNotFound);
        }

        var trimmed = id.Trim();
        var listed = await _tasks.ListAsync(from, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<TaskItem>.Fail(listed.Error!, listed.ExitCode);
        }

        if (listed.Value!.FirstOrDefault(item => Same(item.Id, trimmed)) is not { } moving)
        {
            return OperationResult<TaskItem>.Fail(
                $"'{trimmed}' is not on {TaskPaths.Describe(from)}.", ExitCode.InvalidArguments);
        }

        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return OperationResult<TaskItem>.Ok(moving);
        }

        var source = new IdeaPlace(from, trimmed);
        var landed = new IdeaPlace(to, trimmed);

        // A round writes its answer to the record where it found it, so a
        // record moved from under one would be written back beside nothing.
        if (_locks.Held(source))
        {
            return OperationResult<TaskItem>.Fail(
                $"A round on {trimmed} is running on this machine. Move it when the round has finished.",
                ExitCode.InvalidArguments);
        }

        // Held for the move, so a round cannot start on it half way.
        var taken = _locks.Take(source);

        if (taken.Failed)
        {
            return OperationResult<TaskItem>.Fail(taken.Error!, taken.ExitCode);
        }

        using var held = taken.Value!;

        var there = await _tasks.ListAsync(to, ct).ConfigureAwait(false);

        if (there.Failed)
        {
            return OperationResult<TaskItem>.Fail(there.Error!, there.ExitCode);
        }

        var record = RecordPath(source);
        var plan = PlanPath(source);
        var toRecord = RecordPath(landed);
        var toPlan = PlanPath(landed);

        // Checked before anything is copied, so a refusal leaves both lists
        // exactly as they were rather than with a stray copy to clear up.
        if (there.Value!.Any(item => Same(item.Id, trimmed)) || File.Exists(toRecord) || File.Exists(toPlan))
        {
            return OperationResult<TaskItem>.Fail(
                $"{TaskPaths.Describe(to)} already has something called '{trimmed}', so nothing was moved. "
                + "Remove or rename one of them first.",
                ExitCode.InvalidArguments);
        }

        var oldPlan = TaskPaths.Relative(from, $"ideas/{trimmed}.md");
        var newPlan = TaskPaths.Relative(to, $"ideas/{trimmed}.md");

        // Copied first, then the task moved, then the originals taken away: the
        // same order as the task's own move, for the same reason. Something in
        // two places can be seen and cleared; something in neither is gone.
        if (await LoadAsync(source, ct).ConfigureAwait(false) is { } kept)
        {
            if (kept.Accepted is { } acceptance)
            {
                acceptance.Project = to ?? string.Empty;

                if (string.Equals(acceptance.PlanPath, oldPlan, StringComparison.Ordinal))
                {
                    acceptance.PlanPath = newPlan;
                }
            }

            var saved = await _yaml.SaveAsync(toRecord, kept, true, ct).ConfigureAwait(false);

            if (saved.Failed)
            {
                return OperationResult<TaskItem>.Fail(saved.Error!, saved.ExitCode);
            }
        }

        if (File.Exists(plan))
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(toPlan)!);
                File.Copy(plan, toPlan);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                TryDelete(toRecord);

                return OperationResult<TaskItem>.Fail($"The plan could not be copied: {ex.Message}");
            }
        }

        var moved = await _tasks.MoveAsync(from, to, trimmed, ct).ConfigureAwait(false);

        if (moved.Failed)
        {
            TryDelete(toRecord);
            TryDelete(toPlan);

            return moved;
        }

        var result = moved.Value!;

        // The note an acceptance wrote names where the plan is, and would go
        // on naming the list it left. Only that mention is changed.
        if (File.Exists(toPlan) && result.Note.Contains(oldPlan, StringComparison.Ordinal))
        {
            var rewritten = await _yaml.UpdateAsync<TaskList>(
                TaskPaths.List(_workspace.LocalPath, to),
                () => new TaskList(),
                list =>
                {
                    if (list.Items.FirstOrDefault(item => Same(item.Id, trimmed)) is { } landedTask)
                    {
                        landedTask.Note = landedTask.Note.Replace(oldPlan, newPlan, StringComparison.Ordinal);
                        result = landedTask;
                    }
                },
                true,
                ct).ConfigureAwait(false);

            if (rewritten.Failed)
            {
                return OperationResult<TaskItem>.Fail(
                    $"{trimmed} moved to {TaskPaths.Describe(to)}, but its note still names the plan at "
                    + $"{oldPlan}: {rewritten.Error}",
                    rewritten.ExitCode);
            }
        }

        if (File.Exists(toRecord))
        {
            TryDelete(record);
        }

        if (File.Exists(toPlan))
        {
            TryDelete(plan);
        }

        return OperationResult<TaskItem>.Ok(result);
    }

    private string PlanPath(IdeaPlace place) =>
        Path.Combine(TaskPaths.Ideas(_workspace.LocalPath, place.Project), $"{place.Id}.md");

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private Task<OperationResult<IdeaRecord>> ChangeAsync(
        IdeaPlace place,
        string? screen,
        Func<IdeaRecord, string?> change,
        CancellationToken ct) =>
        EditAsync(place, [new IdeaEdit(null, screen, change)], null, ct);

    private async Task<IdeaRecord?> LoadAsync(IdeaPlace place, CancellationToken ct)
    {
        var path = RecordPath(place);

        if (!File.Exists(path))
        {
            return null;
        }

        var loaded = await _kept.ReadAsync(path, ct).ConfigureAwait(false);

        return loaded.Succeeded ? loaded.Value!.Value : null;
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
