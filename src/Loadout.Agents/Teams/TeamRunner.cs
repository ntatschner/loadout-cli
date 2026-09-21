using System.Text;
using System.Text.Json;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Security;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
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
/// <param name="OutwardAllowed">
/// Outward actions this machine has agreed a team may allow its nodes,
/// already decided against the team's request. Null or empty means none, which
/// is what a caller that has not decided gets: a team file may only ask, and a
/// run that granted what it was asked for would be no boundary at all.
/// </param>
/// <param name="TrustedRemedies">
/// The remedies this machine has agreed may run, from its own configuration.
/// Never read from a team's directory, which its own nodes write in.
/// </param>
/// <param name="Criteria">
/// What this run is judged on, each one checkable, or null for a run with
/// nothing but its goal.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Goal"/> is the directive and <paramref name="Criteria"/> is
/// how anybody tells whether it was met. Without them a run ends when the lead
/// says done and nothing argues, which is fine while somebody is watching and
/// is the whole of the check on an autonomous run - the case where nobody is.
/// </para>
/// <para>
/// With them, the lead's brief says it owes a verdict on each, every node is
/// told what the run is being judged on, and a done that leaves one unmet or
/// unanswered is sent back. None of that is new machinery: it is the rule that
/// already governs a worker's report, applied at the level of the goal.
/// </para>
/// </remarks>
/// <param name="Remediation">
/// What this machine says a remediator may do with each kind of task, by kind.
/// Passed in rather than read here, for the same reason the outward list is:
/// what a machine allows is decided before a run starts and arrives already
/// decided, so forgetting to decide it grants nothing rather than everything.
/// </param>
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
    string? Model = null,
    IReadOnlyList<string>? OutwardAllowed = null,
    IReadOnlyDictionary<string, string>? Remediation = null,
    IReadOnlyList<Loadout.Models.Configuration.TrustedRemedy>? TrustedRemedies = null,
    IReadOnlyList<string>? Criteria = null);

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

    /// <summary>
    /// In manual mode, before a worker's brief goes out: the task it would be
    /// given, for changing before it does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lead wrote that task, and the lead can be wrong about it in a way
    /// that is obvious to whoever is watching and expensive to find out any
    /// other way - the worker goes off and does the wrong thing, competently,
    /// for ten minutes. A checkpoint that only says yes or no makes somebody
    /// choose between the wrong brief and no brief.
    /// </para>
    /// <para>
    /// Null stops the run, like refusing. Anything else is the task the worker
    /// is given, whether or not it is the one that came in.
    /// </para>
    /// </remarks>
    async Task<string?> ReviseAsync(string what, string task, CancellationToken ct = default) =>
        await ConfirmAsync($"{what}: {task}", ct).ConfigureAwait(false) ? task : null;

    /// <summary>A question the lead could not decide. The option chosen, or null to stop the run.</summary>
    Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default);

    /// <summary>Something happened worth a line.</summary>
    void Note(string line);

    /// <summary>
    /// Told where the run is writing, before anything is asked.
    /// </summary>
    /// <remarks>
    /// A console that answers from a terminal ignores this. One that answers
    /// from somewhere else - a browser, through the daemon - needs to know
    /// where to leave the question, and the run directory is the only channel
    /// between a run and anything outside the process driving it.
    /// </remarks>
    void Starting(string runDirectory)
    {
    }

    /// <summary>
    /// Whether there is somebody who could answer a question right now.
    /// </summary>
    /// <remarks>
    /// Asked before a run tells its nodes they may stop and ask. Down a pipe,
    /// in CI or from a schedule there is nobody, and a node that stopped there
    /// would wait out the whole patience and be refused anyway — later, and
    /// having done nothing in between.
    /// </remarks>
    bool CanAsk { get; }

    /// <summary>
    /// Whether this console answers by writing into the run's own directory.
    /// </summary>
    /// <remarks>
    /// A terminal is told a question and says yes or no. The dashboard's
    /// console answers by leaving an ask in the run directory for a browser to
    /// pick up - so when the watcher carried a node's question there, the
    /// question it was carrying and the question it asked were two entries in
    /// one queue, under two ids. Answering either let the node go; the other
    /// stayed on the list, and the watcher sat on it for the full five minutes
    /// without looking at any other node.
    /// </remarks>
    bool AnswersInPlace => false;
}

/// <summary>
/// One console, with only one question on it at a time.
/// </summary>
/// <remarks>
/// A run asks the person from two places now: the coordinator at its gates,
/// and the watcher that carries a node's permission question up from the
/// answerer's file. Those happen on different threads and could otherwise land
/// two prompts on one terminal, which reads as a single garbled question and
/// takes one answer for both.
/// </remarks>
internal sealed class OneAtATime(ITeamConsole inner) : ITeamConsole, IDisposable
{
    private readonly SemaphoreSlim _turn = new(1, 1);

    public bool CanAsk => inner.CanAsk;

    public bool AnswersInPlace => inner.AnswersInPlace;

    public void Starting(string runDirectory) => inner.Starting(runDirectory);

