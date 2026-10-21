using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Ideas;

/// <summary>What to run a round of refinement on, and how.</summary>
/// <param name="Place">The idea.</param>
/// <param name="AgentName">
/// The agent to ask, or null for the one the last round that came back used,
/// and the machine's default before there was one.
/// </param>
/// <param name="Model">The model to ask for, or null to choose as <see cref="IdeaWork.Choose"/> does.</param>
/// <param name="DryRun">Say what would be started, and start nothing.</param>
/// <param name="Watching">Told what the agent is doing while it works, or null.</param>
public sealed record RefineRequest(
    IdeaPlace Place,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false,
    Func<string, Task>? Watching = null);

/// <summary>What a round did.</summary>
/// <param name="Record">The idea as it now stands; unchanged on a dry run.</param>
/// <param name="Before">Where it stood before the round.</param>
/// <param name="After">Where it stands now.</param>
/// <param name="Plan">What was started, or would have been.</param>
/// <param name="Warnings">Anything worth telling the person about how it was started.</param>
/// <param name="CostUsd">What the round cost, when the agent said.</param>
/// <param name="Choice">The agent and model asked for, and why.</param>
/// <param name="Agent">The agent started, the machine's default resolved to its name.</param>
public sealed record RefineOutcome(
    IdeaRecord Record,
    IdeaStage Before,
    IdeaStage After,
    LaunchPlan Plan,
    IReadOnlyList<string> Warnings,
    decimal CostUsd,
    IdeaChoice Choice,
    string Agent);

