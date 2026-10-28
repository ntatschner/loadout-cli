using System.Runtime.CompilerServices;
using Loadout.Agents.Teams;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Teams;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Runs the nodes a lead asked for: each started, given its brief, judged on
/// its report and ended, together where the team allows it.
/// </summary>
internal sealed class StartWorkersStep
{
    private readonly NodeSessions _sessions;
    private readonly ReviewTreeStep _reviewTree;
    private readonly FoldStep _fold;
    private readonly Core.Git.IGitManager? _git;

    public StartWorkersStep(NodeSessions sessions, ReviewTreeStep reviewTree, FoldStep fold, Core.Git.IGitManager? git)
    {
        _sessions = sessions;
        _reviewTree = reviewTree;
        _fold = fold;
        _git = git;
    }

    /// <summary>
    /// Runs briefed nodes together, within what the team allows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each node says how many of itself may run at once, and that is the
    /// limit honoured here - the same number the lead is told in its brief,
    /// so what it was promised and what it gets are the same thing.
    /// </para>
    /// <para>
    /// Starting is serialised even while the turns overlap. A node with a
    /// worktree of its own has one made for it at launch, and two of those
    /// at once are two gits writing the same repository's index.
    /// </para>
    /// <para>
    /// Results come back in the order asked for, not the order finished, so
    /// the same run twice reads the same way.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<WorkerOutcome>> RunAsync(
        RunContext run,
        JournalWriter journal,
        TeamRunRequest request,
        TeamDefinition team,
        IReadOnlyList<Briefed> briefed,
        CancellationToken ct)
    {
        if (briefed.Count <= 1)
        {
            return briefed.Count == 0
                ? []
                : [await RunOneAsync(run, journal, request, team, briefed[0], new SemaphoreSlim(1, 1), ct).ConfigureAwait(false)];
        }

        using var starting = new SemaphoreSlim(1, 1);

        var limits = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        foreach (var one in briefed)
        {
            var name = TeamRunner.BaseNode(one.Ask.Node);

            if (!limits.ContainsKey(name))
            {
                limits[name] = new SemaphoreSlim(Math.Max(1, one.Node.Parallel));
            }
        }

        try
        {
            var running = briefed.Select(async one =>
            {
                var limit = limits[TeamRunner.BaseNode(one.Ask.Node)];

                await limit.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    return await RunOneAsync(run, journal, request, team, one, starting, ct).ConfigureAwait(false);
                }
                finally
                {
                    limit.Release();
                }
            });

            return await Task.WhenAll(running).ConfigureAwait(false);
        }
        finally
        {
            foreach (var limit in limits.Values)
            {
                limit.Dispose();
            }
        }
    }

    /// <summary>One brief, one report, and the session is gone.</summary>
    /// <remarks>
    /// Its warnings are its own rather than added to the run's list, because
    /// several of these run at once and a shared list would be written from
    /// more than one thread. The caller folds them in, in the order the lead
    /// asked for the nodes.
    /// </remarks>
    public async Task<WorkerOutcome> RunOneAsync(
        RunContext run,
        JournalWriter journal,
        TeamRunRequest request,
        TeamDefinition team,
        Briefed briefed,
        SemaphoreSlim starting,
        CancellationToken ct)
    {
        var (node, role, brief) = (briefed.Node, briefed.Role, briefed.Brief);
        var warnings = new List<string>();

        // A node that reads another node's work, run where that work is. See
        // ReviewTreeStep for why this is not left to the node.
        var reviewing = await _reviewTree.RunAsync(run, journal, request, role, brief, warnings, ct).ConfigureAwait(false);

        await starting.WaitAsync(ct).ConfigureAwait(false);

        OperationResult<HeadlessLaunch> started;

        try
        {
            started = await _sessions.StartAsync(run, request, team, node, role, brief, dryRun: false, ct, reviewing: reviewing)
                .ConfigureAwait(false);
        }
        finally
        {
            starting.Release();
        }

        if (started.Failed)
        {
            await journal.WriteAsync("node.failed", brief.Node, new { error = started.Error }, ct).ConfigureAwait(false);

            return new WorkerOutcome(null, 0m, false, started.Error, warnings);
        }

        // Whichever launch the node ends on is the one disposed: one started
        // again after going silent has replaced the one it began with.
        var holding = new StrongBox<HeadlessLaunch>(started.Value!);

        try
        {
            return await TalkAsync(run, journal, request, team, briefed, reviewing, holding, warnings, ct).ConfigureAwait(false);
        }
        finally
        {
            await holding.Value!.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<WorkerOutcome> TalkAsync(
        RunContext run,
        JournalWriter journal,
        TeamRunRequest request,
        TeamDefinition team,
        Briefed briefed,
        string? reviewing,
        StrongBox<HeadlessLaunch> holding,
        List<string> warnings,
        CancellationToken ct)
    {
        var (node, role, brief) = (briefed.Node, briefed.Role, briefed.Brief);
        var launch = holding.Value!;
        warnings.AddRange(launch.Warnings);

        /*
          Where this node's branch started, as a commit rather than a name.
          A name moves and can be deleted; the commit is what makes "what did
          this node produce" still answerable after the branch has been merged
          and tidied away, which is when somebody most wants to know.

          The branch was made with no commits on it, so its tip is where it
          started. Read just after the process was handed the tree rather than
          just before, which leaves a window of a few milliseconds in which a
          node could commit; nothing has ever done so that fast, and the cost
          of being wrong is a diff that misses its own first commit rather than
          anything that breaks.
        */
        string? branchedAt = null;

        if (_git is not null && brief.Constraints.Worktree is { Length: > 0 } made)
        {
            var at = await _git.ResolveAsync(launch.Plan.WorkingDirectory, made, ct).ConfigureAwait(false);

            branchedAt = at.Succeeded ? at.Value : null;
        }

        await journal.WriteAsync(
            "node.launched",
            brief.Node,
            new
            {
                launch = launch.LaunchId,
                role = node.Role,
                directory = launch.Plan.WorkingDirectory,
                worktree = brief.Constraints.Worktree,

                // The branch whose tree it was started in, to read rather
                // than to commit to, or null where it is in its own tree or
                // in the repository.
                reviewing,
                @base = branchedAt,

                // Null means whatever the agent would pick by itself, which is
                // a real answer and not a missing one - so it is written down
                // as null rather than as the name of whatever it turned out to
                // be, which nothing here knows.
                model = request.Model ?? (node.Model is { Length: > 0 } pinned ? pinned : null),
            },
            ct).ConfigureAwait(false);

        if (brief.Constraints.Worktree is { Length: > 0 } tree)
        {
            // Named where a person will look for it. A branch that passes
            // the gate takes its tree with it; one that does not is still
            // here, and either way somebody may want to go and read it.
            warnings.Add(
                $"{brief.Node} worked in a new worktree at {launch.Plan.WorkingDirectory}, on branch '{tree}'. "
                + "A branch the merge gate takes is cleared away with its tree; one it does not is left for you.");
        }

        var turn = await _sessions.TurnAsync(
            launch,
            brief,
            TeamRunner.Render(brief) + TeamRunner.Placed(reviewing, launch.Plan.WorkingDirectory),
            journal,
            ct,
            (conversation, token) => _sessions.StartAsync(
                run, request, team, node, role, brief, dryRun: false, token, conversation, reviewing),
            team.Rules.Quiet).ConfigureAwait(false);

        var exit = 0;
        var stderr = string.Empty;

        if (turn.Launch is { } ending)
        {
            launch = ending;
            holding.Value = ending;

            var (code, killed) = await ending.Session!.EndAsync(TeamRunner.EndGrace, CancellationToken.None).ConfigureAwait(false);
            exit = code;
            stderr = TeamRunner.Tail(ending.Session.StandardError);

            await journal.WriteAsync("node.ended", brief.Node, new { exit, killed, stderr }, CancellationToken.None).ConfigureAwait(false);
            await ending.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);
        }

        await _fold.RunAsync(run, journal, brief, warnings, CancellationToken.None).ConfigureAwait(false);

        if (turn.Stuck)
        {
            // Failed the way a node that cannot start fails, so the lead
            // carries on. What it committed is not thrown away: the lead is
            // told where it is, and that it was left unfinished.
            var unfinished = brief.Constraints.Worktree is { Length: > 0 } branch
                ? $" What it committed is on branch '{branch}', unfinished: merge it, finish it, or leave it."
                : string.Empty;

            var stuck = $"{brief.Node} went silent twice and was stopped.{unfinished}";
            warnings.Add(stuck);

            return new WorkerOutcome(null, turn.Cost, false, stuck, warnings);
        }

        if (turn.Report is null)
        {
            // Both halves of what it left behind: what it wrote to the
            // person, and what it wrote to its error stream. Either can be
            // the reason, and neither is worth hiding.
            if (turn.Said.Length > 0)
            {
                warnings.Add($"{brief.Node} produced no report. It said: {turn.Said}");
            }

            if (stderr.Length > 0)
            {
                warnings.Add($"{brief.Node} wrote to its error stream (exit code {exit}): {stderr}");
            }
        }

        return new WorkerOutcome(
            turn.Report,
            turn.Cost,
            turn.Halted,
            turn.Report is null
                ? $"The node ended without a report (exit code {exit})."
                    + (turn.Said.Length > 0 ? $" It said: {turn.Said}" : string.Empty)
                    + (stderr.Length > 0 ? $" It wrote to its error stream: {stderr}" : string.Empty)
                : null,
            warnings);
    }
}

/// <summary>A request that passed its gate and has a brief waiting.</summary>
internal sealed record Briefed(ReportRequest Ask, TeamNode Node, SpecialistDocument Role, Brief Brief);

internal sealed record WorkerOutcome(
    Report? Report,
    decimal Cost,
    bool Halted,
    string? Failure,
    IReadOnlyList<string> Warnings);
