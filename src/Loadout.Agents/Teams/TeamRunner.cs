using System.Text;
using System.Text.Json;
using Loadout.Core.Instructions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams;

/// <summary>What a person asked a team to do.</summary>
/// <param name="ProjectHandle">Slug, alias or name of the project the team works on.</param>
/// <param name="Team">The team, already loaded and checked.</param>
/// <param name="Specialists">The library the team's roles come from.</param>
/// <param name="Goal">What the run is for, in the person's words. The lead's whole task.</param>
/// <param name="Autonomy">manual, supervised or autonomous; null for the team's own setting.</param>
/// <param name="DryRun">Prepare the lead's launch and start nothing.</param>
/// <param name="AgentName">An agent for every node, overriding the team and the project.</param>
/// <param name="Model">A model for every node, overriding the team and the project.</param>
/// <param name="MaxRounds">How many times the lead may come back with more requests before the run stops.</param>
/// <param name="Offline">Skip the network for every launch.</param>
/// <param name="NoSync">Skip the workspace sync for every launch.</param>
public sealed record TeamRunRequest(
    string ProjectHandle,
    TeamDefinition Team,
    SpecialistCatalogue Specialists,
    string Goal,
    string? Autonomy = null,
    bool DryRun = false,
    string? AgentName = null,
    int MaxRounds = 5,
    bool Offline = false,
    bool NoSync = false,
    string? Model = null);

/// <summary>How a run ended.</summary>
/// <param name="RunId">The run's identifier, which names its directory under the state root.</param>
/// <param name="Directory">Where the journal, briefs and reports are, or null on a dry run.</param>
/// <param name="Ended">Why the run stopped: the lead's final status, or a run-level reason.</param>
/// <param name="FinalReport">The lead's last report, when there was one.</param>
/// <param name="CostUsd">Everything every node spent, from the agents' own figures.</param>
/// <param name="Rounds">How many times the lead was given the floor.</param>
/// <param name="Warnings">Everything worth telling the person.</param>
/// <param name="Branches">What the run left on branches of its own, node by node.</param>
/// <param name="Merged">Branches taken into the repository's own branch, and how.</param>
/// <param name="LeadPlan">On a dry run, what the lead would have been started with.</param>
public sealed record TeamRunOutcome(
    string RunId,
    string? Directory,
    string Ended,
    Report? FinalReport,
    decimal CostUsd,
    int Rounds,
    IReadOnlyList<string> Warnings,
    IReadOnlyDictionary<string, string>? Branches = null,
    IReadOnlyList<string>? Merged = null,
    LaunchPlan? LeadPlan = null);

/// <summary>
/// The person, as the run sees them: the only party that can hold a gate.
/// </summary>
/// <remarks>
/// Core never asks a question, and neither does the runner. It says what it
/// is about to do and lets whichever front door is open decide, so the
/// terminal, the launcher and the dashboard can each answer in their own
/// way and the run behaves the same behind all three.
/// </remarks>
public interface ITeamConsole
{
    /// <summary>
    /// In manual mode, before every brief goes out and before every report
    /// is acted on. False stops the run.
    /// </summary>
    Task<bool> ConfirmAsync(string what, CancellationToken ct = default);

    /// <summary>A question the lead could not decide. The option chosen, or null to stop the run.</summary>
    Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default);

    /// <summary>Something happened worth a line.</summary>
    void Note(string line);
}

/// <summary>Runs a team against a project.</summary>
public interface ITeamRunner
{
    Task<OperationResult<TeamRunOutcome>> RunAsync(
        TeamRunRequest request,
        ITeamConsole console,
        CancellationToken ct = default);
}

/// <summary>
/// The coordinator: briefs the lead, briefs whoever the lead asks for one
/// at a time, judges every report, and gives the lead the floor again until
/// it finishes or a rule stops the run.
/// </summary>
/// <remarks>
/// <para>
/// Every node is an ordinary launch through <see cref="IAgentLauncher.StartHeadlessAsync"/>,
/// with the role named as an explicit specialist, the role's posture as the
/// mode, and the role's tool policy as the headless options. The lead's
/// session stays open for the whole run so it keeps its own context; a
/// worker's is one brief, one report, and gone.
/// </para>
/// <para>
/// Nothing here consults a model. Every judgement about a report is
/// <see cref="ReportCheck"/>, every decision a person could make is put to
/// the <see cref="ITeamConsole"/>, and every step is a line in the journal
/// before it is acted on.
/// </para>
/// <para>
/// This first runner briefs requests one at a time, so a node that could
/// have run beside another waits for it. Running them together, the merge
/// gate, the answerer for permission prompts and the daemon are later
/// pieces, and each is said in the outcome's warnings rather than implied.
/// </para>
/// </remarks>
public sealed class TeamRunner : ITeamRunner
{
    private static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions JournalLine = new() { WriteIndented = false };

    private readonly IAgentLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly Core.Projects.IProjectService? _projects;
    private readonly Core.Git.IGitManager? _git;
    private readonly IChildLifetime? _lifetime;

