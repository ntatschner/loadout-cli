using System.Globalization;
using System.Text.Json;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Security;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Usage;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;

namespace Loadout.Core.Ask;

/// <summary>What sort of reply a request got.</summary>
public enum AskReplyKind
{
    /// <summary>Answered, and nothing needs doing.</summary>
    Answer,

    /// <summary>Something would start or change, set out to approve first.</summary>
    Plan,

    /// <summary>Not placed: the closest built-in requests are offered instead.</summary>
    Unplaced,
}

/// <summary>One model's share of a spend answer.</summary>
/// <param name="Model">The model, as the agent recorded it.</param>
/// <param name="Tokens">Every token it handled.</param>
/// <param name="Usd">Its estimate at list rates, or null when the model has no price.</param>
public sealed record AskModelSpend(string Model, long Tokens, decimal? Usd);

/// <summary>What was spent, as an estimate at list rates.</summary>
/// <param name="Period">The days counted.</param>
/// <param name="Project">The one project counted, or null for all of them.</param>
/// <param name="Estimate">The figure, and what could not be priced.</param>
/// <param name="Models">The figure by model, largest first.</param>
/// <param name="Caveat">Why the figure may be short, or null when every transcript was understood.</param>
public sealed record AskSpend(
    DateSpan Period,
    string? Project,
    SpendEstimate Estimate,
    IReadOnlyList<AskModelSpend> Models,
    string? Caveat);

/// <summary>What one step of a plan does.</summary>
public enum AskStepKind
{
    /// <summary>Add a task to a team's queue.</summary>
    QueueTask,

    /// <summary>Start the team on its queue.</summary>
    StartTeam,

    /// <summary>Open a terminal with a task loaded.</summary>
    OpenTerminal,
}

