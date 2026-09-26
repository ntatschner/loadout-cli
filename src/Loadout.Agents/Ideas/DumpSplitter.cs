using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Ideas;

/// <summary>A dump to split, and how.</summary>
public sealed record SplitRequest(
    DumpPlace Place,
    string? AgentName = null,
    string? Model = null,
    bool DryRun = false);

/// <summary>What splitting did.</summary>
/// <param name="Dump">The dump as it now stands; unchanged on a dry run.</param>
/// <param name="Plan">What was started, or would have been.</param>
/// <param name="Warnings">Anything worth saying about how it was started.</param>
/// <param name="CostUsd">What it cost, when the agent said.</param>
public sealed record SplitOutcome(
    IdeaDump Dump,
    LaunchPlan Plan,
    IReadOnlyList<string> Warnings,
    decimal CostUsd);

/// <summary>Has an agent split a dump of notes into separate ideas and tasks.</summary>
public interface IDumpSplitter
{
    /// <summary>Proposes a split, replacing any earlier one, and records nothing else.</summary>
    Task<OperationResult<SplitOutcome>> SplitAsync(SplitRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// The agent is given the notes and the projects that exist and nothing else:
/// no tools at all, and a directory of its own. Everything it needs is in the
/// prompt, and a split is a reading of text, not an investigation.
/// </remarks>
internal sealed class DumpSplitter : IDumpSplitter
{
    private readonly IIdeaDumps _dumps;
    private readonly IProjectService _projects;
    private readonly IDetachedLauncher _launcher;
    private readonly IPlatformPaths _paths;

    public DumpSplitter(
        IIdeaDumps dumps,
        IProjectService projects,
        IDetachedLauncher launcher,
        IPlatformPaths paths)
    {
        _dumps = dumps;
        _projects = projects;
        _launcher = launcher;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SplitOutcome>> SplitAsync(SplitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var read = await _dumps.ReadAsync(request.Place, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return OperationResult<SplitOutcome>.Fail(read.Error!, read.ExitCode);
        }

        var dump = read.Value!;

        if (dump.Items.Any(item => item.Recorded.Length > 0))
        {
            return OperationResult<SplitOutcome>.Fail(
                $"Some of {dump.Id} has been recorded already, so it is not split again. Apply the rest, "
                + "or drop the notes in afresh.",
                ExitCode.InvalidArguments);
        }

        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        var hints = projects.Succeeded
            ? projects.Value!.Select(p => new IdeaProjectHint(p.Entry.Slug, p.Entry.Name)).ToList()
            : [];

        var directory = Path.Combine(_paths.Paths.State, "ideas", dump.Id);

        if (request.DryRun)
        {
            directory = _paths.Paths.State;
        }
        else
        {
            Directory.CreateDirectory(directory);
        }

        var asked = await SchemaRound.AskAsync(
            _launcher,
            new DetachedLaunchRequest(directory, $"Splitting the notes in '{dump.Id}'", request.AgentName, request.Model, request.DryRun),
            [],
            DumpSchema.Version1,
            DumpSchema.Version,
            DumpWork.Prompt(dump, hints),
            json => DumpWork.Read(json, dump.Text),
            null,
            ct).ConfigureAwait(false);

        if (asked.Failed)
        {
            return OperationResult<SplitOutcome>.Fail(asked.Error!, asked.ExitCode);
        }

        var round = asked.Value!;

        if (!round.Started)
        {
            return OperationResult<SplitOutcome>.Ok(new SplitOutcome(dump, round.Plan, round.Warnings, 0));
        }

        if (round.Answer.Failed)
        {
            await _dumps.RecordFailureAsync(request.Place, round.Answer.Error!, CancellationToken.None).ConfigureAwait(false);

            return OperationResult<SplitOutcome>.Fail(round.Answer.Error!, ExitCode.GeneralFailure);
        }

        var recorded = await _dumps.RecordSplitAsync(request.Place, round.Answer.Value!, CancellationToken.None)
            .ConfigureAwait(false);

        return recorded.Succeeded
            ? OperationResult<SplitOutcome>.Ok(new SplitOutcome(recorded.Value!, round.Plan, round.Warnings, round.CostUsd))
            : OperationResult<SplitOutcome>.Fail(recorded.Error!, recorded.ExitCode);
    }
}
