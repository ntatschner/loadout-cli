using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Starts a node and holds one judged turn with it. The lead, the workers and
/// the resolver all go through here, so they are started and judged the same
/// way.
/// </summary>
internal sealed class NodeSessions
{
    private readonly IAgentLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly Core.Tools.IToolRegistry? _tools;

    public NodeSessions(IAgentLauncher launcher, IPlatformPaths paths, TimeProvider time, Core.Tools.IToolRegistry? tools)
    {
        _launcher = launcher;
        _paths = paths;
        _time = time;
        _tools = tools;
    }

    /// <summary>What a node that was cut off is told when it is started again.</summary>
    internal const string CutOff =
        "Your last turn was cut off: you wrote nothing for longer than this team allows, so you were "
        + "stopped and started again. Anything you had already changed in your working tree is still there. "
        + "Check where you got to, then carry on and finish with your report.";

    /// <summary>
    /// One exchange with a node, judged. A report that fails the check goes
    /// back once with the reasons; a second failure is the node's answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A turn that goes silent for longer than the team allows is a second,
    /// separate retry with its own count. The agent is killed with everything
    /// it started, and started again once on its own conversation with a note
    /// that it was cut off; where that conversation cannot be picked up it is
    /// started afresh in the same tree, and that is the one retry spent. A
    /// second silence is the node stuck.
    /// </para>
    /// <para>
    /// A restart is a new launch, so the turn hands back the one the node is
    /// now on, and that is the one the caller ends.
    /// </para>
    /// </remarks>
    /// <param name="launch">The node, started.</param>
    /// <param name="brief">What it was briefed with.</param>
    /// <param name="prompt">What to say to it.</param>
    /// <param name="journal">The run's record.</param>
    /// <param name="ct">Cancels the turn.</param>
    /// <param name="restart">
    /// Starts the node again, on the conversation named or afresh for null.
    /// Null for a caller that cannot restart a node, whose silent turn is then
    /// stuck at once.
    /// </param>
    /// <param name="quiet">How long the node may be silent, or null for no limit.</param>
    public async Task<NodeTurn> TurnAsync(
        HeadlessLaunch launch,
        Brief brief,
        string prompt,
        JournalWriter journal,
        CancellationToken ct,
        Func<string?, CancellationToken, Task<OperationResult<HeadlessLaunch>>>? restart = null,
        TimeSpan? quiet = null)
    {
        var session = launch.Session!;
        var cost = 0m;
        var said = string.Empty;
        var watching = new TeamRunner.Progress(journal, brief.Node, _time) { Steering = session };
        var limit = quiet is { } span ? Quiet(span, brief) : null;
        var restarted = false;
        var resumed = false;
        var briefing = prompt;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var turn = await session.TurnAsync(prompt, watching.SawAsync, ct, limit).ConfigureAwait(false);
            cost += turn.CostUsd;

            // A resume that never got going: the conversation could not be
            // picked up. Started afresh in the same tree, with the brief
            // again, and still the one retry.
            if (resumed && restart is not null && !turn.Silent && !turn.Completed
                && !turn.Events.OfType<HeadlessStarted>().Any())
            {
                resumed = false;

                await journal.WriteAsync("node.retried", brief.Node, new
                {
                    how = "fresh",
                    because = "its conversation could not be picked up",
                    stderr = TeamRunner.Tail(session.StandardError),
                }, ct).ConfigureAwait(false);

                var fresh = await ReplaceAsync(launch, restart, null, ct).ConfigureAwait(false);

                if (fresh.Failed)
                {
                    await StuckAsync(journal, brief, fresh.Error, ct).ConfigureAwait(false);

                    return new NodeTurn(null, cost, false, said) { Stuck = true, Launch = null };
                }

                launch = fresh.Value!;
                session = launch.Session!;
                watching.Steering = session;
                prompt = CutOff + "\n\n" + briefing;
                attempt--;
                continue;
            }

            if (turn.Silent)
            {
                await journal.WriteAsync("node.silent", brief.Node, new
                {
                    limit = TeamDuration.Spell(quiet!.Value),
                    pid = session.ProcessId,
                    exited = session.Exited.IsCompleted,
                    exit = session.Exited.IsCompletedSuccessfully ? session.Exited.Result : (int?)null,
                    session = session.SessionId,
                    lines = session.RecentOutput,
                    stderr = TeamRunner.Tail(session.StandardError),
                }, ct).ConfigureAwait(false);

                // Everything it started goes with it: an agent's tools and
                // servers left behind hold its pipes and its tree, which is
                // the same hang one level down.
                session.Kill();

                if (restarted || restart is null)
                {
                    await StuckAsync(journal, brief, null, ct).ConfigureAwait(false);

                    return new NodeTurn(null, cost, false, said) { Stuck = true, Launch = launch };
                }

                restarted = true;
                var conversation = session.SessionId;
                var how = conversation is { Length: > 0 } ? "resumed" : "fresh";
                var again = await ReplaceAsync(launch, restart, conversation, ct).ConfigureAwait(false);
                string? refused = null;

                if (again.Failed && conversation is { Length: > 0 })
                {
                    refused = again.Error;
                    how = "fresh";
                    again = await restart(null, ct).ConfigureAwait(false);
                }

                if (again.Failed)
                {
                    await StuckAsync(journal, brief, again.Error, ct).ConfigureAwait(false);

                    // The old launch is already ended, and nothing new started.
                    return new NodeTurn(null, cost, false, said) { Stuck = true, Launch = null };
                }

                await journal.WriteAsync("node.retried", brief.Node, new
                {
                    how,
                    session = conversation,
                    because = refused,
                }, ct).ConfigureAwait(false);

                launch = again.Value!;
                session = launch.Session!;
                watching.Steering = session;
                resumed = how == "resumed";
                prompt = resumed ? CutOff : CutOff + "\n\n" + briefing;

                // Not a report attempt: the node has not answered yet.
                attempt--;
                continue;
            }

            resumed = false;

            if (Tidy(turn.Text) is { Length: > 0 } text)
            {
                said = text;
            }

            await journal.WriteAsync("node.turn", brief.Node, new
            {
                attempt,
                completed = turn.Completed,
                turns = turn.Result?.Turns,
                cost = turn.CostUsd,
                subtype = turn.Result?.Subtype,
                denials = turn.Result?.Denials.Count ?? 0,
                unparsed = turn.Unparsed.Count,

                // The agent's own session, which is what picking this node up
                // again after the run has ended needs: without it a lead that
                // stopped on its budget can only be started over.
                session = session.SessionId,
            }, ct).ConfigureAwait(false);

            if (!turn.Completed)
            {
                return new NodeTurn(null, cost, false, said) { Launch = launch };
            }

            var read = ReportReader.Read(turn.StructuredOutputJson);

            if (read.Failed)
            {
                await journal.WriteAsync("report.unreadable", brief.Node, new { error = read.Error, said }, ct).ConfigureAwait(false);

                if (attempt == 2)
                {
                    return new NodeTurn(null, cost, false, said) { Launch = launch };
                }

                prompt = $"Your report could not be read: {read.Error} Reply with one report/1 document and nothing else.";
                continue;
            }

            var report = read.Value!;
            var verdict = ReportCheck.Check(report, brief);

            await journal.WriteAsync("report.checked", brief.Node, new
            {
                status = TeamRunner.Spell(report.Status),
                outcome = verdict.Outcome.ToString().ToLowerInvariant(),
                verdict.Reasons,

                // Written on every check, not only the accepted one. A lead
                // that was sent back is the interesting case: the journal is
                // then the only record of which criterion it could not answer,
                // and without it a returned report reads as "something was
                // wrong" with no way to see what.
                // Every name spelt out, none by anonymous-object shorthand.
                // `new { one.Criterion }` writes "Criterion", the reader asks
                // for "criterion", and it finds nothing - silently, because a
                // missing property is indistinguishable from an absent one.
                //
                // This is the fault RunEvent.Text's own remarks warn about, and
                // it was made here anyway: the first real run with criteria
                // produced a perfectly good coverage block, wrote it to the
                // journal as Criterion/Because, and `team status` showed no
                // criteria at all.
                coverage = report.Coverage is { Count: > 0 }
                    ? report.Coverage.Select(one => new
                    {
                        criterion = one.Criterion,
                        verdict = RunJournal.VerdictWord(one.Verdict),
                        because = one.Because,
                        understood = one.Understood,
                        delivered = one.Delivered is { Count: > 0 } refs ? refs : null,
                    })
                    : null,

                // What the lead took the goal to mean. Only a lead says, and
                // only where it was asked, so null is ordinary.
                goalUnderstood = report.GoalUnderstood,
            }, ct).ConfigureAwait(false);

            switch (verdict.Outcome)
            {
                case ReportOutcome.Halted:
                    return new NodeTurn(report, cost, true, said) { Launch = launch };

                case ReportOutcome.Accepted:
                    return new NodeTurn(report, cost, false, said) { Launch = launch };

                default:
                    if (attempt == 2)
                    {
                        // Returned twice. The node's answer is what it is,
                        // and the record says it was returned.
                        return new NodeTurn(report, cost, false, said) { Launch = launch };
                    }

                    prompt = "Your report was returned. " + string.Join(" ", verdict.Reasons)
                        + " Reply with a corrected report/1 document.";
                    break;
            }
        }

