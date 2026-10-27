using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Runs a chosen set of tasks from a team's queue now, back to back, under
/// one cap shared between them.
/// </summary>
/// <remarks>
/// <para>
/// What approving an Ask plan does, and a command of its own so the terminal
/// can do the same. The tasks go to the front of the queue, so they are what
/// runs and nothing that was already waiting spends the cap. Each run is given
/// whatever is left of the cap as its own budget, and nothing more starts once
/// it is spent. That is what a team run's budget already means for one run,
/// and it has the same give: the agent checks its spend after each turn, so
/// the run that crosses the cap finishes that turn. Tried for real with $0.40
/// over two tasks, the first spent $0.32, the second was given $0.08 and
/// stopped at $0.19, and the batch ended at $0.51.
/// </para>
/// <para>
/// The limit is a window opened as the command starts, as the daemon's queue
/// windows are, so the queue's own rules decide what starts and when to stop.
/// </para>
/// </remarks>
[Description("Run the tasks given from a team's queue now, back to back, under one spending cap shared between them. They go to the front of the queue first, added when not already in it.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue run now batch start tasks cap budget approve plan", Mutates = true)]
public sealed class TeamQueueRunNowCommand : AsyncCommand<TeamQueueRunNowCommand.Settings>
{
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly ICommandCatalogue _commands;
    private readonly TeamQueueDriver _driver;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamQueueRunNowCommand(
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        ICommandCatalogue commands,
        TeamQueueDriver driver,
        TimeProvider time,
        IAnsiConsole console)
    {
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _commands = commands;
        _driver = driver;
        _time = time;
        _console = console;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandOption("--task <PROJECT/TASK>")]
        [Description("A task to run, as its project and id: talkwatch/fix-login. Give it once for each, in the order they should run.")]
        public string[] Tasks { get; init; } = [];

        [CommandOption("--usd <AMOUNT>")]
        [Description("What the whole batch may spend, in US dollars, or none for no cap. Required. The run that crosses it finishes its turn, so it can end a little over.")]
        public string? Usd { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is starting it. Defaults to this machine's user.")]
        public string? By { get; init; }

        public override ValidationResult Validate()
        {
            if (Tasks.Length == 0)
            {
                return ValidationResult.Error("Give at least one --task, as project/id.");
            }

            if (Tasks.FirstOrDefault(one => Split(one) is null) is { } bad)
            {
                return ValidationResult.Error($"'{bad}' is not project/id: give it as talkwatch/fix-login.");
            }

            // Blank parses as "not said", which would run the batch with no cap
            // at all; this command only runs under one somebody said out loud.
            if (!UsdCap.TryParse(Usd, out var cap) || !cap.IsSet)
            {
                return ValidationResult.Error(
                    $"Give --usd: the most the batch may spend, as a figure above zero, or '{UsdCap.NoneWord}' for no cap.");
            }

            return base.Validate();
        }
    }

    /// <summary>A task named as project/id, split, or null when it is not.</summary>
    internal static (string Project, string Task)? Split(string given)
    {
        var at = given.IndexOf('/', StringComparison.Ordinal);

        return at > 0 && at < given.Length - 1
            ? (given[..at].Trim(), given[(at + 1)..].Trim())
            : null;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (team, refused) = await TeamQueueing
            .TeamAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (team is null)
        {
            return output.Fail(refused!, ExitCode.ProjectNotFound);
        }

        UsdCap.TryParse(settings.Usd, out var cap);

        var by = settings.By is { Length: > 0 } who ? who : Environment.UserName;
        var items = new List<QueueItem>();
        var lists = new Dictionary<string, IReadOnlyList<TaskItem>?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (project, task) in settings.Tasks.Select(one => Split(one)!.Value))
        {
            var resolved = await _projects.ResolveAsync(project, cancellationToken).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return output.Fail(resolved);
            }

            var slug = resolved.Value!.Entry.Slug;

            if (!lists.ContainsKey(slug))
            {
                var listed = await _tasks.ListAsync(slug, cancellationToken).ConfigureAwait(false);

                lists[slug] = listed.Succeeded ? listed.Value : null;
            }

