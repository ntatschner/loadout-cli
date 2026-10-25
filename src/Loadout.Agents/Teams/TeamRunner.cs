using System.Text;
using System.Text.Json;
using Loadout.Agents.Teams.Steps;
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
/// <param name="TrustedTools">
/// The catalogue tool versions this machine has agreed may run, from its own
/// configuration. Never read from the catalogue, which agents write in.
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
/// <param name="Resuming">
/// An ended run to pick up rather than a new one to start. The run keeps its
/// identifier, directory and journal; its lead resumes its own agent session
/// where one was recorded, and is otherwise started fresh and told where the
/// run got to.
/// </param>
/// <param name="ResumeMessage">What the person said when picking it up, for the lead.</param>
/// <param name="Task">
/// The id of a task already on the project's list that this run is for, or
/// null for a run of its own. With one, the run moves that task to doing and
/// then to where it landed, and adds no <c>team-</c> task beside it.
/// </param>
/// <param name="Budget">
/// What this run may spend, said for this run: a figure, no cap, or not set
/// for the team's own. Written to the run's override file at the start, where
/// a later <c>team budget</c> changes it.
/// </param>
/// <param name="MachineBudget">
/// This machine's default, from its own configuration, for a team whose file
/// sets no budget. Below the run's and the team's, so a figure in either wins.
/// </param>
/// <param name="TakeRecommendationAfter">
/// How long a lead's question with a recommendation waits for an answer before
/// the recommendation is taken, or null to wait for a person. Only where the
/// console answers in place: a terminal prompt cannot be answered for anybody.
/// </param>
/// <param name="Remediation">
/// What this machine says a remediator may do with each kind of task, by kind.
/// Passed in rather than read here, for the same reason the outward list is:
/// what a machine allows is decided before a run starts and arrives already
/// decided, so forgetting to decide it grants nothing rather than everything.
/// </param>
/// <param name="Standing">
/// The project's standing criteria for this team, and which of them somebody
/// dropped for this run. Resolved by whoever builds the request, the way the
/// machine's settings are, so the runner reads no workspace files. Held on top
/// of <paramref name="Criteria"/> or the team's own, never instead of them.
/// </param>
/// <param name="OnTimeout">
/// What a question timed by <paramref name="TakeRecommendationAfter"/> does when
/// nobody answers: one of <see cref="TeamTimeout"/>'s words, or null for
/// think-again-once.
/// </param>
/// <param name="Origin">
/// What started the run where something other than a person did, written to
/// its journal as <see cref="Loadout.Core.Triggers.RunOrigin"/> reads it.
/// </param>
/// <param name="Follows">
/// The run this one carries on from, after it ended short of its done-when:
/// its lead is told how that one ended and what the person said about it.
/// </param>
public sealed record TeamRunRequest(
    string ProjectHandle,
    TeamDefinition Team,
    SpecialistCatalogue Specialists,
    string Goal,
    string? Autonomy = null,
    bool DryRun = false,
    string? AgentName = null,
    int MaxRounds = 0,
    bool Offline = false,
    bool NoSync = false,
    string? Model = null,
    IReadOnlyList<string>? OutwardAllowed = null,
    IReadOnlyDictionary<string, string>? Remediation = null,
    IReadOnlyList<Loadout.Models.Configuration.TrustedRemedy>? TrustedRemedies = null,
    IReadOnlyList<string>? Criteria = null,
    string? Resuming = null,
    string? ResumeMessage = null,
    TimeSpan? TakeRecommendationAfter = null,
    IReadOnlyList<Loadout.Models.Configuration.TrustedTool>? TrustedTools = null,
    UsdCap Budget = default,
    UsdCap MachineBudget = default,
    string? Task = null,
    StandingCriteria? Standing = null,
    string? OnTimeout = null,
    string? Origin = null,
    string? Follows = null);

