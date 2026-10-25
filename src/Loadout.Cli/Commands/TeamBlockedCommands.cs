using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Core.Triggers;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What the blocked-run commands share.</summary>
/// <remarks>
/// <para>
/// A run that ends short of its done-when used to be one line in the morning
/// summary. These put each one in front of a person with a suggestion and the
/// reason for it, and let them decide - carry on, raise the limit, re-queue,
/// edit and re-queue, set aside, or drop - in one place, whatever started the
/// run. Studio's Blocked section runs these same commands.
/// </para>
/// </remarks>
internal static class BlockedReading
{
    /// <summary>
    /// A reader that knows which runs have lost their process, where that can
    /// be told.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A run's journal says nothing more once the process driving it is
    /// killed, so it reads as running for ever. Where the queue started it,
    /// the queue recorded the process and its start time, and that is checked
    /// here; a run from anywhere else reads as going until its own driver can
    /// be checked.
    /// </para>
    /// <para>
    /// Worked out before the reader is made rather than asked of it as it
    /// goes, because the queue is read asynchronously and the judging is not.
    /// </para>
    /// </remarks>
    public static async Task<BlockedRunReader> ReaderAsync(
        IRunJournal journal,
        IQueueService queues,
        IProcessInspector processes,
        IRunOwnership ownership,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var dead = new HashSet<string>(StringComparer.Ordinal);
        var going = journal.List(BlockedRunReader.Looked)
            .Select(journal.Summarise)
            .Where(one => one is { Succeeded: true, Value.Running: true })
            .Select(one => one.Value!)
            .ToList();

        foreach (var team in going.Select(one => one.Team).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var queue = await queues.LoadAsync(team, ct).ConfigureAwait(false);

            if (queue.Failed)
            {
                continue;
            }

            foreach (var run in queue.Value!.Runs.Where(one => one.RunId.Length > 0 && one.Pid > 0 && one.PidStarted is not null))
            {
                if (going.Any(one => one.RunId == run.RunId) && !processes.IsRunning(run.Pid, run.PidStarted!.Value))
                {
                    dead.Add(run.RunId);
                }
            }
        }

        return new BlockedRunReader(journal, run => dead.Contains(run.RunId) || Gone(run, ownership, now));
    }

    /// <summary>Whether the run's owner lock was taken and is now free, so whatever drove it has ended.</summary>
    /// <remarks>
    /// Only that, not a run that has merely gone quiet with no owner record:
    /// that is a guess about a run from an older build, which closing asks
    /// about first, and carrying on from a run that is still going would put
    /// two leads on one task.
    /// </remarks>
    internal static bool Gone(RunSummary run, IRunOwnership ownership, DateTimeOffset now) =>
        TeamStyle.LivenessOf(run, ownership, now) is Liveness.NotResponding;

    /// <summary>One blocked run as JSON, the same for the list, one run, and Studio.</summary>
    public static object Json(BlockedRun one) => new
    {
        id = one.Run.RunId,
        team = one.Run.Team,
        project = one.Run.Project,
        task = one.Run.Task,
        goal = one.Run.Goal,
        kind = one.Kind,
        ended = one.Run.Ended,
        started = one.Run.Started,
        finished = one.Run.Finished,
        cost = one.Run.CostUsd,
        budget = one.Run.BudgetUsd,
        rounds = one.Run.Rounds,
        roundLimit = one.Run.RoundLimit,
        origin = RunOrigin.Parse(one.Run.Origin) is { } started ? started.Rule : null,
        follows = one.Run.Follows,
        chain = one.Chain,
        merged = one.Run.Merged,
        unmerged = one.Run.Nodes
            .Where(node => node.Branch is { Length: > 0 } branch && !one.Run.Merged.Contains(branch, StringComparer.Ordinal))
            .Select(node => node.Branch)
            .Distinct(StringComparer.Ordinal),
        lead = one.Lead is { } lead
            ? new { blocker = lead.Blocker, unblockedBy = lead.UnblockedBy, questions = lead.Questions, next = lead.Next, summary = lead.Summary }
            : null,
        suggested = new
        {
            action = BlockedActions.Spell(one.Suggested.Action),
            label = BlockedActions.Label(one.Suggested.Action),
            reason = one.Suggested.Reason,
            rule = one.Suggested.Rule,
        },
        offered = one.Offered.Select(action => new { action = BlockedActions.Spell(action), label = BlockedActions.Label(action) }),
        raise = one.Raise is { } raise
            ? new { cap = raise.Cap == BlockedCap.Rounds ? "rounds" : "budget", figure = raise.Figure, used = raise.Used, said = raise.Describe() }
            : null,
    };