    /// <summary>
    /// The project service and the git manager are optional so a caller that
    /// only drives nodes needs neither; without them a run cannot merge and
    /// says so rather than appearing to. The child lifetime is optional for
    /// the same reason, and a run that has one says so when it is the weaker
    /// kind.
    /// </summary>
    public TeamRunner(
        IAgentLauncher launcher,
        IPlatformPaths paths,
        TimeProvider time,
        Core.Projects.IProjectService? projects = null,
        Core.Git.IGitManager? git = null,
        IChildLifetime? lifetime = null)
    {
        _launcher = launcher;
        _paths = paths;
        _time = time;
        _projects = projects;
        _git = git;
        _lifetime = lifetime;
    }

    /// <inheritdoc />
    public async Task<OperationResult<TeamRunOutcome>> RunAsync(
        TeamRunRequest request,
        ITeamConsole console,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(console);

        var team = request.Team;

        if (team.Template)
        {
            return OperationResult<TeamRunOutcome>.Fail(
                $"'{team.Name}' is a template, a shape to copy rather than a team to run. "
                + $"Copy it with: loadout team new <name> --from {team.Name}",
                ExitCode.InvalidArguments);
        }

        var problems = TeamCatalogue.Check(team, request.Specialists)
            .Where(f => f.Severity == RuleFindingSeverity.Error)
            .Select(f => f.Detail)
            .ToList();

        if (problems.Count > 0)
        {
            return OperationResult<TeamRunOutcome>.Fail(
                $"'{team.Name}' cannot run: {string.Join(" ", problems)}",
                ExitCode.ConfigurationInvalid);
        }

        var autonomy = (request.Autonomy ?? team.Rules.Autonomy).Trim().ToLowerInvariant();

        if (autonomy is not ("manual" or "supervised" or "autonomous"))
        {
            return OperationResult<TeamRunOutcome>.Fail(
                $"'{autonomy}' is not an autonomy. It must be manual, supervised or autonomous.",
                ExitCode.InvalidArguments);
        }

        var runId = $"{_time.GetUtcNow():yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4]}";
        var warnings = new List<string>
        {
            "This runner briefs requests one at a time, so a node that could run beside another waits for it.",
        };

        if (_lifetime is { IsEnforced: false })
        {
            // Said rather than implied: an unattended run is exactly where
            // nobody would notice an agent still spending after the thing
            // driving it had gone.
            warnings.Add(
                "If this coordinator is killed rather than stopped, its nodes keep running and keep spending, "
                + $"because {_lifetime.Detail}.");
        }

        var leadNode = team.Nodes[team.Lead];
        var leadRole = request.Specialists.Find(leadNode.Role)!;

        var leadBrief = MakeBrief(
            runId, team.Lead, parent: null, leadNode, leadRole, request.Goal, inputs: [],
            doneWhen: ["the goal is met, with the evidence cited from your nodes' reports"],
            team, autonomy, request.Specialists.Find);

        if (request.DryRun)
        {
            var dry = await StartNodeAsync(request, team, leadNode, leadRole, leadBrief, dryRun: true, ct)
                .ConfigureAwait(false);

            if (dry.Failed)
            {
                return OperationResult<TeamRunOutcome>.Fail(dry.Error!, dry.ExitCode);
            }

            await using var plan = dry.Value!;
            warnings.AddRange(plan.Warnings);

            return OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
                runId, null, "dry run", null, 0m, 0, warnings, LeadPlan: plan.Plan));
        }

        var directory = Path.Combine(_paths.Paths.State, "teams", "runs", runId);
        Directory.CreateDirectory(directory);

        var journal = new Journal(Path.Combine(directory, "journal.jsonl"), runId, _time);

        await journal.WriteAsync("run.started", null, new { team = team.Name, goal = request.Goal, autonomy }, ct)
            .ConfigureAwait(false);

        if (!await GateAsync(autonomy, console, $"Brief the lead ({leadNode.Role}) with the goal", ct).ConfigureAwait(false))
        {
            await journal.WriteAsync("run.finished", null, new { ended = "stopped before the lead was briefed" }, ct).ConfigureAwait(false);

            return OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
                runId, directory, "stopped before the lead was briefed", null, 0m, 0, warnings));
        }

        await WriteDocumentAsync(directory, $"brief-{Safe(team.Lead)}.json", ReportReader.Write(leadBrief), ct).ConfigureAwait(false);

        var started = await StartNodeAsync(request, team, leadNode, leadRole, leadBrief, dryRun: false, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            await journal.WriteAsync("run.finished", null, new { ended = "the lead could not be started", error = started.Error }, ct).ConfigureAwait(false);

            return OperationResult<TeamRunOutcome>.Fail(started.Error!, started.ExitCode);
        }

        var lead = started.Value!;
        warnings.AddRange(lead.Warnings);

        await journal.WriteAsync("node.launched", team.Lead, new { launch = lead.LaunchId, role = leadNode.Role }, ct).ConfigureAwait(false);

        var cost = 0m;
        var rounds = 0;
        var quietRounds = 0;
        string ended;
        Report? final = null;

        // What the run put on branches of its own, and what the team's merge
        // gate nodes decided about it. Both are read at the end, when the
        // question is whether any of it may come back to the repository.
        var branches = new Dictionary<string, string>(StringComparer.Ordinal);
        var decisions = new Dictionary<string, string>(StringComparer.Ordinal);
        var askedAboutTheGate = false;

        try
        {
            var prompt = Render(leadBrief);

            while (true)
            {
                rounds++;

                var turn = await NodeTurnAsync(lead, leadBrief, prompt, journal, ct).ConfigureAwait(false);
                var report = turn.Report;
                cost += turn.Cost;

                if (turn.Halted)
                {
                    ended = "halted: the lead took an outward action its brief did not allow";
                    final = report;
                    break;
                }

                if (report is null)
                {
                    // What it said instead is the explanation, and it is
                    // usually a sentence a person can act on.
                    ended = turn.Said.Length > 0
                        ? $"the lead ended without a report. It said: {turn.Said}"
                        : "the lead ended without a report";

                    break;
                }

                final = report;

                // A lead that says done while the team's gate has not been
                // consulted is told once, because its role says to route
                // every change through those nodes and a model that skipped
                // them will otherwise end the run with the work stranded on
                // a branch nobody looked at. Once: the second answer is the
                // lead's to give.
                if (report.Status == ReportStatus.Done && !askedAboutTheGate
                    && Ungated(team, branches, decisions) is { Count: > 0 } pending)
                {
                    askedAboutTheGate = true;

                    await journal.WriteAsync("gate.reminded", team.Lead, new { pending }, ct).ConfigureAwait(false);

                    console.Note($"The lead reported done without the merge gate: {string.Join("; ", pending)}. Telling it once.");

                    prompt =
                        $"You reported done. This team will not take {string.Join(" or ", branches.Values)} back into "
                        + $"the repository until {string.Join(" and ", team.Rules.Gates.Merge)} have accepted it, and "
                        + $"{string.Join("; ", pending)}. Request them, or report done again and the work stays on its branch.";

                    continue;
                }

                // A lead that has requests is mid-round whatever status it
                // wrote: blocked on its own requests is how a round is asked
                // for. Blocked with nothing requested is blocked.
                var hasRequests = report.Requests is { Count: > 0 };

                if (report.Status is ReportStatus.Done or ReportStatus.Failed
                    || (report.Status == ReportStatus.Blocked && !hasRequests))
                {
                    ended = Spell(report.Status);
                    break;
                }

                if (!await GateAsync(autonomy, console, $"Act on the lead's report (round {rounds}, {Spell(report.Status)})", ct).ConfigureAwait(false))
                {
                    ended = "stopped by the person";
                    break;
                }

                var feedback = new StringBuilder();

                if (report.Status == ReportStatus.NeedsDecision)
                {
                    var decided = await DecideAsync(autonomy, console, report.Questions ?? [], journal, ct).ConfigureAwait(false);

                    if (decided is null)
                    {
                        ended = "stopped at a decision";
                        break;
                    }

                    feedback.AppendLine("## Decisions").AppendLine();

                    foreach (var (question, answer) in decided)
                    {
                        feedback.AppendLine($"- {question}: **{answer}**");
                    }

                    feedback.AppendLine();
                }

                var requests = report.Requests ?? [];
                var reports = new List<Report>();
                var refused = new List<string>();

                foreach (var ask in requests)
                {
                    // "implementer/1" asks for an instance of the node called
                    // "implementer"; the instance name is what the worker is
                    // briefed as and reports as.
                    var baseName = BaseNode(ask.Node);

                    if (!leadNode.Delegates.Contains(baseName, StringComparer.Ordinal) || !team.Nodes.TryGetValue(baseName, out var node))
                    {
                        var reason =
                            $"The lead asked for '{ask.Node}', which it may not request. It may request: "
                            + string.Join(", ", leadNode.Delegates) + ".";
                        warnings.Add(reason);
                        refused.Add(reason);
                        await journal.WriteAsync("request.refused", team.Lead, new { ask.Node, reason }, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (!await GateAsync(autonomy, console, $"Brief {ask.Node} ({node.Role}): {ask.Task}", ct).ConfigureAwait(false))
                    {
                        ended = "stopped by the person";
                        goto finished;
                    }

                    var role = request.Specialists.Find(node.Role)!;
                    var brief = MakeBrief(
                        runId, ask.Node, team.Lead, node, role, ask.Task, ask.Inputs ?? [], doneWhen: [], team, autonomy,
                        request.Specialists.Find);

                    await WriteDocumentAsync(directory, $"brief-{Safe(ask.Node)}-{rounds}.json", ReportReader.Write(brief), ct).ConfigureAwait(false);

                    if (brief.Constraints.Worktree is { Length: > 0 } made)
                    {
                        branches[ask.Node] = made;
                    }

                    var workerReport = await RunWorkerAsync(request, team, node, role, brief, journal, warnings, ct).ConfigureAwait(false);
                    cost += workerReport.Cost;

                    if (workerReport.Report is { } decided)
                    {
                        // A gate node's answer is the decision it hands back,
                        // in the one word its role tells it to use.
                        foreach (var deliverable in decided.Deliverables.Where(d => d.Kind == DeliverableKind.Decision))
                        {
                            decisions[BaseNode(ask.Node)] = deliverable.Ref.Trim();
                        }
                    }

                    if (workerReport.Halted)
                    {
                        ended = $"halted: {ask.Node} took an outward action its brief did not allow";
                        final = report;
                        goto finished;
                    }

                    var handed = workerReport.Report ?? new Report(
                        ask.Node, ReportStatus.Failed, workerReport.Failure ?? "The node ended without a report.", [], [], []);

                    reports.Add(handed);
                    await WriteDocumentAsync(directory, $"report-{Safe(ask.Node)}-{rounds}.json", ReportReader.Write(handed), ct).ConfigureAwait(false);

                    if (!await GateAsync(autonomy, console, $"Hand {ask.Node}'s report ({Spell(handed.Status)}) to the lead", ct).ConfigureAwait(false))
                    {
                        ended = "stopped by the person";
                        goto finished;
                    }
                }

                if (report.OutwardRequested is { Count: > 0 } wanted)
                {
                    warnings.Add($"The lead asked for outward actions it was not allowed: {string.Join("; ", wanted)}.");
                }

                // A round that asked for nothing moved nothing. A question is
                // not progress either: a lead with several to ask asks them in
                // one report, and one that asks round after round is stuck.
                quietRounds = requests.Count == 0 ? quietRounds + 1 : 0;

                if (quietRounds >= 2)
                {
                    ended = "no progress: two rounds without a request or a finish";
                    break;
                }

                if (team.Rules.Budget.Usd is { } budget && cost >= budget)
                {
                    ended = $"budget spent: {cost:0.00} of {budget:0.00} USD";
                    break;
                }

                if (rounds >= request.MaxRounds)
                {
                    ended = $"round limit: {request.MaxRounds} rounds";
                    break;
                }

                if (reports.Count > 0)
                {
                    feedback.AppendLine("## Reports from your requests").AppendLine();

                    foreach (var handed in reports)
                    {
                        feedback.AppendLine("```json").AppendLine(ReportReader.Write(handed)).AppendLine("```").AppendLine();
                    }
                }

                if (refused.Count > 0)
                {
                    // Said to the lead as well as to the person: a lead that
                    // is refused and not told asks again, and did, twice.
                    feedback.AppendLine("## Requests refused").AppendLine();

                    foreach (var reason in refused)
                    {
                        feedback.AppendLine($"- {reason}");
                    }

                    feedback.AppendLine();
                }

                feedback.AppendLine(
                    "Decide what happens next: more requests, or finish. Reply with one report/1 document. "
                    + "Status done needs evidence cited from these reports.");

                prompt = feedback.ToString();
            }

        finished:
            ;
        }
        finally
        {
            var (exit, killed) = await lead.Session!.EndAsync(EndGrace, CancellationToken.None).ConfigureAwait(false);
            var stderr = Tail(lead.Session.StandardError);

            await journal.WriteAsync("node.ended", team.Lead, new { exit, killed, stderr }, CancellationToken.None).ConfigureAwait(false);
            await lead.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);

            // A node that produced no report and said why on its error
            // stream has explained itself, and that explanation must reach
            // the person. The first real run ended "without a report" in
            // under a second, exit code 1, and nothing said what the agent
            // had printed.
            if (final is null && stderr.Length > 0)
            {
                warnings.Add($"The lead wrote to its error stream (exit code {exit}): {stderr}");
            }
        }

        var merged = await MergeAsync(
            request, team, autonomy, ended, branches, decisions, console, journal, warnings, ct)
            .ConfigureAwait(false);

        await journal.WriteAsync("run.finished", null, new { ended, cost, rounds, merged }, ct).ConfigureAwait(false);

        if (final is not null)
        {
            await WriteDocumentAsync(directory, "final-report.json", ReportReader.Write(final), ct).ConfigureAwait(false);
        }

        // Every node's launch contributes the same preflight notices, so a
        // four-node run said each of them four times. The same sentence
        // twice is noise that buries the one that was only said once.
        return OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
            runId, directory, ended, final, cost, rounds,
            warnings.Distinct(StringComparer.Ordinal).ToList(), branches, merged));
    }

    /// <summary>
    /// The one word each reviewing role is told to hand back when it is
    /// satisfied. Anything else, including silence, is not satisfaction.
    /// </summary>
    private static readonly HashSet<string> Accepting = new(StringComparer.OrdinalIgnoreCase)
    {
        "accept", "accepted", "approve", "approved", "verified", "holds", "fit", "reads-true",
    };

    /// <summary>
    /// The gate's nodes that have not accepted, each with what it did
    /// instead, or nothing when there is no gate or nothing to take through
    /// it.
    /// </summary>
    private static IReadOnlyList<string> Ungated(
        TeamDefinition team,
        IReadOnlyDictionary<string, string> branches,
        IReadOnlyDictionary<string, string> decisions)
    {
        if (branches.Count == 0 || team.Rules.Gates.Merge.Count == 0)
        {
            return [];
        }

        return team.Rules.Gates.Merge
            .Where(node => !decisions.TryGetValue(node, out var decision) || !Accepting.Contains(decision))
            .Select(node => decisions.TryGetValue(node, out var decision)
                ? $"{node} said '{decision}'"
                : $"{node} decided nothing")
            .ToList();
    }

    /// <summary>
    /// Takes the run's branches into the repository, where the team's gate
    /// says they may go and a person agrees.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mechanical, and deliberately: no model decides this and none performs
    /// it. The coordinator reads what the gate's nodes decided, asks whoever
    /// is there, and then it is git's business. A conflict is not resolved
    /// here at all.
    /// </para>
    /// <para>
    /// Nothing is merged unless the run finished, the team names a gate, and
    /// every node in that gate handed back an accepting decision. Each of
    /// those is a reason a person would want to hear, so each is said.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<string>> MergeAsync(
        TeamRunRequest request,
        TeamDefinition team,
        string autonomy,
        string ended,
        IReadOnlyDictionary<string, string> branches,
        IReadOnlyDictionary<string, string> decisions,
        ITeamConsole console,
        Journal journal,
        List<string> warnings,
        CancellationToken ct)
    {
        var merged = new List<string>();

        if (branches.Count == 0)
        {
            return merged;
        }

        var left = string.Join(", ", branches.Values);

        if (!string.Equals(ended, "done", StringComparison.Ordinal))
        {
            warnings.Add($"Nothing was merged: the run ended {ended}. What the nodes committed is on {left}.");

            return merged;
        }

        var gate = team.Rules.Gates.Merge;

        if (gate.Count == 0)
        {
            warnings.Add(
                $"Nothing was merged: '{team.Name}' names no merge gate, and a change nobody is required to "
                + $"look at is not one this will land for you. It is on {left}.");

            return merged;
        }

        var missing = Ungated(team, branches, decisions);

        if (missing.Count > 0)
        {
            warnings.Add($"Nothing was merged: the gate needs {string.Join(" and ", gate)}, and {string.Join("; ", missing)}. It is on {left}.");
            await journal.WriteAsync("gate.refused", null, new { gate, decisions }, ct).ConfigureAwait(false);

            return merged;
        }

        if (_projects is null || _git is null)
        {
            warnings.Add($"Nothing was merged: this runner was built without a project service or git. It is on {left}.");

            return merged;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        if (resolution.Failed || resolution.Value?.LocalPath is not { Length: > 0 } repository)
        {
            warnings.Add($"Nothing was merged: the project could not be resolved. It is on {left}.");

            return merged;
        }

        var state = await _git.GetStateAsync(repository, ct).ConfigureAwait(false);

        if (state.Failed)
        {
            warnings.Add($"Nothing was merged: {state.Error} It is on {left}.");

            return merged;
        }

        if (!state.Value!.IsClean)
        {
            // Merging into a tree somebody is working in would mix the run's
            // change with theirs, and untangling that is worse than waiting.
            warnings.Add(
                $"Nothing was merged: {repository} has uncommitted changes, and merging into a tree "
                + $"somebody is working in mixes their work with the run's. It is on {left}.");

            return merged;
        }

        var target = state.Value.Branch ?? "the current branch";

        foreach (var (node, branch) in branches)
        {
            await journal.WriteAsync("gate.opened", node, new { gate = "merge", branch, target }, ct).ConfigureAwait(false);

            // Autonomous has already been given the rules to follow and the
            // gate's nodes have agreed; anything else asks, because this is
            // the moment the run changes the repository somebody works in.
            var allowed = autonomy == "autonomous"
                || await console.ConfirmAsync($"Merge {branch} into {target}", ct).ConfigureAwait(false);

            await journal.WriteAsync("gate.decided", node, new { gate = "merge", branch, allowed }, ct).ConfigureAwait(false);

            if (!allowed)
            {
                warnings.Add($"{branch} was not merged, because you said not to. It is still there.");

                continue;
            }

            var result = await _git.MergeAsync(repository, branch, ct).ConfigureAwait(false);

            if (result.Failed)
            {
                warnings.Add($"{branch} could not be merged: {result.Error}");
                await journal.WriteAsync("merge.failed", node, new { branch, error = result.Error }, ct).ConfigureAwait(false);

                continue;
            }

            if (!result.Value!.Merged)
            {
                warnings.Add(
                    $"{branch} conflicts with {target} in {string.Join(", ", result.Value.Conflicts)}, so nothing was "
                    + "merged and the repository is as it was. Resolving it is not something this does for you.");

                await journal.WriteAsync("merge.conflicted", node, new { branch, result.Value.Conflicts }, ct).ConfigureAwait(false);

                continue;
            }

            merged.Add(branch);

            await journal.WriteAsync("merge.done", node, new { branch, target, result.Value.FastForward }, ct)
                .ConfigureAwait(false);

            console.Note(
                result.Value.FastForward
                    ? $"Merged {branch} into {target}, fast-forward."
                    : $"Merged {branch} into {target} with a merge commit.");

            await TidyAsync(repository, branch, journal, node, warnings, ct).ConfigureAwait(false);
        }

        return merged;
    }

    /// <summary>
    /// Clears away a branch that has arrived: its worktree, then the branch
    /// itself.
    /// </summary>
    /// <remarks>
    /// Only ever after a merge, so nothing is thrown away: the commits are
    /// in the repository and git refuses both steps if that turns out not to
    /// be true. A tree left for every implementer of every run is litter
    /// somebody has to clear by hand, and three of them had already
    /// accumulated before this existed.
    /// </remarks>
    private async Task TidyAsync(
        string repository,
        string branch,
        Journal journal,
        string node,
        List<string> warnings,
        CancellationToken ct)
    {
        var worktrees = await _git!.ListWorktreesAsync(repository, ct).ConfigureAwait(false);

        var tree = worktrees.Value?.FirstOrDefault(
            w => !w.IsPrimary && string.Equals(w.Branch, branch, StringComparison.Ordinal));

        if (tree is not null)
        {
            var removed = await _git.RemoveWorktreeAsync(repository, tree.Path, ct).ConfigureAwait(false);

            if (removed.Failed)
            {
                // Almost always because the node left something uncommitted
                // in there, which is worth keeping and worth saying.
                warnings.Add($"The worktree at {tree.Path} was kept: {removed.Error}");

                return;
            }
        }

        var deleted = await _git.DeleteMergedBranchAsync(repository, branch, ct).ConfigureAwait(false);

        await journal.WriteAsync(
            "worktree.tidied", node, new { branch, path = tree?.Path, branchDeleted = deleted.Succeeded }, ct)
            .ConfigureAwait(false);

        if (deleted.Failed)
        {
            warnings.Add($"The branch {branch} was kept: {deleted.Error}");
        }
    }

    private sealed record WorkerOutcome(Report? Report, decimal Cost, bool Halted, string? Failure);

    /// <summary>One brief, one report, and the session is gone.</summary>
    private async Task<WorkerOutcome> RunWorkerAsync(
        TeamRunRequest request,
        TeamDefinition team,
        TeamNode node,
        SpecialistDocument role,
        Brief brief,
        Journal journal,
        List<string> warnings,
        CancellationToken ct)
    {
        var started = await StartNodeAsync(request, team, node, role, brief, dryRun: false, ct).ConfigureAwait(false);

        if (started.Failed)
        {
            await journal.WriteAsync("node.failed", brief.Node, new { error = started.Error }, ct).ConfigureAwait(false);

            return new WorkerOutcome(null, 0m, false, started.Error);
        }

        await using var launch = started.Value!;
        warnings.AddRange(launch.Warnings);

        await journal.WriteAsync(
            "node.launched",
            brief.Node,
            new { launch = launch.LaunchId, role = node.Role, directory = launch.Plan.WorkingDirectory, worktree = brief.Constraints.Worktree },
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

        var turn = await NodeTurnAsync(launch, brief, Render(brief), journal, ct).ConfigureAwait(false);

        var (exit, killed) = await launch.Session!.EndAsync(EndGrace, CancellationToken.None).ConfigureAwait(false);
        var stderr = Tail(launch.Session.StandardError);

        await journal.WriteAsync("node.ended", brief.Node, new { exit, killed, stderr }, CancellationToken.None).ConfigureAwait(false);
        await launch.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);

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
                : null);
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
    private sealed record NodeTurn(Report? Report, decimal Cost, bool Halted, string Said);

    /// <summary>
    /// One exchange with a node, judged. A report that fails the check goes
    /// back once with the reasons; a second failure is the node's answer.
    /// </summary>
    private static async Task<NodeTurn> NodeTurnAsync(
        HeadlessLaunch launch,
        Brief brief,
        string prompt,
        Journal journal,
        CancellationToken ct)
    {
        var session = launch.Session!;
        var cost = 0m;
        var said = string.Empty;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var turn = await session.TurnAsync(prompt, ct).ConfigureAwait(false);
            cost += turn.CostUsd;

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
            }, ct).ConfigureAwait(false);

            if (!turn.Completed)
            {
                return new NodeTurn(null, cost, false, said);
            }

            var read = ReportReader.Read(turn.StructuredOutputJson);

            if (read.Failed)
            {
                await journal.WriteAsync("report.unreadable", brief.Node, new { read.Error, said }, ct).ConfigureAwait(false);

                if (attempt == 2)
                {
                    return new NodeTurn(null, cost, false, said);
                }

                prompt = $"Your report could not be read: {read.Error} Reply with one report/1 document and nothing else.";
                continue;
            }

            var report = read.Value!;
            var verdict = ReportCheck.Check(report, brief);

            await journal.WriteAsync("report.checked", brief.Node, new
            {
                status = Spell(report.Status),
                outcome = verdict.Outcome.ToString().ToLowerInvariant(),
                verdict.Reasons,
            }, ct).ConfigureAwait(false);

            switch (verdict.Outcome)
            {
                case ReportOutcome.Halted:
                    return new NodeTurn(report, cost, true, said);

                case ReportOutcome.Accepted:
                    return new NodeTurn(report, cost, false, said);

                default:
                    if (attempt == 2)
                    {
                        // Returned twice. The node's answer is what it is,
                        // and the record says it was returned.
                        return new NodeTurn(report, cost, false, said);
                    }

                    prompt = "Your report was returned. " + string.Join(" ", verdict.Reasons)
                        + " Reply with a corrected report/1 document.";
                    break;
            }
        }

        return new NodeTurn(null, cost, false, said);
    }

    /// <summary>One line of what a node said, short enough to read in a warning.</summary>
    private static string Tidy(string text)
    {
        var one = string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()));

        return one.Length > 400 ? one[..400] + "…" : one;
    }

    private async Task<OperationResult<HeadlessLaunch>> StartNodeAsync(
        TeamRunRequest request,
        TeamDefinition team,
        TeamNode node,
        SpecialistDocument role,
        Brief brief,
        bool dryRun,
        CancellationToken ct)
    {
        var definition = role.Role ?? new RoleDefinition(null, null, null, [], []);

        var options = new HeadlessOptions(
            Permission: Tier(definition.Mode),
            AllowedTools: definition.AllowedTools,
            DeniedTools: definition.DeniedTools,
            PermissionAnswerer: null,
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
            // after it reads a change already on that branch.
            Worktree: brief.Constraints.Worktree,
            CreateWorktree: brief.Constraints.Worktree is { Length: > 0 });

        return await _launcher.StartHeadlessAsync(launch, options, ct).ConfigureAwait(false);
    }

    /// <summary>The permission tier a posture gets: edits accepted only for one that changes the repository.</summary>
    private static HeadlessPermission Tier(string? mode) =>
        string.Equals(mode, "implement", StringComparison.OrdinalIgnoreCase)
            ? HeadlessPermission.AcceptEdits
            : HeadlessPermission.DenyUnlessAllowed;

    private static Brief MakeBrief(
        string runId,
        string nodeName,
        string? parent,
        TeamNode node,
        SpecialistDocument role,
        string task,
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> doneWhen,
        TeamDefinition team,
        string autonomy,
        Func<string, SpecialistDocument?> specialistsOf)
    {
        var definition = role.Role;

        var outwardAllowed = autonomy == "autonomous"
            ? team.Rules.Gates.OutwardAllowedWhenAutonomous
            : [];

        var delegates = node.Delegates.Count == 0
            ? null
            : node.Delegates
                .Where(team.Nodes.ContainsKey)
                .Select(name => new BriefDelegate(
                    name,
                    team.Nodes[name].Role,
                    specialistsOf(team.Nodes[name].Role)?.Role?.Deliverable,
                    team.Nodes[name].Parallel))
                .ToList();

        return new Brief(
            runId,
            nodeName,
            parent,
            role.Id,
            task,
            Enum.TryParse<DeliverableKind>(definition?.Deliverable, ignoreCase: true, out var kind) ? kind : DeliverableKind.Answer,
            inputs,
            new BriefConstraints(
                definition?.Mode ?? "implement",
                BudgetUsd: null,
                MaxTurns: team.Rules.Budget.TurnsPerNode,

                // A branch of its own, named after the run and the node, so
                // two runs of the same team never meet and a person reading
                // the branch list can tell which run made what.
                Worktree: node.Worktree ? $"teams/{runId}/{Safe(nodeName)}" : null,
                OutwardAllowed: outwardAllowed),
            doneWhen,
            node.Parameters.Count > 0 ? node.Parameters : null,
            delegates);
    }

    /// <summary>"implementer/2" is an instance of "implementer"; anything else is itself.</summary>
    private static string BaseNode(string name)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 ? name[..slash] : name;
    }

    /// <summary>The brief as the node reads it: the prose first, the JSON beside it, the contract last.</summary>
    internal static string Render(Brief brief)
    {
        var text = new StringBuilder();

        text.AppendLine($"# Brief for node {brief.Node} ({brief.Role})").AppendLine();
        text.AppendLine($"Run {brief.Run}. " + (brief.Parent is null ? "You report to the person." : $"You report to {brief.Parent}.")).AppendLine();
        text.AppendLine("## Task").AppendLine().AppendLine(brief.Task).AppendLine();
        text.AppendLine("## Deliverable").AppendLine().AppendLine(brief.Deliverable.ToString().ToLowerInvariant()).AppendLine();

        if (brief.Inputs.Count > 0)
        {
            text.AppendLine("## Inputs").AppendLine();

            foreach (var input in brief.Inputs)
            {
                text.AppendLine($"- {input}");
            }

            text.AppendLine();
        }

        if (brief.DoneWhen.Count > 0)
        {
            text.AppendLine("## Done when").AppendLine();

            for (var i = 0; i < brief.DoneWhen.Count; i++)
            {
                text.AppendLine($"{i + 1}. {brief.DoneWhen[i]}");
            }

            text.AppendLine();
        }

        var c = brief.Constraints;

        text.AppendLine("## Constraints").AppendLine();
        text.AppendLine($"- mode: {c.Mode}");

        if (c.Worktree is { Length: > 0 } worktree)
        {
            text.AppendLine(
                $"- worktree: you are in a git worktree of your own, on branch `{worktree}`. Commit here. "
                + "Do not switch branches, do not touch the repository's other trees, and do not merge.");
        }

        text.AppendLine($"- turns: {(c.MaxTurns is { } t ? t.ToString(System.Globalization.CultureInfo.InvariantCulture) : "the agent's default")}");
        text.AppendLine($"- outward actions allowed: {(c.OutwardAllowed is { Count: > 0 } o ? string.Join(", ", o) : "none")}");
        text.AppendLine();

        if (brief.Delegates is { Count: > 0 } delegates)
        {
            text.AppendLine("## You may request").AppendLine();
            text.AppendLine("Use these names exactly in `requests`. A node that may run in parallel is asked for by instance: `name/1`, `name/2`.").AppendLine();

            foreach (var delegate_ in delegates)
            {
                text.AppendLine(
                    $"- `{delegate_.Node}` ({delegate_.Role}, hands back {delegate_.Deliverable ?? "an answer"})"
                    + (delegate_.Parallel > 1 ? $", up to {delegate_.Parallel} at once" : string.Empty));
            }

            text.AppendLine();
        }

        if (brief.Parameters is { Count: > 0 })
        {
            text.AppendLine("## Parameters").AppendLine();

            foreach (var (key, value) in brief.Parameters)
            {
                text.AppendLine($"- {key}: {value}");
            }

            text.AppendLine();
        }

        text.AppendLine("## The brief as JSON").AppendLine().AppendLine("```json").AppendLine(ReportReader.Write(brief)).AppendLine("```").AppendLine();
        text.AppendLine("Reply with one report/1 document as your structured output. Status done needs evidence.");

        return text.ToString();
    }

    private static async Task<bool> GateAsync(string autonomy, ITeamConsole console, string what, CancellationToken ct)
    {
        if (autonomy != "manual")
        {
            console.Note(what);

            return true;
        }

        return await console.ConfirmAsync(what, ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<(string Question, string Answer)>?> DecideAsync(
        string autonomy,
        ITeamConsole console,
        IReadOnlyList<ReportQuestion> questions,
        Journal journal,
        CancellationToken ct)
    {
        var decided = new List<(string, string)>();

        foreach (var question in questions)
        {
            string? answer;

            if (autonomy == "autonomous")
            {
                // Nobody is watching. The lead's own recommendation is the
                // only answer available, and the journal says it was taken
                // that way.
                answer = question.Recommendation;
                console.Note($"Decided on the lead's recommendation: {question.Question} → {answer}");
            }
            else
            {
                answer = await console.DecideAsync(question, ct).ConfigureAwait(false);
            }

            await journal.WriteAsync("decision", null, new { question.Question, answer, by = autonomy == "autonomous" ? "recommendation" : "person" }, ct)
                .ConfigureAwait(false);

            if (answer is null)
            {
                return null;
            }

            decided.Add((question.Question, answer));
        }

        return decided;
    }

    private static string Spell(ReportStatus status) => status switch
    {
        ReportStatus.NeedsDecision => "needs-decision",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>The last few lines an agent wrote to its error stream, as one line, or empty.</summary>
    private static string Tail(IReadOnlyList<string> lines)
    {
        var kept = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(5)
            .Select(line => line.Trim());

        return string.Join(" | ", kept);
    }

    private static string Safe(string node) =>
        string.Concat(node.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));

    private static async Task WriteDocumentAsync(string directory, string name, string json, CancellationToken ct)
    {
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, name), json, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The journal is the record; a document beside it is a convenience.
        }
    }

    /// <summary>The run's record: one JSON line per thing that happened, appended as it happens.</summary>
    private sealed class Journal
    {
        private readonly string _path;
        private readonly string _run;
        private readonly TimeProvider _time;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public Journal(string path, string run, TimeProvider time)
        {
            _path = path;
            _run = run;
            _time = time;
        }

        public async Task WriteAsync(string kind, string? node, object? data, CancellationToken ct)
        {
            var line = JsonSerializer.Serialize(new { at = _time.GetUtcNow(), run = _run, node, kind, data }, JournalLine);

            await _lock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                await File.AppendAllTextAsync(_path, line + "\n", ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A journal that cannot be written must not stop the run; the
                // outcome still carries the result.
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
