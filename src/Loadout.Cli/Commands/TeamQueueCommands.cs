using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Delivery;
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

/// <summary>What every queue command is given: the team whose queue it is.</summary>
public class TeamQueueSettings : TeamSettings
{
    [CommandArgument(0, "<TEAM>")]
    [Description("The team whose queue it is, as 'team list' shows it.")]
    public string Team { get; init; } = string.Empty;
}

/// <summary>What the queue commands share: finding the team, and reading task lists.</summary>
internal static class TeamQueueing
{
    /// <summary>The team's name as its queue is kept under, or why there is no such team.</summary>
    /// <remarks>
    /// Checked against the catalogue so a queue can't be filled for a team
    /// that does not exist, which would wait in a file for a run that can
    /// never start.
    /// </remarks>
    public static async Task<(string? Team, string? Refused)> TeamAsync(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        TeamQueueSettings settings,
        CancellationToken ct)
    {
        var (catalogue, _, _) = await TeamLoading
            .LoadAsync(teams, library, workspace, projects, settings, ct)
            .ConfigureAwait(false);

        return catalogue.Find(settings.Team) switch
        {
            null => (null, $"No team named '{settings.Team}'. See what there is with: loadout team list"),
            { Template: true } found => (null, $"'{found.Name}' is a template, a shape to copy rather than a team to run."),
            var found => (found.Name, null),
        };
    }

    /// <summary>Every project's task list a queue names, read once each.</summary>
    /// <remarks>A list that can't be read is null, which the rules turn into a reason rather than a crash.</remarks>
    public static async Task<Func<string, IReadOnlyList<TaskItem>?>> ListsAsync(
        ITaskService tasks,
        TeamQueue queue,
        CancellationToken ct)
    {
        var read = new Dictionary<string, IReadOnlyList<TaskItem>?>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in queue.Items.Select(one => one.Project).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var listed = await tasks.ListAsync(project, ct).ConfigureAwait(false);

            read[project] = listed.Succeeded ? listed.Value : null;
        }

        return project => read.TryGetValue(project, out var list) ? list : null;
    }

    /// <summary>
    /// Every list in the workspace, for the requires guard; or null when the
    /// workspace has turned the guard off, so nothing is held for it.
    /// </summary>
    /// <remarks>
    /// A list that cannot be read reads as null, which holds any task that
    /// requires one on it: an unreadable list is no reason to let work start.
    /// </remarks>
    public static async Task<TaskRequires.Lists?> RequiresAsync(
        ITaskService tasks,
        Core.Configuration.IConfigurationService? configuration,
        CancellationToken ct)
    {
        if (configuration is not null
            && (await configuration.LoadConfigAsync(ct).ConfigureAwait(false)).Value is { Workspace.RequiresGuard: false })
        {
            return null;
        }

        var every = await tasks.EveryListAsync(ct).ConfigureAwait(false);
        var lists = every.Value ?? new Dictionary<string, IReadOnlyList<TaskItem>>();

        return key => lists.TryGetValue(key, out var list) ? list : null;
    }

    /// <summary>One item, in a line.</summary>
    public static string Describe(QueueItem item) =>
        $"{item.Task} on {item.Project}" + (item.After.Length > 0 ? $", after {item.After}" : string.Empty)
        + (item.PrMode.Length > 0 ? $", pull request {item.PrMode}" : string.Empty);

    /// <summary>The item a task names, by its id and, when given, its project.</summary>
    public static QueueItem? Find(TeamQueue queue, string task, string? project) =>
        queue.Items.FirstOrDefault(one =>
            QueueRules.Same(one.Task, task) && (project is null || QueueRules.Same(one.Project, project)));

    /// <summary>The project a command names, resolved, or null when none was given.</summary>
    public static async Task<(string? Slug, string? Refused)> ProjectAsync(
        IProjectService projects,
        TeamQueueSettings settings,
        CancellationToken ct)
    {
        if (settings.Project is null && settings.Repo is null)
        {
            return (null, null);
        }

        var resolution = await ProjectHandle.ResolveAsync(projects, settings.Project, settings.Repo, ct).ConfigureAwait(false);

        return resolution.Succeeded ? (resolution.Value!.Entry.Slug, null) : (null, resolution.Error);
    }
}

