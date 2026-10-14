using Loadout.Core.Teams;
using Loadout.Models.Results;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Closes a run: ends the lead, takes its branches through the merge gate,
/// writes the ending down and says how it went.
/// </summary>
internal sealed class CloseRunStep
{
    private readonly FoldStep _fold;
    private readonly MergeStep _merge;
    private readonly DeclareStep _declare;

    public CloseRunStep(FoldStep fold, MergeStep merge, DeclareStep declare)
    {
        _fold = fold;
        _merge = merge;
        _declare = declare;
    }

    /// <summary>
    /// Ends the lead and folds in what it left, however the run left its loop.
    /// Not cancellable, because a run being cancelled is one of the ways.
    /// </summary>
    public async Task EndLeadAsync(RunContext run, JournalWriter journal)
    {
        var (team, lead) = (run.Team, run.Lead!);

        var (exit, killed) = await lead.Session!.EndAsync(TeamRunner.EndGrace, CancellationToken.None).ConfigureAwait(false);
        var stderr = TeamRunner.Tail(lead.Session.StandardError);

        await journal.WriteAsync("node.ended", team.Lead, new { exit, killed, stderr }, CancellationToken.None).ConfigureAwait(false);
        await lead.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);

        await _fold.RunAsync(run, journal, run.LeadBrief, run.Warnings, CancellationToken.None).ConfigureAwait(false);

        // A node that produced no report and said why on its error
        // stream has explained itself, and that explanation must reach
        // the person. The first real run ended "without a report" in
        // under a second, exit code 1, and nothing said what the agent
        // had printed.
        if (run.Final is null && stderr.Length > 0)
        {
            run.Warnings.Add($"The lead wrote to its error stream (exit code {exit}): {stderr}");
        }

    }

    /// <summary>The merge, the ending written down, and the outcome.</summary>
    public async Task<OperationResult<TeamRunOutcome>> RunAsync(
        RunContext run,
        JournalWriter journal,
        Watching watching,
        CancellationToken ct)
    {
        var (request, team, console, autonomy, runId, directory) =
            (run.Request, run.Team, run.Console, run.Autonomy, run.RunId, run.Directory);
        var (slug, forTask) = (run.Slug, run.ForTask);

        // A node asked to resolve a conflict spends money like any other, and
        // the run's total is written after the merge, so it is still in time
        // to count it.
        var afterwards = new List<decimal>();

        var merged = await _merge.RunAsync(run, journal, request, team, runId, autonomy, console, afterwards, ct)
            .ConfigureAwait(false);

        run.Cost += afterwards.Sum();

        // The watch stops before the ending is written, so its last look -
        // an answer given just before the end - goes down ahead of it, where
        // somebody following the journal still reads it.
        await watching.StopAsync().ConfigureAwait(false);

        await journal.WriteAsync(
            "run.finished", null, new { ended = run.Ended, outcome = TeamRunner.Filed(run.Ended), cost = run.Cost, rounds = run.Rounds, merged }, ct).ConfigureAwait(false);

        await _declare.RunAsync(
            run, journal, slug, runId, forTask, team, TeamRunner.Landed(run.Ended), request.Goal,
            $"{run.Ended}; {run.Rounds} round(s), ${run.Cost:0.00}. loadout team status {runId}", ct)
            .ConfigureAwait(false);

        if (run.Final is not null)
        {
            await TeamRunner.WriteDocumentAsync(directory, "final-report.json", ReportReader.Write(run.Final), ct).ConfigureAwait(false);
        }

        // Every node's launch contributes the same preflight notices, so a
        // four-node run said each of them four times. The same sentence
        // twice is noise that buries the one that was only said once.
        return OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
            runId, directory, run.Ended, run.Final, run.Cost, run.Rounds,
            run.Warnings.Distinct(StringComparer.Ordinal).ToList(), run.Branches, merged));

    }
}
