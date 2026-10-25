using Loadout.Agents.Teams;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Teams.Steps;

/// <summary>
/// Everything a run settles before its context exists: what the lead is
/// briefed with, who it is, and what it is held to.
/// </summary>
internal sealed record Opening(
    string RunId,
    List<string> Warnings,
    TeamNode LeadNode,
    SpecialistDocument LeadRole,
    string TeamDirectory,
    bool FromTeam,
    IReadOnlyList<string>? Criteria,
    IReadOnlyList<string> Standing,
    Brief LeadBrief);

/// <summary>
/// Opens a run: refuses one that cannot run, works out the lead's brief, and
/// once the run has a context, writes it down, passes the first gate and
/// starts the lead.
/// </summary>
internal sealed class OpenRunStep
{
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly IChildLifetime? _lifetime;
    private readonly Core.Projects.IProjectService? _projects;
    private readonly NodeSessions _sessions;
    private readonly DeclareStep _declare;

    public OpenRunStep(
        IPlatformPaths paths,
        TimeProvider time,
        IChildLifetime? lifetime,
        Core.Projects.IProjectService? projects,
        NodeSessions sessions,
        DeclareStep declare)
    {
        _paths = paths;
        _time = time;
        _lifetime = lifetime;
        _projects = projects;
        _sessions = sessions;
        _declare = declare;
    }

    /// <summary>
    /// A run that cannot start, refused before anything is made; otherwise its
    /// autonomy and, for a run being picked up, what it is picked up from.
    /// </summary>
    public (OperationResult<TeamRunOutcome>? Refused, string Autonomy, RunResumption? Resuming) Check(TeamRunRequest request)
    {
        var team = request.Team;

        if (team.Template)
        {
            return (OperationResult<TeamRunOutcome>.Fail(
                $"'{team.Name}' is a template, a shape to copy rather than a team to run. "
                + $"Copy it with: loadout team new <name> --from {team.Name}",
                ExitCode.InvalidArguments), string.Empty, null);
        }

        var problems = TeamCatalogue.Check(team, request.Specialists)
            .Where(f => f.Severity == RuleFindingSeverity.Error)
            .Select(f => f.Detail)
            .ToList();

        if (problems.Count > 0)
        {
            return (OperationResult<TeamRunOutcome>.Fail(
                $"'{team.Name}' cannot run: {string.Join(" ", problems)}",
                ExitCode.ConfigurationInvalid), string.Empty, null);
        }

        var autonomy = (request.Autonomy ?? team.Rules.Autonomy).Trim().ToLowerInvariant();

        if (autonomy is not ("manual" or "supervised" or "autonomous"))
        {
            return (OperationResult<TeamRunOutcome>.Fail(
                $"'{autonomy}' is not an autonomy. It must be manual, supervised or autonomous.",
                ExitCode.InvalidArguments), string.Empty, null);
        }

        /*
          Something has to be able to stop this.

          A round limit is one ceiling and the budget is the other, and a run
          may have either. It may not have neither: the remaining stops are a
          lead that says done, a person who says stop, and two rounds that ask
          for nothing - and the last of those only catches a lead asking for
          nothing at all. A lead that keeps asking for one more thing trips
          none of them and spends until somebody notices.

          Every team that ships sets a budget, so this is about a team somebody
          wrote or edited. Refused here rather than in the command, because the
          schedules, the webhook and the dashboard all start runs without going
          near it.
        */
        //
        // No cap is allowed, but only said: 'none' in the run, the team file or
        // this machine's team-budget is somebody choosing it, where a missing
        // figure is somebody forgetting one.
        var said = (request.Resuming is { Length: > 0 } resumed ? RunControl.Cap(TeamRunner.RunDirectory(_paths, resumed)) : UsdCap.Unset)
            .Or(TeamRunner.StartingCap(request, team));

        if (request.MaxRounds <= 0 && !said.IsSet)
        {
            return (OperationResult<TeamRunOutcome>.Fail(
                $"Nothing would stop this run: '{team.Name}' sets no budget and no round limit "
                + "was given. Set one - 'budget: usd:' in the team, --usd, or --rounds - "
                + $"or say '{UsdCap.NoneWord}' for no cap.",
                ExitCode.InvalidArguments), string.Empty, null);
        }

        // Carrying on from another run starts a run of its own; picking one up
        // reopens the old one. Both at once would be two answers to one run.
        if (request.Follows is { Length: > 0 } follows)
        {
            if (request.Resuming is { Length: > 0 })
            {
                return (OperationResult<TeamRunOutcome>.Fail(
                    "A run is either picked up or carried on from, not both.", ExitCode.InvalidArguments), string.Empty, null);
            }

            var earlier = RunFollowing.Read(new RunJournal(_paths), follows);

            if (earlier.Failed)
            {
                return (OperationResult<TeamRunOutcome>.Fail(earlier.Error!, earlier.ExitCode), string.Empty, null);
            }
        }

        // Picking up an ended run rather than starting one. Read before
        // anything is made, so a run that cannot be picked up changes nothing.
        RunResumption? resuming = null;

        if (request.Resuming is { Length: > 0 } picking)
        {
            var (read, why) = RunResumption.Read(new RunJournal(_paths), picking);

            if (read is null)
            {
                return (OperationResult<TeamRunOutcome>.Fail(why!, ExitCode.InvalidArguments), string.Empty, null);
            }

            resuming = read;
        }

        return (null, autonomy, resuming);
    }