            items.Add(new QueueItem
            {
                Task = lists[slug] is { } known ? TaskRuns.Find(task, known)?.Id ?? task : task,
                Project = slug,
                AddedBy = by,
                AddedUtc = _time.GetUtcNow(),
            });
        }

        IReadOnlyList<TaskItem>? TasksOf(string project) => lists.TryGetValue(project, out var list) ? list : null;

        var capText = cap.Usd is { } figure ? Usd(figure) : "no cap";

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            if (QueueBatch.ToFront(current.Value!, items, TasksOf) is { } wouldRefuse)
            {
                return output.Fail(wouldRefuse, ExitCode.InvalidArguments);
            }

            output.WriteLine(
                $"[dim]Dry run: nothing was queued or started.[/] {items.Count} task(s) would go to the front of "
                + $"{Markup.Escape(QueueSettling.Whose(team))} queue and run back to back, sharing {capText}: "
                + Markup.Escape(string.Join(", ", items.Select(TeamQueueing.Describe))));

            return CommandOutput.Success();
        }

        var fronted = await _queues.UpdateAsync(team, queue => QueueBatch.ToFront(queue, items, TasksOf), cancellationToken)
            .ConfigureAwait(false);

        if (fronted.Failed)
        {
            return output.Fail(fronted);
        }

        // What ran before, settled first, so a run left recorded as going does
        // not hold the batch up.
        await _driver.SettleAsync(team, cancellationToken).ConfigureAwait(false);

        var opened = _time.GetUtcNow();
        var window = new QueueWindow(opened, cap.Usd, items.Count);
        var outcomes = new List<object>();
        var code = CommandOutput.Success();
        var stopped = string.Empty;

        output.WriteLine($"Running {items.Count} task(s) from {Markup.Escape(QueueSettling.Whose(team))} queue, sharing {capText}.");

        while (true)
        {
            var (item, run, decision, error) = await _driver.TakeAsync(team, window, by, cancellationToken).ConfigureAwait(false);

            if (error is not null)
            {
                stopped = error;
                code = (int)ExitCode.GeneralFailure;

                break;
            }

            if (item is null || run is null)
            {
                stopped = decision?.Reason ?? "Nothing more started.";

                break;
            }

            var spent = await SpentAsync(team, opened, cancellationToken).ConfigureAwait(false);

            var tasks = await _tasks.ListAsync(item.Project, cancellationToken).ConfigureAwait(false);
            var task = tasks.Succeeded ? TaskRuns.Find(item.Task, tasks.Value!) : null;

            string[] budget = cap.Usd is { } whole
                ? ["--usd", QueueBatch.Left(whole, spent).ToString("0.00", CultureInfo.InvariantCulture)]
                : ["--usd", UsdCap.NoneWord];

            output.WriteLine(
                $"Starting {Markup.Escape(TeamQueueing.Describe(item))} on branch {Markup.Escape(run.Branch)}"
                + (cap.Usd is { } shown ? $", with {Usd(QueueBatch.Left(shown, spent))} of {Usd(shown)} left." : "."));

            await _commands.RunAsync(
                "team run",
                [.. TeamQueueDriver.RunArguments(team, item, task, run.Worktree, autonomy: null, unattended: false), .. budget],
                cancellationToken).ConfigureAwait(false);

            var ended = await _driver.SettleAsync(team, CancellationToken.None).ConfigureAwait(false);

            if (ended.FirstOrDefault(one => QueueRules.Same(one.Task, item.Task) && one.StartedUtc == run.StartedUtc) is { } settled)
            {
                output.WriteLine(
                    $"{Markup.Escape(item.Task)}: {settled.State.ToString().ToLowerInvariant()}, {Usd(settled.CostUsd)}"
                    + (settled.Reason.Length > 0 ? $" - {Markup.Escape(settled.Reason)}" : string.Empty));

                outcomes.Add(new
                {
                    task = item.Task,
                    project = item.Project,
                    state = settled.State.ToString().ToLowerInvariant(),
                    costUsd = settled.CostUsd,
                    reason = settled.Reason,
                    branch = settled.Branch,
                    pullRequest = settled.PullRequest,
                });
            }
        }

        var total = await SpentAsync(team, opened, CancellationToken.None).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new { team, capUsd = cap.Usd, spentUsd = total, runs = outcomes, stopped });

            return code;
        }

        output.WriteLine($"[dim]Stopped: {Markup.Escape(stopped)} Spent {Usd(total)} of {capText}.[/]");

        return code;
    }

    /// <summary>What the team's queue has spent on runs started since the batch began.</summary>
    private async Task<decimal> SpentAsync(string team, DateTimeOffset opened, CancellationToken ct)
    {
        var loaded = await _queues.LoadAsync(team, ct).ConfigureAwait(false);

        return loaded.Succeeded
            ? loaded.Value!.Runs.Where(run => run.StartedUtc >= opened).Sum(run => run.CostUsd)
            : 0m;
    }

    private static string Usd(decimal usd) => usd.ToString("$#,##0.00", CultureInfo.InvariantCulture);
}
