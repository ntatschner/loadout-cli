using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Deletes one of a project's memory topics.</summary>
/// <remarks>
/// <para>
/// There was no way to remove a topic short of deleting its file and
/// reindexing by hand, so a page offering it would have had a button with no
/// command behind it. This is the command, and the backup it takes lives in
/// Core with the others, so the page and this cannot differ about it.
/// </para>
/// <para>
/// Topics that link to it are named, not changed. A link to a topic that does
/// not exist marks something worth writing later, and the audit points at it.
/// </para>
/// </remarks>
[Description("Delete a memory topic, after backing it up. Topics that link to it are named and left as they are.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "memory delete remove forget topic", Mutates = true)]
public sealed class MemoryDeleteCommand : MemoryCommandBase<MemoryDeleteCommand.Settings>
{
    private readonly IMemoryChanges _changes;

    public MemoryDeleteCommand(
        IMemoryChanges changes,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console) => _changes = changes;

    public sealed class Settings : MemorySettings
    {
        [CommandArgument(0, "<topic>")]
        [Description("The topic to delete, as 'loadout memory list' names it.")]
        public string Topic { get; init; } = string.Empty;

        [CommandArgument(1, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public override string? Project { get; init; }

        [CommandOption("--yes")]
        [Description("Do not ask before deleting it.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);
        var slug = await ResolveSlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var preview = await _changes
            .PreviewDeleteAsync(Workspace.LocalPath, slug.Value!, settings.Topic, cancellationToken)
            .ConfigureAwait(false);

        if (preview.Failed)
        {
            return output.Fail(preview);
        }

        var deletion = preview.Value!;

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(Json(slug.Value!, deletion, applied: false, backup: null));

                return CommandOutput.Success();
            }

            Describe(output, deletion);
            output.WriteLine("[dim]Nothing was deleted.[/]");

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            Describe(output, deletion);

            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That would delete '{deletion.Topic.Name}', and nobody is here to agree to it. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!Console.Confirm($"Delete '{deletion.Topic.Name}'? A backup is taken first", false))
            {
                output.WriteLine("[dim]Nothing was deleted.[/]");

                return CommandOutput.Success();
            }
        }

        var deleted = await _changes
            .DeleteAsync(Workspace.LocalPath, slug.Value!, deletion.Topic.Name, cancellationToken)
            .ConfigureAwait(false);

        if (deleted.Failed)
        {
            return output.Fail(deleted);
        }

        var backup = deleted.Value!.Backup!;

        if (output.IsJson)
        {
            output.WriteJson(Json(slug.Value!, deletion, applied: true, backup.Id));

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]Deleted[/] {Markup.Escape(deletion.Topic.Name)}.");
        output.WriteLine($"[dim]Undo it with:[/] loadout backup restore {Markup.Escape(backup.Id)}");

        return CommandOutput.Success();
    }

    /// <summary>The topic, what it held, and what links to it.</summary>
    private static void Describe(CommandOutput output, MemoryDeletion deletion)
    {
        output.WriteLine(
            $"Would delete [bold]{Markup.Escape(deletion.Topic.Name)}[/] "
            + $"[dim]({deletion.Topic.Facts.Count} fact(s)): {Markup.Escape(deletion.Topic.Description)}[/]");

        if (deletion.LinkedFrom.Count > 0)
        {
            output.WriteLine(
                $"[yellow]Linked from[/] {Markup.Escape(string.Join(", ", deletion.LinkedFrom))}. "
                + "[dim]Those links are left as they are; the audit will point at them.[/]");
        }
    }

    private static object Json(string slug, MemoryDeletion deletion, bool applied, string? backup) => new
    {
        project = slug,
        topic = deletion.Topic.Name,
        facts = deletion.Topic.Facts.Count,
        linkedFrom = deletion.LinkedFrom,
        applied,
        backup,
    };
}
