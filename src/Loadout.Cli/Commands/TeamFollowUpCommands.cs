using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What the follow-up commands share: reading them and writing them out.</summary>
internal static class FollowUpReading
{
    /// <summary>How far back the list looks for runs with follow-ups waiting.</summary>
    internal const int Runs = 500;

    internal static object Json(RunSummary run) => new
    {
        run = run.RunId,
        project = run.Project,
        team = run.Team,
        waiting = RunFollowUps.Waiting(run),
        followUps = run.FollowUps.Select(Json),
    };

    internal static object Json(RunFollowUp one) => new
    {
        id = one.Id,
        title = one.Title,
        why = one.Why,
        state = one.State.ToString().ToLowerInvariant(),
        task = one.Task,
        by = one.By,
        reason = one.Reason,
    };

    internal static void Write(CommandOutput output, RunSummary run)
    {
        output.WriteLine(
            $"[bold]{Markup.Escape(run.RunId)}[/]  {Markup.Escape(run.Team)}"
            + (run.Project is { Length: > 0 } project ? $" on {Markup.Escape(project)}" : string.Empty)
            + $"  [dim]{RunFollowUps.Waiting(run)} of {run.FollowUps.Count} waiting[/]");

        foreach (var one in run.FollowUps)
        {
            var state = one.State switch
            {
                FollowUpState.Accepted => $"[green]accepted[/] as {Markup.Escape(one.Task ?? "a task")}",
                FollowUpState.Dismissed => "[dim]dismissed[/]" + (one.Reason is { Length: > 0 } why ? $" [dim]({Markup.Escape(why)})[/]" : string.Empty),
                _ => "[yellow]waiting[/]",
            };

            output.WriteLine($"  {Markup.Escape(one.Id)}  {Markup.Escape(one.Title)}  {state}");

            if (one.State == FollowUpState.Proposed && one.Why is { Length: > 0 } reason)
            {
                output.WriteLine($"      [dim]{Markup.Escape(reason)}[/]");
            }
        }
    }
}

