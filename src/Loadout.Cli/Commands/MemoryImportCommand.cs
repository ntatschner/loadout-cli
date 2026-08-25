using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backups;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Brings memory recorded by an agent's own tooling into the workspace.
/// <para>
/// Several repositories were managed that way before this launcher existed.
/// Without a route in, adopting the launcher would mean starting from nothing
/// on exactly the projects that had accumulated the most, and quietly leaving
/// somebody's curated facts behind on one machine.
/// </para>
/// </summary>
[Description("Import memory an agent recorded outside the workspace.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "bring in adopt existing memory", Mutates = true)]
public sealed class MemoryImportCommand : MemoryCommandBase<MemoryImportCommand.Settings>
{
    private readonly IMemoryImporter _importer;
    private readonly IBackupService _backups;

    public MemoryImportCommand(
        IMemoryImporter importer,
        IBackupService backups,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
        : base(projects, workspace, console)
    {
        _importer = importer;
        _backups = backups;
    }

    public sealed class Settings : MemorySettings
    {
        [CommandOption("--from <DIRECTORY>")]
        [Description("Where to read from. Found automatically when the agent's own layout is used.")]
        public string? From { get; init; }

        [CommandOption("--all")]
        [Description("Every project on this machine that has memory waiting, rather than one.")]
        public bool All { get; init; }

        [CommandOption("--apply")]
        [Description("Actually import. Without this the command only reports what it would do.")]
        public bool ApplyRequested { get; init; }

        /// <summary>
        /// Whether to go ahead, once --dry-run has had its say.
        /// </summary>
        /// <remarks>
        /// --dry-run is accepted on every command and always means the
        /// same thing, so it overrides --apply rather than
        /// competing with it. Asking for both is not a contradiction to
        /// resolve: the more cautious of the two is what was meant.
        /// </remarks>
        public bool Apply => ApplyRequested && !DryRun;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(Console, settings);

        if (settings.All)
        {
            if (settings.Project is not null)
            {
                return output.Fail(
                    "Give a project or --all, not both.",
                    ExitCode.InvalidArguments);
            }

            return await ImportAllAsync(settings, output).ConfigureAwait(false);
        }

        var resolution = settings.Project is not null
            ? await Projects.ResolveAsync(settings.Project).ConfigureAwait(false)
            : await Projects
                .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory())
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;
        var source = settings.From;

        if (source is null)
        {
            if (project.LocalPath is null)
            {
                return output.Fail(
                    $"'{project.Entry.Name}' is not on this machine, so there is nowhere to look "
                    + "for memory it recorded here. Pass --from with a directory.",
                    ExitCode.RepositoryUnavailable);
            }

            source = _importer.Discover(project.LocalPath);

            if (source is null)
            {
                output.WriteLine(
                    "[dim]No memory was found for this project outside the workspace. "
                    + "If it was recorded against a different directory, point at it with:[/] "
                    + "--from <directory>");

                return CommandOutput.Success();
            }
        }

        var previewed = await _importer
            .ImportAsync(Workspace.LocalPath, project.Entry.Slug, source, apply: false)
            .ConfigureAwait(false);

        if (previewed.Failed)
        {
            return output.Fail(previewed);
        }

        var preview = previewed.Value!;

        if (output.IsJson && !settings.Apply)
        {
            WriteJson(output, preview);
            return CommandOutput.Success();
        }

        Render(output, preview);

        if (preview.Imported.Count == 0)
        {
            return CommandOutput.Success();
        }

        if (!settings.Apply)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Add --apply to import.[/]");

            return CommandOutput.Success();
        }

        var captured = await _backups.CaptureAsync(
            "memory import",
            project.Entry.Slug,
            preview.Imported
                .Select(topic => Path.Combine(
                    Workspace.LocalPath, "projects", project.Entry.Slug, "memory",
                    topic.Name + ".md"))
                .ToList()).ConfigureAwait(false);

        if (captured.Failed)
        {
            return output.Fail(
                "The import was not started because a backup could not be taken: "
                + captured.Error,
                captured.ExitCode);
        }

        var applied = await _importer
            .ImportAsync(Workspace.LocalPath, project.Entry.Slug, source, apply: true)
            .ConfigureAwait(false);

        if (applied.Failed)
        {
            return output.Fail(applied);
        }

        if (output.IsJson)
        {
            WriteJson(output, applied.Value!);
            return CommandOutput.Success();
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[green]Imported[/] {applied.Value!.Imported.Count} topic(s), "
            + $"{applied.Value.Facts} fact(s).");

        output.WriteLine(
            $"[dim]Undo it with:[/] loadout backup restore {Markup.Escape(captured.Value!.Id)}");

        output.WriteLine("[dim]The original was copied, not moved. Check the import, then remove "
            + "the old copy yourself so there is only one.[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Imports for every project on this machine that has something waiting.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Memory recorded outside the workspace is the single most common thing
    /// wrong with a registry: fifteen of sixteen projects on the machine this
    /// was written for. That is one finding about how the agents have been
    /// used, not fifteen findings about fifteen projects, and answering it one
    /// project at a time meant typing the same command fifteen times.
    /// </para>
    /// <para>
    /// Each project is still previewed, backed up and imported on its own, so
    /// one failure leaves the rest done and reported rather than abandoning
    /// the run half way.
    /// </para>
    /// </remarks>
    private async Task<int> ImportAllAsync(Settings settings, CommandOutput output)
    {
        var listed = await Projects.ListAsync().ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var waiting = new List<(ProjectResolution Project, string Source, MemoryImport Preview)>();

        foreach (var project in listed.Value!)
        {
            if (project.LocalPath is null)
            {
                continue;
            }

            var source = _importer.Discover(project.LocalPath);

            if (source is null)
            {
                continue;
            }

            var previewed = await _importer
                .ImportAsync(Workspace.LocalPath, project.Entry.Slug, source, apply: false)
                .ConfigureAwait(false);

            if (previewed.Failed || previewed.Value!.Imported.Count == 0)
            {
                continue;
            }

            waiting.Add((project, source, previewed.Value));
        }

        if (waiting.Count == 0)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { projects = Array.Empty<object>() });
            }
            else
            {
                output.WriteLine("[dim]No project has memory waiting outside the workspace.[/]");
            }

            return CommandOutput.Success();
        }

        if (!settings.Apply)
        {
            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    projects = waiting.Select(w => new
                    {
                        project = w.Project.Entry.Slug,
                        source = w.Preview.SourcePath,
                        topics = w.Preview.Imported.Count,
                        facts = w.Preview.Facts,
                    }),
                });

                return CommandOutput.Success();
            }

            foreach (var (project, _, preview) in waiting)
            {
                output.WriteLine(
                    $"  [bold]{Markup.Escape(project.Entry.Name)}[/]  "
                    + $"[dim]{preview.Imported.Count} topic(s), {preview.Facts} fact(s)[/]");
            }

            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Add --apply to import all of it.[/]");

            return CommandOutput.Success();
        }

        var topics = 0;
        var facts = 0;
        var failed = new List<string>();

        foreach (var (project, source, preview) in waiting)
        {
            var captured = await _backups.CaptureAsync(
                "memory import",
                project.Entry.Slug,
                preview.Imported
                    .Select(topic => Path.Combine(
                        Workspace.LocalPath, "projects", project.Entry.Slug, "memory",
                        topic.Name + ".md"))
                    .ToList()).ConfigureAwait(false);

            if (captured.Failed)
            {
                failed.Add($"{project.Entry.Name}: no backup could be taken, so nothing was imported");
                continue;
            }

            var applied = await _importer
                .ImportAsync(Workspace.LocalPath, project.Entry.Slug, source, apply: true)
                .ConfigureAwait(false);

            if (applied.Failed)
            {
                failed.Add($"{project.Entry.Name}: {applied.Error}");
                continue;
            }

            topics += applied.Value!.Imported.Count;
            facts += applied.Value.Facts;

            if (!output.IsJson)
            {
                output.WriteLine(
                    $"  [green]imported[/]  {Markup.Escape(project.Entry.Name)}  "
                    + $"[dim]{applied.Value.Imported.Count} topic(s)[/]");
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new { projects = waiting.Count, topics, facts, failed });
        }
        else
        {
            output.WriteBlankLine();
            output.WriteLine($"[green]Imported[/] {topics} topic(s), {facts} fact(s) "
                + $"across {waiting.Count - failed.Count} project(s).");

            foreach (var failure in failed)
            {
                output.WriteLine($"  [yellow]![/] [dim]{Markup.Escape(failure)}[/]");
            }

            output.WriteLine("[dim]The originals were copied, not moved.[/]");
        }

        return failed.Count == 0
            ? CommandOutput.Success()
            : (int)ExitCode.GeneralFailure;
    }

    private static void Render(CommandOutput output, MemoryImport import)
    {
        output.WriteLine($"[dim]From {Markup.Escape(import.SourcePath)}[/]");
        output.WriteBlankLine();

        foreach (var topic in import.Imported)
        {
            var description = string.IsNullOrWhiteSpace(topic.Description)
                ? string.Empty
                : "  [dim]" + Markup.Escape(topic.Description) + "[/]";

            output.WriteLine(
                $"  [green]import[/]  {Markup.Escape(topic.Name)}  "
                + $"[dim]{topic.Facts.Count} fact(s)[/]{description}");
        }

        foreach (var (name, reason) in import.Skipped)
        {
            var colour = reason.Contains("credential", StringComparison.OrdinalIgnoreCase)
                ? "red"
                : "yellow";

            output.WriteLine(
                $"  [{colour}]skip[/]    {Markup.Escape(name)}  [dim]{Markup.Escape(reason)}[/]");
        }

        if (import.Imported.Count == 0 && import.Skipped.Count == 0)
        {
            output.WriteLine("[dim]There is nothing there to import.[/]");
        }
        else if (import.Imported.Count == 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing left to bring across.[/]");
        }
    }

    private static void WriteJson(CommandOutput output, MemoryImport import) =>
        output.WriteJson(new
        {
            source = import.SourcePath,
            applied = import.Applied,
            facts = import.Facts,
            imported = import.Imported.Select(t => new { t.Name, t.Description, facts = t.Facts.Count }),
            skipped = import.Skipped,
        });
}