    /// <summary>The command that resolves a run with its suggestion, for the end of a line.</summary>
    public static string Command(BlockedRun one) =>
        $"loadout team blocked resolve {one.Run.RunId} --action {BlockedActions.Spell(one.Suggested.Action)}";
}

/// <summary>Every run that ended short of its done-when and is waiting on a person.</summary>
[Description("List team runs that ended short of their done-when - blocked, failed, limited, unrecorded or dead - with what to do about each.")]
[CommandMeta(CommandCategory.Start, Intent = "team blocked list failed limited stuck runs decide what next")]
public sealed class TeamBlockedListCommand : AsyncCommand<TeamSettings>
{
    private readonly IRunJournal _journal;
    private readonly IQueueService _queues;
    private readonly IProcessInspector _processes;
    private readonly IRunOwnership _ownership;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamBlockedListCommand(
        IRunJournal journal, IQueueService queues, IProcessInspector processes, IRunOwnership ownership, TimeProvider time, IAnsiConsole console)
    {
        _journal = journal;
        _queues = queues;
        _processes = processes;
        _ownership = ownership;
        _time = time;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TeamSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var reader = await BlockedReading.ReaderAsync(_journal, _queues, _processes, _ownership, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var blocked = reader.List(settings.Project);

        if (output.IsJson)
        {
            output.WriteJson(new { runs = blocked.Select(BlockedReading.Json) });

            return CommandOutput.Success();
        }

        if (blocked.Count == 0)
        {
            output.WriteLine("No runs are waiting on you: every run that ended has been dealt with or got there.");

            return CommandOutput.Success();
        }

        output.WriteLine($"{blocked.Count} run(s) ended short of their done-when:");
        output.WriteLine(string.Empty);

        foreach (var one in blocked)
        {
            output.WriteLine(
                $"[bold]{Markup.Escape(one.Run.RunId)}[/]  {Markup.Escape(one.Run.Team)}"
                + (one.Run.Project is { Length: > 0 } project ? $" on {Markup.Escape(project)}" : string.Empty)
                + (one.Run.Task is { Length: > 0 } task ? $", task {Markup.Escape(task)}" : string.Empty)
                + $"  [yellow]{Markup.Escape(one.Kind)}[/]");
            output.WriteLine($"  {Markup.Escape(one.Run.Goal)}");
            output.WriteLine($"  [green]Suggested:[/] {Markup.Escape(BlockedActions.Label(one.Suggested.Action))}. {Markup.Escape(one.Suggested.Reason)}");
            output.WriteLine($"  [dim]{Markup.Escape(BlockedReading.Command(one))}[/]");
            output.WriteLine(string.Empty);
        }

        output.WriteLine("[dim]See one in full with: loadout team blocked show <run>[/]");

        return CommandOutput.Success();
    }
}

/// <summary>One blocked run in full: how it ended, what its lead said, and what can be done.</summary>
[Description("Show one run that ended short of its done-when: how it ended, what stopped it, how far it got, and every action open to it.")]
[CommandMeta(CommandCategory.Start, Intent = "team blocked show one stuck run detail")]
public sealed class TeamBlockedShowCommand : AsyncCommand<TeamBlockedShowCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IQueueService _queues;
    private readonly IProcessInspector _processes;
    private readonly IRunOwnership _ownership;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamBlockedShowCommand(
        IRunJournal journal, IQueueService queues, IProcessInspector processes, IRunOwnership ownership, TimeProvider time, IAnsiConsole console)
    {
        _journal = journal;
        _queues = queues;
        _processes = processes;
        _ownership = ownership;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<RUN>")]
        [Description("The run, as 'team blocked list' names it.")]
        public string Run { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var reader = await BlockedReading.ReaderAsync(_journal, _queues, _processes, _ownership, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var found = reader.Find(settings.Run.Trim());

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var one = found.Value!;

        if (output.IsJson)
        {
            output.WriteJson(BlockedReading.Json(one));

            return CommandOutput.Success();
        }

        var run = one.Run;

        output.WriteLine($"[bold]{Markup.Escape(run.RunId)}[/]  {Markup.Escape(run.Team)}  [yellow]{Markup.Escape(one.Kind)}[/]");
        output.WriteLine($"Goal: {Markup.Escape(run.Goal)}");

        if (run.Task is { Length: > 0 } task)
        {
            output.WriteLine($"Task: {Markup.Escape(task)} on {Markup.Escape(run.Project ?? "?")}");
        }

        output.WriteLine($"Ended: {Markup.Escape(run.Ended is { Length: > 0 } ended ? ended : one.Kind == "dead" ? "its process went away" : "without saying")}");
        output.WriteLine(
            $"Got through {run.Rounds} of {run.RoundLimit} round(s), spending ${run.CostUsd:0.00}"
            + (run.BudgetUsd is { } budget ? $" of ${budget:0.00}" : string.Empty)
            + $"; {run.Merged.Count} branch(es) merged.");

        if (RunOrigin.Parse(run.Origin) is { } origin)
        {
            output.WriteLine($"Started by trigger rule {Markup.Escape(origin.Rule)}.");
        }

        if (one.Chain > 1)
        {
            output.WriteLine($"Run {one.Chain} in a row on this task to end short; it carries on from {Markup.Escape(run.Follows ?? "?")}.");
        }

        if (one.Lead is { } lead)
        {
            output.WriteLine(string.Empty);
            output.WriteLine("[bold]What its lead last said[/]");

            if (lead.Summary is { } summary)
            {
                output.WriteLine($"  {Markup.Escape(summary)}");
            }

            if (lead.Blocker is { } blocker)
            {
                output.WriteLine($"  Blocked on: {Markup.Escape(blocker)}");
            }

            if (lead.UnblockedBy is { } unblock)
            {
                output.WriteLine($"  Would be unblocked by: {Markup.Escape(unblock)}");
            }

            foreach (var question in lead.Questions)
            {
                output.WriteLine($"  Asked: {Markup.Escape(question)}");
            }

            if (lead.Next is { } next)
            {
                output.WriteLine($"  Next, it said: {Markup.Escape(next)}");
            }
        }

        foreach (var unmet in run.Outstanding)
        {
            output.WriteLine($"  [dim]Not met:[/] {Markup.Escape(unmet.Criterion)}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"[green]Suggested:[/] {Markup.Escape(BlockedActions.Label(one.Suggested.Action))}");
        output.WriteLine($"  {Markup.Escape(one.Suggested.Reason)}");

        if (one.Raise is { } raise)
        {
            output.WriteLine($"  A raise starts from {Markup.Escape(raise.Describe())}.");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("Open to it:");

        foreach (var action in one.Offered)
        {
            output.WriteLine($"  [dim]--action[/] {BlockedActions.Spell(action),-13} {Markup.Escape(BlockedActions.Label(action))}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"[dim]{Markup.Escape(BlockedReading.Command(one))}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Decides what happens to a run that ended short of its done-when.</summary>
/// <remarks>
/// <para>
/// Whatever is chosen, the run is written down as dealt with, in its own
/// journal, so it leaves the Blocked section and the decision travels with it.
/// A run started to carry on names itself on that decision, and says which run
/// it follows, so the chain reads from either end.
/// </para>
/// <para>
/// The task moves with the decision - doing for a run carrying on, open for a
/// re-queue or a run set aside, dropped for a drop - so the board and the
/// Blocked section never disagree about it.
/// </para>
/// </remarks>
[Description("Decide what happens to a run that ended short of its done-when: carry on, raise the limit, re-queue, edit and re-queue, set aside, or drop.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team blocked resolve carry on raise limit requeue set aside drop decide stuck run", Mutates = true,
    Example = "loadout team blocked resolve 20261025-0900-ab12 --action carry-on --answer \"Use the staging database\"")]
public sealed class TeamBlockedResolveCommand : AsyncCommand<TeamBlockedResolveCommand.Settings>
{
    /// <summary>How long a carry-on waits to see the run it started name itself.</summary>
    internal static readonly TimeSpan Naming = TimeSpan.FromSeconds(15);

    private readonly IRunJournal _journal;
    private readonly IQueueService _queues;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IProcessInspector _processes;
    private readonly IRunOwnership _ownership;
    private readonly IProcessLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly YamlStore _yaml;
    private readonly IFilePermissions _permissions;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamBlockedResolveCommand(
        IRunJournal journal,
        IQueueService queues,
        ITaskService tasks,
        IProjectService projects,
        IProcessInspector processes,
        IRunOwnership ownership,
        IProcessLauncher launcher,
        IPlatformPaths paths,
        YamlStore yaml,
        IFilePermissions permissions,
        TimeProvider time,
        IAnsiConsole console)
    {
        _journal = journal;
        _queues = queues;
        _tasks = tasks;
        _projects = projects;
        _processes = processes;
        _ownership = ownership;
        _launcher = launcher;
        _paths = paths;
        _yaml = yaml;
        _permissions = permissions;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<RUN>")]
        [Description("The run, as 'team blocked list' names it.")]
        public string Run { get; init; } = string.Empty;

        [CommandOption("--action <ACTION>")]
        [Description("carry-on, raise, requeue, edit-requeue, set-aside or drop. 'team blocked show' lists what is open to the run and suggests one.")]
        public string? Action { get; init; }

        [CommandOption("--answer <TEXT>")]
        [Description("What to tell the next lead: the answer to what stopped this run. Kept on the run for any action.")]
        public string? Answer { get; init; }

        [CommandOption("--rounds <N>")]
        [Description("For raise on a run that ran out of rounds: how many the next run gets. Defaults to the same again.")]
        public int? Rounds { get; init; }

        [CommandOption("--usd <AMOUNT>")]
        [Description("For raise on a run that ran out of money: what the next run may spend. Defaults to the same again.")]
        public string? Usd { get; init; }

        [CommandOption("--title <TEXT>")]
        [Description("For edit-requeue: the task's new title.")]
        public string? Title { get; init; }

        [CommandOption("--detail <TEXT>")]
        [Description("For edit-requeue: the task's new detail.")]
        public string? Detail { get; init; }

        [CommandOption("--done-when <CRITERION>")]
        [Description("For edit-requeue: the task's done-when, replacing what it has. Give it once per criterion.")]
        public string[] DoneWhen { get; init; } = [];

        [CommandOption("--by <WHO>")]
        [Description("Who is deciding. Defaults to this machine's user.")]
        public string? By { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!BlockedActions.TryParse(settings.Action, out var action))
        {
            return output.Fail(
                $"Say what to do with --action: one of {string.Join(", ", BlockedActions.Names)}. "
                + $"'loadout team blocked show {settings.Run}' suggests one.",
                ExitCode.InvalidArguments);
        }

        var reader = await BlockedReading.ReaderAsync(_journal, _queues, _processes, _ownership, _time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        var found = reader.Find(settings.Run.Trim());

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var one = found.Value!;

        if (!one.Offered.Contains(action))
        {
            return output.Fail(
                $"'{BlockedActions.Spell(action)}' is not open to run {one.Run.RunId}"
                + (action == BlockedAction.Raise ? ", which did not run out of rounds or money" : string.Empty)
                + $". Open to it: {string.Join(", ", one.Offered.Select(BlockedActions.Spell))}.",
                ExitCode.InvalidArguments);
        }

        var by = settings.By is { Length: > 0 } who ? who.Trim() : Environment.UserName;
        var answer = settings.Answer is { Length: > 0 } said ? said.Trim() : null;

        return action switch
        {
            BlockedAction.SetAside or BlockedAction.Drop =>
                await FileAsync(reader, one, action, by, answer, settings, output, cancellationToken).ConfigureAwait(false),
            BlockedAction.Requeue or BlockedAction.EditRequeue =>
                await RequeueAsync(reader, one, action, by, answer, settings, output, cancellationToken).ConfigureAwait(false),
            _ => await CarryOnAsync(reader, one, action, by, answer, settings, output, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Set aside or drop: the run is filed and the task moved.</summary>
    private async Task<int> FileAsync(
        BlockedRunReader reader, BlockedRun one, BlockedAction action, string by, string? answer,
        Settings settings, CommandOutput output, CancellationToken ct)
    {
        var state = action == BlockedAction.Drop ? TaskState.Dropped : TaskState.Open;
        var verb = action == BlockedAction.Drop ? "dropped" : "set aside";

        if (settings.DryRun)
        {
            return Said(output, one, action, null, dryRun: true,
                $"Dry run: nothing was written. Run {one.Run.RunId} would be filed as {verb}"
                + (one.Run.Task is { Length: > 0 } task ? $", and task {task} would go to {state.ToString().ToLowerInvariant()}." : "."));
        }

        var recorded = reader.Record(one.Run.RunId, new RunResolution(_time.GetUtcNow(), BlockedActions.Spell(action), by, answer));

        if (recorded.Failed)
        {
            return output.Fail(recorded);
        }

        var moved = await MoveTaskAsync(one, state, by, $"run {one.Run.RunId} {verb} from the Blocked section", ct).ConfigureAwait(false);

        return Said(output, one, action, null, dryRun: false,
            $"Run {one.Run.RunId} is {verb}." + moved);
    }

    /// <summary>Re-queue, with or without editing the task first: at the front of its team's queue.</summary>
    private async Task<int> RequeueAsync(
        BlockedRunReader reader, BlockedRun one, BlockedAction action, string by, string? answer,
        Settings settings, CommandOutput output, CancellationToken ct)
    {
        var run = one.Run;
        var (project, taskId) = (run.Project!, run.Task!);
        var editing = action == BlockedAction.EditRequeue;
        var doneWhen = settings.DoneWhen.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();

        if (editing && settings.Title is not { Length: > 0 } && settings.Detail is not { Length: > 0 } && doneWhen.Count == 0)
        {
            return output.Fail(
                "Say what changes with --title, --detail or --done-when. To put it back as it is, use --action requeue.",
                ExitCode.InvalidArguments);
        }

        var listed = await _tasks.ListAsync(project, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var tasks = listed.Value!.ToList();
        var task = TaskRuns.Find(taskId, tasks);

        if (task is null)
        {
            return output.Fail($"Task '{taskId}' is no longer on {project}'s list, so there is nothing to re-queue.", ExitCode.ProjectNotFound);
        }

        // Admission is judged on the task as it will be, so an edit that gives
        // it the done-when it lacked is let in.
        var edited = new TaskItem
        {
            Id = task.Id,
            Title = settings.Title is { Length: > 0 } title ? title.Trim() : task.Title,
            State = TaskState.Open,
            Kind = task.Kind,
            Detail = task.Detail,
            DoneWhen = doneWhen.Count > 0 ? doneWhen : task.DoneWhen,
        };

        tasks[tasks.IndexOf(task)] = edited;

        var item = new QueueItem
        {
            Task = task.Id,
            Project = project,
            AddedBy = by,
            AddedUtc = _time.GetUtcNow(),
            Follows = run.RunId,
        };

        string? Admit(TeamQueue queue)
        {
            if (TeamQueueing.Find(queue, item.Task, item.Project) is not null)
            {
                return $"'{item.Task}' on {item.Project} is already in {QueueSettling.Whose(run.Team)} queue.";
            }

            return QueueRules.Admission(item, tasks, queue);
        }

        if (settings.DryRun)
        {
            var current = await _queues.LoadAsync(run.Team, ct).ConfigureAwait(false);

            if (current.Failed)
            {
                return output.Fail(current);
            }

            if (Admit(current.Value!) is { } wouldRefuse)
            {
                return output.Fail($"Not re-queued: {wouldRefuse}", ExitCode.InvalidArguments);
            }

            return Said(output, one, action, null, dryRun: true,
                $"Dry run: nothing was written. {(editing ? $"Task {task.Id} would be edited, then it" : $"Task {task.Id}")} "
                + $"would go to the front of {QueueSettling.Whose(run.Team)} queue, carrying on from run {run.RunId}.");
        }

        // The queue first: it is the step that can be refused, and a run filed
        // as re-queued with nothing in the queue would be lost from both.
        var queued = await _queues.UpdateAsync(
            run.Team,
            queue =>
            {
                if (Admit(queue) is { } no)
                {
                    return no;
                }

                queue.Items.Insert(0, item);

                return null;
            },
            ct).ConfigureAwait(false);

        if (queued.Failed)
        {
            return output.Fail($"Not re-queued: {queued.Error}", queued.ExitCode == ExitCode.Success ? ExitCode.InvalidArguments : queued.ExitCode);
        }

        var recorded = reader.Record(run.RunId, new RunResolution(_time.GetUtcNow(), BlockedActions.Spell(action), by, answer));

        if (recorded.Failed)
        {
            // Somebody else dealt with it first. Take back what was added so
            // the task is not queued on a decision that did not stand.
            await _queues.UpdateAsync(run.Team, queue =>
            {
                queue.Items.RemoveAll(one => ReferenceEquals(one, item) || (one.Follows == run.RunId && QueueRules.Same(one.Task, item.Task)));

                return null;
            }, ct).ConfigureAwait(false);

            return output.Fail(recorded);
        }

        var declared = await _tasks.DeclareAsync(
            project,
            task.Id,
            TaskState.Open,
            by,
            title: editing && settings.Title is { Length: > 0 } newTitle ? newTitle.Trim() : null,
            note: $"re-queued at the front after run {run.RunId}",
            ct: ct,
            detail: editing && settings.Detail is { Length: > 0 } newDetail ? newDetail.Trim() : null,
            doneWhen: editing && doneWhen.Count > 0 ? doneWhen : null).ConfigureAwait(false);

        return Said(output, one, action, null, dryRun: false,
            $"Task {task.Id} is first in {QueueSettling.Whose(run.Team)} queue, and will carry on from run {run.RunId}"
            + " in the next window, within the queue's held and window caps."
            + (declared.Failed ? $" The task could not be updated: {declared.Error}" : string.Empty));
    }

    /// <summary>Carry on, or raise the limit and carry on: a new run, started now, that follows this one.</summary>
    private async Task<int> CarryOnAsync(
        BlockedRunReader reader, BlockedRun one, BlockedAction action, string by, string? answer,
        Settings settings, CommandOutput output, CancellationToken ct)
    {
        var run = one.Run;
        var rounds = run.RoundLimit;
        var usd = run.BudgetUsd;
        string? cap = null;
        decimal? figure = null;

        if (action == BlockedAction.Raise)
        {
            var raise = one.Raise!;

            if (raise.Cap == BlockedCap.Rounds)
            {
                rounds = settings.Rounds ?? (int)raise.Figure;

                if (rounds <= 0)
                {
                    return output.Fail("--rounds must be a whole number above nought.", ExitCode.InvalidArguments);
                }

                (cap, figure) = ("rounds", rounds);
            }
            else
            {
                if (settings.Usd is { Length: > 0 } typed)
                {
                    if (!decimal.TryParse(typed.TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var given) || given <= 0m)
                    {
                        return output.Fail($"'{typed}' is not an amount of money above nought.", ExitCode.InvalidArguments);
                    }

                    usd = given;
                }
                else
                {
                    usd = raise.Figure;
                }

                (cap, figure) = ("budget", usd);
            }
        }

        // A run a trigger rule started carries on charged to the same rule,
        // inside its cap for the window. Checked before anything is written,
        // so a refusal leaves nothing behind: no run, nothing set aside.
        string? origin = null;
        var desk = new TriggerDesk(_paths, _processes, _yaml, _permissions, _time);

        if (RunOrigin.Parse(run.Origin) is { } started)
        {
            var (refused, follow) = await ReserveAsync(desk, started, usd, settings.DryRun, ct).ConfigureAwait(false);

            if (refused is not null)
            {
                return output.Fail(refused, ExitCode.InvalidArguments);
            }

            origin = follow;
        }

        var arguments = await ArgumentsAsync(run, rounds, usd, origin, ct).ConfigureAwait(false);

        if (settings.DryRun)
        {
            return Said(output, one, action, null, dryRun: true,
                $"Dry run: nothing was written or started. It would run: loadout team run {string.Join(' ', arguments.Select(Quoted))}");
        }

        var recorded = reader.Record(run.RunId, new RunResolution(_time.GetUtcNow(), BlockedActions.Spell(action), by, answer, Cap: cap, Figure: figure));

        if (recorded.Failed)
        {
            return output.Fail(recorded);
        }

        if (Loadout.Core.Agents.LauncherInvocation.Parts() is not { } launcher)
        {
            Unresolve(run.RunId, "the launcher could not name itself to start the run");

            return output.Fail("The launcher could not name itself to start the run, so nothing was started.");
        }

        var begun = _launcher.StartBackground(new ProcessRequest(
            launcher.Command,
            [.. launcher.Prefix, "team", "run", .. arguments],
            WorkingDirectory: run.Path is { Length: > 0 } where && Directory.Exists(where) ? where : null));

        if (begun.Failed)
        {
            Unresolve(run.RunId, $"the run could not be started: {begun.Error}");

            return output.Fail($"The run could not be started, so run {run.RunId} is still waiting on you: {begun.Error}");
        }

        if (run.Task is { Length: > 0 } task && run.Project is { Length: > 0 } project)
        {
            await _tasks.DeclareAsync(project, task, TaskState.Doing, by, note: $"carried on from run {run.RunId}", ct: ct)
                .ConfigureAwait(false);
        }

        var next = await NextAsync(run.RunId, ct).ConfigureAwait(false);

        return Said(output, one, action, next, dryRun: false,
            (next is { Length: > 0 }
                ? $"Run {next} carries on from {run.RunId}"
                : $"A run is starting to carry on from {run.RunId}; it names itself on the run when it opens")
            + (cap == "rounds" ? $", with {figure:0} round(s)" : cap == "budget" ? $", with ${figure:0.00} to spend" : string.Empty)
            + (next is { Length: > 0 } ? $". Follow it with: loadout team log {next} --follow" : "."));
    }

    /// <summary>The words 'team run' is given to carry on from a run.</summary>
    private async Task<List<string>> ArgumentsAsync(RunSummary run, int rounds, decimal? usd, string? origin, CancellationToken ct)
    {
        var arguments = new List<string>
        {
            run.Team,
            run.Goal,
            "--project", run.Project!,
            "--follows", run.RunId,
            "--non-interactive",
        };

        if (run.Autonomy is { Length: > 0 } autonomy)
        {
            arguments.AddRange(["--autonomy", autonomy]);
        }

        if (run.Task is { Length: > 0 } task)
        {
            arguments.AddRange(["--task", task]);
        }

        if (rounds > 0)
        {
            arguments.AddRange(["--rounds", rounds.ToString(CultureInfo.InvariantCulture)]);
        }

        if (usd is { } money)
        {
            arguments.AddRange(["--usd", money.ToString("0.00", CultureInfo.InvariantCulture)]);
        }
        else if (run.Uncapped)
        {
            arguments.AddRange(["--usd", UsdCap.NoneWord]);
        }

        if (origin is { Length: > 0 })
        {
            arguments.AddRange(["--origin", origin]);
        }

        // The criteria it was held to, where somebody typed them rather than
        // they came with the task: the task's own come back with --task.
        if (RunResumption.Read(_journal, run.RunId) is ({ Criteria: { Count: > 0 } criteria }, _))
        {
            foreach (var criterion in criteria)
            {
                arguments.AddRange(["--done-when", criterion]);
            }
        }

        // In the worktree it worked in, when that was not the project's own
        // checkout: a queued run's branch is where its work is.
        if (run.Path is { Length: > 0 } path && Directory.Exists(path))
        {
            var project = await _projects.ResolveAsync(run.Project!, ct).ConfigureAwait(false);

            if (project.Succeeded && project.Value!.LocalPath is { Length: > 0 } checkout
                && !string.Equals(Path.GetFullPath(checkout).TrimEnd('\\', '/'), Path.GetFullPath(path).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                arguments.AddRange(["--work-in", path]);
            }
        }

        return arguments;
    }

    /// <summary>
    /// Sets the next run's money aside under the rule that started this one,
    /// or says why it cannot be.
    /// </summary>
    /// <returns>The refusal, or the origin the next run is started with.</returns>
    private async Task<(string? Refused, string? Origin)> ReserveAsync(
        TriggerDesk desk, RunOrigin started, decimal? usd, bool dryRun, CancellationToken ct)
    {
        if (usd is not { } figure)
        {
            return ($"Rule '{started.Rule}' started this run, and its spend cap is the ceiling, so the run after it "
                + "needs a figure. Raise it with --action raise --usd <amount>.", null);
        }

        var listed = await desk.Rules.ListAsync(ct).ConfigureAwait(false);
        var rule = listed.Value?.FirstOrDefault(one => string.Equals(one.Rule.Id, started.Rule, StringComparison.OrdinalIgnoreCase))?.Rule;

        if (rule is null)
        {
            return ($"Rule '{started.Rule}', which started this run, is gone, so its cap cannot be checked and nothing was started.", null);
        }

        var reservation = Guid.NewGuid().ToString("N");
        var request = Guid.NewGuid().ToString("N");
        var (problem, _, entries) = TriggerSpine.Following(desk.Ledger(), rule, reservation, figure, _time.GetUtcNow(), request);

        if (problem is not null)
        {
            return (Refusal(problem, rule.Id), null);
        }

        if (dryRun)
        {
            return (null, new RunOrigin(rule.Id, rule.Version, reservation, started.Chain).ToString());
        }

        // Through the daemon when one runs, so the ledger it decides starts by
        // is the one this is checked against; straight to the journal when
        // none does, which it reads when it starts.
        if (desk.Daemon is not null)
        {
            var inbox = new TriggerInbox(_paths.Paths.State);
            var asking = new TriggerAsk(request, TriggerVerbs.Follow, RuleId: rule.Id, Reservation: reservation, Usd: figure);
            var answer = await inbox.AskAsync(asking, desk.Wait, _time, ct).ConfigureAwait(false)
                ?? await inbox.AskAsync(asking, desk.Wait, _time, ct).ConfigureAwait(false);

            if (answer is null)
            {
                return ("The daemon did not answer, so nothing was set aside and nothing was started. "
                    + "'loadout doctor' says whether it is still running.", null);
            }

            if (!answer.Applied)
            {
                return (Refusal(answer.Message, rule.Id), null);
            }
        }
        else
        {
            desk.Journal.Append([.. entries]);
        }

        return (null, new RunOrigin(rule.Id, rule.Version, reservation, started.Chain).ToString());
    }

    /// <summary>A refusal against a rule's cap, with where to raise the cap on purpose.</summary>
    internal static string Refusal(string problem, string rule) =>
        $"Refused: {problem}. Nothing was started and nothing set aside. Raise {rule}'s cap on purpose on its page, "
        + "Studio's Teams > Trigger rules (#teams/+triggers), then try again.";

    /// <summary>Takes back a decision whose run never started, so the run is waiting on a person again.</summary>
    private void Unresolve(string runId, string why)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(_journal.DirectoryOf(runId), "journal.jsonl"),
                RunJournalLine.Serialize(_time.GetUtcNow(), runId, null, "run.unresolved", new { why }) + "\n");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Said by the caller's failure; the run reads as dealt with until
            // somebody notices, which is the lesser harm than throwing here.
        }
    }

    /// <summary>The run that names itself as carrying on from this one, waited for briefly.</summary>
    private async Task<string?> NextAsync(string runId, CancellationToken ct)
    {
        var until = _time.GetUtcNow() + Naming;

        while (_time.GetUtcNow() < until && !ct.IsCancellationRequested)
        {
            if (_journal.Summarise(runId).Value?.Resolved?.Next is { Length: > 0 } next)
            {
                return next;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Moves the run's task to a state, saying so, or nothing for a run with no task.</summary>
    private async Task<string> MoveTaskAsync(BlockedRun one, TaskState state, string by, string note, CancellationToken ct)
    {
        if (one.Run.Task is not { Length: > 0 } task || one.Run.Project is not { Length: > 0 } project)
        {
            return string.Empty;
        }

        var declared = await _tasks.DeclareAsync(project, task, state, by, note: note, ct: ct).ConfigureAwait(false);

        return declared.Succeeded
            ? $" Task {task} is {state.ToString().ToLowerInvariant()}."
            : $" Task {task} could not be updated: {declared.Error}";
    }

    private static int Said(CommandOutput output, BlockedRun one, BlockedAction action, string? next, bool dryRun, string message)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                run = one.Run.RunId,
                action = BlockedActions.Spell(action),
                next,
                dryRun,
                message,
            });
        }
        else
        {
            output.WriteLine((dryRun ? string.Empty : "[green]+[/] ") + Markup.Escape(message));
        }

        return CommandOutput.Success();
    }

    private static string Quoted(string word) => word.Contains(' ', StringComparison.Ordinal) ? $"\"{word}\"" : word;
}
