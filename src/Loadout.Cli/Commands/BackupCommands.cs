using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backups;
using Loadout.Models;
using Loadout.Models.Backups;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Lists the snapshots taken before mutating operations.</summary>
[Description("List backup sets taken before mutating operations.")]
public sealed class BackupListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IBackupService _backups;
    private readonly IAnsiConsole _console;

    public BackupListCommand(IBackupService backups, IAnsiConsole console)
    {
        _backups = backups;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _backups.ListAsync().ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var sets = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                sets = sets.Select(s => new
                {
                    id = s.Id,
                    operation = s.Operation,
                    detail = s.Detail,
                    created = s.CreatedUtc,
                    files = s.Entries.Count(e => e.Existed),
                    creates = s.Entries.Count(e => !e.Existed),
                }),
            });

            return CommandOutput.Success();
        }

        if (sets.Count == 0)
        {
            output.WriteLine("[dim]No backup sets. One is taken before any operation that changes files.[/]");
            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn("Id");
        table.AddColumn("When");
        table.AddColumn("Operation");
        table.AddColumn("Files");

        foreach (var set in sets)
        {
            table.AddRow(
                Markup.Escape(set.Id),
                $"{set.CreatedUtc:dd MMM HH:mm}",
                Markup.Escape(set.Operation + (set.Detail.Length > 0 ? $"  {set.Detail}" : string.Empty)),
                set.Entries.Count(e => e.Existed).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        output.Write(table);
        output.WriteLine("[dim]Undo one with:[/] loadout backup restore <id>");

        return CommandOutput.Success();
    }
}

/// <summary>
/// Puts a backup set back.
/// <para>
/// Shows what it would do unless <c>--apply</c> is passed, and verifies every
/// stored digest before writing anything, so a corrupted set cannot leave the
/// tree half restored.
/// </para>
/// </summary>
[Description("Restore a backup set, undoing the operation that created it.")]
public sealed class BackupRestoreCommand : AsyncCommand<BackupRestoreCommand.Settings>
{
    private readonly IBackupService _backups;
    private readonly IAnsiConsole _console;

    public BackupRestoreCommand(IBackupService backups, IAnsiConsole console)
    {
        _backups = backups;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("Backup set id, or an unambiguous prefix of one.")]
        public string Id { get; init; } = string.Empty;

        [CommandOption("--apply")]
        [Description("Actually restore. Without this the command only reports what it would do.")]
        public bool Apply { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        if (settings.Apply && settings.AllowsPrompting)
        {
            var set = await _backups.GetAsync(settings.Id).ConfigureAwait(false);

            if (set.Failed)
            {
                return output.Fail(set);
            }

            var confirmed = _console.Confirm(
                $"Restore {set.Value!.Entries.Count(e => e.Existed)} file(s) from "
                + $"'{Markup.Escape(set.Value.Id)}'?",
                defaultValue: false);

            if (!confirmed)
            {
                output.WriteLine("[dim]Cancelled.[/]");
                return CommandOutput.Success();
            }
        }

        var result = await _backups.RestoreAsync(settings.Id, settings.Apply).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var report = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = report.Set.Id,
                applied = report.Applied,
                restored = report.Restored,
                removed = report.Removed,
                skipped = report.Skipped,
                drift = report.Drift.Select(d => new
                {
                    file = d.File,
                    key = d.KeyPath,
                    kind = d.Kind.ToString().ToLowerInvariant(),
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[bold]{Markup.Escape(report.Set.Id)}[/]  "
            + $"[dim]{Markup.Escape(report.Set.Operation)} on "
            + $"{report.Set.CreatedUtc:dd MMM yyyy HH:mm} UTC[/]");

        output.WriteBlankLine();

        foreach (var path in report.Restored)
        {
            output.WriteLine($"  restore  {Markup.Escape(path)}");
        }

        foreach (var path in report.Removed)
        {
            // These are files the original operation created. Putting things
            // back means taking them away again, which is worth spelling out
            // rather than leaving somebody to notice later.
            output.WriteLine($"  [yellow]remove[/]   {Markup.Escape(path)}  [dim]created by the operation[/]");
        }

        foreach (var (path, reason) in report.Skipped)
        {
            output.WriteLine($"  [red]skip[/]     {Markup.Escape(path)}  [dim]{Markup.Escape(reason)}[/]");
        }

        WriteDrift(output, report);

        output.WriteBlankLine();

        if (report.Applied)
        {
            output.WriteLine("[green]Restored.[/] [dim]A snapshot of the previous state was taken first, "
                + "so this is itself reversible.[/]");
        }
        else
        {
            output.WriteLine("[dim]Nothing was changed. Add --apply to restore.[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Names the settings a whole-file restore would take away.
    /// <para>
    /// Without this the operation looks clean: every digest matches and the
    /// command reports success, while a key somebody set after the snapshot
    /// disappears with nothing to show it was ever there. Key paths only, never
    /// values, because a settings file can hold a credential.
    /// </para>
    /// </summary>
    private static void WriteDrift(CommandOutput output, RestoreReport report)
    {
        var dropped = report.Dropped.ToList();

        if (dropped.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine("[yellow]Settings that would be lost[/] "
            + "[dim](present now, absent in the backup):[/]");

        foreach (var group in dropped.GroupBy(d => d.File))
        {
            output.WriteLine($"  {Markup.Escape(group.Key)}");

            foreach (var key in group)
            {
                output.WriteLine($"    - {Markup.Escape(key.KeyPath)}");
            }
        }
    }
}