/// <summary>Adds a fully specified task to a team's queue.</summary>
/// <remarks>
/// Admission is checked here, while somebody is there to fix what it finds,
/// and again when the task is about to run, because the task can change in
/// between.
/// </remarks>
[Description("Add a task to a team's queue. Only a task with a title and at least one done-when gets in.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue add enqueue overnight backlog unattended", Mutates = true)]
public sealed class TeamQueueAddCommand : AsyncCommand<TeamQueueAddCommand.Settings>
{
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamQueueAddCommand(
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console,
        TimeProvider time)
    {
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
        _time = time;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandArgument(1, "<TASK>")]
        [Description("The task's id on the project's list.")]
        public string Task { get; init; } = string.Empty;

        [CommandOption("--after <TASK>")]
        [Description("A task on the same project this one waits for, so the pieces of a split plan run in order.")]
        public string? After { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is adding it. Defaults to this machine's user.")]
        public string? By { get; init; }

        [CommandOption("--pr-mode <MODE>")]
        [Description("draft, ready or none: what this task's run does about a pull request when it meets its done-when, over the queue's and the project's.")]
        public string? PrMode { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (!PullRequestModes.TryParse(settings.PrMode, out var mode))
        {
            return output.Fail(PullRequestModes.Refusal(settings.PrMode!), ExitCode.InvalidArguments);
        }

        var (team, refused) = await TeamQueueing
            .TeamAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (team is null)
        {
            return output.Fail(refused!, ExitCode.ProjectNotFound);
        }

        var listed = await _tasks.ListAsync(slug, cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var item = new QueueItem
        {
            Task = TaskRuns.Find(settings.Task, listed.Value!)?.Id ?? settings.Task.Trim(),
            Project = slug,
            After = settings.After is { Length: > 0 } after
                ? TaskRuns.Find(after, listed.Value!)?.Id ?? after.Trim()
                : string.Empty,
            AddedBy = settings.By is { Length: > 0 } who ? who : Environment.UserName,
            AddedUtc = _time.GetUtcNow(),
            PrMode = mode is { } chosen ? PullRequestModes.Word(chosen) : string.Empty,
        };

        string? Admit(TeamQueue queue)
        {
            if (TeamQueueing.Find(queue, item.Task, item.Project) is not null)
            {
                return $"'{item.Task}' on {item.Project} is already in {QueueSettling.Whose(team)} queue.";
            }

            if (queue.Runs.Any(run => QueueRules.Same(run.Task, item.Task) && QueueRules.Same(run.Project, item.Project)
                    && run.State is QueueRunState.Running or QueueRunState.Held))
            {
                return $"'{item.Task}' on {item.Project} already has a queued run going.";
            }

            return QueueRules.Admission(item, listed.Value!, queue);
        }

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            if (Admit(current.Value!) is { } wouldRefuse)
            {
                return output.Fail(wouldRefuse, ExitCode.InvalidArguments);
            }

            output.WriteLine(
                $"[dim]Dry run: nothing was added.[/] {Markup.Escape(TeamQueueing.Describe(item))} would join "
                + $"{Markup.Escape(QueueSettling.Whose(team))} queue at {current.Value!.Items.Count + 1}.");

            return CommandOutput.Success();
        }

        var saved = await _queues.UpdateAsync(
            team,
            queue =>
            {
                if (Admit(queue) is { } no)
                {
                    return no;
                }

                queue.Items.Add(item);

                return null;
            },
            cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                team,
                task = item.Task,
                project = item.Project,
                after = item.After,
                prMode = item.PrMode.Length > 0 ? item.PrMode : null,
                position = saved.Value!.Items.Count,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(TeamQueueing.Describe(item))} is number {saved.Value!.Items.Count} "
            + $"in {Markup.Escape(QueueSettling.Whose(team))} queue.");
        output.WriteLine($"[dim]Run it now with:[/] loadout team queue run-next {Markup.Escape(team)}");

        return CommandOutput.Success();
    }
}

/// <summary>What is in a team's queue, what would be refused, and what it has run.</summary>
[Description("Show a team's queue: what waits, in order, what would be refused and why, and what it has run.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue list show what is queued overnight")]
public sealed class TeamQueueListCommand : AsyncCommand<TeamQueueSettings>
{
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TeamQueueListCommand(
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console,
        IScheduleService schedules,
        TimeProvider time)
    {
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
        _schedules = schedules;
        _time = time;
    }

