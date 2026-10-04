using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What the removal commands and the bin commands share: how long the bin
/// keeps things, and clearing out what has been there longer.
/// </summary>
internal static class Binning
{
    /// <summary>The configured retention, or the default where none is set.</summary>
    /// <remarks>
    /// A configuration that will not load is read as the default rather than
    /// as a failure. Refusing to remove a run because a settings file is
    /// unreadable would be a stranger failure than keeping it thirty days.
    /// </remarks>
    internal static async Task<int> DaysAsync(IConfigurationService configuration, CancellationToken ct)
    {
        var machine = await configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        return TeamBin.Days(machine.Value?.Teams.BinDays);
    }

    /// <summary>How long the bin keeps things, as the end of a sentence.</summary>
    internal static string Kept(int days) =>
        days switch
        {
            <= 0 => "until you empty it",
            1 => "for 1 day",
            _ => $"for {days.ToString(CultureInfo.InvariantCulture)} days",
        };

    /// <summary>
    /// Deletes what has been in the bin longer than it is kept.
    /// </summary>
    /// <remarks>
    /// Called whenever something is put in the bin, as well as by the daemon.
    /// A machine that never runs the daemon still removes things, and without
    /// this its bin would only ever grow.
    /// </remarks>
    internal static IReadOnlyList<BinEntry> Sweep(TeamBin bin, TimeProvider time, int days) =>
        bin.Sweep(time.GetUtcNow(), days);

    /// <summary>One entry as a line: what it is, when it went, how long it has left, how big it is.</summary>
    internal static string Line(BinEntry entry, DateTimeOffset now, int days)
    {
        var left = entry.DaysLeft(now, days) is { } count
            ? count == 1 ? "1 day left" : $"{count} days left"
            : "kept until emptied";

        return $"{entry.KindWord,-5} [bold]{Markup.Escape(entry.Name),-28}[/] "
            + $"[dim]removed {entry.Removed.ToLocalTime():yyyy-MM-dd HH:mm}, {left}, "
            + $"{TeamRunsRemoveCommand.Size(entry.Bytes)}[/]";
    }

    /// <summary>One entry for <c>--json</c>.</summary>
    internal static object Json(BinEntry entry, DateTimeOffset now, int days) => new
    {
        kind = entry.KindWord,
        name = entry.Name,
        team = entry.Team,
        removed = entry.Removed,
        expires = entry.Expires(days),
        daysLeft = entry.DaysLeft(now, days),
        bytes = entry.Bytes,
        from = entry.From,
        unmerged = entry.Unmerged,
        directory = entry.Directory,
    };
}

/// <summary>
/// What is in the bin.
/// </summary>
/// <remarks>
/// Its own listing rather than a flag on <c>team runs</c>, because the bin
/// holds teams as well as runs and somebody looking for a team they removed
/// would not think to look in a list of runs.
/// </remarks>
[Description("List the runs and teams in the bin, when each was removed and how long it has left.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team bin trash recycle removed deleted runs teams undo get back restore")]
public sealed class TeamBinCommand : AsyncCommand<GlobalSettings>
{
    private readonly TeamBin _bin;
    private readonly IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamBinCommand(
        TeamBin bin,
        IConfigurationService configuration,
        TimeProvider time,
        IAnsiConsole console)
    {
        _bin = bin;
        _configuration = configuration;
        _time = time;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var days = await Binning.DaysAsync(_configuration, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var entries = _bin.List();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                keptDays = days,
                entries = entries.Select(entry => Binning.Json(entry, now, days)),
            });

            return CommandOutput.Success();
        }

        if (entries.Count == 0)
        {
            output.WriteLine($"[dim]The bin is empty. Removed runs and teams wait here {Binning.Kept(days)}.[/]");

            return CommandOutput.Success();
        }

        foreach (var entry in entries)
        {
            output.WriteLine(Binning.Line(entry, now, days));
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Get a run back with:[/] loadout team runs restore [[<run>]]");
        output.WriteLine("[dim]Get a team back with:[/] loadout team restore [[<team>]]");
        output.WriteLine("[dim]Delete them for good with:[/] loadout team bin empty");

        return CommandOutput.Success();
    }
}