/// <summary>The follow-ups runs left for later, and which are still waiting on somebody.</summary>
[Description("List the work finished runs left for later. With a run, every follow-up it named; without, each run with some still waiting to be accepted or dismissed.")]
[CommandMeta(CommandCategory.Start, Intent = "team follow-ups list left for later deferred work proposals waiting")]
public sealed class TeamFollowUpsListCommand : Command<TeamFollowUpsListCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamFollowUpsListCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "[RUN]")]
        [Description("One run, to see all of its follow-ups, including those already decided.")]
        public string? Run { get; init; }
    }

    /// <inheritdoc />
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Run is { Length: > 0 } named)
        {
            var read = _journal.Summarise(named.Trim());

            if (read.Failed)
            {
                return output.Fail(read);
            }

            if (output.IsJson)
            {
                output.WriteJson(FollowUpReading.Json(read.Value!));

                return CommandOutput.Success();
            }

            if (read.Value!.FollowUps.Count == 0)
            {
                output.WriteLine($"Run {Markup.Escape(read.Value.RunId)} left nothing for later.");

                return CommandOutput.Success();
            }

            FollowUpReading.Write(output, read.Value);

            return CommandOutput.Success();
        }

        var waiting = _journal.List(FollowUpReading.Runs)
            .Select(id => _journal.Summarise(id))
            .Where(read => read.Succeeded && RunFollowUps.Waiting(read.Value!) > 0)
            .Select(read => read.Value!)
            .Where(run => settings.Project is not { Length: > 0 } project || string.Equals(run.Project, project, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new { runs = waiting.Select(FollowUpReading.Json) });

            return CommandOutput.Success();
        }

        if (waiting.Count == 0)
        {
            output.WriteLine("No follow-ups are waiting: every one a run left for later has been accepted or dismissed.");

            return CommandOutput.Success();
        }

        output.WriteLine($"{waiting.Count} run(s) left work for later that nobody has decided about:");
        output.WriteLine(string.Empty);

        foreach (var run in waiting)
        {
            FollowUpReading.Write(output, run);
            output.WriteLine(string.Empty);
        }

        output.WriteLine("[dim]Make one a task with: loadout team follow-ups accept <run> <id>, or drop it with: loadout team follow-ups dismiss <run> <id>[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Settings both decisions share: the run and the follow-ups.</summary>
public class TeamFollowUpDecisionSettings : GlobalSettings
{
    [CommandArgument(0, "<RUN>")]
    [Description("The run, as 'team follow-ups list' names it.")]
    public string Run { get; init; } = string.Empty;

    [CommandArgument(1, "<ID>")]
    [Description("The follow-ups, such as F1 F2.")]
    public string[] Ids { get; init; } = [];

    [CommandOption("--by <WHO>")]
    [Description("Who is deciding. Defaults to the user signed in here.")]
    public string? By { get; init; }

    internal string Who() => By is { Length: > 0 } who ? who.Trim() : System.Environment.UserName;
}

/// <summary>Makes follow-ups into tasks on the run's project.</summary>
[Description("Make follow-ups a run left for later into tasks on the run's project, each with the run as its source. Move one elsewhere afterwards with task move.")]
[CommandMeta(CommandCategory.Start, Intent = "team follow-ups accept make deferred work a task")]
public sealed class TeamFollowUpsAcceptCommand : AsyncCommand<TeamFollowUpDecisionSettings>
{
    private readonly RunFollowUps _followUps;
    private readonly IAnsiConsole _console;

    public TeamFollowUpsAcceptCommand(IRunJournal journal, ITaskService tasks, TimeProvider time, IAnsiConsole console)
    {
        _followUps = new RunFollowUps(journal, tasks, time);
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TeamFollowUpDecisionSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var made = new List<object>();
        var code = CommandOutput.Success();

        foreach (var id in settings.Ids)
        {
            var accepted = await _followUps.AcceptAsync(settings.Run.Trim(), id, settings.Who(), settings.DryRun, cancellationToken).ConfigureAwait(false);

            if (accepted.Failed)
            {
                code = output.IsJson ? (int)accepted.ExitCode : output.Fail(accepted);
                made.Add(new { id, applied = false, error = accepted.Error });
                continue;
            }

            made.Add(new { id, applied = !settings.DryRun, task = TaskOperations.Json(accepted.Value!) });

            if (!output.IsJson)
            {
                output.WriteLine(settings.DryRun
                    ? $"Would make {Markup.Escape(id)} the task \"{Markup.Escape(accepted.Value!.Title)}\". Nothing was changed."
                    : $"Made {Markup.Escape(id)} the task [bold]{Markup.Escape(accepted.Value!.Id)}[/]: {Markup.Escape(accepted.Value.Title)}");
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new { run = settings.Run.Trim(), dryRun = settings.DryRun, results = made });
        }

        return code;
    }
}

/// <summary>Records follow-ups as decided against, so they are not offered again.</summary>
[Description("Dismiss follow-ups a run left for later, so they stop waiting and are not offered again.")]
[CommandMeta(CommandCategory.Start, Intent = "team follow-ups dismiss drop deferred work not wanted")]
public sealed class TeamFollowUpsDismissCommand : Command<TeamFollowUpsDismissCommand.Settings>
{
    private readonly RunFollowUps _followUps;
    private readonly IAnsiConsole _console;

    public TeamFollowUpsDismissCommand(IRunJournal journal, ITaskService tasks, TimeProvider time, IAnsiConsole console)
    {
        _followUps = new RunFollowUps(journal, tasks, time);
        _console = console;
    }

    public sealed class Settings : TeamFollowUpDecisionSettings
    {
        [CommandOption("--why <TEXT>")]
        [Description("Why it is not wanted, kept on the run.")]
        public string? Why { get; init; }
    }

    /// <inheritdoc />
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var done = new List<object>();
        var code = CommandOutput.Success();

        foreach (var id in settings.Ids)
        {
            var dismissed = _followUps.Dismiss(settings.Run.Trim(), id, settings.Who(), settings.Why?.Trim(), settings.DryRun);

            if (dismissed.Failed)
            {
                code = output.IsJson ? (int)dismissed.ExitCode : output.Fail(dismissed);
                done.Add(new { id, applied = false, error = dismissed.Error });
                continue;
            }

            done.Add(new { id, applied = !settings.DryRun, followUp = FollowUpReading.Json(dismissed.Value!) });

            if (!output.IsJson)
            {
                output.WriteLine(settings.DryRun
                    ? $"Would dismiss {Markup.Escape(id)}: {Markup.Escape(dismissed.Value!.Title)}. Nothing was changed."
                    : $"Dismissed {Markup.Escape(id)}: {Markup.Escape(dismissed.Value!.Title)}");
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new { run = settings.Run.Trim(), dryRun = settings.DryRun, results = done });
        }

        return code;
    }
}
