using Loadout.Agents.Ideas;
using Loadout.Agents.Tasks;
using Loadout.Core.Backfill;
using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Backfill;

/// <summary>What a pass is asked to do.</summary>
/// <param name="Lists">The lists to look at: project slugs, and null for the workspace-wide one.</param>
/// <param name="Limit">The most records to ask an agent about, or null for every one.</param>
/// <param name="AgentName">The agent to ask, or null for the default.</param>
/// <param name="Model">The model to ask for, or null for the agent's own.</param>
/// <param name="DryRun">Say what would be asked, and ask nothing.</param>
public sealed record BackfillRequest(
    IReadOnlyList<string?> Lists,
    int? Limit = null,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false);

/// <summary>What became of one record a pass asked an agent about.</summary>
/// <param name="Item">The record, as the pass found it.</param>
/// <param name="DoneWhen">What was written or held, or empty when nothing was.</param>
/// <param name="Error">What went wrong, or null when nothing did.</param>
/// <param name="CostUsd">What asking cost.</param>
public sealed record BackfillResult(BackfillItem Item, IReadOnlyList<string> DoneWhen, string? Error, decimal CostUsd)
{
    public bool Filled => Error is null;
}

/// <summary>What a pass found, and, unless it was a dry run, what it did.</summary>
/// <param name="Found">Every record short of the contract, filled or skipped, in the order found.</param>
/// <param name="Asking">The records a real pass asks about, after the limit: one agent call each.</param>
/// <param name="Results">What became of each, empty for a dry run.</param>
/// <param name="DryRun">Whether nothing was asked.</param>
public sealed record BackfillOutcome(
    IReadOnlyList<BackfillItem> Found,
    IReadOnlyList<BackfillItem> Asking,
    IReadOnlyList<BackfillResult> Results,
    bool DryRun)
{
    /// <summary>The model calls a real pass makes, or made.</summary>
    public int Calls => Asking.Count;
}