/// <summary>Deleting what is in the bin, for good.</summary>
/// <remarks>
/// The one command here with no way back, so it asks the way <c>team runs
/// prune</c> asks: everything it would take is named first, a terminal is asked
/// to agree, and anything without one has to say <c>--yes</c>.
/// </remarks>
[Description("Delete what is in the bin for good: everything, or only what has been there longer than an age.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team bin empty purge delete for good clear trash", Mutates = true)]
public sealed class TeamBinEmptyCommand : AsyncCommand<TeamBinEmptyCommand.Settings>
{
    private readonly TeamBin _bin;
    private readonly IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamBinEmptyCommand(
        TeamBin bin,
        IConfigurationService configuration,
        TimeProvider time,
        IAnsiConsole console)
    {
        _bin = bin;
        _configuration = configuration;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[name]")]
        [Description("Only these, as 'team bin' names them: a run's identifier or a team's name. Everything when none is named.")]
        public string[] Names { get; init; } = [];

        [CommandOption("--older-than <AGE>")]
        [Description("Take only what has been in the bin longer than this: 30d, 12h, 90m.")]
        public string? OlderThan { get; init; }

        [CommandOption("--yes")]
        [Description("Do not ask before deleting them.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        TimeSpan? age = null;

        if (settings.OlderThan is { Length: > 0 } said)
        {
            age = TeamScheduleAddCommand.Duration(said);

            if (age is null)
            {
                return output.Fail(
                    $"'{said}' is not an age. Write one as 30d, 12h or 90m.",
                    ExitCode.InvalidArguments);
            }
        }

        var days = await Binning.DaysAsync(_configuration, cancellationToken).ConfigureAwait(false);
        var now = _time.GetUtcNow();
        var chosen = _bin.Choose(now, age);

        // Named: only those, and a name that is not in the bin at all is a
        // mistake to say out loud rather than a quiet "nothing to delete" -
        // the garbage room's "delete for good" names one thing, and a typo
        // there would otherwise look like it had worked.
        if (settings.Names.Length > 0)
        {
            var all = _bin.List();
            var missing = settings.Names
                .Where(name => all.All(entry => !string.Equals(entry.Name, name, StringComparison.Ordinal)))
                .ToList();

            if (missing.Count > 0)
            {
                return output.Fail(
                    $"Not in the bin: {string.Join(", ", missing)}. See what is with: loadout team bin",
                    ExitCode.ProjectNotFound);
            }

            chosen = [.. chosen.Where(entry => settings.Names.Contains(entry.Name, StringComparer.Ordinal))];
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    dryRun = true,
                    deleting = chosen.Select(entry => Binning.Json(entry, now, days)),
                });

                return CommandOutput.Success();
            }

            foreach (var entry in chosen)
            {
                output.WriteLine("[dim]Would delete[/] " + Binning.Line(entry, now, days));
            }

            output.WriteLine(chosen.Count == 0
                ? "[dim]Nothing in the bin to delete.[/]"
                : "[dim]Nothing was deleted. Repeat without --dry-run to delete them.[/]");

            return CommandOutput.Success();
        }

        if (chosen.Count == 0)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dryRun = false, deleted = Array.Empty<object>(), refused = Array.Empty<string>() });
            }
            else
            {
                output.WriteLine("[dim]Nothing in the bin to delete.[/]");
            }

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            // Named before the question, for the reason prune names them: a
            // count is something people agree to without knowing what is in it.
            foreach (var entry in chosen)
            {
                output.WriteLine("  " + Binning.Line(entry, now, days));
            }

            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That would delete {chosen.Count} thing(s) for good, and nobody is here to "
                    + "agree to it. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm(
                $"Delete {chosen.Count} thing(s) for good? Nothing brings them back", false))
            {
                output.WriteLine("[dim]Nothing was deleted.[/]");

                return CommandOutput.Success();
            }
        }

        var deleted = new List<BinEntry>();
        var refused = new List<string>();

        foreach (var entry in chosen)
        {
            var gone = _bin.Delete(entry);

            if (gone.Failed)
            {
                refused.Add(gone.Error!);

                continue;
            }

            deleted.Add(entry);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = false,
                deleted = deleted.Select(entry => Binning.Json(entry, now, days)),
                refused,
            });
        }
        else
        {
            foreach (var entry in deleted)
            {
                output.WriteLine(
                    $"[green]+[/] Deleted {entry.KindWord} [bold]{Markup.Escape(entry.Name)}[/] for good");
            }

            foreach (var why in refused)
            {
                output.WriteLine($"[yellow]-[/] {Shown.Safely(why)}");
            }
        }

        return refused.Count == 0 ? CommandOutput.Success() : (int)ExitCode.GeneralFailure;
    }
}

