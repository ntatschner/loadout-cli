using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Records a team run that should happen again and again.</summary>
/// <remarks>
/// <para>
/// Recorded here, fired by the daemon. Keeping the two apart means a person
/// can write a schedule down, read it back and change it without anything
/// resident running, and it means the record exists before the thing that acts
/// on it does.
/// </para>
/// <para>
/// Machine-local, deliberately. A schedule in the workspace would travel to
/// every machine that clones it, and three machines waking at nine to run the
/// same sweep on the same repository is one useful run and two that fight it
/// for the branch.
/// </para>
/// </remarks>
[Description("Record a team run to happen again and again on this machine. The daemon fires them.")]
[CommandMeta(CommandCategory.Start, Intent = "team schedule nightly recurring cron every daily", Mutates = true)]
public sealed class TeamScheduleAddCommand : AsyncCommand<TeamScheduleAddCommand.Settings>
{
    private readonly IScheduleService _schedules;
    private readonly IProjectService _projects;
    private readonly ITeamCatalogue _teams;
    private readonly Loadout.Core.Instructions.ISpecialistLibrary _library;
    private readonly Loadout.Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamScheduleAddCommand(
        IScheduleService schedules,
        IProjectService projects,
        ITeamCatalogue teams,
        Loadout.Core.Instructions.ISpecialistLibrary library,
        Loadout.Core.Workspace.IWorkspaceManager workspace,
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

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("What to call this schedule, for the other commands.")]
        public string Name { get; init; } = string.Empty;

        [CommandArgument(1, "<TEAM>")]
        [Description("The team to run, as 'team list' shows it.")]
        public string Team { get; init; } = string.Empty;

        [CommandArgument(2, "<GOAL>")]
        [Description("What to ask it for, in your own words.")]
        public string Goal { get; init; } = string.Empty;

        [CommandOption("--every <DURATION>")]
        [Description("How often it runs: 30m, 2h, 1d. At least five minutes.")]
        public string? Every { get; init; }

        [CommandOption("--at <TIME>")]
        [Description("The time of day it runs, as 09:00, in this machine's own time.")]
        public string? At { get; init; }

        [CommandOption("--autonomy <MODE>")]
        [Description("supervised or autonomous. Never manual: nobody is watching when it fires.")]
        public string? Autonomy { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
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

        // Checked now rather than at three in the morning. A schedule naming a
        // team nobody has is one that fails every night in a log somebody
        // reads once a month.
        var root = _workspace.IsAvailable() ? _workspace.LocalPath : null;
        var specialists = await _library.LoadAsync(root, slug, cancellationToken).ConfigureAwait(false);
        var catalogue = await _teams.LoadAsync(root, slug, specialists, cancellationToken).ConfigureAwait(false);

        if (catalogue.Find(settings.Team) is not { } team)
        {
            return output.Fail(
                $"No team named '{settings.Team}'. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        if (team.Template)
        {
            return output.Fail(
                $"'{team.Name}' is a template, a shape to copy rather than a team to run.",
                ExitCode.InvalidArguments);
        }

        TimeSpan? every = null;

        if (settings.Every is { Length: > 0 } typed)
        {
            if (Duration(typed) is not { } parsed)
            {
                return output.Fail(
                    $"'{typed}' is not a duration. Write it as 30m, 2h or 1d.",
                    ExitCode.InvalidArguments);
            }

            every = parsed;
        }

        TimeOnly? at = null;

        if (settings.At is { Length: > 0 } clock)
        {
            if (!TimeOnly.TryParse(clock, CultureInfo.InvariantCulture, out var parsed))
            {
                return output.Fail($"'{clock}' is not a time of day. Write it as 09:00.", ExitCode.InvalidArguments);
            }

            at = parsed;
        }

        var schedule = new TeamSchedule
        {
            Id = settings.Name,
            Project = slug,
            Team = team.Name,
            Goal = settings.Goal,
            Autonomy = settings.Autonomy ?? team.Rules.Autonomy,
            Every = every,
            At = at,
        };

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was recorded.[/] {Markup.Escape(Describe(schedule, _time.GetUtcNow()))}");

            return CommandOutput.Success();
        }

        var saved = await _schedules.SaveAsync(schedule, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { schedule = saved.Value!.Id, next = ScheduleService.Next(saved.Value!, _time.GetUtcNow()) });

            return CommandOutput.Success();
        }

        output.WriteLine(Markup.Escape(Describe(saved.Value!, _time.GetUtcNow())));
        output.WriteLine("[dim]Nothing fires until the daemon is running:[/] loadout team daemon");

        return CommandOutput.Success();
    }