    private readonly IScheduleService _schedules;
    private readonly TimeProvider _time;

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TeamQueueSettings settings, CancellationToken cancellationToken)
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

        var loaded = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return output.Fail(loaded);
        }

        var queue = loaded.Value!;
        var lists = await TeamQueueing.ListsAsync(_tasks, queue, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var scheduled = await _schedules.ListAsync(cancellationToken).ConfigureAwait(false);
        var window = scheduled.Value?.FirstOrDefault(one => one.IsQueueWindow
            && string.Equals(one.Team, team, StringComparison.OrdinalIgnoreCase));

        // Each item judged as it would be at its turn, so the list says now
        // what would otherwise be found out at three in the morning.
        var judged = queue.Items
            .Select(item => (Item: item, Refused: lists(item.Project) is { } tasks
                ? QueueRules.Admission(item, tasks, Without(queue, item))
                : $"{item.Project}'s task list could not be read."))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                team,
                items = judged.Select((one, index) => new
                {
                    position = index + 1,
                    task = one.Item.Task,
                    project = one.Item.Project,
                    after = one.Item.After.Length > 0 ? one.Item.After : null,
                    by = one.Item.AddedBy,
                    added = one.Item.AddedUtc,
                    refused = one.Refused,
                }),
                runs = queue.Runs.Select(run => new
                {
                    task = run.Task,
                    project = run.Project,
                    run = run.RunId.Length > 0 ? run.RunId : null,
                    state = run.State.ToString().ToLowerInvariant(),
                    reason = run.Reason.Length > 0 ? run.Reason : null,
                    costUsd = run.CostUsd,
                    started = run.StartedUtc,
                    ended = run.EndedUtc,
                    by = run.StartedBy,
                }),
                settings = Json(queue.Settings),
                window = window is null ? null : new
                {
                    at = window.At?.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    lengthMinutes = (int)(window.Length ?? TimeSpan.Zero).TotalMinutes,
                    autonomy = window.Autonomy,
                    open = ScheduleService.Window(window, now) is not null,
                    next = ScheduleService.Next(window, now),
                },
                windowOpened = queue.WindowOpened,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(QueueSettling.Whose(team))} queue[/]  [dim]{Markup.Escape(Spell(queue.Settings))}[/]");
        output.WriteLine(window is null
            ? $"[dim]No window: its runs start only with[/] loadout team queue run-next {Markup.Escape(team)}"
            : Markup.Escape(TeamScheduleAddCommand.Describe(window, now)));

        if (judged.Count == 0)
        {
            output.WriteLine("Nothing is waiting.");
            output.WriteLine("[dim]Add a task with:[/] loadout team queue add " + Markup.Escape(team) + " <task> --project <slug>");
        }

        foreach (var (one, index) in judged.Select((one, index) => (one, index)))
        {
            output.WriteLine(
                $"  {index + 1,2}. {Markup.Escape(TeamQueueing.Describe(one.Item))}"
                + (one.Refused is { } why ? $"\n      [yellow]would be refused:[/] {Markup.Escape(why)}" : string.Empty));
        }

        if (queue.Runs.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Runs it started[/]");

            foreach (var run in queue.Runs.AsEnumerable().Reverse().Take(10))
            {
                output.WriteLine(
                    $"  {run.StartedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {run.State.ToString().ToLowerInvariant(),-8} "
                    + $"{Markup.Escape(run.Task)} on {Markup.Escape(run.Project)}"
                    + (run.RunId.Length > 0 ? $"  [dim]{Markup.Escape(run.RunId)}[/]" : string.Empty)
                    + (run.CostUsd > 0 ? $"  ${run.CostUsd:0.00}" : string.Empty)
                    + (run.Reason.Length > 0 ? $"\n      [dim]{Markup.Escape(run.Reason)}[/]" : string.Empty));
            }
        }

        return CommandOutput.Success();
    }

    internal static TeamQueue Without(TeamQueue queue, QueueItem item) => new()
    {
        Items = [.. queue.Items.Where(one => !ReferenceEquals(one, item))],
        Runs = queue.Runs,
        Settings = queue.Settings,
    };

    internal static object Json(QueueSettings settings) => new
    {
        windowUsd = settings.WindowUsd.Length > 0 ? settings.WindowUsd : null,
        windowRuns = settings.WindowRuns,
        heldCap = settings.HeldCap,
        draftPullRequests = PullRequestModes.OfQueue(settings) == PullRequestMode.Draft,
        prMode = PullRequestModes.OfQueue(settings) is { } mode ? PullRequestModes.Word(mode) : null,
    };

    internal static string Spell(QueueSettings settings) =>
        string.Join(
            ", ",
            (settings.WindowUsd.Length > 0 ? $"${settings.WindowUsd} a window" : "no spending cap of its own"),
            (settings.WindowRuns is { } most ? $"{most} run(s) a window" : "no cap on runs"),
            $"at most {settings.HeldCap} held",
            PullRequestModes.OfQueue(settings) switch
            {
                PullRequestMode.Draft => "may open draft pull requests",
                PullRequestMode.Ready => "may open pull requests ready for review",
                PullRequestMode.None => "opens no pull requests",
                _ => "opens pull requests as each project says",
            });
}