/// <summary>Getting a removed run back.</summary>
[Description("Put a run back from the bin, where it was before it was removed.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team runs restore undo remove get back run from bin undelete", Mutates = true)]
public sealed class TeamRunsRestoreCommand : AsyncCommand<TeamRunsRestoreCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly TeamBin _bin;
    private readonly IAnsiConsole _console;

    public TeamRunsRestoreCommand(IRunJournal journal, TeamBin bin, IAnsiConsole console)
    {
        _journal = journal;
        _bin = bin;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<run>")]
        [Description("The run to put back, as 'team bin' names it.")]
        public string Run { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var restored = _bin.RestoreRun(settings.Run, _journal.DirectoryOf(settings.Run), settings.DryRun);

        if (restored.Failed)
        {
            return Task.FromResult(output.Fail(restored));
        }

        var entry = restored.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = settings.DryRun,
                run = entry.Name,
                team = entry.Team,
                directory = _journal.DirectoryOf(settings.Run),
            });

            return Task.FromResult(CommandOutput.Success());
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Would restore[/] [bold]{Markup.Escape(entry.Name)}[/]"
                + (entry.Team is { Length: > 0 } ? $" [dim]({Markup.Escape(entry.Team)})[/]" : string.Empty));
            output.WriteLine("[dim]Nothing was restored.[/]");

            return Task.FromResult(CommandOutput.Success());
        }

        output.WriteLine(
            $"[green]+[/] Restored [bold]{Markup.Escape(entry.Name)}[/]"
            + (entry.Team is { Length: > 0 } ? $" [dim]({Markup.Escape(entry.Team)})[/]" : string.Empty));
        output.WriteLine($"[dim]See it with:[/] loadout team status {Markup.Escape(entry.Name)}");

        return Task.FromResult(CommandOutput.Success());
    }
}

/// <summary>Getting a removed team back.</summary>
/// <remarks>
/// The most recent copy, because a name removed twice is two files and the
/// newer one is the one somebody who has just typed <c>team remove</c> by
/// mistake wants. The older ones are still listed by <c>team bin</c>.
/// </remarks>
[Description("Put a team of yours back from the bin: the copy removed most recently.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team restore undo remove get back team from bin undelete", Mutates = true)]
public sealed class TeamRestoreCommand : AsyncCommand<TeamRestoreCommand.Settings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly TeamBin _bin;
    private readonly IAnsiConsole _console;

    public TeamRestoreCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        TeamBin bin,
        IAnsiConsole console)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _bin = bin;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<team>")]
        [Description("The team to put back.")]
        public string Name { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, _, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        // Refused rather than put beside it. Two files defining one name is a
        // catalogue deciding silently which of them wins.
        if (catalogue.Find(settings.Name) is not null)
        {
            return output.Fail(
                $"There is already a team called '{settings.Name}'. Remove it first, or copy the bin's "
                + "file by hand; 'loadout team bin --json' says where it is.",
                ExitCode.InvalidArguments);
        }

        var restored = _bin.RestoreTeam(settings.Name, settings.DryRun);

        if (restored.Failed)
        {
            return output.Fail(restored);
        }

        var entry = restored.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new { dryRun = settings.DryRun, team = entry.Name, path = entry.From });

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Would restore[/] [bold]{Markup.Escape(entry.Name)}[/] to {Markup.Escape(entry.From ?? string.Empty)}");
            output.WriteLine("[dim]Nothing was restored.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Restored [bold]{Markup.Escape(entry.Name)}[/]");
        output.WriteLine($"  [dim]{Markup.Escape(entry.From ?? string.Empty)}[/]");

        if (TeamRemoveCommand.InWorkspace(_workspace, entry.From))
        {
            output.WriteLine(
                "[dim]The workspace has the file back as a change, saved like any other with:[/] "
                + "loadout workspace save");
        }

        return CommandOutput.Success();
    }
}