/// <summary>Runs one round of fleshing out an idea: asks an agent, and records its answer.</summary>
public interface IIdeaRefiner
{
    /// <summary>Runs the next round, or says why there is none to run.</summary>
    Task<OperationResult<RefineOutcome>> RefineAsync(RefineRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// One round, one fresh agent, then it exits. The person may take a day to
/// answer, and from another machine, so nothing is kept running between rounds
/// and no session is resumed: the record is the whole state, and all of it
/// goes into every round's prompt.
/// </para>
/// <para>
/// The agent reads and does nothing else. It is started in the project's
/// repository when the idea is on a project, so it can read the code the idea
/// would change, and in an empty directory of its own otherwise. Everything it
/// may use is named; nothing else is allowed, and nothing asks.
/// </para>
/// </remarks>
internal sealed class IdeaRefiner : IIdeaRefiner
{
    /// <summary>What the agent may use: reading, and nothing that changes or reaches out.</summary>
    internal static readonly IReadOnlyList<string> Allowed = ["Read", "Glob", "Grep"];

    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly IIdeaLocks _locks;

    public IdeaRefiner(
        IIdeaService ideas,
        IProjectService projects,
        IDetachedLauncher launcher,
        IPlatformPaths paths,
        IIdeaLocks locks)
    {
        _ideas = ideas;
        _projects = projects;
        _launcher = launcher;
        _paths = paths;
        _locks = locks;
    }

    /// <inheritdoc />
    public async Task<OperationResult<RefineOutcome>> RefineAsync(RefineRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var read = await _ideas.ReadAsync(request.Place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<RefineOutcome>.Fail(read.Error!, read.ExitCode);
        }

        var record = read.Value!;
        var before = IdeaWork.StageOf(record);

        // The notes this round is shown, taken now: one the person adds while
        // the agent works was never in its prompt, so it stays unread.
        var seen = IdeaWork.Unread(record).Select(note => note.Id).ToList();

        if (Nothing(request.Place, record, before) is { } reason)
        {
            return OperationResult<RefineOutcome>.Fail(reason, ExitCode.InvalidArguments);
        }

        // Asked of a dry run too: one that said what it would start, when the
        // real thing would be refused, would be describing something that
        // cannot happen.
        if (request.DryRun && _locks.Held(request.Place))
        {
            return OperationResult<RefineOutcome>.Fail(
                $"A round on {request.Place.Id} is already running on this machine.", ExitCode.InvalidArguments);
        }

        var held = request.DryRun ? null : _locks.Take(request.Place);

        if (held is { Failed: true })
        {
            return OperationResult<RefineOutcome>.Fail(held.Error!, held.ExitCode);
        }

        using var _ = held?.Value;

        var choice = IdeaWork.Choose(record, request.AgentName, request.Model);

        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        var hints = projects.Succeeded
            ? projects.Value!.Select(p => new IdeaProjectHint(p.Entry.Slug, p.Entry.Name)).ToList()
            : [];

        var (directory, target) = Where(request.Place, projects.Value);

        if (!request.DryRun)
        {
            Directory.CreateDirectory(directory);
        }
        else if (!Directory.Exists(directory))
        {
            // A dry run changes nothing, including making the directory the
            // agent would have started in, so it describes the start from one
            // that is sure to exist.
            directory = _paths.Paths.State;
        }

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(
                directory,
                $"Fleshing out the idea '{record.Id}'",
                choice.Agent,
                choice.Model,
                request.DryRun),
            Allowed,
            IdeaSchema.Version1,
            IdeaSchema.Version,
            IdeaWork.Prompt(record, hints, target),
            IdeaReply.Read,
            request.Watching,
            ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            return OperationResult<RefineOutcome>.Fail(asked.Error!, asked.ExitCode);
        }

        var round = asked.Value!;

        if (!round.Started)
        {
            return OperationResult<RefineOutcome>.Ok(
                new RefineOutcome(record, before, before, round.Plan, round.Warnings, 0, choice, round.Agent));
        }

        // The agent by the name it resolved to, so a round asked of the
        // machine's default is repeated with that agent even after the
        // default changes.
        var attempt = new IdeaAttempt { Agent = round.Agent, Model = choice.Model ?? string.Empty };

        if (round.Answer.Failed)
        {
            await _ideas.RecordFailureAsync(request.Place, round.Answer.Error!, CancellationToken.None, attempt)
                .ConfigureAwait(false);

            return OperationResult<RefineOutcome>.Fail(round.Answer.Error!, ExitCode.GeneralFailure);
        }

        var recorded = await _ideas.RecordReplyAsync(request.Place, round.Answer.Value!, CancellationToken.None, attempt, seen)
            .ConfigureAwait(false);

        return recorded.Succeeded
            ? OperationResult<RefineOutcome>.Ok(new RefineOutcome(
                recorded.Value!,
                before,
                IdeaWork.StageOf(recorded.Value!),
                round.Plan,
                round.Warnings,
                round.CostUsd,
                choice,
                round.Agent))
            : OperationResult<RefineOutcome>.Fail(recorded.Error!, recorded.ExitCode);
    }

    /// <summary>Why there is no round to run, or null when there is one.</summary>
    internal static string? Nothing(IdeaPlace place, IdeaRecord record, IdeaStage stage) => stage switch
    {
        IdeaStage.Accepted =>
            $"{place.Id} has been accepted, onto {record.Accepted!.Project}. There is nothing left to refine.",

        IdeaStage.Answering =>
            $"{place.Id} is waiting on answers to "
            + $"{string.Join(", ", IdeaWork.Unanswered(record).Select(q => q.Id))}. Answer them first: "
            + "a round run now would be asked the same questions again.",

        IdeaStage.Proposed =>
            $"Nothing about {place.Id} has changed since its plan was proposed. Mark a piece to improve, "
            + "or ask something of the whole plan, and refine again; or accept it.",

        _ => null,
    };

    /// <summary>Where the agent starts, and what it is told about that.</summary>
    private (string Directory, string? Target) Where(
        IdeaPlace place,
        IReadOnlyList<Models.Projects.ProjectResolution>? projects)
    {
        if (place.Project is { Length: > 0 } slug
            && projects?.FirstOrDefault(p => p.Entry.Slug == slug) is { LocalPath: { Length: > 0 } path }
            && Directory.Exists(path))
        {
            return (path, $"This idea is on the project '{slug}', and you have been started in its "
                + "repository. Read the code wherever it helps you understand what the idea would change.");
        }

        var scratch = Path.Combine(_paths.Paths.State, "ideas", place.Id);

        return place.Project is { Length: > 0 } elsewhere
            ? (scratch, $"This idea is on the project '{elsewhere}', which is not cloned on this machine, "
                + "so there is no code to read.")
            : (scratch, null);
    }
}