/// <summary>Moves an item to another place in the queue.</summary>
[Description("Move a task to another place in a team's queue.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue move reorder priority first", Mutates = true)]
public sealed class TeamQueueMoveCommand : AsyncCommand<TeamQueueMoveCommand.Settings>
{
    private readonly IQueueService _queues;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TeamQueueMoveCommand(
        IQueueService queues,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _queues = queues;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandArgument(1, "<TASK>")]
        [Description("The task to move. Give --project too when two projects' tasks share the id.")]
        public string Task { get; init; } = string.Empty;

        [CommandArgument(2, "<POSITION>")]
        [Description("Where it goes, counting from 1 at the front, as 'team queue list' numbers them.")]
        public int Position { get; init; }
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

        var (project, unresolved) = await TeamQueueing.ProjectAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (unresolved is not null)
        {
            return output.Fail(unresolved, ExitCode.ProjectNotFound);
        }

        string? Move(TeamQueue queue)
        {
            var item = TeamQueueing.Find(queue, settings.Task, project);

            if (item is null)
            {
                return $"'{settings.Task}' is not in {QueueSettling.Whose(team)} queue.";
            }

            if (settings.Position < 1 || settings.Position > queue.Items.Count)
            {
                return $"{settings.Position} is not a place in the queue: it has {queue.Items.Count}, numbered from 1.";
            }

            queue.Items.Remove(item);
            queue.Items.Insert(settings.Position - 1, item);

            return null;
        }

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            return Move(current.Value!) is { } no
                ? output.Fail(no, ExitCode.InvalidArguments)
                : Said(output, $"[dim]Dry run: nothing was moved.[/] '{Markup.Escape(settings.Task)}' would be number {settings.Position}.");
        }

        var saved = await _queues.UpdateAsync(team, Move, cancellationToken).ConfigureAwait(false);

        return saved.Failed
            ? output.Fail(saved)
            : Said(output, $"'{Markup.Escape(settings.Task)}' is number {settings.Position} in {Markup.Escape(QueueSettling.Whose(team))} queue.");
    }

    internal static int Said(CommandOutput output, string line)
    {
        output.WriteLine(line);

        return CommandOutput.Success();
    }
}

/// <summary>Takes an item out of the queue, leaving the task as it is.</summary>
[Description("Take a task out of a team's queue. The task itself is left as it is.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue remove dequeue take out", Mutates = true)]
public sealed class TeamQueueRemoveCommand : AsyncCommand<TeamQueueRemoveCommand.Settings>
{
    private readonly IQueueService _queues;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TeamQueueRemoveCommand(
        IQueueService queues,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _queues = queues;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandArgument(1, "<TASK>")]
        [Description("The task to take out. Give --project too when two projects' tasks share the id.")]
        public string Task { get; init; } = string.Empty;
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

        var (project, unresolved) = await TeamQueueing.ProjectAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (unresolved is not null)
        {
            return output.Fail(unresolved, ExitCode.ProjectNotFound);
        }