/// <summary>
/// A project's standing done-when criteria as they reach one run.
/// </summary>
/// <param name="Applied">What the run is held to.</param>
/// <param name="Dropped">What matched the team and was left out of this run.</param>
/// <param name="DroppedBy">
/// Who left them out: <c>person</c> for some unticked, <c>flag</c> for all of
/// them with <c>--no-project-done-when</c>. Null when nothing was dropped.
/// </param>
public sealed record StandingCriteria(
    IReadOnlyList<string> Applied,
    IReadOnlyList<string> Dropped,
    string? DroppedBy = null)
{
    /// <summary>None at all, which is every project that sets none.</summary>
    public static StandingCriteria None { get; } = new([], []);

    /// <summary>
    /// What matched the team, less what was dropped.
    /// </summary>
    /// <param name="matching">The project's criteria for this team.</param>
    /// <param name="dropAll">Whether all of them were dropped.</param>
    /// <param name="drop">Individual ones dropped, matched trimmed and ignoring case.</param>
    /// <remarks>
    /// A drop that names a criterion the project does not have is ignored
    /// rather than refused. The page sends what it showed, and the manifest
    /// may have changed since it was drawn; nothing is held to less than it
    /// would have been by a drop that matched nothing.
    /// </remarks>
    public static StandingCriteria Resolve(
        IReadOnlyList<string> matching,
        bool dropAll,
        IEnumerable<string>? drop)
    {
        ArgumentNullException.ThrowIfNull(matching);

        if (matching.Count == 0)
        {
            return None;
        }

        if (dropAll)
        {
            return new StandingCriteria([], matching, "flag");
        }

        var unticked = new HashSet<string>(
            (drop ?? []).Select(one => one.Trim()).Where(one => one.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        var dropped = matching.Where(one => unticked.Contains(one.Trim())).ToList();

        return dropped.Count == 0
            ? new StandingCriteria(matching, [])
            : new StandingCriteria(
                [.. matching.Where(one => !unticked.Contains(one.Trim()))], dropped, "person");
    }
}

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

    /// <summary>
    /// The same three questions, saying which kind of stop each is, as
    /// <see cref="GateKind"/> names them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a console that draws them differently: the runner's readings and
    /// budget questions go through the same call as the lead's own, the
    /// proposed done-when through the same call as a brief, and a merge
    /// through the same call as every other step a manual run holds. Told
    /// here, by whoever asks, rather than guessed from the words, which
    /// change whenever somebody rewords them.
    /// </para>
    /// <para>
    /// A console that draws them all alike asks the plain question, which is
    /// what every console did before.
    /// </para>
    /// </remarks>
    Task<bool> ConfirmAsync(string what, string kind, CancellationToken ct = default) =>
        ConfirmAsync(what, ct);

    /// <inheritdoc cref="ConfirmAsync(string, string, CancellationToken)"/>
    Task<string?> ReviseAsync(string what, string task, string kind, CancellationToken ct = default) =>
        ReviseAsync(what, task, ct);

    /// <inheritdoc cref="ConfirmAsync(string, string, CancellationToken)"/>
    Task<string?> DecideAsync(ReportQuestion question, string kind, QuestionTimer? timer, CancellationToken ct = default) =>
        timer is null ? DecideAsync(question, ct) : DecideAsync(question, timer, ct);

    /// <summary>
    /// A node's question about a call its role does not cover, and what the
    /// person said.
    /// </summary>
    /// <remarks>
    /// The ask carries its own choices - yes, yes and don't ask again, no -
    /// and a console that can offer them should, with room to say what to do
    /// instead after a no. One that cannot is asked yes or no, which is what
    /// every console did before.
    /// </remarks>
    async Task<AskAnswer> PermitAsync(PendingAsk ask, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ask);

        var allowed = await ConfirmAsync(ask.Question, ct).ConfigureAwait(false);

        return new AskAnswer(allowed, NodePermissions.Told(allowed, null), Chosen: allowed ? "yes" : "no");
    }

    /// <summary>A question the lead could not decide. The option chosen, or null to stop the run.</summary>
    Task<string?> DecideAsync(ReportQuestion question, CancellationToken ct = default);

    /// <summary>
    /// The same, for a question the run will answer itself at
    /// <see cref="QuestionTimer.At"/> if nobody has.
    /// </summary>
    /// <remarks>
    /// So whoever is answering can be told when and what. A console with no
    /// way to say so asks the plain question, which is what every console did
    /// before.
    /// </remarks>
    Task<string?> DecideAsync(ReportQuestion question, QuestionTimer timer, CancellationToken ct = default) =>
        DecideAsync(question, ct);

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

    /// <remarks>
    /// Forwarded for the same reason as <see cref="ReviseAsync(string, string, CancellationToken)"/>: the default
    /// would ask yes or no through this wrapper, and a console offering the
    /// three answers would never be asked.
    /// </remarks>
    public async Task<AskAnswer> PermitAsync(PendingAsk ask, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.PermitAsync(ask, ct).ConfigureAwait(false);
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

    /// <remarks>
    /// Forwarded for the reason the others are: the default would ask this
    /// wrapper's untimed question, and the page would count down to the wrong
    /// thing.
    /// </remarks>
    public async Task<string?> DecideAsync(ReportQuestion question, QuestionTimer timer, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.DecideAsync(question, timer, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    /// <remarks>
    /// The three that say which kind of stop they are, forwarded for the same
    /// reason as the rest: the defaults would ask this wrapper's plain
    /// questions, and every stop would reach the page as the kind it used to
    /// share with something else.
    /// </remarks>
    public async Task<bool> ConfirmAsync(string what, string kind, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.ConfirmAsync(what, kind, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<string?> ReviseAsync(string what, string task, string kind, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.ReviseAsync(what, task, kind, ct).ConfigureAwait(false);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<string?> DecideAsync(
        ReportQuestion question, string kind, QuestionTimer? timer, CancellationToken ct = default)
    {
        await _turn.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            return await inner.DecideAsync(question, kind, timer, ct).ConfigureAwait(false);
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
public sealed partial class TeamRunner : ITeamRunner
{
    internal static readonly TimeSpan EndGrace = TimeSpan.FromSeconds(15);

    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly WatchAsksStep _watchAsks;
    private readonly StartWorkersStep _workers;
    private readonly OpenRunStep _open;
    private readonly RoundLimitsStep _limits;
    private readonly LeadReportStep _leadReport;
    private readonly BriefRequestsStep _briefRequests;
    private readonly FeedbackStep _feedback;
    private readonly CloseRunStep _close;
    private readonly Core.Teams.IRunOwnership? _ownership;

    /// <summary>
    /// The project service and the git manager are optional so a caller that
    /// only drives nodes needs neither; without them a run cannot merge and
    /// says so rather than appearing to. The child lifetime is optional for
    /// the same reason, and a run that has one says so when it is the weaker
    /// kind. Without the task service a run is still recorded in its own
    /// journal, and only the project's task list goes without it. Without
    /// the run ownership nothing holds a run while it goes, and a run whose
    /// process died cannot be told from one still going.
    /// </summary>
    public TeamRunner(
        IAgentLauncher launcher,
        IPlatformPaths paths,
        TimeProvider time,
        Core.Projects.IProjectService? projects = null,
        Core.Git.IGitManager? git = null,
        IChildLifetime? lifetime = null,
        Core.Tasks.ITaskService? tasks = null,
        Core.Tools.IToolRegistry? tools = null,
        Core.Teams.IRunOwnership? ownership = null)
    {
        _paths = paths;
        _time = time;
        _ownership = ownership;

        // Some steps are shared: the lead, the workers and the resolver are
        // all started through the same sessions, and the opening and the
        // close both say where the run is on the project's task list.
        var declare = new DeclareStep(tasks);
        var fold = new FoldStep(paths);
        var sessions = new NodeSessions(launcher, paths, time, tools);

        _watchAsks = new WatchAsksStep(time);
        _workers = new StartWorkersStep(sessions, new ReviewTreeStep(projects, git), fold, git);
        _open = new OpenRunStep(paths, time, lifetime, projects, sessions, declare);
        _limits = new RoundLimitsStep(time, ownership);
        _leadReport = new LeadReportStep(sessions);
        _briefRequests = new BriefRequestsStep(new DecideStep(time));
        _feedback = new FeedbackStep();
        _close = new CloseRunStep(fold, new MergeStep(projects, git, paths, new TidyStep(git), _workers), declare);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TeamRunOutcome>> RunAsync(
        TeamRunRequest request,
        ITeamConsole console,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(console);

        var (cannot, autonomy, resuming) = _open.Check(request);

        if (cannot is not null)
        {
            return cannot;
        }

        // From here on every question goes through one voice, because the
        // watcher below asks from another thread.
        using var one = new OneAtATime(console);
        console = one;

        var (dryRun, opening) = await _open.PrepareAsync(request, autonomy, resuming, ct).ConfigureAwait(false);

        if (dryRun is not null)
        {
            return dryRun;
        }

        var team = request.Team;
        var (runId, warnings, leadNode, teamDirectory, fromTeam, standing) =
            (opening!.RunId, opening.Warnings, opening.LeadNode, opening.TeamDirectory, opening.FromTeam, opening.Standing);

        var directory = RunDirectory(runId);
        Directory.CreateDirectory(directory);

        // Before anything is written, on a new run and a reopened one alike,
        // so two coordinators picking up one run at the same moment cannot
        // both carry on. Held until the method returns, which is after the
        // close has written run.finished: the lock going is what says the
        // process that drove the run has gone.
        IDisposable? owner = null;

        if (_ownership is not null)
        {
            var taken = _ownership.Take(directory);

            // Passed on as the take said it. Another process holding the run
            // is one reason; a lock file that could not be opened or written
            // is another, and calling that an owner sends somebody looking for
            // a process that is not there.
            if (taken.Failed)
            {
                return OperationResult<TeamRunOutcome>.Fail(taken.Error!, taken.ExitCode);
            }

            owner = taken.Value;
        }

        using var holding = owner;

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
        var run = new RunContext(autonomy != "autonomous" && console.CanAsk, request.OnTimeout, warnings)
        {
            Request = request,
            Team = team,
            Console = console,
            Autonomy = autonomy,
            RunId = runId,
            Directory = directory,
            TeamDirectory = teamDirectory,
            LeadNode = leadNode,
            LeadRole = opening.LeadRole,
            Standing = standing,
            Resuming = resuming,
            LeadBrief = opening.LeadBrief,
            Criteria = opening.Criteria,
            Owner = owner,
        };

        var journal = new JournalWriter(Path.Combine(directory, "journal.jsonl"), runId, _time);

        // Before the watcher starts, not after: a question still open when it
        // first looks is journalled as asked again and kept as waiting, for a
        // wait that ended with the run. Journalled once the run says it was
        // reopened.
        if (resuming is not null)
        {
            // Those it closed itself as it ended, in the leg that ended, are
            // already written down; the lead still has to hear of them.
            run.ClosedAtEnd = NodePermissions.ClosedBy(directory, NodePermissions.Ended, RunJournal.LastReopened(directory));
            run.Retired = await NodePermissions.RetireAsync(directory, ct).ConfigureAwait(false);
        }

        // Runs for the whole run rather than around each turn. A question only
        // appears while a node is mid-turn, so a watcher that covered anything
        // narrower would be four watchers with four chances to miss one. It
        // stops however the run leaves, including the six places that return
        // early.
        await using var watching = Watching.Start(
            run.Asking, token => _watchAsks.RunAsync(run, journal, directory, console, token), ct);

        if (await _open.RunAsync(run, journal, fromTeam, ct).ConfigureAwait(false) is { } ended)
        {
            return ended;
        }

        var lead = run.Lead!;
        var stopped = false;

        try
        {
            run.Prompt = resuming is null
                ? Render(run.LeadBrief) + Previously(request)
                : Picking(resuming, request, lead.Plan, run.LeadBrief, RunControl.Cap(directory).Or(StartingCap(request, team)).Usd, [.. run.ClosedAtEnd, .. run.Retired]);

            while (true)
            {
                if (await _limits.RunAsync(run, journal, ct).ConfigureAwait(false) is not null)
                {
                    break;
                }

                var said = await _leadReport.RunAsync(run, journal, ct).ConfigureAwait(false);

                if (said.Ended is not null)
                {
                    break;
                }

                if (said.Reminded)
                {
                    continue;
                }

                var report = said.Report!;

                var briefing = await _briefRequests.RunAsync(run, journal, report, ct).ConfigureAwait(false);

                if (briefing.Ended is not null)
                {
                    break;
                }

                var ran = await _workers.RunAsync(run, journal, request, team, briefing.Accepted, ct).ConfigureAwait(false);

                if (await _feedback.RunAsync(run, journal, report, briefing, ran, ct).ConfigureAwait(false) is not null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The process running this is being stopped: Ctrl+C, or the
            // daemon that started it told to stop now. Not a kill - there is
            // still time to say so - and a run that does not write its ending
            // reads as running for good.
            stopped = true;
        }
        finally
        {
            await _close.EndLeadAsync(run, journal).ConfigureAwait(false);
        }

        if (stopped)
        {
            await _close.StoppedAsync(run, journal, watching).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
        }

        return await _close.RunAsync(run, journal, watching, ct).ConfigureAwait(false);
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
    /// <summary>The ending written when a person stops a run at its first gate.</summary>
    internal const string Stopping = "stopped before the lead was briefed";

    /// <summary>The ending written when the lead never got going at all.</summary>
    internal const string Unstartable = "the lead could not be started";

    /// <summary>The ending written when the process running the run was stopped mid-round.</summary>
    internal const string ProcessStopped = "stopped with the process running it";

    /// <summary>
    /// The category an ending falls into, written down beside it.
    /// </summary>
    /// <remarks>
    /// Settled here, when the run ends, so that a later change to any of
    /// these sentences cannot re-categorise a run that has already finished.
    /// A sentence the classifier does not recognise writes nothing rather
    /// than a guess, and is read back as a run whose category cannot be said.
    /// </remarks>
    internal static string? Filed(string? ended) =>
        RunOutcomes.From(ended) is { } outcome ? RunOutcomes.Spell(outcome) : null;

    internal static TaskState Landed(string ended) => ended switch
    {
        "done" => TaskState.Done,
        "blocked" => TaskState.Blocked,
        _ => TaskState.Doing,
    };

    /// <summary>The launcher's own tools that role.member requires every node to call.</summary>
    internal static readonly IReadOnlyList<string> MemberTools =
    [
        "mcp__loadout__loadout_progress",
        "mcp__loadout__loadout_task_declare",
    ];

    /// <summary>What a node in this role is allowed: its own list and what every node needs.</summary>
    /// <remarks>
    /// What role.member says every node MUST call, on the launcher's own
    /// server. The contract belongs to no one job, so no role's list named
    /// them and every node was refused both. A role's deny list still wins.
    /// The brief every node is given tells it to search the shared tool
    /// catalogue before building, which run 20260923-1216-be60's lead was
    /// then refused for the same reason.
    /// </remarks>
    internal static List<string> AllowedFor(string roleId, RoleDefinition definition) =>
        (definition.AllowedTools ?? [])
            .Union(MemberTools, StringComparer.Ordinal)
            .Union(CatalogueReading, StringComparer.Ordinal)
            .Union(JudgesOnly.Contains(roleId) ? [] : CatalogueWriting, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// A node's lists with the outward actions its brief allows laid over
    /// them: each one allowed exactly, and every deny it falls under lifted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The brief said a node may take them and its tools said it may not. The
    /// posture denies <c>Bash(git push:*)</c> to every role, the agent reads a
    /// deny before an allow, and the gate found nothing allowing it, so a
    /// tagger told it may run <c>git push --tags</c> was refused at the one
    /// step it was started for.
    /// </para>
    /// <para>
    /// This is the one place a deny is lifted, and on purpose: the action was
    /// asked for by the team and agreed by this machine, and reaches only the
    /// run that carries it. A command is allowed exactly, as the ceiling
    /// agreed it, so <c>git push --tags</c> carries no other push with it;
    /// what lifting the broad deny would let through is still refused by the
    /// gate, which nothing else allows it past. A deny the action does not
    /// fall under, <c>git push --force</c>, stays. Delegating never lifts.
    /// </para>
    /// </remarks>
    internal static (List<string> Allowed, List<string> Denied) WithOutward(
        IReadOnlyList<string> allowed,
        IReadOnlyList<string> denied,
        IReadOnlyList<string>? outward)
    {
        var allow = allowed.ToList();
        var deny = denied.ToList();

        foreach (var action in (outward ?? []).Select(one => one.Trim()).Where(one => one.Length > 0))
        {
            // Spelled as the agent spells a tool, and exactly: an action
            // already written as a rule is taken as written.
            var rule = action.Contains('(', StringComparison.Ordinal) ? action : $"Bash({action})";
            var open = rule.IndexOf('(', StringComparison.Ordinal);
            var (tool, target) = open > 0 && rule.EndsWith(')')
                ? (rule[..open], rule[(open + 1)..^1])
                : (rule, (string?)null);

            if (!allow.Contains(rule, StringComparer.Ordinal))
            {
                allow.Add(rule);
            }

            deny.RemoveAll(one => !NeverDelegated.Contains(one, StringComparer.Ordinal)
                && NodePermissions.Matches(one, tool, target));
        }

        return (allow, deny);
    }

    /// <summary>The catalogue's reading tools, which every node may call.</summary>
    internal static readonly IReadOnlyList<string> CatalogueReading =
    [
        "mcp__loadout__loadout_tools_search",
        "mcp__loadout__loadout_tools_show",
    ];

    /// <summary>The catalogue's recording tools: a submission to its inbox, and a usage record.</summary>
    internal static readonly IReadOnlyList<string> CatalogueWriting =
    [
        "mcp__loadout__loadout_tools_submit",
        "mcp__loadout__loadout_tools_used",
    ];

    /// <summary>Roles that judge one piece of work and write nothing, the catalogue included.</summary>
    /// <remarks>
    /// What a reviewer or verifier finds belongs in its report, where the lead
    /// decides whether it is a lesson worth submitting; and neither ran a tool
    /// whose use there would be to record.
    /// </remarks>
    private static readonly HashSet<string> JudgesOnly = new(StringComparer.Ordinal)
    {
        "role.reviewer",
        "role.verifier",
    };

    /// <summary>The agent's own ways of handing work to another agent, which no node is given.</summary>
    /// <remarks>
    /// <para>
    /// A node that wants work done asks for it in its report, and the run
    /// briefs a node for it: that is how the work gets a branch, a review, a
    /// place in the journal and a share of the budget. A subagent gets none of
    /// those. The second sandbox trial's lead started one to write the code
    /// itself, which ran under the lead's own rules, was refused every write,
    /// and left the lead reporting blocked on a job still "running in the
    /// background" - the whole run, over in one round.
    /// </para>
    /// <para>
    /// Denied rather than left off the allow list, because being off the list
    /// is not enough: Claude Code starts a subagent without asking, so neither
    /// the list nor the launcher's own check ever sees it. <c>Task</c> is the
    /// same tool's older name.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyList<string> NeverDelegated = ["Agent", "Task"];

    /// <summary>Where a run keeps everything it writes.</summary>
    private string RunDirectory(string runId) => RunDirectory(_paths, runId);

    /// <summary>Where a run keeps everything it writes, for whatever holds the paths.</summary>
    internal static string RunDirectory(IPlatformPaths paths, string runId) =>
        Path.Combine(paths.Paths.State, "teams", "runs", runId);

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
    private string TeamDirectory(string team) => TeamDirectory(_paths, team);

    /// <summary>Where a team keeps what it makes, for whatever holds the paths.</summary>
    internal static string TeamDirectory(IPlatformPaths paths, string team) =>
        Path.Combine(paths.Paths.State, "teams", "work", Slug(team));

    /// <summary>A team name as a directory name, with nothing in it that can climb.</summary>
    /// <remarks>
    /// A team name comes out of a file that may have come from anywhere, and a
    /// name with a separator or a pair of dots in it would make a path that is
    /// not under the one intended. Anything that is not a letter, a digit or a
    /// hyphen becomes a hyphen.
    /// </remarks>
    internal static string Slug(string team)
    {
        var clean = new string([.. team.Select(one =>
            char.IsAsciiLetterOrDigit(one) || one == '-' ? char.ToLowerInvariant(one) : '-')]);

        return clean.Trim('-') is { Length: > 0 } named ? named : "team";
    }

    /// <summary>
    /// The one branch of this run a brief names, or null where it names none
    /// or more than one.
    /// </summary>
    /// <remarks>
    /// By the branch's own name, which this runner made and which is written
    /// in full wherever a lead asks for a review: "Review commit 9c5e7b2 on
    /// branch teams-20260923-1216-be60-implementer-1". Only this run's names
    /// count, so a brief quoting an older run's branch is not sent there. Two
    /// is not guessed between: a node put in the wrong tree reviews the wrong
    /// work and says it is fine.
    /// </remarks>
    internal static string? BranchNamed(Brief brief)
    {
        var text = brief.Task + "\n" + string.Join("\n", brief.Inputs ?? []);
        var prefix = "teams-" + brief.Run + "-";

        var found = System.Text.RegularExpressions.Regex
            .Matches(text, System.Text.RegularExpressions.Regex.Escape(prefix) + "[A-Za-z0-9_.-]+")
            .Select(match => match.Value.TrimEnd('.', '-', '_'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return found.Count == 1 ? found[0] : null;
    }

    /// <summary>
    /// What a node started in another node's tree is told about where it is,
    /// or nothing for one that was not.
    /// </summary>
    internal static string Placed(string? reviewing, string directory) =>
        reviewing is { Length: > 0 }
            ? $"\n\n## Where you are\n\nYou are started in the worktree of `{reviewing}`, at {directory}, "
              + "which is the work you were asked about. Run what you need to run here, as it is: "
              + "there is no need to change directory first. "
              + "Read and run; do not commit, check out or change anything here - it is another node's work."
            : string.Empty;

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

    /// <summary>Whether a lead's <c>from</c> is a commit or branch name git can take as an argument, and nothing more.</summary>
    internal static bool IsRef(string value) => RefName().IsMatch(value);

    [System.Text.RegularExpressions.GeneratedRegex(@"^(?!.*\.\.)[A-Za-z0-9_][A-Za-z0-9_./-]{0,199}$")]
    private static partial System.Text.RegularExpressions.Regex RefName();

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
        IReadOnlyList<string>? criteria = null,
        string? from = null)
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
                OutwardAllowed: outwardAllowed,
                From: from),
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
    internal static string BaseNode(string name)
    {
        var slash = name.IndexOf('/', StringComparison.Ordinal);

        return slash > 0 ? name[..slash] : name;
    }

    /// <summary>What every brief says about the machine's shared tools, and all it says.</summary>
    internal const string ToolsPointer =
        "This machine keeps a catalogue of tools shared by every team. Before building something "
        + "that might already exist, search it: `loadout tools search <words>` or "
        + "`loadout_tools_search`. Use `show` for the detail. Submit a tool, idea, bug or lesson "
        + "with `loadout_tools_submit`, and say what you used with `loadout_tools_used`.";

    /// <summary>
    /// For a run carrying on from another, what the lead is told about that
    /// one; empty for a run started afresh.
    /// </summary>
    /// <remarks>
    /// After the brief rather than in it, because the brief is the run's own
    /// and travels as JSON to every node it briefs. This is for the lead's
    /// first turn alone.
    /// </remarks>
    private string Previously(TeamRunRequest request) =>
        request.Follows is { Length: > 0 } follows
        && RunFollowing.Read(new RunJournal(_paths), follows) is { Succeeded: true, Value: { } earlier }
            ? Environment.NewLine + earlier.Previously()
            : string.Empty;

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

        // Fixed text, the same length whatever the catalogue holds. What is in
        // it is fetched when somebody asks, not paid for in every brief.
        text.AppendLine("## Shared tools").AppendLine().AppendLine(ToolsPointer).AppendLine();

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

            if (c.From is { Length: > 0 } from)
            {
                text.AppendLine($"- started from: `{from}`, which is already under your branch. Do not cherry-pick or merge it in.");
            }
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
        // The summary cap is said rather than left to be discovered. The schema
        // has always enforced it and nothing ever mentioned it, so a node over
        // the limit was refused and wrote its whole report again - a model call
        // spent on a number nobody had told it.
        // Asked for rather than enforced. A length rule in the schema refused
        // the whole report and the model rewrote it, three times over in one
        // observed run - and that retry is inside the agent's turn, where
        // nothing here can reach it. Before the evidence rule, because the last
        // sentence a node reads is the contract and a test holds it there.
        text.AppendLine(
            "Reply with one report/1 document as your structured output. Keep summary short: a "
            + "few sentences, not a report of its own. Status done needs evidence.");

        return text.ToString();
    }

    /// <summary>
    /// What the run will be judged on, from the lead's proposal and a person's
    /// say-so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lead proposes, a person disposes. It is put through the same gate a
    /// brief goes through - text in a box, changed or accepted - because that is
    /// exactly the shape of the thing: a list somebody reads, disagrees with
    /// half of, and rewrites.
    /// </para>
    /// <para>
    /// An autonomous run has nobody to ask, so the proposal stands and the
    /// journal says nobody agreed it. That is worth more than no criteria at
    /// all: a lead held to specifics it wrote itself can still report a
    /// criterion unmet, which the one it had before - "the goal is met" - never
    /// could.
    /// </para>
    /// <para>
    /// Null when there is nothing to hold it to, which is the behaviour a run
    /// had before any of this: the lead proposed none, or somebody declined
    /// them. Declining is a real answer and not a failure, so the run carries
    /// on.
    /// </para>
    /// </remarks>
    internal static async Task<IReadOnlyList<string>?> AgreedAsync(
        Report report,
        string autonomy,
        ITeamConsole console,
        JournalWriter journal,
        IReadOnlyList<string> standing,
        CancellationToken ct)
    {
        // A lead shown the project's criteria tends to propose them back. They
        // are held already, and a person handed them in the box would think
        // deleting them there dropped them.
        var fixedOnes = new HashSet<string>(standing.Select(one => one.Trim()), StringComparer.OrdinalIgnoreCase);

        if (Tidied(report.ProposedDoneWhen)?.Where(one => !fixedOnes.Contains(one.Trim())).ToList()
            is not { Count: > 0 } proposal)
        {
            await journal.WriteAsync(
                "criteria.none", null, new { by = "lead", project = standing.Count }, ct).ConfigureAwait(false);

            return null;
        }

        if (autonomy == "autonomous")
        {
            await journal.WriteAsync(
                "criteria.agreed", null, new { criteria = proposal, by = "nobody" }, ct).ConfigureAwait(false);

            console.Note(
                "Nobody is watching, so the run is held to the criteria the lead proposed: "
                + string.Join("; ", proposal)
                + (standing.Count > 0 ? $", and to the project's: {string.Join("; ", standing)}" : string.Empty));

            return proposal;
        }

        var answer = await console.ReviseAsync(
            "What this run will be judged on. The lead proposed these; change them if they are "
            + "not what you meant, one per line."
            + (standing.Count > 0
                ? " The project's own are held as well and are not in the box: " + string.Join("; ", standing) + "."
                : string.Empty),
            string.Join(Environment.NewLine, proposal),

            // Not a brief: nobody is about to be sent these.
            GateKind.DoneWhen,
            ct).ConfigureAwait(false);

        if (Tidied(answer?.Split('\n')) is not { Count: > 0 } agreed)
        {
            // Refused, or emptied. Either is somebody saying they do not want
            // this run held to anything, which is what it would have done
            // anyway.
            await journal.WriteAsync(
                "criteria.none", null, new { by = "person", proposed = proposal, project = standing.Count }, ct)
                .ConfigureAwait(false);

            return null;
        }

        await journal.WriteAsync(
            "criteria.agreed",
            null,
            new { criteria = agreed, by = "person", proposed = proposal },
            ct).ConfigureAwait(false);

        return agreed;
    }

    /// <summary>
    /// How the lead reads each criterion the run holds it to, keyed by the
    /// criterion in the run's own words. Empty where it gave none.
    /// </summary>
    /// <remarks>
    /// Only criteria the run has, matched trimmed and past case, so a reading
    /// of something the lead invented is not recorded as one of the run's.
    /// </remarks>
    internal static IReadOnlyDictionary<string, string> Read(Report report, IReadOnlyList<string>? held)
    {
        var read = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (held is not { Count: > 0 })
        {
            return read;
        }

        var said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var one in report.Readings ?? [])
        {
            if (one.Reading?.Trim() is { Length: > 0 } reading)
            {
                said.TryAdd(one.Criterion?.Trim() ?? string.Empty, reading);
            }
        }

        foreach (var criterion in held)
        {
            if (said.TryGetValue(criterion.Trim(), out var reading))
            {
                read[criterion] = reading;
            }
        }

        return read;
    }

    /// <summary>How the readings question begins, which is what it is known by.</summary>
    internal const string ReadingsQuestionStart = "Before any worker starts:";

    /// <summary>The question that holds a run for its lead's readings.</summary>
    internal static ReportQuestion ReadingsQuestion(IReadOnlyDictionary<string, string> readings)
    {
        var text = new StringBuilder(
            ReadingsQuestionStart + " is this how you mean the done-when? The lead is saying how it "
            + "reads each one, not proposing new ones.");

        foreach (var (criterion, reading) in readings)
        {
            text.Append($" • \"{criterion}\" read as: {reading}");
        }

        text.Append($" Choose {ThinkAgain} to have it read them again before anything starts.");

        return new ReportQuestion(text.ToString(), [AcceptReadings], AcceptReadings);
    }

    /// <summary>
    /// Everything a run is held to: the project's standing criteria, then the
    /// run's chosen ones, each once. Null when there are none.
    /// </summary>
    /// <remarks>
    /// Once, matched the way <see cref="Outstanding"/> matches, because a
    /// criterion listed twice is one the lead has to answer twice and one a
    /// count of "2 of 5 met" counts twice.
    /// </remarks>
    internal static IReadOnlyList<string>? Held(IReadOnlyList<string> standing, IReadOnlyList<string>? chosen)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> held = [.. standing.Concat(chosen ?? []).Where(one => seen.Add(one.Trim()))];

        return held.Count > 0 ? held : null;
    }

    /// <summary>
    /// A list of criteria as somebody typed it, with the typing taken off.
    /// </summary>
    /// <remarks>
    /// A person handed a numbered list in a box sends back a numbered list, and
    /// the number is not part of the criterion: "1. the tests pass" would be
    /// matched against a lead's coverage entry for "the tests pass" and miss.
    /// Blank lines go, because a box people edit acquires them.
    /// </remarks>
    private static IReadOnlyList<string>? Tidied(IEnumerable<string>? lines)
    {
        if (lines is null)
        {
            return null;
        }

        var kept = lines
            .Select(line => Unnumbered(line))
            .Where(line => line.Length > 0)
            .ToList();

        return kept.Count > 0 ? kept : null;
    }

    /// <summary>
    /// One line with any leading "1.", "2)", "-" or "*" taken off.
    /// </summary>
    /// <remarks>
    /// By hand rather than by pattern. What it has to strip is three fixed
    /// shapes at the front of a string, and the alternative here is a regular
    /// expression run over text a person typed - which is the shape that has
    /// already cost this codebase a timeout once, in the redactor.
    /// </remarks>
    private static string Unnumbered(string line)
    {
        var said = line.Trim();

        if (said.Length == 0)
        {
            return said;
        }

        if (said[0] is '-' or '*' or '•')
        {
            return said[1..].Trim();
        }

        var digits = 0;

        while (digits < said.Length && char.IsAsciiDigit(said[digits]))
        {
            digits++;
        }

        // A number on its own is a criterion nobody could check, but it is also
        // not a list marker, so it is left exactly as it was typed.
        return digits > 0 && digits < said.Length && said[digits] is '.' or ')'
            ? said[(digits + 1)..].Trim()
            : said;
    }

    /// <summary>
    /// What a lead is told when an ended run is picked up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two cases, told apart by whether the session actually reached the
    /// agent's command line rather than by whether one was recorded. An agent
    /// that cannot resume is started fresh with a warning, and a fresh lead
    /// handed "carry on" with no brief has nothing to carry on from.
    /// </para>
    /// <para>
    /// A lead resuming its own session remembers the run and is told only what
    /// changed: why it stopped, what it has spent, what the person said. A
    /// fresh one gets its whole brief and then where the run got to - the last
    /// report the lead before it wrote, and any work still waiting on the gate
    /// - so it carries on rather than asking for the same work again.
    /// </para>
    /// </remarks>
    internal static string Picking(
        RunResumption resuming,
        TeamRunRequest request,
        LaunchPlan plan,
        Brief brief,
        decimal? budget,
        IReadOnlyList<PendingAsk>? retired = null)
    {
        var summary = resuming.Summary;
        var own = resuming.LeadSession is { Length: > 0 } session
            && plan.Arguments.Contains(session, StringComparer.Ordinal);

        var text = new StringBuilder();

        if (!own)
        {
            text.AppendLine(Render(brief)).AppendLine();
        }

        text.AppendLine("## This run is being picked up again").AppendLine();

        text.AppendLine(
            $"It ended: {summary.Ended ?? "without saying why"}. So far it has taken {summary.Rounds} "
            + $"round(s) and spent ${summary.CostUsd:0.00}"
            + (budget is { } cap ? $" of a ${cap:0.00} budget" : string.Empty)
            + ".").AppendLine();

        if (request.ResumeMessage is { Length: > 0 } said)
        {
            text.AppendLine("The person running this team says:").AppendLine().AppendLine(said.Trim()).AppendLine();
        }

        if (!own)
        {
            text.AppendLine(
                "You are a new session. The lead before you worked in another one, so what it knew is "
                + "only what is written here and in the reports on disk.").AppendLine();

            if (resuming.LastReport is { Length: > 0 } last)
            {
                text.AppendLine("Its last report:").AppendLine()
                    .AppendLine("```json").AppendLine(last.Trim()).AppendLine("```").AppendLine();
            }
        }

        // Named, so a lead that still needs one of these asks it again rather
        // than guessing at the answer or quietly taking its own default. Only
        // its own: a node's permission question went with the node's turn.
        if (retired?.Where(one => one.Kind == "question").ToList() is { Count: > 0 } unanswered)
        {
            text.AppendLine(
                "These questions went unanswered when the run stopped, and have been closed. Ask again "
                + "if you still need an answer:").AppendLine();

            foreach (var one in unanswered)
            {
                text.AppendLine($"- {one.Question}");
            }

            text.AppendLine();
        }

        if (summary.Outstanding is { Count: > 0 } open)
        {
            text.AppendLine("Criteria not yet met:").AppendLine();

            foreach (var one in open)
            {
                text.AppendLine($"- {one.Criterion} ({one.Verdict})");
            }

            text.AppendLine();
        }

        if (resuming.Branches.Count > 0)
        {
            text.AppendLine("Work already done and waiting on the merge gate, which you do not need to ask for again:")
                .AppendLine();

            foreach (var (node, branch) in resuming.Branches)
            {
                text.AppendLine($"- {branch} ({node})");
            }

            text.AppendLine();
        }

        text.AppendLine(
            "Carry on from where it stopped rather than starting over. Decide what happens next: more "
            + "requests, or finish. Reply with one report/1 document. Keep summary short. Status done "
            + "needs evidence cited from your nodes' reports.");

        return text.ToString();
    }

    /// <summary>
    /// Asks the person watching whether a run that has spent its budget may
    /// have more, and how much. Not set ends the run.
    /// </summary>
    /// <remarks>
    /// Two raises offered, half as much again and double, rounded up to a
    /// whole dollar, and taking the cap off. An answer in the person's own
    /// words is read as the new budget when it is a figure above what has been
    /// spent - "40" or "$40" - or as no cap when it is
    /// <see cref="UsdCap.NoneWord" />, and as stopping when it is anything
    /// else, since money is the one thing here not worth guessing about.
    /// </remarks>
    internal static async Task<UsdCap> RaiseAsync(
        ITeamConsole console,
        decimal spent,
        decimal budget,
        CancellationToken ct)
    {
        var half = Math.Ceiling(Math.Max(budget * 1.5m, spent + 1m));
        var twice = Math.Ceiling(Math.Max(budget * 2m, half + 1m));

        string Offer(decimal usd) => $"Raise it to ${usd:0}";

        var chosen = await console.DecideAsync(
            new ReportQuestion(
                $"The run has spent ${spent:0.00} of its ${budget:0.00} budget. Give it more",
                [Offer(half), Offer(twice), TakeTheCapOff],
                Offer(half)),

            // The run's question rather than the lead's, so a page offers a
            // figure of its own and not sending it back to think again.
            GateKind.Budget,
            null,
            ct).ConfigureAwait(false);

        if (chosen is null)
        {
            return UsdCap.Unset;
        }

        if (chosen == Offer(half))
        {
            return UsdCap.Of(half);
        }

        if (chosen == Offer(twice))
        {
            return UsdCap.Of(twice);
        }

        if (chosen == TakeTheCapOff)
        {
            return UsdCap.NoCap;
        }

        return UsdCap.TryParse(chosen, out var typed) && (typed.None || typed.Usd > spent)
            ? typed
            : UsdCap.Unset;
    }

    /// <summary>The answer to <see cref="RaiseAsync" /> that lets the run carry on with no cap.</summary>
    internal const string TakeTheCapOff = "Take the cap off";

    /// <summary>
    /// What a run is held to before anything changes it: the run's own, else
    /// the team file's, else this machine's default.
    /// </summary>
    internal static UsdCap StartingCap(TeamRunRequest request, TeamDefinition team) =>
        request.Budget.Or(team.Rules.Budget.Cap).Or(request.MachineBudget);

    internal static async Task<bool> GateAsync(string autonomy, ITeamConsole console, string what, CancellationToken ct)
    {
        if (autonomy != "manual")
        {
            console.Note(what);

            return true;
        }

        return await console.ConfirmAsync(what, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The option that sends a question back to the lead rather than
    /// answering it.
    /// </summary>
    /// <remarks>
    /// For when none of the options is right and saying which one would be is
    /// the lead's job, not the person's: they have seen that the question is
    /// wrong without knowing what the right one is. The lead is told to look
    /// again and either decide it itself, with its evidence, or ask a better
    /// question - and it goes to the lead as words it can act on, because that
    /// is the only way anything reaches it.
    /// </remarks>
    public const string ThinkAgain = "Think again";

    /// <summary>What the lead's final report owes on each criterion.</summary>
    /// <remarks>
    /// One sentence in three places, so it is written once. 'understood' was
    /// added because a verdict is only worth the reading it was given: "the
    /// tests pass" read as "the new test passes" can be met while the suite is
    /// red, and a person reading "met" had no way to see which was meant.
    /// </remarks>
    internal const string CoverageOwed =
        "your final report carries one coverage entry per criterion, each saying in 'understood' "
        + "what you took the criterion to mean, with a verdict of met, unmet or not-attempted, "
        + "every met saying in 'because' which node, report and evidence shows it, and listing in "
        + "'delivered' the refs of the deliverables that meet it exactly as your nodes reported them";

    /// <summary>What every report of the lead's says about each criterion, before any verdict.</summary>
    internal const string ReadingsOwed =
        "every report you make carries 'readings': one entry per criterion below, repeating it exactly "
        + "and saying in 'reading', in a sentence or two, what you take it to mean. Give them before any "
        + "worker starts and restate them every round; change one only when your understanding has "
        + "changed, and say why in your summary";

    /// <summary>The answer that lets the run go on with the lead's readings as they stand.</summary>
    public const string AcceptReadings = "Accept these readings";

    /// <summary>What the lead is told when its readings were sent back.</summary>
    internal const string ReadingsSentBack =
        "Your readings of the criteria were sent back: the person did not think they were what they "
        + "meant, so no worker was briefed this round. Look at each criterion afresh, restate your "
        + "readings, and ask again for the work you need.";

    /// <summary>What every report of the lead's says about the goal.</summary>
    internal const string GoalReadingOwed =
        "every report you make says in 'goal_understood', in a sentence, what you take the goal to "
        + "mean, so the person who wrote it can see you are working to what they asked";

    /// <summary>What the lead is told when somebody chose <see cref="ThinkAgain"/>.</summary>
    internal const string ThinkAgainTold =
        "Think again. None of the options was chosen: the person asked to decide this did not "
        + "think any of them was right. Look at the question afresh, then either decide it yourself "
        + "with the evidence for your choice, or ask a better question with better options.";

    /// <summary>One answer as the lead reads it, saying who gave it.</summary>
    /// <remarks>
    /// The answer alone reads as the person's. Run 20260923-1216-be60 was
    /// autonomous, so its lead's own recommendation to merge was taken with
    /// nobody asked, and the lead's final report then told the person "the
    /// merge you approved can go ahead". The journal had it right; the lead
    /// had only ever been shown the answer.
    /// </remarks>
    internal static string Decided(string question, string answer, string by) => by switch
    {
        "person" => $"- {question}: **{answer}** (the person decided this)",
        "timed think again" => $"- {question}: **{answer}** (nobody answered in time, so it came back to "
            + "you rather than taking your recommendation: nobody chose anything)",
        _ => $"- {question}: **{answer}** (your own recommendation, taken because nobody answered: "
             + "nobody approved it, so do not say anybody did)",
    };

    /// <summary>The team's default done-when criteria, tidied, or null for none.</summary>
    internal static IReadOnlyList<string>? TeamDefaults(TeamDefinition team) =>
        Tidied(team.DoneWhen) is { Count: > 0 } defaults ? defaults : null;

    internal static string Spell(ReportStatus status) => status switch
    {
        ReportStatus.NeedsDecision => "needs-decision",
        _ => status.ToString().ToLowerInvariant(),
    };

    /// <summary>The last few lines an agent wrote to its error stream, as one line, or empty.</summary>
    internal static string Tail(IReadOnlyList<string> lines)
    {
        var kept = lines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(5)
            .Select(line => line.Trim());

        return string.Join(" | ", kept);
    }

    internal static string Safe(string node) =>
        string.Concat(node.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));

    internal static async Task WriteDocumentAsync(string directory, string name, string json, CancellationToken ct)
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
    internal sealed class Progress
    {
        private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

        private readonly JournalWriter _journal;
        private readonly string _node;
        private readonly TimeProvider _time;
        private DateTimeOffset _last = DateTimeOffset.MinValue;
        private string _said = string.Empty;

        public Progress(JournalWriter journal, string node, TimeProvider time)
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
}