    /// <summary>A duration as people write one: 30m, 2h, 1d.</summary>
    internal static TimeSpan? Duration(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();

        if (trimmed.Length < 2)
        {
            return null;
        }

        if (!double.TryParse(trimmed[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            return null;
        }

        return trimmed[^1] switch
        {
            'm' => TimeSpan.FromMinutes(count),
            'h' => TimeSpan.FromHours(count),
            'd' => TimeSpan.FromDays(count),
            _ => null,
        };
    }

    /// <summary>One schedule in a sentence.</summary>
    internal static string Describe(TeamSchedule schedule, DateTimeOffset now)
    {
        var when = schedule.Every is { } every
            ? $"every {Spell(every)}"
            : schedule.At is { } at
                ? $"daily at {at:HH:mm}"
                : "never";

        var next = ScheduleService.Next(schedule, now) is { } due
            ? $", next {due.ToLocalTime():yyyy-MM-dd HH:mm}"
            : schedule.Enabled ? string.Empty : ", paused";

        return $"{schedule.Id}: {schedule.Team} on {schedule.Project}, {when}, {schedule.Autonomy}{next}. "
            + schedule.Goal;
    }

    private static string Spell(TimeSpan every) =>
        every.TotalDays >= 1 ? $"{every.TotalDays:0.#} day(s)"
        : every.TotalHours >= 1 ? $"{every.TotalHours:0.#} hour(s)"
        : $"{every.TotalMinutes:0.#} minute(s)";
}

/// <summary>What is scheduled on this machine.</summary>
[Description("List the team runs scheduled on this machine, and when each is next due.")]
[CommandMeta(CommandCategory.Start, Intent = "team schedules list what is scheduled nightly")]
public sealed class TeamScheduleListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IScheduleService _schedules;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TeamScheduleListCommand(IScheduleService schedules, IAnsiConsole console, TimeProvider time)
    {
        _schedules = schedules;
        _console = console;
        _time = time;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var listed = await _schedules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var now = _time.GetUtcNow();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                schedules = listed.Value!.Select(schedule => new
                {
                    schedule.Id,
                    schedule.Project,
                    schedule.Team,
                    schedule.Goal,
                    schedule.Autonomy,
                    every = schedule.Every,
                    at = schedule.At?.ToString("HH:mm", CultureInfo.InvariantCulture),
                    schedule.Enabled,
                    lastRun = schedule.LastRun,
                    schedule.LastRunId,
                    next = ScheduleService.Next(schedule, now),
                    due = ScheduleService.IsDue(schedule, now),
                }),
            });

            return CommandOutput.Success();
        }

        if (listed.Value!.Count == 0)
        {
            output.WriteLine("Nothing is scheduled on this machine.");
            output.WriteLine(
                "[dim]Add one with:[/] loadout team schedule add <name> <team> \"<goal>\" --at 09:00");

            return CommandOutput.Success();
        }

        foreach (var schedule in listed.Value!)
        {
            output.WriteLine(
                (ScheduleService.IsDue(schedule, now) ? "[yellow]due[/] " : string.Empty)
                + Markup.Escape(TeamScheduleAddCommand.Describe(schedule, now)));
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Nothing fires until the daemon is running:[/] loadout team daemon");

        return CommandOutput.Success();
    }
}

/// <summary>Forgets a schedule.</summary>
[Description("Remove a scheduled team run from this machine.")]
[CommandMeta(CommandCategory.Start, Intent = "team schedule remove delete stop scheduling", Mutates = true)]
public sealed class TeamScheduleRemoveCommand : AsyncCommand<TeamScheduleRemoveCommand.Settings>
{
    private readonly IScheduleService _schedules;
    private readonly IAnsiConsole _console;

    public TeamScheduleRemoveCommand(IScheduleService schedules, IAnsiConsole console)
    {
        _schedules = schedules;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<NAME>")]
        [Description("The schedule, as 'team schedule list' shows it.")]
        public string Name { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was removed.[/] '{Markup.Escape(settings.Name)}' would go.");

            return CommandOutput.Success();
        }

        var removed = await _schedules.RemoveAsync(settings.Name, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine($"'{Markup.Escape(settings.Name)}' will not run again.");

        return CommandOutput.Success();
    }
}