        string? Remove(TeamQueue queue)
        {
            if (TeamQueueing.Find(queue, settings.Task, project) is not { } item)
            {
                return $"'{settings.Task}' is not in {QueueSettling.Whose(team)} queue.";
            }

            // Said rather than left to stall: an item waiting for this one
            // would otherwise wait for a run that is never going to happen.
            queue.Items.Remove(item);

            return null;
        }

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            return Remove(current.Value!) is { } no
                ? output.Fail(no, ExitCode.InvalidArguments)
                : TeamQueueMoveCommand.Said(output, $"[dim]Dry run: nothing was removed.[/] '{Markup.Escape(settings.Task)}' would come out.");
        }

        var saved = await _queues.UpdateAsync(team, Remove, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        output.WriteLine($"'{Markup.Escape(settings.Task)}' is out of {Markup.Escape(QueueSettling.Whose(team))} queue. The task itself is unchanged.");

        foreach (var waiting in saved.Value!.Items.Where(one => QueueRules.Same(one.After, settings.Task)))
        {
            output.WriteLine(
                $"[yellow]![/] {Markup.Escape(waiting.Task)} waits for it, and will wait until it runs or is done. "
                + $"Take its 'after' off by removing it and adding it again.");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Sets a queue's limits and grants.</summary>
[Description("Set a team queue's limits and grants: what a window may spend, how many runs it may start, how many may wait at once, and whether it may open pull requests.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue settings cap budget window grant draft pull request", Mutates = true)]
public sealed class TeamQueueSettingsCommand : AsyncCommand<TeamQueueSettingsCommand.Settings>
{
    /// <summary>The --pr-mode word that clears the queue's own mode, so each project's applies.</summary>
    internal const string InheritWord = "inherit";

    private readonly IQueueService _queues;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TeamQueueSettingsCommand(
        IQueueService queues,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _queues = queues;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandOption("--window-usd <AMOUNT>")]
        [Description("The most a window may spend, in US dollars, or none for no cap of its own.")]
        public string? WindowUsd { get; init; }

        [CommandOption("--window-runs <COUNT>")]
        [Description("The most runs a window may start, or none.")]
        public string? WindowRuns { get; init; }

        [CommandOption("--held-cap <COUNT>")]
        [Description("How many runs may wait at a gate at once before the queue stops starting more. Two unless set.")]
        public int? HeldCap { get; init; }

        [CommandOption("--draft-prs <ON|OFF>")]
        [Description("The same as --pr-mode draft when on, and --pr-mode none when off.")]
        public string? DraftPrs { get; init; }

        [CommandOption("--pr-mode <MODE>")]
        [Description("draft, ready or none: whether a queued run that meets its done-when pushes its branch and opens a pull request. inherit takes each project's. Inherit unless set.")]
        public string? PrMode { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        // Every value read before anything is written, so a typo in one does
        // not leave the others half set.
        if (!UsdCap.TryParse(settings.WindowUsd, out var usd))
        {
            return output.Fail(UsdCap.Refusal(settings.WindowUsd!), ExitCode.InvalidArguments);
        }

        int? runs = null;
        var clearRuns = false;

        if (settings.WindowRuns is { Length: > 0 } typedRuns)
        {
            if (string.Equals(typedRuns.Trim(), UsdCap.NoneWord, StringComparison.OrdinalIgnoreCase))
            {
                clearRuns = true;
            }
            else if (int.TryParse(typedRuns, out var parsed) && parsed > 0)
            {
                runs = parsed;
            }
            else
            {
                return output.Fail($"'{typedRuns}' is not a number of runs. Give a whole number above zero, or none.", ExitCode.InvalidArguments);
            }
        }

        if (settings.HeldCap is < 0)
        {
            return output.Fail("The held cap is how many runs may wait at once, so it can't be below zero.", ExitCode.InvalidArguments);
        }

        bool? draft = settings.DraftPrs?.Trim().ToLowerInvariant() switch
        {
            null => null,
            "on" or "yes" or "true" => true,
            "off" or "no" or "false" => false,
            _ => default(bool?),
        };

        if (settings.DraftPrs is not null && draft is null)
        {
            return output.Fail($"'{settings.DraftPrs}' is not on or off.", ExitCode.InvalidArguments);
        }

        if (settings.DraftPrs is not null && settings.PrMode is not null)
        {
            return output.Fail("Give --pr-mode or --draft-prs, not both.", ExitCode.InvalidArguments);
        }

        var inherit = string.Equals(settings.PrMode?.Trim(), InheritWord, StringComparison.OrdinalIgnoreCase);
        PullRequestMode? mode = draft switch
        {
            true => PullRequestMode.Draft,
            false => PullRequestMode.None,
            null => null,
        };

        if (settings.PrMode is { } typedMode && !inherit)
        {
            if (!PullRequestModes.TryParse(typedMode, out mode) || mode is null)
            {
                return output.Fail(
                    $"'{typedMode}' is not a pull request mode. Give draft, ready, none or {InheritWord}.", ExitCode.InvalidArguments);
            }
        }

        var (team, refused) = await TeamQueueing
            .TeamAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (team is null)
        {
            return output.Fail(refused!, ExitCode.ProjectNotFound);
        }

        void Apply(QueueSettings into)
        {
            if (usd.IsSet)
            {
                into.WindowUsd = usd.None ? string.Empty : usd.ToString();
            }

            if (clearRuns)
            {
                into.WindowRuns = null;
            }
            else if (runs is { } most)
            {
                into.WindowRuns = most;
            }

            if (settings.HeldCap is { } held)
            {
                into.HeldCap = held;
            }

            // Either way the old grant goes, so the mode is the only thing
            // left saying what the queue does.
            if (inherit)
            {
                into.PrMode = string.Empty;
                into.DraftPullRequests = false;
            }
            else if (mode is { } chosen)
            {
                into.PrMode = PullRequestModes.Word(chosen);
                into.DraftPullRequests = false;
            }
        }

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            Apply(current.Value!.Settings);

            return TeamQueueMoveCommand.Said(
                output,
                $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(QueueSettling.Whose(team))} queue would have "
                + Markup.Escape(TeamQueueListCommand.Spell(current.Value.Settings)) + ".");
        }

        var saved = await _queues.UpdateAsync(
            team,
            queue =>
            {
                Apply(queue.Settings);

                return null;
            },
            cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { team, settings = TeamQueueListCommand.Json(saved.Value!.Settings) });

            return CommandOutput.Success();
        }

        return TeamQueueMoveCommand.Said(
            output,
            $"{Markup.Escape(QueueSettling.Whose(team))} queue: {Markup.Escape(TeamQueueListCommand.Spell(saved.Value!.Settings))}.");
    }
}