    /// <summary>
    /// The lead's brief and what it rests on, or the whole answer for a dry
    /// run, which goes no further.
    /// </summary>
    public async Task<(OperationResult<TeamRunOutcome>? Done, Opening? Opening)> PrepareAsync(
        TeamRunRequest request,
        string autonomy,
        RunResumption? resuming,
        CancellationToken ct)
    {
        var team = request.Team;

        var runId = resuming?.Summary.RunId
            ?? $"{_time.GetUtcNow():yyyyMMdd-HHmm}-{Guid.NewGuid().ToString("N")[..4]}";
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
        var teamDirectory = TeamRunner.TeamDirectory(_paths, team.Name);

        // What the lead is held to. With criteria it is the criteria, said
        // plainly enough that a lead reading its own brief knows the report it
        // owes - and ReportCheck then refuses a done that does not give it,
        // which is what makes this more than a sentence.
        //
        // The team's own, when whoever started the run gave none. Never both:
        // somebody who wrote criteria for this run has said what done means,
        // and the team's added on top would hold them to things they did not
        // ask for. With the team's, the lead is not asked to propose any -
        // the team's author already answered that question.
        var fromTeam = resuming is null
            && request.Criteria is not { Count: > 0 }
            && TeamRunner.TeamDefaults(team) is { Count: > 0 };

        var criteria = resuming?.Criteria
            ?? (request.Criteria is { Count: > 0 } asked ? asked : null)
            ?? (fromTeam ? TeamRunner.TeamDefaults(team) : null);

        // The project's, held on top of whichever of those was chosen and kept
        // apart from it. Kept apart because `criteria` being null is what asks
        // the lead to propose, and a project's generic hygiene - "the suite
        // passes" - is no answer to what this particular run is for. Frozen at
        // the start: a resumed run keeps what it began with, whatever the
        // manifest says now.
        var standing = resuming is not null
            ? resuming.Standing
            : [.. (request.Standing?.Applied ?? []).Select(one => one.Trim()).Where(one => one.Length > 0)];

        var leadBrief = TeamRunner.MakeBrief(
            runId, team.Lead, parent: null, leadNode, leadRole, request.Goal, inputs: [],
            doneWhen: criteria is null && standing.Count > 0
                ?
                [
                    "every criterion below is met, with the evidence cited from your nodes' reports",
                    TeamRunner.CoverageOwed,
                    TeamRunner.ReadingsOwed,

                    // The same question as below, put beside the project's
                    // criteria rather than replaced by them.
                    "your first report proposes, in 'proposed_done_when', what this run should "
                        + "actually be judged on beyond the project's standing criteria below: each "
                        + "one a thing somebody else could check, each one specific to this goal. "
                        + "Say what would have to be true, not that the goal is met",
                    TeamRunner.GoalReadingOwed,
                ]
                : criteria is null
                ?
                [
                    "the goal is met, with the evidence cited from your nodes' reports",

                    // Asked for before anything is briefed, because the answer
                    // decides what the rest of the run is for. The criterion
                    // above is the one this run has until somebody agrees
                    // better ones, and on its own it is unfalsifiable: it says
                    // the goal is met, the lead writes its own verdict on it,
                    // and a run that produced a design document for a goal
                    // somebody wanted code from was right by the only rule it
                    // had.
                    "your first report proposes, in 'proposed_done_when', what this run should "
                        + "actually be judged on: each one a thing somebody else could check, "
                        + "each one specific to this goal. Say what would have to be true, not "
                        + "that the goal is met",
                    TeamRunner.GoalReadingOwed,
                ]
                :
                [
                    "every criterion below is met, with the evidence cited from your nodes' reports",
                    TeamRunner.CoverageOwed,
                    TeamRunner.ReadingsOwed,
                    TeamRunner.GoalReadingOwed,
                ],
            team, autonomy, request.OutwardAllowed ?? [], request.Specialists.Find, teamDirectory,
            TeamRunner.Held(standing, criteria));

        if (request.DryRun)
        {
            var dry = await _sessions.StartAsync(
                    null, request, team, leadNode, leadRole, leadBrief, dryRun: true, ct, resuming?.LeadSession)
                .ConfigureAwait(false);

            if (dry.Failed)
            {
                return (OperationResult<TeamRunOutcome>.Fail(dry.Error!, dry.ExitCode), null);
            }

            await using var plan = dry.Value!;
            warnings.AddRange(plan.Warnings);

            return (OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
                runId, null, "dry run", null, 0m, 0, warnings, LeadPlan: plan.Plan)), null);
        }

        return (null, new Opening(runId, warnings, leadNode, leadRole, teamDirectory, fromTeam, criteria, standing, leadBrief));
    }

    /// <summary>
    /// Writes the run down, passes its first gate and starts the lead; or the
    /// run's answer, where it ends before the lead is going.
    /// </summary>
    public async Task<OperationResult<TeamRunOutcome>?> RunAsync(
        RunContext run,
        JournalWriter journal,
        bool fromTeam,
        CancellationToken ct)
    {
        var (request, team, console, autonomy, runId, directory) =
            (run.Request, run.Team, run.Console, run.Autonomy, run.RunId, run.Directory);
        var (leadNode, leadRole, leadBrief, criteria, standing, resuming) =
            (run.LeadNode, run.LeadRole, run.LeadBrief, run.Criteria, run.Standing, run.Resuming);

        // Resolved before the run is written down rather than after, because
        // which project a run worked on is the first thing somebody reading it
        // back needs and the journal had no way to say it. A machine with
        // several projects showed a list of runs nothing could attribute.
        var slug = await SlugAsync(request, ct).ConfigureAwait(false);
        var where = await PathAsync(request, ct).ConfigureAwait(false);

        if (resuming is null)
        {
            // Where a later 'team budget' or a raise will find and change it,
            // so a budget given for this run outlives the process that took it.
            if (request.Budget.IsSet)
            {
                await RunControl.SetCapAsync(directory, request.Budget, ct).ConfigureAwait(false);
            }

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
                    task = request.Task,
                    origin = request.Origin,
                    follows = request.Follows,

                    // What it may spend, so a page watching it can say where the
                    // spend stands rather than only what it has cost. No cap is
                    // said as such, so a reader can tell it from none given.
                    budget = TeamRunner.StartingCap(request, team).Usd,
                    uncapped = TeamRunner.StartingCap(request, team).None,

                    // Recorded so the run says, read back, that its questions
                    // did not all wait for a person.
                    takeRecommendationAfter = request.TakeRecommendationAfter is { } wait
                        ? TeamDuration.Spell(wait)
                        : null,
                },
                ct).ConfigureAwait(false);

            // The run carried on from names this one, so either end of the
            // link can be found from the other. Whatever was decided about it
            // stands; this adds only which run came of it. A run carried on
            // from by hand, with nothing decided, reads as carried on.
            if (request.Follows is { Length: > 0 } follows)
            {
                var journals = new RunJournal(_paths);
                var decided = journals.Summarise(follows).Value?.Resolved;

                new BlockedRunReader(journals).Record(
                    follows,
                    (decided ?? new RunResolution(_time.GetUtcNow(), BlockedActions.Spell(BlockedAction.CarryOn), Environment.UserName))
                        with { At = _time.GetUtcNow(), Next = runId },
                    again: true);
            }

            // Said once, at the start, so anybody reading the run back knows
            // these were the team's and not something the person typed.
            if (fromTeam)
            {
                await journal.WriteAsync(
                    "criteria.agreed", null, new { criteria, by = "team" }, ct).ConfigureAwait(false);
            }

            // Which of the project's criteria this run is held to and which
            // somebody left out, so a report can answer "why was this run held
            // to that?" - and a resume can keep exactly these.
            if (request.Standing is { } given && (standing.Count > 0 || given.Dropped.Count > 0))
            {
                await journal.WriteAsync(
                    "criteria.defaults",
                    null,
                    new
                    {
                        applied = standing,
                        dropped = given.Dropped,
                        by = given.Dropped.Count > 0 ? given.DroppedBy ?? "person" : null,
                    },
                    ct).ConfigureAwait(false);
            }
        }
        else
        {
            // A stop or a hold left from before it ended would end or hold it
            // again at the first check, before the lead said a word.
            RunControl.Resume(directory);
            RunControl.ClearStop(directory);

            await journal.WriteAsync(
                "run.reopened",
                null,
                new
                {
                    was = resuming.Summary.Ended,
                    session = resuming.LeadSession,
                    autonomy,
                    rounds = request.MaxRounds,
                    message = request.ResumeMessage,
                    budget = RunControl.Cap(directory).Or(TeamRunner.StartingCap(request, team)).Usd,
                    uncapped = RunControl.Cap(directory).Or(TeamRunner.StartingCap(request, team)).None,
                },
                ct).ConfigureAwait(false);

            // Where each question the run stopped on went, so somebody looking
            // for it finds it closed rather than gone.
            foreach (var ask in run.Retired)
            {
                await journal.WriteAsync(
                    "node.answered",
                    ask.Node,
                    new { tool = ask.Tool, allowed = false, by = NodePermissions.Retired, question = ask.Question },
                    ct).ConfigureAwait(false);
            }
        }

        if (!await TeamRunner.GateAsync(autonomy, console, $"Brief the lead ({leadNode.Role}) with the goal", ct).ConfigureAwait(false))
        {
            await journal.WriteAsync(
                "run.finished",
                null,
                new { ended = TeamRunner.Stopping, outcome = TeamRunner.Filed(TeamRunner.Stopping) },
                ct).ConfigureAwait(false);

            return OperationResult<TeamRunOutcome>.Ok(new TeamRunOutcome(
                runId, directory, TeamRunner.Stopping, null, 0m, 0, run.Warnings));
        }

        // Kept as it was on a resume: it is the brief the lead was given, and
        // what the run's record says it was asked.
        if (resuming is null)
        {
            await TeamRunner.WriteDocumentAsync(directory, $"brief-{TeamRunner.Safe(team.Lead)}.json", ReportReader.Write(leadBrief), ct).ConfigureAwait(false);
        }

        var started = await _sessions.StartAsync(
            run, request, team, leadNode, leadRole, leadBrief, dryRun: false, ct, resuming?.LeadSession).ConfigureAwait(false);

        if (started.Failed)
        {
            await journal.WriteAsync(
                "run.finished",
                null,
                new { ended = TeamRunner.Unstartable, outcome = TeamRunner.Filed(TeamRunner.Unstartable), error = started.Error },
                ct).ConfigureAwait(false);

            return OperationResult<TeamRunOutcome>.Fail(started.Error!, started.ExitCode);
        }

        var lead = started.Value!;
        run.Warnings.AddRange(lead.Warnings);

        await journal.WriteAsync(
            "node.launched",
            team.Lead,
            new
            {
                launch = lead.LaunchId,
                role = leadNode.Role,
                model = request.Model ?? (leadNode.Model is { Length: > 0 } pinned ? pinned : null),
                resumed = resuming is not null,
            },
            ct).ConfigureAwait(false);

        // Once the lead is actually going, and not before: a run somebody
        // stopped at the first gate did nothing, and a task list that
        // recorded it would be recording an intention.
        // A resume is not given the task again: it was written down when the
        // run started, and the run goes on updating the one it was for.
        var forTask = request.Task ?? resuming?.Summary.Task;

        await _declare.RunAsync(run, journal, slug, runId, forTask, team, TaskState.Doing, request.Goal, $"{autonomy}, running", ct)
            .ConfigureAwait(false);

        // Carried on from where it stopped on a resume, so the budget and any
        // round limit count what it has already spent and taken.
        run.Cost = resuming?.Summary.CostUsd ?? 0m;
        run.Rounds = resuming?.Summary.Rounds ?? 0;
        run.QuietRounds = 0;

        // The budget the run was last held to, so a change is written down
        // once rather than every round.
        run.HeldTo = resuming is null
            ? TeamRunner.StartingCap(request, team)
            : RunControl.Cap(directory).Or(TeamRunner.StartingCap(request, team));
        run.Final = null;

        // What the run put on branches of its own, and what the team's merge
        // gate nodes decided about it. Both are read at the end, when the
        // question is whether any of it may come back to the repository.
        run.Branches = new Dictionary<string, string>(
            resuming?.Branches ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        run.Decisions = new Dictionary<string, string>(
            resuming?.Decisions ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        run.AskedAboutTheGate = false;

        // Asked once per run, on the lead's first report. A lead that proposes
        // nothing is not asked again: the question was put and it declined to
        // answer, and asking every round would be badgering it with something
        // that costs a turn each time.
        run.Proposed = resuming?.Proposed ?? false;

        // What was agreed this round, for the feedback the lead reads next. It
        // has to be told: it proposed them, a person may have rewritten them,
        // and a lead held to criteria it has not seen cannot report coverage.
        run.AgreedThisRound = null;

        // How the lead last said it reads each criterion, so a change is
        // written down once rather than every round; and whether somebody has
        // already let workers start on those readings. A resumed run that had
        // them accepted is not asked again.
        run.LastReadings = null;
        run.ReadingsAccepted = resuming?.ReadingsAccepted ?? false;

        run.Slug = slug;
        run.ForTask = forTask;
        run.Lead = lead;

        return null;
    }

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

    /// <summary>
    /// The project's slug, or the handle as given when it cannot be resolved.
    /// </summary>
    private async Task<string> SlugAsync(TeamRunRequest request, CancellationToken ct)
    {
        if (_projects is null)
        {
            return request.ProjectHandle;
        }

        var resolution = await _projects.ResolveAsync(request.ProjectHandle, ct).ConfigureAwait(false);

        return resolution.Value?.Entry.Slug ?? request.ProjectHandle;
    }
}