    public async Task<bool> ConfirmAsync(string what, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.ConfirmAsync(what, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <remarks>
    /// Forwarded rather than left to the interface's own default, which is not
    /// an optimisation: the default asks this wrapper's ConfirmAsync, so a
    /// console that offers a brief for changing would never be asked and
    /// everything would carry on working, quietly, with the lead's own words.
    /// That is what happened.
    /// </remarks>
    public async Task<string?> ReviseAsync(string what, string task, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.ReviseAsync(what, task, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.DecideAsync(question, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    // Not held. A line is not a question, and making notes wait behind one
    // would stop a run saying what it was doing while somebody thought.
    public void Note(string line) => inner.Note(line);

    public void Dispose() => _turn.Dispose();
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
/// The coordinator: briefs the lead, briefs whoever the lead asks for,
/// judges every report, and gives the lead the floor again until it
/// finishes or a rule stops the run.
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
/// Nodes the lead asks for in one report are briefed one at a time, run
/// together within what each node allows, and read back in the order they
/// were asked for. Each node's permission questions are answered from its
/// role, by the launcher's own server, and every one is recorded.
/// </para>
/// <para>
/// The daemon is a later piece, and what is not built is said in the
/// outcome's warnings rather than implied.
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
    private readonly Core.Tasks.ITaskService? _tasks;

    /// <summary>
    /// The project service and the git manager are optional so a caller that
    /// only drives nodes needs neither; without them a run cannot merge and
    /// says so rather than appearing to. The child lifetime is optional for
    /// the same reason, and a run that has one says so when it is the weaker
    /// kind. Without the task service a run is still recorded in its own
    /// journal, and only the project's task list goes without it.
    /// </summary>
    /// <summary>
    /// Whether this run may put a node's unmatched call to the person.
    /// </summary>
    /// <remarks>
    /// Set once the posture is known and read when each node's policy is
    /// written. A field rather than a parameter threaded through six methods,
    /// because it is a fact about the run rather than about any one node.
    /// </remarks>
    private bool _asking;

    public TeamRunner(
        IAgentLauncher launcher,
        IPlatformPaths paths,
        TimeProvider time,
        Core.Projects.IProjectService? projects = null,
        Core.Git.IGitManager? git = null,
        IChildLifetime? lifetime = null,
        Core.Tasks.ITaskService? tasks = null)
    {
        _launcher = launcher;
        _paths = paths;
        _time = time;
        _projects = projects;
        _git = git;
        _lifetime = lifetime;
        _tasks = tasks;
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

        // From here on every question goes through one voice, because the
        // watcher below asks from another thread.
        using var one = new OneAtATime(console);
        console = one;

        var runId = $"{_time.GetUtcNow():yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4]}";
        var warnings = new List<string>();

        if (_lifetime is { IsEnforced: false })
        {
            // Said rather than implied: an unattended run is exactly where
            // nobody would notice an agent still spending after the thing
            // driving it had gone.
            warnings.Add(
                "If this coordinator is killed rather than stopped, its nodes keep running and keep spending, "
                + $"because {_lifetime.Detail}.");
        }

        // Before anything is briefed, because a node told by a declaration to
        // look on the shelf before working something out will not find what is
        // sitting there unreadable - and used to be told nothing about it.
        if (new RemedyBook(_paths).Unreadable(team.Name) is { Count: > 0 } broken)
        {
            warnings.Add(
                $"{broken.Count} file(s) in this team's directory are not readable as a remedy "
                + $"({string.Join(", ", broken)}). Nothing will offer them and nothing can run "
                + "them, and something wrote them meaning to register a fix.");
        }

        var leadNode = team.Nodes[team.Lead];
        var leadRole = request.Specialists.Find(leadNode.Role)!;

        // Worked out here and made further down, which is the order --dry-run
        // needs: a dry run says where the team's directory would be and
        // creates nothing, because that is what --dry-run means.
        var teamDirectory = TeamDirectory(team.Name);

        // What the lead is held to. With criteria it is the criteria, said
        // plainly enough that a lead reading its own brief knows the report it
        // owes - and ReportCheck then refuses a done that does not give it,
        // which is what makes this more than a sentence.
        var criteria = request.Criteria is { Count: > 0 } asked ? asked : null;

        var leadBrief = MakeBrief(
            runId, team.Lead, parent: null, leadNode, leadRole, request.Goal, inputs: [],
            doneWhen: criteria is null
                ? ["the goal is met, with the evidence cited from your nodes' reports"]
                :
                [
                    "every criterion below is met, with the evidence cited from your nodes' reports",
                    "your final report carries one coverage entry per criterion, each with a verdict "
                        + "of met, unmet or not-attempted, and every met saying in 'because' which "
                        + "node, report and evidence shows it",
                ],
            team, autonomy, request.OutwardAllowed ?? [], request.Specialists.Find, teamDirectory,
            criteria);

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

        var directory = RunDirectory(runId);
        Directory.CreateDirectory(directory);

        // Made before the first node starts, so a declaration that tells one
        // to write there is not telling it to make a directory first.
        try
        {
            Directory.CreateDirectory(teamDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A run whose team directory cannot be made still runs. The brief
            // says where it would have been, and a node that cannot write
            // there reports that it could not, which is the ordinary way
            // anything else here fails.
        }

        // Before anything can be asked, so a console that answers from
        // elsewhere knows where to leave the question.
        console.Starting(directory);

        // And decided after that, not before it, which is the whole of a bug
        // that made the daemon's half of this dead.
        //
        // It is a fact about the run - an autonomous run has nobody by
        // definition, and a run down a pipe has nobody whatever its posture
        // says - so it was settled early, at the top of the method. But a
        // console that answers from elsewhere can only ask once it knows where
        // to leave the question, and that is what Starting tells it. Asked
        // before, it truthfully said no. Every run watched by the daemon
        // therefore decided that nobody was watching, and a held remedy was
        // refused instead of put to the person sitting in front of the
        // dashboard.
        _asking = autonomy != "autonomous" && console.CanAsk;

        var journal = new Journal(Path.Combine(directory, "journal.jsonl"), runId, _time);

        // Runs for the whole run rather than around each turn. A question only
        // appears while a node is mid-turn, so a watcher that covered anything
        // narrower would be four watchers with four chances to miss one. It
        // stops however the run leaves, including the six places that return
        // early.
        await using var watching = Watching.Start(
            _asking, token => WatchAsksAsync(directory, console, journal, token), ct);

        // Resolved before the run is written down rather than after, because
        // which project a run worked on is the first thing somebody reading it
        // back needs and the journal had no way to say it. A machine with
        // several projects showed a list of runs nothing could attribute.
        var slug = await SlugAsync(request, ct).ConfigureAwait(false);
        var where = await PathAsync(request, ct).ConfigureAwait(false);

        await journal.WriteAsync(
            "run.started",
            null,
            new
            {
                team = team.Name,
                goal = request.Goal,
                autonomy,
                rounds = request.MaxRounds,
                project = slug,
                path = where,

                // What it may spend, so a page watching it can say where the
                // spend stands rather than only what it has cost.
                budget = team.Rules.Budget.Usd,
            },
            ct).ConfigureAwait(false);

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

        await journal.WriteAsync(
            "node.launched",
            team.Lead,
            new
            {
                launch = lead.LaunchId,
                role = leadNode.Role,
                model = request.Model ?? (leadNode.Model is { Length: > 0 } pinned ? pinned : null),
            },
            ct).ConfigureAwait(false);

        // Once the lead is actually going, and not before: a run somebody
        // stopped at the first gate did nothing, and a task list that
        // recorded it would be recording an intention.
        await DeclareAsync(slug, runId, team, TaskState.Doing, request.Goal, $"{autonomy}, running", ct)
            .ConfigureAwait(false);

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
                // Checked here rather than at the end of the loop, because the
                // end is not the only way back to the top. The merge-gate
                // reminder continues past it, which bought a fourth round on a
                // run that asked for three - a real lead turn, real money, and
                // a summary that said "round limit: 3 rounds ... 4 rounds" in
                // one breath. Found by the first run that reached the reminder.
                if (rounds >= request.MaxRounds)
                {
                    ended = $"round limit: {request.MaxRounds} round{(request.MaxRounds == 1 ? string.Empty : "s")}";
                    break;
                }

                // And the money, here for exactly the reason above. It was at
                // the end of the loop, where the reminder's way back to the
                // top does not pass, so a lead that reported done over budget
                // was told about the gate and given another turn to answer -
                // spending past a ceiling the run had already reached. One
                // turn, once, which is small; it is also the only check
                // standing between an unattended run and its budget, and a
                // ceiling with a way round it is not one.
                if (team.Rules.Budget.Usd is { } ceiling && cost >= ceiling)
                {
                    ended = $"budget spent: {cost:0.00} of {ceiling:0.00} USD";
                    break;
                }

                // Asked to stop. Between rounds rather than mid-turn, because
                // a node is a headless agent in the middle of one and the only
                // ways to end that sooner are to kill it - losing the turn and
                // what was paid for it - or to ask it, which it cannot hear.
                if (RunControl.Stopped(directory))
                {
                    ended = "stopped by request";
                    break;
                }

                // Or asked to hold. Nothing is spent while it waits, and it
                // carries on the moment the hold is lifted.
                if (RunControl.Paused(directory))
                {
                    await journal.WriteAsync("run.paused", null, new { round = rounds + 1 }, ct)
                        .ConfigureAwait(false);

                    console.Note("Held. It carries on when you let it.");

                    while (RunControl.Paused(directory) && !ct.IsCancellationRequested)
                    {
                        await Task.Delay(NodePermissions.Glance, _time, ct).ConfigureAwait(false);
                    }

                    if (RunControl.Stopped(directory))
                    {
                        ended = "stopped by request";
                        break;
                    }

                    await journal.WriteAsync("run.resumed", null, new { round = rounds + 1 }, ct)
                        .ConfigureAwait(false);
                }

                // Anything said to the lead while it was working, handed over
                // before it takes its next turn, in the order it was said.
                if (RunControl.TakeMessages(directory) is { Count: > 0 } messages)
                {
                    foreach (var message in messages)
                    {
                        await journal.WriteAsync("run.told", team.Lead, new { message }, ct)
                            .ConfigureAwait(false);
                    }

                    prompt = "The person running this team says:\n\n"
                        + string.Join("\n\n", messages)
                        + "\n\n"
                        + prompt;
                }

                rounds++;

                // Written as it starts rather than counted at the end, so a
                // run still going can be told how far through it is.
                await journal.WriteAsync("round.started", null, new { round = rounds, of = request.MaxRounds }, ct)
                    .ConfigureAwait(false);

                var turn = await NodeTurnAsync(lead, leadBrief, prompt, journal, _time, ct).ConfigureAwait(false);
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
                    /*
                        A done the lead could not account for is not a done.

                        ReportCheck already refused this twice - a returned
                        report goes back once and the second answer is the
                        node's, whatever it says. That contract is right for a
                        worker and leaves a hole at the top of a run: a lead
                        that never fills in coverage is asked twice, and the
                        run then ends recording "done" while the journal it
                        wrote says two of three criteria were met.

                        So the run does not argue further and does not spend
                        another round. It records what happened. An autonomous
                        run nobody watched must not be readable afterwards as
                        having met a goal it did not, and "ended: done" beside
                        "2 of 3 met" is exactly that.
                    */
                    if (report.Status == ReportStatus.Done
                        && Outstanding(criteria, report) is { Count: > 0 } missed)
                    {
                        await journal.WriteAsync(
                            "goal.unmet",
                            team.Lead,
                            new { criteria = missed },
                            ct).ConfigureAwait(false);

                        console.Note(
                            $"The lead reported done without accounting for {missed.Count} of "
                            + $"{criteria!.Count} criteria: {string.Join("; ", missed)}");

                        ended = missed.Count == criteria.Count
                            ? "the lead reported done without accounting for the goal"
                            : $"the lead reported done with {missed.Count} of {criteria.Count} "
                              + "criteria unmet";

                        break;
                    }

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

                // Briefed one at a time, run together, read back in the order
                // the lead asked. Only the middle of those three is worth
                // overlapping: a person cannot answer two gates at once, and
                // a record that arrived in whatever order the nodes finished
                // would read differently every time the same run happened.
                var accepted = new List<Briefed>();

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
                        await journal.WriteAsync("request.refused", team.Lead, new { node = ask.Node, reason }, ct).ConfigureAwait(false);
                        continue;
                    }

                    // The one checkpoint that offers a third answer. Yes and no
                    // make somebody choose between the wrong brief and no brief,
                    // and the lead being wrong about a task is both ordinary and
                    // expensive: the worker goes off and does the wrong thing,
                    // competently, for ten minutes.
                    var task = ask.Task;

                    if (autonomy == "manual")
                    {
                        var revised = await console
                            .ReviseAsync($"Brief {ask.Node} ({node.Role})", task, ct)
                            .ConfigureAwait(false);

                        if (revised is null)
                        {
                            ended = "stopped by the person";
                            goto finished;
                        }

                        if (!string.Equals(revised, task, StringComparison.Ordinal))
                        {
                            await journal.WriteAsync(
                                "brief.revised",
                                ask.Node,
                                new { was = task, now = revised },
                                ct).ConfigureAwait(false);

                            task = revised;
                        }
                    }
                    else
                    {
                        console.Note($"Brief {ask.Node} ({node.Role}): {task}");
                    }

                    var role = request.Specialists.Find(node.Role)!;
                    var brief = MakeBrief(
                        runId, ask.Node, team.Lead, node, role, task, ask.Inputs ?? [], doneWhen: [], team, autonomy,
                        request.OutwardAllowed ?? [], request.Specialists.Find, teamDirectory, criteria);

                    await WriteDocumentAsync(directory, $"brief-{Safe(ask.Node)}-{rounds}.json", ReportReader.Write(brief), ct).ConfigureAwait(false);

                    if (brief.Constraints.Worktree is { Length: > 0 } made)
                    {
                        branches[ask.Node] = made;
                    }

                    accepted.Add(new Briefed(ask, node, role, brief));
                }

                var ran = await RunTogetherAsync(request, team, accepted, journal, ct).ConfigureAwait(false);

                for (var index = 0; index < accepted.Count; index++)
                {
                    var ask = accepted[index].Ask;
                    var workerReport = ran[index];

                    cost += workerReport.Cost;
                    warnings.AddRange(workerReport.Warnings);

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

                // Written down, because a run heading for this is something
                // somebody would want to know before it gets there - and the
                // journal had no way to say "that round asked for nothing".
                await journal.WriteAsync(
                    "round.ended",
                    null,
                    new { round = rounds, requests = requests.Count, quiet = quietRounds },
                    ct).ConfigureAwait(false);

                if (quietRounds >= 2)
                {
                    ended = "no progress: two rounds without a request or a finish";
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

            await FoldQuestionsAsync(leadBrief, journal, warnings, CancellationToken.None).ConfigureAwait(false);

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

        // A node asked to resolve a conflict spends money like any other, and
        // the run's total is written after the merge, so it is still in time
        // to count it.
        var afterwards = new List<decimal>();

        var merged = await MergeAsync(
            request, team, runId, autonomy, ended, branches, decisions, console, journal, warnings, afterwards, ct)
            .ConfigureAwait(false);

        cost += afterwards.Sum();

        await journal.WriteAsync("run.finished", null, new { ended, cost, rounds, merged }, ct).ConfigureAwait(false);

        await DeclareAsync(
            slug, runId, team, Landed(ended), request.Goal,
            $"{ended}; {rounds} round(s), ${cost:0.00}. loadout team status {runId}", ct)
            .ConfigureAwait(false);

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
    /// The project's slug, or the handle as given when it cannot be resolved.
    /// </summary>
    /// <summary>
    /// Where on this machine the run works, or null when nothing can say.
    /// </summary>
    /// <remarks>
    /// Recorded beside the project rather than derived from it later: a
    /// project's registered path can be changed afterwards, and a run that
    /// said where it went is worth more than one that says where it would go
    /// today.
    /// </remarks>
    private async Task<string?> PathAsync(TeamRunRequest request, CancellationToken ct)
    {
        if (_projects is null)
        {
            return null;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        return resolution.Value?.LocalPath;
    }

    private async Task<string> SlugAsync(TeamRunRequest request, CancellationToken ct)
    {
        if (_projects is null)
        {
            return request.ProjectHandle;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        return resolution.Value?.Entry.Slug ?? request.ProjectHandle;
    }

    /// <summary>
    /// Puts the run in the project's task list, so what a team is doing shows
    /// up beside everything else the project is working on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One task for the run, not one per node. The list is a shared file that
    /// travels with the workspace and is meant to hold tens of things; a
    /// row per node would put hundreds there within a few runs, and the
    /// per-node view already exists in <c>team status</c>, which reads the
    /// run's own journal.
    /// </para>
    /// <para>
    /// A failure here is not the run's failure. The task list is a
    /// convenience beside the journal, which is the record, so this reports
    /// nothing and stops nothing.
    /// </para>
    /// </remarks>
    private async Task DeclareAsync(
        string slug,
        string runId,
        TeamDefinition team,
        TaskState state,
        string goal,
        string note,
        CancellationToken ct)
    {
        if (_tasks is null)
        {
            return;
        }

        await _tasks.DeclareAsync(
            slug,
            $"team-{runId}",
            state,
            $"team {team.Name}",
            Cut(goal),
            note,
            ct).ConfigureAwait(false);
    }

    /// <summary>A goal short enough to read in a list.</summary>
    private static string Cut(string goal)
    {
        var trimmed = goal.Trim();

        return trimmed.Length > 120 ? trimmed[..120] + "..." : trimmed;
    }

    /// <summary>
    /// Where a run's ending leaves the task.
    /// </summary>
    /// <remarks>
    /// Only a lead that said it was done gets Done. Everything else - a
    /// blocked lead, a halt, a round limit, a person stopping it - leaves
    /// something unfinished, and calling that done is the one thing a task
    /// list must not do.
    /// </remarks>
    private static TaskState Landed(string ended) => ended switch
    {
        "done" => TaskState.Done,
        "blocked" => TaskState.Blocked,
        _ => TaskState.Doing,
    };

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
        string runId,
        string autonomy,
        string ended,
        IReadOnlyDictionary<string, string> branches,
        IReadOnlyDictionary<string, string> decisions,
        ITeamConsole console,
        Journal journal,
        List<string> warnings,
        List<decimal> spent,
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
                await journal.WriteAsync("merge.conflicted", node, new { branch, result.Value.Conflicts }, ct).ConfigureAwait(false);

                // Back to whoever wrote it. The node still has its worktree,
                // it knows why it made the change, and it is better placed to
                // reconcile the two than a person reading a list of file
                // names afterwards.
                //
                // Once only. A node that cannot resolve its own conflict on a
                // second look will not on a third, and the person needs to
                // hear about it rather than watch it spend.
                var second = await ResolveAsync(
                    request, team, runId, node, branch, target, result.Value.Conflicts,
                    autonomy, repository, console, journal, warnings, spent, ct).ConfigureAwait(false);

                if (second is null)
                {
                    warnings.Add(
                        $"{branch} conflicts with {target} in {string.Join(", ", result.Value.Conflicts)}, so nothing "
                        + "was merged and the repository is as it was.");

                    continue;
                }

                if (!second.Merged)
                {
                    warnings.Add(
                        $"{branch} still conflicts with {target} in {string.Join(", ", second.Conflicts)}, after "
                        + $"{node} was asked to resolve it. Nothing was merged and the repository is as it was.");

                    await journal.WriteAsync("merge.conflicted", node, new { branch, conflicts = second.Conflicts, again = true }, ct)
                        .ConfigureAwait(false);

                    continue;
                }

                result = OperationResult<GitMerge>.Ok(second);
            }

            merged.Add(branch);

            await journal.WriteAsync("merge.done", node, new { branch, target, result.Value!.FastForward }, ct)
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

    /// <summary>
    /// Asks the node that wrote a branch to reconcile it with the target, and
    /// tries the merge once more.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Null when nobody was asked: the node is not in the team any more, its
    /// role is gone, a person said no, or it came back without doing the
    /// work. The caller then reports the first conflict as it stands.
    /// </para>
    /// <para>
    /// The node is briefed under the same instance name, which resolves to
    /// the same branch and therefore the same worktree it was working in.
    /// It is a fresh session with no memory of the first one, so the brief
    /// has to carry everything: which branch, which target, which files.
    /// </para>
    /// </remarks>
    private async Task<GitMerge?> ResolveAsync(
        TeamRunRequest request,
        TeamDefinition team,
        string runId,
        string nodeName,
        string branch,
        string target,
        IReadOnlyList<string> conflicts,
        string autonomy,
        string repository,
        ITeamConsole console,
        Journal journal,
        List<string> warnings,
        List<decimal> spent,
        CancellationToken ct)
    {
        if (!team.Nodes.TryGetValue(BaseNode(nodeName), out var node)
            || request.Specialists.Find(node.Role) is not { } role)
        {
            return null;
        }

        var files = string.Join(", ", conflicts);

        var task =
            $"Your branch '{branch}' cannot be merged into '{target}': the two changed {files}. "
            + $"In your own worktree, merge '{target}' into '{branch}' and resolve every conflict, "
            + "keeping what both sides were trying to do. Do not change "
            + $"'{target}' itself, and do not merge your branch anywhere. Commit the resolution on "
            + "your own branch and report that commit.";

        if (!await GateAsync(autonomy, console, $"Ask {nodeName} to resolve the conflict between {branch} and {target}", ct)
            .ConfigureAwait(false))
        {
            return null;
        }

        await journal.WriteAsync("conflict.briefed", nodeName, new { branch, target, conflicts }, ct).ConfigureAwait(false);

        var brief = MakeBrief(
            runId, nodeName, team.Lead, node, role, task, conflicts,
            doneWhen: [$"'{branch}' merges into '{target}' with no conflict"],
            team, autonomy, request.OutwardAllowed ?? [], request.Specialists.Find,
            TeamDirectory(team.Name));

        var ask = new ReportRequest(nodeName, task, DeliverableKind.Commit);

        using var alone = new SemaphoreSlim(1, 1);

        var outcome = await RunWorkerAsync(
            request, team, new Briefed(ask, node, role, brief), journal, alone, ct).ConfigureAwait(false);

        warnings.AddRange(outcome.Warnings);
        spent.Add(outcome.Cost);

        if (outcome.Report is not { Status: ReportStatus.Done })
        {
            return null;
        }

        var again = await _git!.MergeAsync(repository, branch, ct).ConfigureAwait(false);

        if (again.Failed)
        {
            warnings.Add($"{branch} could not be merged after {nodeName} resolved it: {again.Error}");

            return null;
        }

        return again.Value;
    }

    /// <summary>A request that passed its gate and has a brief waiting.</summary>
    private sealed record Briefed(ReportRequest Ask, TeamNode Node, SpecialistDocument Role, Brief Brief);

    private sealed record WorkerOutcome(
        Report? Report,
        decimal Cost,
        bool Halted,
        string? Failure,
        IReadOnlyList<string> Warnings);

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
    private async Task<IReadOnlyList<WorkerOutcome>> RunTogetherAsync(
        TeamRunRequest request,
        TeamDefinition team,
        IReadOnlyList<Briefed> briefed,
        Journal journal,
        CancellationToken ct)
    {
        if (briefed.Count <= 1)
        {
            return briefed.Count == 0
                ? []
                : [await RunWorkerAsync(request, team, briefed[0], journal, new SemaphoreSlim(1, 1), ct).ConfigureAwait(false)];
        }

        using var starting = new SemaphoreSlim(1, 1);

        var limits = new Dictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

        foreach (var one in briefed)
        {
            var name = BaseNode(one.Ask.Node);

            if (!limits.ContainsKey(name))
            {
                limits[name] = new SemaphoreSlim(Math.Max(1, one.Node.Parallel));
            }
        }

        try
        {
            var running = briefed.Select(async one =>
            {
                var limit = limits[BaseNode(one.Ask.Node)];

                await limit.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    return await RunWorkerAsync(request, team, one, journal, starting, ct).ConfigureAwait(false);
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
    private async Task<WorkerOutcome> RunWorkerAsync(
        TeamRunRequest request,
        TeamDefinition team,
        Briefed briefed,
        Journal journal,
        SemaphoreSlim starting,
        CancellationToken ct)
    {
        var (node, role, brief) = (briefed.Node, briefed.Role, briefed.Brief);
        var warnings = new List<string>();

        await starting.WaitAsync(ct).ConfigureAwait(false);

        OperationResult<HeadlessLaunch> started;

        try
        {
            started = await StartNodeAsync(request, team, node, role, brief, dryRun: false, ct).ConfigureAwait(false);
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

        await using var launch = started.Value!;
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

        var turn = await NodeTurnAsync(launch, brief, Render(brief), journal, _time, ct).ConfigureAwait(false);

        var (exit, killed) = await launch.Session!.EndAsync(EndGrace, CancellationToken.None).ConfigureAwait(false);
        var stderr = Tail(launch.Session.StandardError);

        await journal.WriteAsync("node.ended", brief.Node, new { exit, killed, stderr }, CancellationToken.None).ConfigureAwait(false);
        await launch.CompleteAsync(exit, CancellationToken.None).ConfigureAwait(false);

        await FoldQuestionsAsync(brief, journal, warnings, CancellationToken.None).ConfigureAwait(false);

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
        TimeProvider time,
        CancellationToken ct)
    {
        var session = launch.Session!;
        var cost = 0m;
        var said = string.Empty;
        var watching = new Progress(journal, brief.Node, time) { Steering = session };

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var turn = await session.TurnAsync(prompt, watching.SawAsync, ct).ConfigureAwait(false);
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
                await journal.WriteAsync("report.unreadable", brief.Node, new { error = read.Error, said }, ct).ConfigureAwait(false);

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

                // Written on every check, not only the accepted one. A lead
                // that was sent back is the interesting case: the journal is
                // then the only record of which criterion it could not answer,
                // and without it a returned report reads as "something was
                // wrong" with no way to see what.
                coverage = report.Coverage is { Count: > 0 }
                    ? report.Coverage.Select(one => new
                    {
                        one.Criterion,
                        verdict = one.Verdict.ToString().ToLowerInvariant(),
                        one.Because,
                    })
                    : null,
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
                    definition.AllowedTools ?? [],
                    definition.DeniedTools ?? [],

                    // Only where somebody is watching. An autonomous run has
                    // nobody, and a question nobody answers is a node sitting
                    // still for five minutes and then being refused anyway -
                    // worse than the refusal it would have had at once.
                    Ask: _asking,

                    // What this team has registered and what was decided about
                    // each, worked out here so that whatever answers the node's
                    // questions reads no files and holds no opinion.
                    Remedies: Standing(team, request.Remediation, request.TrustedRemedies)),
                ct).ConfigureAwait(false);

        var options = new HeadlessOptions(
            Permission: Tier(definition.Mode),
            AllowedTools: definition.AllowedTools,
            DeniedTools: definition.DeniedTools,

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
            // after it reads a change already on that branch.
            Worktree: brief.Constraints.Worktree,
            CreateWorktree: brief.Constraints.Worktree is { Length: > 0 },
            PermissionPolicyPath: policy,

            // The directory the brief has just told this node to keep things
            // in. Without it the agent refuses every write there, which is
            // what it did: the directory existed, the path in the brief was
            // right, the declaration was clear, and nothing could be written.
            ReachableDirectories: brief.TeamDirectory is { Length: > 0 } kept ? [kept] : null);

        return await _launcher.StartHeadlessAsync(launch, options, ct).ConfigureAwait(false);
    }

    /// <summary>Where a run keeps everything it writes.</summary>
    private string RunDirectory(string runId) =>
        Path.Combine(_paths.Paths.State, "teams", "runs", runId);

    /// <summary>
    /// Where a team keeps what it makes, across every run of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Beside the runs rather than inside one, because that is the whole
    /// point: a run's directory goes when the run is over, and a team that
    /// works out how to fix something should not have to work it out again
    /// next week.
    /// </para>
    /// <para>
    /// One path for every node of every run, so a declaration that says
    /// "register it in the team's directory" names one place. Left to each
    /// node, "the team's directory" is a phrase that resolves differently
    /// every time and the work is lost between runs.
    /// </para>
    /// <para>
    /// Not in the repository. What a team learns about keeping a system up is
    /// not a change to whatever it was looking at, and committing it into
    /// somebody's project because a declaration said "register it" would be a
    /// surprise.
    /// </para>
    /// </remarks>
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

    private string TeamDirectory(string team) =>
        Path.Combine(_paths.Paths.State, "teams", "work", Slug(team));

    /// <summary>A team name as a directory name, with nothing in it that can climb.</summary>
    /// <remarks>
    /// A team name comes out of a file that may have come from anywhere, and a
    /// name with a separator or a pair of dots in it would make a path that is
    /// not under the one intended. Anything that is not a letter, a digit or a
    /// hyphen becomes a hyphen.
    /// </remarks>
    private static string Slug(string team)
    {
        var clean = new string([.. team.Select(one =>
            char.IsAsciiLetterOrDigit(one) || one == '-' ? char.ToLowerInvariant(one) : '-')]);

        return clean.Trim('-') is { Length: > 0 } named ? named : "team";
    }

    /// <summary>
    /// Takes what the answerer recorded into the run's own record, once the
    /// node that asked has gone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answerer runs in another process, so it writes its own file rather
    /// than appending to the journal: two processes appending to one file is
    /// how a record acquires half lines. This is the fold, and it happens
    /// after the node has ended, when nothing more can be written to it.
    /// </para>
    /// <para>
    /// A refusal is worth telling the person about, because it is usually not
    /// a node misbehaving. It is a role whose lists are narrower than the work
    /// it was given, and nobody finds that out from a count of denials.
    /// </para>
    /// </remarks>
    /// <summary>When a folded event says it happened, or nothing.</summary>
    private static DateTimeOffset? When(RunEvent asked) =>
        asked.Word("at") is { } said
        && DateTimeOffset.TryParse(
            said,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind,
            out var then)
            ? then
            : null;

    private async Task FoldQuestionsAsync(
        Brief brief,
        Journal journal,
        List<string> warnings,
        CancellationToken ct)
    {
        var path = Path.Combine(RunDirectory(brief.Run), NodePermissions.AskedFileName(brief.Node));

        string[] lines;

        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            lines = await File.ReadAllLinesAsync(path, ct).ConfigureAwait(false);

            // Removed once folded, so a node briefed a second time in the
            // same run does not have its first turn's questions counted
            // again.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var refused = new List<string>();

        foreach (var line in lines)
        {
            if (RunJournal.Parse($$"""{"kind":"permission.asked","data":{{line}}}""") is not { } asked)
            {
                continue;
            }

            // The decision carries its own time. Dating it at the fold is what
            // put it after the node's death in the log.
            await journal
                .WriteAsync("permission.asked", brief.Node, asked.Data, ct, When(asked))
                .ConfigureAwait(false);

            if (asked.Data.TryGetProperty("allowed", out var allowed) && allowed.ValueKind == JsonValueKind.False)
            {
                var what = asked.Text("tool") ?? "something";
                var target = asked.Text("target");

                refused.Add(target is { Length: > 0 } ? $"{what} ({target})" : what);
            }
        }

        if (refused.Count > 0)
        {
            warnings.Add(
                $"{brief.Node} asked for {refused.Count} thing(s) its role does not allow: "
                + string.Join(", ", refused.Distinct(StringComparer.Ordinal))
                + ". Either the role is narrower than the work, or the node was going somewhere it should not.");
        }

        await FoldProgressAsync(brief, journal, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Folds a node's own account of what it was doing into the journal.
    /// </summary>
    /// <remarks>
    /// Kept as its own kind of entry rather than merged with what the run
    /// observed. Two accounts, and neither corrects the other: the run's is
    /// precise about what happened and says nothing about why, the node's says
    /// why and may be wrong. Merging them would lose whichever disagreed.
    /// </remarks>
    private async Task FoldProgressAsync(Brief brief, Journal journal, CancellationToken ct)
    {
        var path = Path.Combine(RunDirectory(brief.Run), NodeProgress.FileName(brief.Node));

        var said = NodeProgress.Read(path);

        if (said.Count == 0)
        {
            return;
        }

        foreach (var one in said)
        {
            await journal
                .WriteAsync("node.said", brief.Node, new { step = one.Step, of = one.Of, doing = one.Doing, line = one.Line }, ct)
                .ConfigureAwait(false);
        }

        try
        {
            // Removed once folded, so a node briefed twice in one run does not
            // have its first turn's account replayed into the second.
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Carries a node's unanswerable permission question up to the person, for
    /// as long as the run lasts.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The node is stopped inside a turn this process is awaiting, so the
    /// question cannot come back up the way everything else does. It arrives as
    /// a file in the run's directory, written by the answerer — a different
    /// process, started by the agent — and the answer goes back the same way.
    /// </para>
    /// <para>
    /// Both the question and the answer are journalled, because "the run was
    /// allowed to do that" and "somebody allowed it" are different sentences to
    /// whoever reads it afterwards, and only one of them is true here.
    /// </para>
    /// </remarks>
    private async Task WatchAsksAsync(
        string directory,
        ITeamConsole console,
        Journal journal,
        CancellationToken ct)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Questions noted and not yet answered, for the console that answers
        // into this directory. Kept rather than awaited: awaiting one held the
        // loop for the whole five minutes of somebody's patience, and every
        // other node's question went unnoted until it let go.
        var outstanding = new Dictionary<string, PendingAsk>(StringComparer.Ordinal);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var ask in NodePermissions.Pending(directory))
                {
                    if (!seen.Add(ask.Id))
                    {
                        continue;
                    }

                    await journal
                        .WriteAsync("node.asked", ask.Node, new { tool = ask.Tool, target = ask.Target }, ct)
                        .ConfigureAwait(false);

                    if (console.AnswersInPlace)
                    {
                        // The question is already where this console reads it.
                        // Asking would put it in the same queue a second time,
                        // under a second id, and whoever answered one would
                        // leave the other on the list.
                        outstanding[ask.Id] = ask;
                        continue;
                    }

                    var allowed = await console.ConfirmAsync(ask.Question, ct).ConfigureAwait(false);

                    await NodePermissions.AnswerAsync(
                        directory,
                        ask.Id,
                        new AskAnswer(
                            allowed,
                            allowed
                                ? "The person running this team allowed it, for this call only."
                                : "The person running this team refused it. Report what you needed and "
                                    + "why rather than finding another way to do it."),
                        ct).ConfigureAwait(false);

                    await journal
                        .WriteAsync("node.answered", ask.Node, new { tool = ask.Tool, allowed }, ct)
                        .ConfigureAwait(false);
                }

                // Whatever has been answered since the last glance. Pending
                // stops returning a question once its answer is there, so what
                // has gone from that list is what somebody decided - and it is
                // their answer that gets journalled, not one made up here.
                foreach (var (id, ask) in outstanding.ToList())
                {
                    if (NodePermissions.Answered(directory, id) is not { } said)
                    {
                        continue;
                    }

                    outstanding.Remove(id);

                    await journal
                        .WriteAsync(
                            "node.answered",
                            ask.Node,
                            new { tool = ask.Tool, allowed = said.Allowed, reason = said.Reason },
                            ct)
                        .ConfigureAwait(false);
                }

                await Task.Delay(NodePermissions.Glance, _time, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // The run ended. Anything still waiting is answered by the
            // answerer's own patience running out, which says so.
        }
    }

    /// <summary>
    /// A background watch that stops when the run leaves, however it leaves.
    /// </summary>
    /// <remarks>
    /// Its own type because <see cref="RunAsync"/> returns from six places, and
    /// a cancel written at each of them is five places to forget it. Disposing
    /// a token source does not cancel it, which is the trap this exists to
    /// close.
    /// </remarks>
    private sealed class Watching : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _task;

        private Watching(CancellationTokenSource stop, Task task)
        {
            _stop = stop;
            _task = task;
        }

        public static Watching Start(bool wanted, Func<CancellationToken, Task> watch, CancellationToken ct)
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);

            return new Watching(stop, wanted ? watch(stop.Token) : Task.CompletedTask);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync().ConfigureAwait(false);

            try
            {
                await _task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            _stop.Dispose();
        }
    }

    /// <summary>
    /// What a node's own branch is called.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flat, with hyphens, and that is the whole point. Git refs are files, so
    /// a branch called <c>teams/a/b</c> is a file at <c>refs/heads/teams/a/b</c>
    /// and needs <c>refs/heads/teams</c> to be a directory. A repository whose
    /// current branch is called <c>teams</c> therefore cannot create one, and
    /// says so in the middle of a run:
    /// </para>
    /// <code>
    /// fatal: cannot lock ref 'refs/heads/teams/20260917-1036-16f5/implementer-1':
    /// 'refs/heads/teams' exists
    /// </code>
    /// <para>
    /// Found by the first real run on the branch this feature was written on,
    /// which was called <c>teams</c>. The collision is symmetric and that is
    /// the worse half: a hierarchical run branch also stops anybody creating a
    /// branch called <c>teams</c> ever again, so every run would quietly
    /// poison an ordinary name in somebody's repository.
    /// </para>
    /// <para>
    /// A prefix and no slashes costs the grouping that <c>git branch --list
    /// 'teams/*'</c> gave, and <c>'teams-*'</c> groups them just as well.
    /// </para>
    /// </remarks>
    internal static string BranchFor(string runId, string nodeName) =>
        $"teams-{runId}-{Safe(nodeName)}";

    /// <summary>The permission tier a posture gets: edits accepted only for one that changes the repository.</summary>
    private static HeadlessPermission Tier(string? mode) =>
        string.Equals(mode, "implement", StringComparison.OrdinalIgnoreCase)
            ? HeadlessPermission.AcceptEdits
            : HeadlessPermission.DenyUnlessAllowed;

    /// <summary>
    /// The brief for one node.
    /// </summary>
    /// <remarks>
    /// Internal rather than private for the same reason <see cref="Render" />
    /// is: the mapping from a team file to what a node reads is worth testing
    /// on its own, and nothing tested that a team's standing goal reached a
    /// brief at all until a mutation of that line survived.
    /// </remarks>
    internal static Brief MakeBrief(
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
        IReadOnlyList<string> allowed,
        Func<string, SpecialistDocument?> specialistsOf,
        string? teamDirectory,
        IReadOnlyList<string>? criteria = null)
    {
        var definition = role.Role;

        // What this machine agreed to, not what the team file asked for. A
        // team file is shared and may only ask; the decision is made before the
        // run starts and arrives here already made, so forgetting to make it
        // grants nothing rather than everything.
        var outwardAllowed = autonomy == "autonomous" ? allowed : [];

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
                Worktree: node.Worktree ? BranchFor(runId, nodeName) : null,
                OutwardAllowed: outwardAllowed),
            doneWhen,
            node.Parameters.Count > 0 ? node.Parameters : null,
            delegates,

            // What the team is for, and how it always works. A node given a
            // narrow job still needs both: "find why the disk filled" is a
            // different job inside a team that exists to keep a system up from
            // inside one that exists to write a report about it.
            team.Goal is { Length: > 0 } purpose ? purpose : null,
            team.Declarations.Count > 0 ? team.Declarations : null,
            teamDirectory,

            // The run's criteria reach every node, not only the lead. A worker
            // given a narrow job still needs to know what the run is being
            // judged on, for the same reason it is told the team's standing
            // goal. Answering for them is the lead's alone.
            criteria is { Count: > 0 } ? criteria : null);
    }

    /// <summary>
    /// The run's criteria the lead has not reported as met, in the run's own
    /// words.
    /// </summary>
    /// <remarks>
    /// Matched the way <see cref="ReportCheck"/> matches them, trimmed and
    /// past case, because the two have to agree about what counts as answered.
    /// A run given no criteria has nothing outstanding, which is what keeps
    /// every run written before they existed ending exactly as it did.
    /// </remarks>
    internal static IReadOnlyList<string> Outstanding(
        IReadOnlyList<string>? criteria,
        Report report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (criteria is not { Count: > 0 })
        {
            return [];
        }

        var said = new Dictionary<string, CoverageVerdict>(StringComparer.OrdinalIgnoreCase);

        foreach (var one in report.Coverage ?? [])
        {
            // First answer wins, exactly as the check does, so the two cannot
            // disagree about a criterion a lead answered twice.
            said.TryAdd(one.Criterion?.Trim() ?? string.Empty, one.Verdict);
        }

        return
        [
            .. criteria.Where(criterion =>
                !said.TryGetValue(criterion.Trim(), out var verdict)
                || verdict != CoverageVerdict.Met),
        ];
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

        // Before the task, because it frames it. The task says what to do
        // today; this says what the team is for, and the two answer different
        // questions.
        if (brief.Goal is { Length: > 0 } purpose)
        {
            text.AppendLine("## What this team is for").AppendLine().AppendLine(purpose).AppendLine();
        }

        if (brief.Declarations is { Count: > 0 } declarations)
        {
            text.AppendLine("## How this team works").AppendLine();
            text.AppendLine("These hold for every run of this team, including this one.").AppendLine();

            foreach (var rule in declarations)
            {
                text.AppendLine($"- {rule}");
            }

            text.AppendLine();
        }

        if (brief.TeamDirectory is { Length: > 0 } kept)
        {
            text.AppendLine("## The team's directory").AppendLine();
            text.AppendLine(
                $"`{kept}` is this team's own directory. It is the same directory for every node "
                + "of every run of this team, and it outlives them: put anything the team is "
                + "meant to keep and reuse there, and look there before working something out "
                + "again. It is not in the repository, so nothing written there is a change to "
                + "whatever you are working on.").AppendLine();

            // What "register it" means, because it is a fact about how Loadout
            // works rather than something a team should have to restate. Left
            // out, a node does the sensible human thing - one wrote the script
            // and a README index - and everything that reads registered
            // remedies sees an empty shelf.
            text.AppendLine(
                "Something the team should be able to run again goes in as a pair of files under "
                + "`remedies/`, which is what makes it a remedy this machine can be asked about "
                + "rather than a file in a folder:").AppendLine();

            text.AppendLine("```");
            text.AppendLine("remedies/<name>.ps1     the script itself, in whatever suits the job");
            text.AppendLine("remedies/<name>.yaml    what it is:");
            text.AppendLine();
            text.AppendLine("    name: <name>");
            text.AppendLine("    kind: <one word for the sort of task: disk, service, network>");
            text.AppendLine("    what: <what it does, in a sentence>");
            text.AppendLine("    assumes: <what it assumes about the machine it runs on>");
            text.AppendLine("    proves: <how somebody would know it worked>");
            text.AppendLine("    script: <name>.ps1");
            text.AppendLine("```").AppendLine();

            text.AppendLine(
                "Nothing you write decides whether it may run. A person at this machine agrees to "
                + "a remedy once, against that exact script, and until they have a remediator asks "
                + "before running it. A record claiming to be trusted decides nothing.").AppendLine();
        }

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

            await journal.WriteAsync("decision", null, new { question = question.Question, answer, by = autonomy == "autonomous" ? "recommendation" : "person" }, ct)
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

    /// <summary>
    /// Turns what a node is doing into occasional journal lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node's turn can run for minutes, and until this existed the run said
    /// nothing at all in between: `team status` could say a node was working
    /// and never what on, which is the difference between a progress report
    /// and a spinner.
    /// </para>
    /// <para>
    /// Throttled on purpose. An agent writes hundreds of events in a turn and
    /// a line for each would bury the record it shares with everything else
    /// that happened. One line when something starts, and one every few
    /// seconds after that, is what a person reading along can use.
    /// </para>
    /// <para>
    /// What it writes is a tool's name and, where the tool has one, the thing
    /// it is pointed at, redacted and cut short. Never the call's arguments,
    /// which carry file contents, and never the agent's own prose beyond its
    /// first line.
    /// </para>
    /// </remarks>
    private sealed class Progress
    {
        private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

        private readonly Journal _journal;
        private readonly string _node;
        private readonly TimeProvider _time;
        private DateTimeOffset _last = DateTimeOffset.MinValue;
        private string _said = string.Empty;

        public Progress(Journal journal, string node, TimeProvider time)
        {
            _journal = journal;
            _node = node;
            _time = time;
        }

        /// <summary>
        /// The session to pass anything somebody says along to, or null.
        /// </summary>
        /// <remarks>
        /// Here rather than anywhere else because this is the one thing that
        /// runs while a node's turn is in flight. Everything else that reaches
        /// into a run waits for a round to come back, which for a worker going
        /// the wrong way means waiting for exactly the spend somebody is trying
        /// to stop.
        /// </remarks>
        public HeadlessSession? Steering { get; init; }

        public async Task SawAsync(HeadlessEvent evt, CancellationToken ct)
        {
            await PassOnAsync(ct).ConfigureAwait(false);

            // Everything, before the throttling below. The journal is
            // deliberately thin - one line every few seconds, repeats dropped -
            // which is right for watching a run and wrong for working out what
            // it did, and those two wants deserve different files.
            await KeptAsync(evt, ct).ConfigureAwait(false);

            if (Describe(evt) is not { Length: > 0 } doing)
            {
                return;
            }

            var now = _time.GetUtcNow();

            // The same thing again is not progress, and a burst of them is
            // not progress five times over.
            if (string.Equals(doing, _said, StringComparison.Ordinal) || now - _last < Quiet)
            {
                return;
            }

            _last = now;
            _said = doing;

            await _journal.WriteAsync("node.doing", _node, new { doing }, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Tells the node whatever somebody has left for it.
        /// </summary>
        /// <remarks>
        /// Between the events the run is already reading, which is as often as
        /// a node says anything and no more often than that. A node that has
        /// gone quiet is a node nobody can steer, which is the same thing the
        /// needs-you rail already says out loud.
        /// </remarks>
        private async Task PassOnAsync(CancellationToken ct)
        {
            if (Steering is null || _journal.Directory is not { Length: > 0 } directory)
            {
                return;
            }

            foreach (var message in NodeControl.Take(directory, _node))
            {
                await Steering.SayAsync(message, ct).ConfigureAwait(false);

                await _journal.WriteAsync(
                    "node.told", _node, new { message }, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Writes one step to the node's own stream.
        /// </summary>
        /// <remarks>
        /// In the launcher's vocabulary rather than the agent's, so nothing
        /// here reads anybody's JSON and a node's stream never carries whatever
        /// its agent felt like printing.
        /// </remarks>
        private async Task KeptAsync(HeadlessEvent evt, CancellationToken ct)
        {
            var step = evt switch
            {
                HeadlessToolUse use => new NodeStep(
                    _journal.Now, "tool", use.Tool, Target(use.InputJson).TrimStart(), null, use.FromSubagent),

                HeadlessText text => new NodeStep(
                    _journal.Now, "said", null, null, text.Text, text.FromSubagent),

                HeadlessToolResult answered => new NodeStep(
                    _journal.Now, answered.IsError ? "failed" : "answered", null, null, answered.Content),

                HeadlessPermissionDenied refused => new NodeStep(
                    _journal.Now, "refused", refused.Tool),

                HeadlessStarted begun => new NodeStep(
                    _journal.Now, "started", null, begun.Model),

                _ => null,
            };

            if (step is null)
            {
                return;
            }

            await _journal.AppendAsync(
                NodeStream.FileFor(_node),
                NodeStream.Line(step),
                ct).ConfigureAwait(false);
        }

        /// <summary>What an event says the node is doing, or null for one that says nothing.</summary>
        private static string? Describe(HeadlessEvent evt) => evt switch
        {
            HeadlessToolUse use => Cut($"{use.Tool}{Target(use.InputJson)}"
                + (use.FromSubagent ? " (subagent)" : string.Empty)),
            HeadlessText { FromSubagent: false } text => Cut(FirstLine(text.Text)),
            _ => null,
        };

        /// <summary>
        /// The one thing a tool call is pointed at, from the names tools
        /// actually use for it.
        /// </summary>
        private static string Target(string inputJson)
        {
            if (string.IsNullOrWhiteSpace(inputJson))
            {
                return string.Empty;
            }

            try
            {
                using var document = JsonDocument.Parse(inputJson);

                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return string.Empty;
                }

                foreach (var name in (string[])["file_path", "path", "command", "pattern", "url", "description"])
                {
                    if (document.RootElement.TryGetProperty(name, out var value)
                        && value.ValueKind == JsonValueKind.String
                        && value.GetString() is { Length: > 0 } text)
                    {
                        return " " + FirstLine(text);
                    }
                }
            }
            catch (JsonException)
            {
                // A tool whose input is not an object tells us nothing more
                // than its name, which is already worth a line.
            }

            return string.Empty;
        }

        private static string FirstLine(string text)
        {
            var line = text.AsSpan().Trim();
            var end = line.IndexOfAny('\r', '\n');

            return (end < 0 ? line : line[..end]).ToString();
        }

        private static string? Cut(string text)
        {
            var trimmed = SecretRedactor.Redact(text).Trim();

            return trimmed.Length switch
            {
                0 => null,
                > 90 => trimmed[..90] + "...",
                _ => trimmed,
            };
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

        /// <summary>The run's directory, which is where everything else goes.</summary>
        public string Directory => Path.GetDirectoryName(_path) ?? string.Empty;

        /// <summary>When it is, by whatever clock the run is keeping.</summary>
        public DateTimeOffset Now => _time.GetUtcNow();

        /// <summary>
        /// Appends one line to a file beside the journal, and never fails a run
        /// over it.
        /// </summary>
        public async Task AppendAsync(string file, string line, CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                await File.AppendAllTextAsync(Path.Combine(Directory, file), line + "\n", ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Records one thing that happened.</summary>
        /// <param name="kind">What happened.</param>
        /// <param name="node">Which node, or null for the run itself.</param>
        /// <param name="data">Whatever that kind carries.</param>
        /// <param name="ct">Cancellation.</param>
        /// <param name="at">
        /// When it happened, where that is not now. An event harvested out of a
        /// side file happened when the file says, not when it was read: the
        /// permission decisions are folded in at the end of a node's turn, and
        /// dating them at the fold put two of them minutes late and after the
        /// node had already ended.
        /// </param>
        public async Task WriteAsync(
            string kind,
            string? node,
            object? data,
            CancellationToken ct,
            DateTimeOffset? at = null)
        {
            var line = JsonSerializer.Serialize(
                new { at = at ?? _time.GetUtcNow(), run = _run, node, kind, data }, JournalLine);

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