/// <summary>One step of a plan, as it would run.</summary>
/// <param name="Kind">What it does.</param>
/// <param name="Project">The project it acts on, when it acts on one.</param>
/// <param name="Task">The task it carries, when it carries one.</param>
/// <param name="Says">The step in words.</param>
/// <param name="Reasons">Why this task was picked, from the scorer.</param>
public sealed record AskPlanStep(
    AskStepKind Kind,
    string? Project,
    string? Task,
    string Says,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Work a request would start, set out step by step for somebody to approve.
/// </summary>
/// <param name="Shape">The kind of request it came from.</param>
/// <param name="Team">The team it uses, or null when one has to be chosen.</param>
/// <param name="TeamChoices">The teams it could use, when one has to be chosen.</param>
/// <param name="Steps">What would happen, in order.</param>
/// <param name="Capped">Whether this plan carries a spending cap: one that starts a team does. Interactive terminals do not, since somebody is there.</param>
/// <param name="Cap">The cap it starts with, from this machine's team-budget, or unset when there is none.</param>
/// <param name="Left">Tasks that would have been picked and could not be, each with why.</param>
public sealed record AskPlan(
    AskShape Shape,
    string? Team,
    IReadOnlyList<string> TeamChoices,
    IReadOnlyList<AskPlanStep> Steps,
    bool Capped,
    UsdCap Cap,
    IReadOnlyList<string> Left)
{
    /// <summary>
    /// What still has to be settled before the plan can be approved, or empty
    /// when it is ready.
    /// </summary>
    public IReadOnlyList<string> Needs
    {
        get
        {
            var needs = new List<string>();

            if (Shape == AskShape.TeamQueue && Team is null)
            {
                needs.Add(TeamChoices.Count == 0 ? "a team to run, and there are none" : "which team to use");
            }

            if (Capped && !Cap.IsSet)
            {
                needs.Add("a spending cap: team-budget is not set on this machine");
            }

            if (Steps.Count == 0)
            {
                needs.Add("something to do: no task qualified");
            }

            return needs;
        }
    }
}

/// <summary>Everything a request got back.</summary>
/// <param name="Query">What was asked, as typed.</param>
/// <param name="Kind">Answer, plan, or not placed.</param>
/// <param name="AnsweredBy">Which path answered: "loadout" for built-in logic, "agent" when the agent was used.</param>
/// <param name="Summary">The reply in a sentence or two.</param>
/// <param name="Request">The request as placed, or null when it was not.</param>
/// <param name="Spend">The spend answer, for a spend request.</param>
/// <param name="Tasks">Tasks listed or ranked, for a task request.</param>
/// <param name="Plan">The plan, for a request that starts something.</param>
/// <param name="NearMisses">The closest built-in requests, when nothing was placed.</param>
/// <param name="AgentOffer">What asking the agent instead would cost, roughly, when nothing was placed and an agent can be asked.</param>
/// <param name="AgentCostUsd">What the agent's turn cost, when the agent was asked. Never part of a plan's cap.</param>
/// <param name="AgentSession">The agent's conversation, for asking it the next request in the same one.</param>
public sealed record AskReply(
    string Query,
    AskReplyKind Kind,
    string AnsweredBy,
    string Summary,
    AskRequest? Request,
    AskSpend? Spend,
    IReadOnlyList<RankedTask> Tasks,
    AskPlan? Plan,
    IReadOnlyList<AskNearMiss> NearMisses,
    AskAgentOffer? AgentOffer = null,
    decimal? AgentCostUsd = null,
    string? AgentSession = null);

/// <summary>What asking the agent would cost, before it is asked.</summary>
/// <param name="EstimatedUsd">The guess.</param>
/// <param name="Basis">Where the guess comes from, in words.</param>
public sealed record AskAgentOffer(decimal EstimatedUsd, string Basis);

/// <summary>Answers a typed request with Loadout's own logic.</summary>
public interface IAskService
{
    /// <summary>Places a request and answers it, sets out a plan for it, or says what it came close to.</summary>
    /// <param name="query">What was typed.</param>
    /// <param name="ct">Cancels a scan of a large history.</param>
    Task<OperationResult<AskReply>> AskAsync(string query, CancellationToken ct = default);

    /// <summary>
    /// Asks the agent, because somebody chose to. Its request is run as one
    /// Loadout placed would be; it never acts.
    /// </summary>
    /// <param name="query">What was typed.</param>
    /// <param name="resumeSessionId">The agent's conversation to carry on, or null for a new one.</param>
    /// <param name="ct">Cancels the round.</param>
    Task<OperationResult<AskReply>> AskAgentAsync(string query, string? resumeSessionId, CancellationToken ct = default);
}

/// <summary>
/// Answers what it can from its own code, and sets out the rest as a plan.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here starts anything. A request that would queue, start or open
/// something comes back as a plan with every step written out, the reasons
/// each task was picked and the cap it would run under; carrying one out is
/// for whoever approves it. That keeps a misread request harmless: the worst
/// it can do is show the wrong plan.
/// </para>
/// <para>
/// A request that cannot be placed is written to a local log. Each one shows
/// where the parser should grow next, which is how the agent stays the
/// exception rather than the habit.
/// </para>
/// </remarks>
internal sealed class AskService : IAskService
{
    /// <summary>How many tasks a listing shows when no number was asked for.</summary>
    internal const int DefaultListed = 5;

    /// <summary>How many days a spend answer covers when no period was named.</summary>
    internal const int DefaultSpendDays = 7;

    private readonly IUsageService _usage;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IConfigurationService _configuration;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IPlatformPaths _paths;
    private readonly TimeProvider _time;
    private readonly IAskAgent? _agent;

    public AskService(
        IUsageService usage,
        ITaskService tasks,
        IProjectService projects,
        IConfigurationService configuration,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IPlatformPaths paths,
        TimeProvider time,
        IAskAgent? agent = null)
    {
        _agent = agent;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _usage = usage;
        _tasks = tasks;
        _projects = projects;
        _configuration = configuration;
        _paths = paths;
        _time = time;
    }

    /// <summary>Where requests that could not be placed are kept, one JSON object a line.</summary>
    internal static string UnplacedLog(IPlatformPaths paths) =>
        Path.Combine(paths.Paths.State, "ask", "unplaced.jsonl");

    /// <summary>What each agent turn cost, one JSON object a line, for the next estimate.</summary>
    internal static string AgentLog(IPlatformPaths paths) =>
        Path.Combine(paths.Paths.State, "ask", "agent-turns.jsonl");

    /// <summary>The guess offered before there is any history to go on.</summary>
    internal const decimal FirstGuessUsd = 0.25m;

    /// <summary>How many recent turns the estimate is taken from.</summary>
    internal const int EstimatedFrom = 10;

    /// <inheritdoc />
    public async Task<OperationResult<AskReply>> AskAsync(
        string query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (AskParser.Normalise(query).Length == 0)
        {
            return OperationResult<AskReply>.Fail("Ask something: how much you spent, which tasks to do, or what to start.", ExitCode.InvalidArguments);
        }

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<AskReply>.Fail(listed.Error!, listed.ExitCode);
        }

        var projects = listed.Value!
            .Select(project => (project.Entry.Slug, (IReadOnlyList<string>)[project.Entry.Name, .. project.Entry.Aliases]))
            .ToList();

        var teams = await TeamsAsync(ct).ConfigureAwait(false);

        var now = _time.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);

        var placement = AskParser.Place(query, new AskVocabulary(projects, teams), today);

        if (placement.Request is not { } request)
        {
            Remember(query, now);

            return OperationResult<AskReply>.Ok(new AskReply(
                query,
                AskReplyKind.Unplaced,
                "loadout",
                "Loadout can't answer that by itself. These are the closest requests it can answer.",
                null,
                null,
                [],
                null,
                placement.NearMisses,
                AgentOffer: _agent is null ? null : Estimate()));
        }

        return await RunAsync(query, request, projects, teams, now, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<OperationResult<AskReply>> AskAgentAsync(
        string query,
        string? resumeSessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (AskParser.Normalise(query).Length == 0)
        {
            return OperationResult<AskReply>.Fail("Ask something first.", ExitCode.InvalidArguments);
        }

        if (_agent is null)
        {
            return OperationResult<AskReply>.Fail("No agent can be asked from here.", ExitCode.AgentUnavailable);
        }

        var listed = await _projects.ListAsync(ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult<AskReply>.Fail(listed.Error!, listed.ExitCode);
        }

        var projects = listed.Value!
            .Select(project => (project.Entry.Slug, (IReadOnlyList<string>)[project.Entry.Name, .. project.Entry.Aliases]))
            .ToList();

        if (projects.Count == 0)
        {
            return OperationResult<AskReply>.Fail(
                "There are no projects registered, so there is nothing for the agent to read about.",
                ExitCode.ProjectNotFound);
        }

        var teams = await TeamsAsync(ct).ConfigureAwait(false);
        var now = _time.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var vocabulary = new AskVocabulary(projects, teams);

        // Its tools read about one project: the one the request names, when
        // the parser found one, or else the one used most recently, which the
        // project list puts first.
        var about = AskParser.Place(query, vocabulary, today).Request?.Project ?? projects[0].Slug;

        var open = (await OpenTasksAsync(null, projects, ct).ConfigureAwait(false))
            .Select(one => new AskTaskLine(one.Project, one.Task.Id, one.Task.Title, one.Task.DoneWhen.Count))
            .ToList();

        var turned = await _agent.AskAsync(query, new AskAgentContext(vocabulary, today, about, open), resumeSessionId, ct)
            .ConfigureAwait(false);

        if (turned.Failed)
        {
            return OperationResult<AskReply>.Fail(turned.Error!, turned.ExitCode);
        }

        var turn = turned.Value!;

        Spent(turn.CostUsd, now);

        if (turn.Request is not { } request)
        {
            return OperationResult<AskReply>.Ok(new AskReply(
                query, AskReplyKind.Answer, "agent", turn.Answer ?? string.Empty, null, null, [], null, [],
                AgentCostUsd: turn.CostUsd, AgentSession: turn.SessionId));
        }

        var ran = await RunAsync(query, request, projects, teams, now, ct).ConfigureAwait(false);

        return ran.Failed
            ? ran
            : OperationResult<AskReply>.Ok(ran.Value! with
            {
                AnsweredBy = "agent",
                AgentCostUsd = turn.CostUsd,
                AgentSession = turn.SessionId,
            });
    }

    /// <summary>Runs a placed request, whoever placed it.</summary>
    private async Task<OperationResult<AskReply>> RunAsync(
        string query,
        AskRequest request,
        List<(string Slug, IReadOnlyList<string> Names)> projects,
        IReadOnlyList<string> teams,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.DateTime);

        return request.Shape switch
        {
            AskShape.Spend => await SpendAsync(query, request, today, ct).ConfigureAwait(false),
            AskShape.Tasks => await TasksAsync(query, request, projects, now, ct).ConfigureAwait(false),
            _ => await PlanAsync(query, request, projects, teams, now, ct).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// What the next agent turn will cost, roughly: the middle of the last
    /// few, or a first guess until there are any.
    /// </summary>
    internal AskAgentOffer Estimate()
    {
        var costs = new List<decimal>();

        try
        {
            if (File.Exists(AgentLog(_paths)))
            {
                foreach (var line in File.ReadLines(AgentLog(_paths)).TakeLast(EstimatedFrom))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(line);

                        if (document.RootElement.TryGetProperty("costUsd", out var cost) && cost.TryGetDecimal(out var usd))
                        {
                            costs.Add(usd);
                        }
                    }
                    catch (JsonException)
                    {
                        // A torn line is one fewer turn to go on, nothing more.
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            costs.Clear();
        }

        if (costs.Count == 0)
        {
            return new AskAgentOffer(FirstGuessUsd, "a first guess, until the agent has been asked a few times");
        }

        costs.Sort();

        var middle = costs.Count % 2 == 1
            ? costs[costs.Count / 2]
            : (costs[(costs.Count / 2) - 1] + costs[costs.Count / 2]) / 2;

        return new AskAgentOffer(middle, $"the middle of the last {costs.Count} turn(s)");
    }

    /// <summary>Notes what an agent turn cost. Best effort: losing one only makes the next guess rougher.</summary>
    private void Spent(decimal costUsd, DateTimeOffset now)
    {
        try
        {
            var log = AgentLog(_paths);

            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, JsonSerializer.Serialize(new { utc = now.ToUniversalTime(), costUsd }) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The estimate goes on what it has.
        }
    }

    private async Task<OperationResult<AskReply>> SpendAsync(
        string query,
        AskRequest request,
        DateOnly today,
        CancellationToken ct)
    {
        var period = request.Period ?? AskDates.LastDays(today, DefaultSpendDays);

        var report = await _usage.ReportAsync(
            new UsageQuery(ProjectSlug: request.Project, From: period.From, Until: period.Until),
            ct).ConfigureAwait(false);

        if (report.Failed)
        {
            return OperationResult<AskReply>.Fail(report.Error!, report.ExitCode);
        }

        var estimate = PriceTable.Estimate(report.Value!.Models);

        var models = report.Value.Models
            .Where(model => model.Totals.Total > 0)
            .Select(model => new AskModelSpend(model.Name, model.Totals.Total, PriceTable.Price(model.Name, model.Totals)))
            .OrderByDescending(model => model.Usd ?? 0m)
            .ToList();

        var scope = request.Project is null ? "across all projects" : $"on {request.Project}";

        var summary = estimate.Usd == 0m && estimate.UnpricedTokens == 0
            ? $"Nothing recorded {scope} for {period.Label}."
            : $"About {Usd(estimate.Usd)} {scope} for {period.Label}, as a list-rate estimate: "
                + "token counts from every session and team run, priced at Anthropic's published API rates. "
                + "A subscription does not charge these rates.";

        if (estimate.UnpricedTokens > 0)
        {
            summary += $" {estimate.UnpricedTokens:N0} tokens from {string.Join(", ", estimate.UnpricedModels)} have no price here and are left out.";
        }

        return OperationResult<AskReply>.Ok(new AskReply(
            query,
            AskReplyKind.Answer,
            "loadout",
            summary,
            request with { Period = period },
            new AskSpend(period, request.Project, estimate, models, report.Value.Integrity.Caveat),
            [],
            null,
            []));
    }

    private async Task<OperationResult<AskReply>> TasksAsync(
        string query,
        AskRequest request,
        List<(string Slug, IReadOnlyList<string> Names)> projects,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var open = await OpenTasksAsync(request.Project, projects, ct).ConfigureAwait(false);
        var ranked = Ranked(request, open, request.Pick, now).Take(request.Count ?? request.Named?.Count ?? DefaultListed).ToList();

        var scope = request.Project is null ? "across all projects" : $"on {request.Project}";
        var how = request.Pick switch
        {
            TaskPick.Easy => "easiest first",
            TaskPick.NeedsWorkFirst => "what needs work first at the top",
            _ => "longest waiting first",
        };

        var summary = ranked.Count == 0
            ? $"No open tasks {scope}."
            : $"{ranked.Count} of {open.Count} open tasks {scope}, {how}. The ranking is a heuristic; each pick says why.";

        return OperationResult<AskReply>.Ok(new AskReply(
            query, AskReplyKind.Answer, "loadout", summary, request, null, ranked, null, []));
    }

    private async Task<OperationResult<AskReply>> PlanAsync(
        string query,
        AskRequest request,
        List<(string Slug, IReadOnlyList<string> Names)> projects,
        IReadOnlyList<string> teams,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var open = await OpenTasksAsync(request.Project, projects, ct).ConfigureAwait(false);
        var pick = request.Pick == TaskPick.Any
            ? (request.Shape == AskShape.TeamQueue ? TaskPick.Easy : TaskPick.NeedsWorkFirst)
            : request.Pick;

        var ranked = Ranked(request, open, pick, now);
        var wanted = request.Named?.Count ?? request.Count ?? (request.Shape == AskShape.TeamQueue ? 3 : 1);

        var steps = new List<AskPlanStep>();
        var left = new List<string>();

        if (request.Shape == AskShape.TeamQueue)
        {
            var team = request.Team ?? (teams.Count == 1 ? teams[0] : null);
            var name = team ?? "the team";

            foreach (var one in ranked)
            {
                if (steps.Count == wanted)
                {
                    break;
                }

                // The queue refuses a task with no stated finish line, so it
                // is left out here rather than shown and then refused.
                if (one.Task.DoneWhen.Count == 0)
                {
                    left.Add($"{one.Task.Id} on {Where(one.Project)}: no done-when, which a team's queue requires");

                    continue;
                }

                steps.Add(new AskPlanStep(
                    AskStepKind.QueueTask,
                    one.Project,
                    one.Task.Id,
                    $"Queue {one.Task.Id} on {Where(one.Project)} for {name}: {one.Task.Title}",
                    one.Reasons));
            }

            if (request.Start && steps.Count > 0)
            {
                steps.Add(new AskPlanStep(AskStepKind.StartTeam, null, null, $"Start {name} on its queue", []));
            }

            var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);
            var cap = machine.Succeeded && UsdCap.TryParse(machine.Value!.Teams.Budget, out var said) ? said : UsdCap.Unset;

            // Only starting spends anything: tasks that are only queued wait for
            // the queue's own window and its limits.
            var plan = new AskPlan(AskShape.TeamQueue, team, team is null ? teams : [], steps, Capped: request.Start, cap, left);

            return OperationResult<AskReply>.Ok(new AskReply(
                query, AskReplyKind.Plan, "loadout", PlanSummary(plan, wanted), request, null, [], plan, []));
        }

        // A terminal per project where there are enough of them, so two
        // sessions are not working in one checkout at once.
        var chosen = new List<RankedTask>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var one in ranked.Where(one => one.Project.Length > 0))
        {
            if (chosen.Count < wanted && used.Add(one.Project))
            {
                chosen.Add(one);
            }
        }

        foreach (var one in ranked.Where(one => one.Project.Length > 0 && !chosen.Contains(one)))
        {
            if (chosen.Count < wanted)
            {
                chosen.Add(one);
            }
        }

        foreach (var one in chosen)
        {
            var reasons = one.Reasons.ToList();

            if (chosen.Count(other => string.Equals(other.Project, one.Project, StringComparison.OrdinalIgnoreCase)) > 1)
            {
                reasons.Add($"shares {one.Project}'s checkout with another terminal here");
            }

            steps.Add(new AskPlanStep(
                AskStepKind.OpenTerminal,
                one.Project,
                one.Task.Id,
                $"Open a terminal on {one.Project} with {one.Task.Id}: {one.Task.Title}",
                reasons));
        }

        var terminals = new AskPlan(AskShape.Terminals, null, [], steps, Capped: false, UsdCap.Unset, left);

        return OperationResult<AskReply>.Ok(new AskReply(
            query, AskReplyKind.Plan, "loadout", PlanSummary(terminals, wanted), request, null, [], terminals, []));
    }

    /// <summary>
    /// The teams that can be run from anywhere: the built-ins and the
    /// workspace's own. Templates are left out, being shapes to copy rather
    /// than teams to run.
    /// </summary>
    private async Task<IReadOnlyList<string>> TeamsAsync(CancellationToken ct)
    {
        var root = _workspace.IsAvailable() ? _workspace.LocalPath : null;

        var specialists = await _library.LoadAsync(root, null, ct).ConfigureAwait(false);
        var catalogue = await _teams.LoadAsync(root, null, specialists, ct).ConfigureAwait(false);

        return catalogue.Teams.Values
            .Where(team => !team.Template)
            .Select(team => team.Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The tasks a request is about, best first: the ones it names, in its
    /// order, when it names any, or else the scorer's ranking. Named ones still
    /// carry the scorer's reasons, so the card says the same things about them.
    /// </summary>
    private static IReadOnlyList<RankedTask> Ranked(
        AskRequest request,
        List<(string Project, TaskItem Task)> open,
        TaskPick pick,
        DateTimeOffset now)
    {
        if (request.Named is not { Count: > 0 } named)
        {
            return TaskScorer.Rank(open, pick, now);
        }

        return named
            .Select(one => open.FirstOrDefault(task =>
                string.Equals(task.Project, one.Project, StringComparison.OrdinalIgnoreCase)
                && string.Equals(task.Task.Id, one.Task, StringComparison.OrdinalIgnoreCase)))
            .Where(found => found.Task is not null)
            .Select(found => TaskScorer.Score(found.Project, found.Task, pick, now))
            .Select(scored => scored with { Reasons = ["named in the request", .. scored.Reasons] })
            .ToList();
    }

    /// <summary>Open tasks on one project's list or on every project's, with the project each is on.</summary>
    private async Task<List<(string Project, TaskItem Task)>> OpenTasksAsync(
        string? project,
        List<(string Slug, IReadOnlyList<string> Names)> projects,
        CancellationToken ct)
    {
        var open = new List<(string, TaskItem)>();

        foreach (var slug in project is null ? projects.Select(one => one.Slug) : [project])
        {
            var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

            // One unreadable list must not cost somebody every other project's.
            if (listed.Failed)
            {
                continue;
            }

            open.AddRange(listed.Value!
                .Where(task => task.State == TaskState.Open && task.Kind == TaskKind.Task)
                .Select(task => (slug, task)));
        }

        return open;
    }

    private static string PlanSummary(AskPlan plan, int wanted)
    {
        var picked = plan.Steps.Count(step => step.Task is not null);
        var what = plan.Shape == AskShape.TeamQueue ? "queue" : "open";

        var summary = picked == wanted
            ? $"Here is what would happen. Nothing starts until you approve it."
            : $"Only {picked} of the {wanted} asked for qualified. Nothing starts until you approve it.";

        if (plan.Needs.Count > 0)
        {
            summary += $" Before it can {what} anything it needs {string.Join(" and ", plan.Needs)}.";
        }

        return summary;
    }

    private static string Where(string project) => project.Length == 0 ? "the workspace list" : project;

    private static string Usd(decimal usd) =>
        usd.ToString(usd < 1m ? "$0.00" : "$#,##0.00", CultureInfo.InvariantCulture);

    /// <summary>Notes a request that could not be placed. Best effort: losing one costs nothing but a hint.</summary>
    private void Remember(string query, DateTimeOffset now)
    {
        try
        {
            var log = UnplacedLog(_paths);

            Directory.CreateDirectory(Path.GetDirectoryName(log)!);

            // Redacted, because people paste things into a search box that
            // were never meant to be kept.
            File.AppendAllText(
                log,
                JsonSerializer.Serialize(new { utc = now.ToUniversalTime(), query = SecretRedactor.Redact(query) }) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // A hint for the parser's next phrasing, not something to fail a
            // reply over. Text the redactor cannot get through is not kept.
        }
    }
}