/// <summary>Says what the next window would run, in order, and what it would refuse.</summary>
/// <remarks>
/// For fixing specs before bed. It assumes every run meets its done-when, which
/// is the best case: a run that fails holds back whatever waits for it, and
/// that can't be known in advance.
/// </remarks>
[Description("Say what a team's queue would run next, in order, and what it would refuse and why. Changes nothing.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue plan preview what would run tonight")]
public sealed class TeamQueuePlanCommand : AsyncCommand<TeamQueueSettings>
{
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    private readonly Core.Configuration.IConfigurationService _configuration;

    public TeamQueuePlanCommand(
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console,
        TimeProvider time,
        Core.Configuration.IConfigurationService configuration)
    {
        _configuration = configuration;
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
        _time = time;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TeamQueueSettings settings, CancellationToken cancellationToken)
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

        var loaded = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

        if (loaded.Failed)
        {
            return output.Fail(loaded);
        }

        var lists = await TeamQueueing.ListsAsync(_tasks, loaded.Value!, cancellationToken).ConfigureAwait(false);
        var requires = await TeamQueueing.RequiresAsync(_tasks, _configuration, cancellationToken).ConfigureAwait(false);
        var plan = Plan(loaded.Value!, lists, _time.GetUtcNow(), requires);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                team,
                order = plan.Order.Select(one => new { task = one.Task, project = one.Project }),
                left = plan.Left.Select(one => new { task = one.Item.Task, project = one.Item.Project, reason = one.Reason }),
                stops = plan.Stops,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]What {Markup.Escape(QueueSettling.Whose(team))} queue would run next[/], if every run met its done-when:");

        if (plan.Order.Count == 0)
        {
            output.WriteLine("  Nothing.");
        }

        foreach (var (item, index) in plan.Order.Select((item, index) => (item, index)))
        {
            output.WriteLine($"  {index + 1,2}. {Markup.Escape(TeamQueueing.Describe(item))}");
        }

        foreach (var (item, reason) in plan.Left)
        {
            output.WriteLine($"  [yellow]left:[/] {Markup.Escape(TeamQueueing.Describe(item))}\n      {Markup.Escape(reason)}");
        }

        output.WriteLine($"[dim]{Markup.Escape(plan.Stops)}[/]");

        return CommandOutput.Success();
    }

    /// <summary>The order a window would take, by asking the rules again after pretending each run met its done-when.</summary>
    internal static (IReadOnlyList<QueueItem> Order, IReadOnlyList<QueueSkip> Left, string Stops) Plan(
        TeamQueue queue,
        Func<string, IReadOnlyList<TaskItem>?> lists,
        DateTimeOffset now,
        TaskRequires.Lists? requires = null)
    {
        // A task the plan pretends has run is done as far as anything that
        // requires it is concerned, as a real one met mid-window would be.
        var finished = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TaskRequires.Lists? pretended = requires is null
            ? null
            : key => requires(key) is { } list
                ? [.. list.Select(task => finished.Contains($"{key}/{task.Id}") ? Done(task) : task)]
                : null;

        // A copy, so the pretending touches nothing real. Runs already going
        // or held are kept, since they count against the caps as they are.
        var pretend = new TeamQueue
        {
            Items = [.. queue.Items],
            Runs = [.. queue.Runs.Where(run => run.State is QueueRunState.Held)],
            Settings = queue.Settings,
        };

        var window = new QueueWindow(
            now,
            UsdCap.TryParse(queue.Settings.WindowUsd, out var cap) ? cap.Usd : null,
            queue.Settings.WindowRuns);

        var order = new List<QueueItem>();

        while (true)
        {
            var decision = QueueRules.Next(pretend, lists, window, pretended);

            if (decision.Verdict != QueueVerdict.Start)
            {
                return (order, decision.Skipped, decision.Reason);
            }

            order.Add(decision.Item!);
            finished.Add($"{decision.Item!.Project}/{decision.Item.Task}");
            pretend.Items.Remove(decision.Item!);
            pretend.Runs.Add(new QueueRun
            {
                Task = decision.Item!.Task,
                Project = decision.Item.Project,
                State = QueueRunState.Met,
                StartedUtc = now,
            });
        }
    }

    /// <summary>A task as the rules see one that is done: only its id and state are read.</summary>
    private static TaskItem Done(TaskItem task) => new() { Id = task.Id, Kind = task.Kind, State = TaskState.Done };
}