/// <summary>
/// Brings open ideas and tasks up to the current contract: fills an idea's
/// missing plan fields as a new revision, and holds a proposed done-when on a
/// task until a person confirms it.
/// </summary>
public interface IContractBackfiller
{
    Task<OperationResult<BackfillOutcome>> RunAsync(BackfillRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// Started by hand only, from the command or from Studio, which runs the same
/// command. A record whose last attempt failed is tried again on every pass,
/// with no point at which it is given up on: the dry run lists the failures
/// and counts their calls first, and the limit caps a run, so a record that
/// keeps failing is seen and bounded without a count of its own.
/// </para>
/// <para>
/// One record at a time. Each is a round with an agent, and a pass is a chore
/// somebody started, not something waited on, so the order is worth more than
/// the minutes.
/// </para>
/// </remarks>
internal sealed class ContractBackfiller : IContractBackfiller
{
    private readonly ITaskService _tasks;
    private readonly IIdeaService _ideas;
    private readonly IIdeaLocks _locks;
    private readonly IDoneWhenProposer _proposer;
    private readonly IProjectService _projects;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;

    public ContractBackfiller(
        ITaskService tasks,
        IIdeaService ideas,
        IIdeaLocks locks,
        IDoneWhenProposer proposer,
        IProjectService projects,
        IDetachedLauncher launcher,
        IPlatformPaths paths)
    {
        _tasks = tasks;
        _ideas = ideas;
        _locks = locks;
        _proposer = proposer;
        _projects = projects;
        _launcher = launcher;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<OperationResult<BackfillOutcome>> RunAsync(BackfillRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Limit is < 1)
        {
            return OperationResult<BackfillOutcome>.Fail("--limit takes a number of records, one or more.", ExitCode.InvalidArguments);
        }

        var found = new List<BackfillItem>();

        foreach (var list in request.Lists)
        {
            var listed = await _tasks.ListAsync(list, ct).ConfigureAwait(false);

            if (listed.Failed)
            {
                return OperationResult<BackfillOutcome>.Fail(listed.Error!, listed.ExitCode);
            }

            foreach (var task in listed.Value!)
            {
                var item = task.Kind == TaskKind.Idea
                    ? ContractBackfill.ForIdea(list, task, await RecordAsync(list, task.Id, ct).ConfigureAwait(false), _locks.Held(new IdeaPlace(list, task.Id)))
                    : ContractBackfill.ForTask(list, task);

                if (item is not null)
                {
                    found.Add(item);
                }
            }
        }

        var asking = ContractBackfill.Capped(found, request.Limit);

        if (request.DryRun)
        {
            return OperationResult<BackfillOutcome>.Ok(new BackfillOutcome(found, asking, [], true));
        }

        var results = new List<BackfillResult>();

        foreach (var item in asking)
        {
            results.Add(item.Kind == BackfillKind.Idea
                ? await IdeaAsync(item, request, ct).ConfigureAwait(false)
                : await TaskAsync(item, request, ct).ConfigureAwait(false));
        }

        return OperationResult<BackfillOutcome>.Ok(new BackfillOutcome(found, asking, results, false));
    }

    private async Task<BackfillResult> TaskAsync(BackfillItem item, BackfillRequest request, CancellationToken ct)
    {
        var proposed = await _proposer.ProposeAsync(
            new DoneWhenRequest(item.Project, item.Id, request.AgentName, request.Model), ct).ConfigureAwait(false);

        if (proposed.Failed || proposed.Value!.Proposal is not { } proposal)
        {
            var error = proposed.Error ?? "The agent was not asked.";

            await _tasks.HoldProposalAsync(item.Project, item.Id, new TaskProposal { Error = error }, ct).ConfigureAwait(false);

            return new BackfillResult(item, [], error, 0m);
        }

        var held = await _tasks.HoldProposalAsync(
            item.Project,
            item.Id,
            new TaskProposal { DoneWhen = [.. proposal.DoneWhen], Why = proposal.Why },
            ct).ConfigureAwait(false);

        if (held.Failed)
        {
            // Refused for what it says, such as something that looks like a
            // credential: kept as a failure so the next pass asks again.
            await _tasks.HoldProposalAsync(item.Project, item.Id, new TaskProposal { Error = held.Error! }, ct).ConfigureAwait(false);

            return new BackfillResult(item, [], held.Error, proposed.Value.CostUsd);
        }

        return new BackfillResult(item, proposal.DoneWhen, null, proposed.Value.CostUsd);
    }

    private async Task<BackfillResult> IdeaAsync(BackfillItem item, BackfillRequest request, CancellationToken ct)
    {
        var place = new IdeaPlace(item.Project, item.Id);
        var taken = _locks.Take(place);

        if (taken.Failed)
        {
            return new BackfillResult(item, [], taken.Error, 0m);
        }

        using var held = taken.Value!;

        var record = await RecordAsync(item.Project, item.Id, ct).ConfigureAwait(false);

        if (record?.Plan is not { } plan)
        {
            return new BackfillResult(item, [], $"{item.Id} has no plan any more.", 0m);
        }

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(
                await DirectoryAsync(item.Project, ct).ConfigureAwait(false),
                $"Filling the done-when of '{item.Id}'s plan",
                request.AgentName,
                request.Model,
                false),
            IdeaRefiner.Allowed,
            DoneWhenSchema.Version1,
            DoneWhenSchema.Version,
            IdeaGapWork.Prompt(record, item.Project),
            DoneWhenWork.Read,
            null,
            ct).ConfigureAwait(false);

        var attempt = new IdeaAttempt { Agent = asked.Value?.Agent ?? request.AgentName ?? string.Empty, Model = request.Model ?? string.Empty };
        var cost = asked.Value?.CostUsd ?? 0m;
        var error = asked.Failed
            ? asked.Error!
            : !asked.Value!.Started
                ? "The agent was not started."
                : asked.Value.Answer.Failed ? asked.Value.Answer.Error! : null;

        if (error is null)
        {
            var conditions = asked.Value!.Answer.Value!.DoneWhen;
            var filled = await _ideas.FillGapsAsync(place, plan.Revision, conditions, ct).ConfigureAwait(false);

            if (filled.Succeeded)
            {
                return new BackfillResult(item, conditions, null, cost);
            }

            error = filled.Error!;
        }

        attempt.Error = error;
        await _ideas.RecordGapFillFailureAsync(place, attempt, ct).ConfigureAwait(false);

        return new BackfillResult(item, [], error, cost);
    }

    private async Task<IdeaRecord?> RecordAsync(string? list, string id, CancellationToken ct)
    {
        var read = await _ideas.ReadAsync(new IdeaPlace(list, id), ct).ConfigureAwait(false);

        return read.Succeeded ? read.Value : null;
    }

    /// <summary>The project's checkout when it has one, so the agent can read the code; otherwise somewhere sure to exist.</summary>
    private async Task<string> DirectoryAsync(string? project, CancellationToken ct) =>
        project is { } slug
        && (await _projects.ResolveAsync(slug, ct).ConfigureAwait(false)).Value?.LocalPath is { Length: > 0 } checkout
            ? checkout
            : _paths.Paths.State;
}