        return new NodeTurn(null, cost, false, said) { Launch = launch };
    }

    /// <summary>
    /// The silence timer for a node's turn, stopped while one of its questions
    /// is waiting on a person.
    /// </summary>
    /// <remarks>
    /// Read from the run's directory, where the question is written by the
    /// process answering the node, so it holds whether or not anybody in this
    /// process is watching for questions.
    /// </remarks>
    private QuietLimit Quiet(TimeSpan span, Brief brief)
    {
        var directory = RunDirectory(brief.Run);

        return new QuietLimit(span, _time)
        {
            Held = () => NodePermissions.Pending(directory)
                .Any(ask => string.Equals(ask.Node, brief.Node, StringComparison.Ordinal)),
            Glance = span < Glance ? span : Glance,
        };
    }

    /// <summary>How often a node's silence timer looks at the clock.</summary>
    private static readonly TimeSpan Glance = TimeSpan.FromSeconds(15);

    /// <summary>Ends a killed node's launch and starts it again.</summary>
    private static async Task<OperationResult<HeadlessLaunch>> ReplaceAsync(
        HeadlessLaunch old,
        Func<string?, CancellationToken, Task<OperationResult<HeadlessLaunch>>> restart,
        string? conversation,
        CancellationToken ct)
    {
        old.Session!.Kill();

        var (exit, _) = await old.Session.EndAsync(TeamRunner.EndGrace, CancellationToken.None).ConfigureAwait(false);
        await old.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);
        await old.DisposeAsync().ConfigureAwait(false);

        return await restart(conversation, ct).ConfigureAwait(false);
    }

    private static Task StuckAsync(JournalWriter journal, Brief brief, string? error, CancellationToken ct) =>
        journal.WriteAsync("node.stuck", brief.Node, new { error }, ct);

    /// <summary>One line of what a node said, short enough to read in a warning.</summary>
    private static string Tidy(string text)
    {
        var one = string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));

        return one.Length > 400 ? one[..400] + "…" : one;
    }

    public async Task<OperationResult<HeadlessLaunch>> StartAsync(
        RunContext? run,
        TeamRunRequest request,
        TeamDefinition team,
        TeamNode node,
        SpecialistDocument role,
        Brief brief,
        bool dryRun,
        CancellationToken ct,
        string? resumeSession = null,
        string? reviewing = null)
    {
        var definition = role.Role ?? new RoleDefinition(null, null, null, [], []);

        // What the brief lets the node take outward, last, so that the agent
        // and the gate both allow what the node was told it may do. Empty
        // outside an autonomous run, where the person is asked instead.
        var (allowed, denied) = TeamRunner.WithOutward(
            TeamRunner.AllowedFor(role.Id, definition),
            (definition.DeniedTools ?? []).Union(TeamRunner.NeverDelegated, StringComparer.Ordinal).ToList(),
            brief.Constraints.OutwardAllowed);

        // Written before the node starts, because whatever answers its
        // permission questions is a separate process with nothing else to
        // answer from: the agent calls the launcher's own server, and the
        // server reads this. A dry run writes none, as it writes nothing.
        var policy = dryRun
            ? null
            : await NodePermissions.WriteAsync(
                RunDirectory(brief.Run),
                new NodePolicy(
                    brief.Run,
                    brief.Node,
                    role.Id,
                    allowed,
                    denied,

                    // Only where somebody is watching. An autonomous run has
                    // nobody, and a question nobody answers is a node sitting
                    // still for five minutes and then being refused anyway -
                    // worse than the refusal it would have had at once.
                    Ask: run?.Asking ?? false,

                    // What this team has registered and what was decided about
                    // each, worked out here so that whatever answers the node's
                    // questions reads no files and holds no opinion.
                    //
                    // The catalogue's tools join them for a remediator only,
                    // the one role that runs scripts, ruled the same way.
                    Remedies:
                    [
                        .. Standing(team, request.Remediation, request.TrustedRemedies),
                        .. Core.Tools.ToolOffer.For(_tools, role.Id, request.Remediation, request.TrustedTools),
                    ]),
                ct).ConfigureAwait(false);

        var options = new HeadlessOptions(
            Permission: Tier(definition.Mode, answered: policy is not null),
            AllowedTools: allowed,
            DeniedTools: denied,

            // Named as the agent addresses a tool on the launcher's own
            // server. Reached only where the agent asks at all, which its
            // posture decides: one that asks nothing never calls it.
            PermissionAnswerer: policy is null ? null : "mcp__loadout__loadout_permission",
            MaxTurns: team.Rules.Budget.TurnsPerNode,
            BudgetUsd: null,
            OutputSchemaJson: ReportSchema.Version1,
            DisableHooks: true,
            IsolateMcpServers: true);

        // Four places can name a model and the nearest wins: the run, then
        // this node, then whatever the project pinned for the role's mode,
        // then the agent's own default. The same order the agent follows.
        var model = request.Model ?? (node.Model is { Length: > 0 } pinned ? pinned : null);

        // A dry run makes nothing, so it describes the launch against the
        // repository rather than a tree that would have to exist first.
        var launch = new LaunchRequest(
            request.ProjectHandle,
            request.AgentName ?? (node.Agent is { Length: > 0 } agent ? agent : null),
            Offline: request.Offline,
            NoSync: request.NoSync,
            Task: brief.Task,
            Specialists: [role.Id],
            Mode: definition.Mode,
            DryRun: dryRun,
            Model: model,

            // The node's own tree, made for it: without one it commits to
            // whatever the repository has checked out, and the reviewer
            // after it reads a change already on that branch. Or the tree of
            // the branch it was asked to read, which already exists and is
            // never made: a reviewer that asked for one would be given a
            // fresh empty branch and review nothing.
            Worktree: brief.Constraints.Worktree ?? reviewing,
            CreateWorktree: brief.Constraints.Worktree is { Length: > 0 },

            // Where the lead said the work builds on something not yet
            // merged. Only a tree being made reads it: an instance sent back
            // to its own tree finds it there, already on its branch.
            WorktreeFrom: brief.Constraints.Worktree is { Length: > 0 } ? brief.Constraints.From : null,
            PermissionPolicyPath: policy,
            ResumeSessionId: resumeSession,

            // The directory the brief has just told this node to keep things
            // in. Without it the agent refuses every write there, which is
            // what it did: the directory existed, the path in the brief was
            // right, the declaration was clear, and nothing could be written.
            ReachableDirectories: Reachable(brief, role.Id, dryRun));

        return await _launcher.StartHeadlessAsync(launch, options, ct).ConfigureAwait(false);
    }

    /// <summary>The roles that write drafts and read the inbox of the tool catalogue.</summary>
    private static readonly HashSet<string> CatalogueWriters =
        new(StringComparer.Ordinal) { "role.tool-creator", "role.tool-refiner" };

    /// <summary>
    /// The directories a node may write outside its tree: the team's own, and
    /// for the catalogue's creator and refiner its drafts and inbox.
    /// </summary>
    /// <remarks>
    /// Those two and never the catalogue's root. The verify records, the audit
    /// log, the heads and the versions sit beside them, and a node that could
    /// write those could forge any of them.
    /// </remarks>
    private List<string>? Reachable(Brief brief, string role, bool dryRun)
    {
        var reach = new List<string>();

        if (brief.TeamDirectory is { Length: > 0 } kept)
        {
            reach.Add(kept);
        }

        if (CatalogueWriters.Contains(role))
        {
            foreach (var one in new[] { "drafts", "inbox" })
            {
                var directory = Path.Combine(_paths.Paths.State, "tools", one);

                if (!dryRun)
                {
                    Directory.CreateDirectory(directory);
                }

                reach.Add(directory);
            }
        }

        return reach.Count > 0 ? reach : null;
    }

    /// <summary>
    /// Every remedy this team has registered, with what this machine has
    /// decided about each.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both keys applied here, once, before anything starts: whether a person
    /// trusted that exact script, and what this machine says about that kind of
    /// task. A node never sees either - it sees the answer.
    /// </para>
    /// <para>
    /// A team with no directory, or one nothing has been registered in, gets an
    /// empty list, which changes nothing about how its calls are answered.
    /// </para>
    /// </remarks>
    private IReadOnlyList<RemedyStanding> Standing(
        TeamDefinition team,
        IReadOnlyDictionary<string, string>? rules,
        IReadOnlyList<Loadout.Models.Configuration.TrustedRemedy>? trusted)
    {
        var book = new RemedyBook(_paths);
        var read = book.All(team.Name);

        if (read.Failed || read.Value is not { Count: > 0 } remedies)
        {
            return [];
        }

        var standing = new List<RemedyStanding>();

        foreach (var remedy in remedies)
        {
            var script = book.ScriptOf(team.Name, remedy);

            var rule = rules is not null
                && rules.TryGetValue(remedy.Kind is { Length: > 0 } kind ? kind.Trim() : "unclassified", out var said)
                    ? said
                    : RemedyRules.Default;

            // Trust comes from this machine's configuration, never from the
            // record: the record is in the team's directory, which its own
            // nodes are told to write in.
            var decided = RemedyCeiling.Decide(
                remedy,
                rule,
                script.Succeeded ? script.Value : null,
                RemedyCeiling.For(trusted, team.Name));

            standing.Add(new RemedyStanding(
                remedy.Name,
                remedy.Script,
                decided.Ruling.ToString().ToLowerInvariant(),
                decided.Because,

                // What a person is actually deciding about. Carried now rather
                // than read when the question is asked, because whatever asks
                // is another process with nothing to read it from.
                remedy.What,
                remedy.Assumes,
                remedy.Proves));
        }

        return standing;
    }

    /// <summary>The permission tier a posture gets: edits accepted only for one that changes the repository.</summary>
    /// <remarks>
    /// <para>
    /// A node that does not change the repository used to be run with nothing
    /// asked at all: what its allow list named ran, and the agent's own
    /// matcher refused everything else. That matcher reads a rule as a prefix
    /// of the whole command, so a reviewer allowed <c>git diff</c> and
    /// <c>dotnet test</c> was refused <c>cd tree &amp;&amp; dotnet test</c>,
    /// <c>dotnet test | tail -5</c> and <c>dotnet --version</c> alike, and the
    /// launcher never saw the question.
    /// </para>
    /// <para>
    /// Where there is a policy to answer from, it asks instead, and the
    /// launcher's own check answers: every part of a command judged on its
    /// own, the deny list read against each part. Nothing is allowed that was
    /// refused before - what matches the allow list still runs without
    /// asking, and what matches nothing is still refused - but a command made
    /// only of allowed parts now runs. A dry run writes no policy, so it keeps
    /// the old tier.
    /// </para>
    /// </remarks>
    private static HeadlessPermission Tier(string? mode, bool answered) =>
        string.Equals(mode, "implement", StringComparison.OrdinalIgnoreCase)
            ? HeadlessPermission.AcceptEdits
            : answered
                ? HeadlessPermission.Ask
                : HeadlessPermission.DenyUnlessAllowed;

    // The same place TeamRunner keeps a run, worked out the same way.
    private string RunDirectory(string runId) =>
        Path.Combine(_paths.Paths.State, "teams", "runs", runId);
}

/// <summary>One exchange with a node, judged.</summary>
/// <param name="Report">What the node reported, or null when it produced nothing readable.</param>
/// <param name="Cost">What the exchange cost, including any correction.</param>
/// <param name="Halted">Whether the node took an outward action its brief did not allow.</param>
/// <param name="Said">
/// What the node wrote as ordinary text when it produced no report. An
/// agent that cannot answer usually says why in prose, and that sentence
/// is the whole explanation: a run whose nodes stopped reported only
/// "ended without a report" while every one of them was saying "You've
/// reached your Fable limit".
/// </param>
internal sealed record NodeTurn(Report? Report, decimal Cost, bool Halted, string Said)
{
    /// <summary>
    /// Whether the node went silent twice and was given up on. Its agent has
    /// been killed.
    /// </summary>
    public bool Stuck { get; init; }

    /// <summary>
    /// The launch the node is on now, which is a new one where it was started
    /// again after going silent, and the one the caller ends. Null only where
    /// a stuck node's launch has already been ended.
    /// </summary>
    public HeadlessLaunch? Launch { get; init; }
}