/// <summary>Starts the next item in a team's queue now, by hand.</summary>
/// <remarks>
/// <para>
/// Runs the same command a person would type, <c>team run --task</c>, in a
/// worktree of its own, so a queued run is held to the task's done-when, the
/// project's standing ones and the team's own rules exactly as any other run
/// is, without touching the checkout the person is using.
/// </para>
/// <para>
/// Not held to a window's caps: whoever asks by hand is there to see what it
/// costs. One run at a time and the held cap still apply. It runs here, in
/// this terminal, so its questions are asked here.
/// </para>
/// </remarks>
[Description("Start the next task in a team's queue now, in a worktree of its own, as 'team run --task' would, and record how it went.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue run next start now", Mutates = true)]
public sealed class TeamQueueRunNextCommand : AsyncCommand<TeamQueueSettings>
{
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly ICommandCatalogue _commands;
    private readonly TeamQueueDriver _driver;
    private readonly Core.Configuration.IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamQueueRunNextCommand(
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        ICommandCatalogue commands,
        TeamQueueDriver driver,
        IAnsiConsole console,
        Core.Configuration.IConfigurationService configuration)
    {
        _configuration = configuration;
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _commands = commands;
        _driver = driver;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TeamQueueSettings settings, CancellationToken cancellationToken)
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

        if (settings.DryRun)
        {
            var loaded = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (loaded.Failed)
            {
                return output.Fail(loaded);
            }

            var lists = await TeamQueueing.ListsAsync(_tasks, loaded.Value!, cancellationToken).ConfigureAwait(false);
            var requires = await TeamQueueing.RequiresAsync(_tasks, _configuration, cancellationToken).ConfigureAwait(false);
            var preview = QueueRules.Next(loaded.Value!, lists, window: null, requires);

            return TeamQueueMoveCommand.Said(
                output,
                "[dim]Dry run: nothing was started.[/] " + Markup.Escape(preview.Verdict == QueueVerdict.Start
                    ? $"{TeamQueueing.Describe(preview.Item!)} would start, in a worktree of its own."
                    : preview.Reason));
        }

        // What ran before, settled first: a run whose process was killed would
        // otherwise be recorded as going for ever, and stop the queue.
        await _driver.SettleAsync(team, cancellationToken).ConfigureAwait(false);

        var (item, run, decision, error) = await _driver
            .TakeAsync(team, window: null, Environment.UserName, cancellationToken)
            .ConfigureAwait(false);

        if (error is not null)
        {
            return output.Fail(error, ExitCode.GeneralFailure);
        }

        if (item is null || run is null)
        {
            return TeamQueueMoveCommand.Said(output, Markup.Escape(decision?.Reason ?? "Nothing started."));
        }

        var tasks = await _tasks.ListAsync(item.Project, cancellationToken).ConfigureAwait(false);
        var task = tasks.Succeeded ? TaskRuns.Find(item.Task, tasks.Value!) : null;

        output.WriteLine(
            $"Starting {Markup.Escape(TeamQueueing.Describe(item))} from {Markup.Escape(QueueSettling.Whose(team))} queue, "
            + $"on branch {Markup.Escape(run.Branch)}.");

        var code = await _commands.RunAsync(
            "team run",
            TeamQueueDriver.RunArguments(team, item, task, run.Worktree, autonomy: null, unattended: false),
            cancellationToken).ConfigureAwait(false);

        var ended = await _driver.SettleAsync(team, CancellationToken.None).ConfigureAwait(false);

        if (ended.FirstOrDefault(one => QueueRules.Same(one.Task, item.Task) && one.StartedUtc == run.StartedUtc) is { } settled)
        {
            output.WriteLine(
                $"{Markup.Escape(item.Task)}: {settled.State.ToString().ToLowerInvariant()}"
                + (settled.Reason.Length > 0 ? $" - {Markup.Escape(settled.Reason)}" : string.Empty)
                + (settled.PullRequest.Length > 0 ? $"\n  draft pull request: {Markup.Escape(settled.PullRequest)}" : string.Empty));
        }

        return code;
    }
}

/// <summary>Sets when the daemon works through a team's queue on its own.</summary>
/// <remarks>
/// Kept in the schedule list, as a queue window, so 'team schedule list'
/// still answers "what starts runs for me" in one place. One window per team.
/// </remarks>
[Description("Set the daily window in which the daemon works through a team's queue on its own, or take it away with --off.")]
[CommandMeta(CommandCategory.Start, Intent = "team queue window overnight schedule hours unattended", Mutates = true)]
public sealed class TeamQueueWindowCommand : AsyncCommand<TeamQueueWindowCommand.Settings>
{
    private readonly IScheduleService _schedules;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamQueueWindowCommand(
        IScheduleService schedules,
        IProjectService projects,
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IAnsiConsole console,
        TimeProvider time)
    {
        _schedules = schedules;
        _projects = projects;
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _console = console;
        _time = time;
    }

    public sealed class Settings : TeamQueueSettings
    {
        [CommandOption("--at <TIME>")]
        [Description("When the window opens each day, as 22:00, in this machine's own time.")]
        public string? At { get; init; }

        [CommandOption("--for <DURATION>")]
        [Description("How long it stays open: 30m, 8h. At most a day.")]
        public string? For { get; init; }

        [CommandOption("--autonomy <MODE>")]
        [Description("supervised or autonomous. The team's own when left out. Never manual: nobody is watching.")]
        public string? Autonomy { get; init; }

        [CommandOption("--off")]
        [Description("Take the window away. The queue stays, and run-next still works.")]
        public bool Off { get; init; }
    }

    /// <summary>The schedule name a team's window is kept under.</summary>
    internal static string Name(string team) => $"queue-{team.Trim().ToLowerInvariant()}";

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, _, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (catalogue.Find(settings.Team) is not { Template: false } team)
        {
            return output.Fail(
                $"No team named '{settings.Team}' that can be run. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        var name = Name(team.Name);

        if (settings.Off)
        {
            if (settings.DryRun)
            {
                return TeamQueueMoveCommand.Said(output, $"[dim]Dry run: nothing was removed.[/] {Markup.Escape(QueueSettling.Whose(team.Name))} window would go.");
            }

            var removed = await _schedules.RemoveAsync(name, cancellationToken).ConfigureAwait(false);

            return removed.Failed
                ? output.Fail($"{QueueSettling.Whose(team.Name)} queue has no window.", ExitCode.ProjectNotFound)
                : TeamQueueMoveCommand.Said(output, $"{Markup.Escape(QueueSettling.Whose(team.Name))} queue has no window now. Its runs start only with run-next.");
        }

        if (settings.At is not { Length: > 0 } clock
            || !TimeOnly.TryParse(clock, System.Globalization.CultureInfo.InvariantCulture, out var at))
        {
            return output.Fail("Say when the window opens, as --at 22:00.", ExitCode.InvalidArguments);
        }

        if (settings.For is not { Length: > 0 } typed || TeamDuration.Parse(typed) is not { } length)
        {
            return output.Fail("Say how long it stays open, as --for 8h.", ExitCode.InvalidArguments);
        }

        var window = new TeamSchedule
        {
            Id = name,
            Team = team.Name,
            Kind = TeamSchedule.QueueKind,
            At = at,
            Length = length,
            Autonomy = (settings.Autonomy ?? team.Rules.Autonomy).Trim().ToLowerInvariant(),
        };

        var opens = ScheduleService.Next(window, _time.GetUtcNow());
        var said = $"{QueueSettling.Whose(team.Name)} queue: worked through daily from {at:HH:mm} for {typed}, {window.Autonomy}"
            + (opens is { } next ? $", next from {next.ToLocalTime():yyyy-MM-dd HH:mm}." : ".");

        if (settings.DryRun)
        {
            return TeamQueueMoveCommand.Said(output, "[dim]Dry run: nothing was recorded.[/] " + Markup.Escape(said));
        }

        var saved = await _schedules.SaveAsync(window, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        output.WriteLine(Markup.Escape(said));
        output.WriteLine("[dim]Nothing is worked through until the daemon is running:[/] loadout daemon");

        return CommandOutput.Success();
    }
}
