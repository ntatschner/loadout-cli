using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backups;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Reports agent state on this machine that the workspace does not account for,
/// and optionally adopts what can be adopted without guessing.
/// <para>
/// Agents key their state by the directory they were started in, which is not
/// always a repository. Somebody working across several repositories from their
/// parent accumulates memory against that parent, where it describes all of them
/// and belongs to none of them. Nothing surfaced that before: the state simply
/// sat there while the launcher reported those projects as having none.
/// </para>
/// </summary>
[Description("Find agent state on this machine that no project accounts for.")]
public sealed class ProjectSurveyCommand : AsyncCommand<ProjectSurveyCommand.Settings>
{
    private readonly IRepositoryAttribution _attribution;
    private readonly IProjectService _projects;
    private readonly IMemoryImporter _importer;
    private readonly IWorkspaceManager _workspace;
    private readonly IBackupService _backups;
    private readonly IAnsiConsole _console;

    public ProjectSurveyCommand(
        IRepositoryAttribution attribution,
        IProjectService projects,
        IMemoryImporter importer,
        IWorkspaceManager workspace,
        IBackupService backups,
        IAnsiConsole console)
    {
        _attribution = attribution;
        _projects = projects;
        _importer = importer;
        _workspace = workspace;
        _backups = backups;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--adopt")]
        [Description("Also register unregistered repositories and import their memory.")]
        public bool Adopt { get; init; }

        [CommandOption("--apply")]
        [Description("With --adopt, actually do it. Without this it only shows what it would do.")]
        public bool Apply { get; init; }

        [CommandOption("--yes")]
        [Description("Accept every proposed adoption without asking.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var surveyed = await _attribution.SurveyAsync().ConfigureAwait(false);
        if (surveyed.Failed)
        {
            return output.Fail(surveyed);
        }

        var found = surveyed.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                state = found.Select(item => new
                {
                    path = item.StatePath,
                    recordedAgainst = item.SubjectPath,
                    kind = item.Kind.ToString().ToLowerInvariant(),
                    project = item.Slug,
                    repositories = item.Repositories,
                    topics = item.Topics,
                }),
            });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine("[dim]No agent state was found outside the workspace.[/]");
            return CommandOutput.Success();
        }

        foreach (var item in found)
        {
            Describe(output, item);
        }

        if (!settings.Adopt)
        {
            output.WriteBlankLine();
            output.WriteLine(
                "[dim]Nothing here was changed. This only reports what is on the machine. "
                + "Adopt what can be adopted with:[/] --adopt");

            return CommandOutput.Success();
        }

        return await AdoptAsync(output, settings, found).ConfigureAwait(false);
    }

    private static void Describe(CommandOutput output, StateAttribution item)
    {
        output.WriteBlankLine();
        output.WriteLine(
            $"[bold]{Markup.Escape(item.SubjectPath)}[/]  [dim]{item.Topics} topic(s)[/]");

        switch (item.Kind)
        {
            case AttributionKind.Project:
                output.WriteLine($"  [green]{Markup.Escape(item.Slug!)}[/]  [dim]a registered project[/]");
                break;

            case AttributionKind.Unregistered:
                output.WriteLine("  [yellow]a repository that is not registered[/]");
                break;

            case AttributionKind.Container:
                // Named, not chosen. The state describes work across all of
                // these, so picking one would be a guess presented as a fact,
                // and the wrong guess files a repository's hard-won notes under
                // its neighbour.
                output.WriteLine(
                    $"  [yellow]holds {item.Repositories.Count} "
                    + $"{(item.Repositories.Count == 1 ? "repository" : "repositories")}[/] "
                    + "[dim]so this was recorded from the parent rather than inside one[/]");

                foreach (var repository in item.Repositories)
                {
                    output.WriteLine($"    {Markup.Escape(Path.GetFileName(repository))}");
                }

                output.WriteLine(
                    "  [dim]decide which project it belongs to, then:[/] "
                    + $"loadout memory import <project> --from {Markup.Escape(item.StatePath)}");
                break;

            case AttributionKind.NotARepository:
                output.WriteLine(
                    "  [dim]not a repository, and holds none, so this state describes work done "
                    + "somewhere that is not a project[/]");
                break;

            default:
                output.WriteLine(
                    "  [dim]nothing is there any more, so this describes a directory that has "
                    + "moved or gone[/]");
                break;
        }
    }

    /// <summary>
    /// Takes on what can be taken on without a judgement call.
    /// <para>
    /// Only a registered project with memory waiting, and a repository that is
    /// plainly one repository. Everything else is reported and left: a
    /// directory holding several repositories needs somebody to say which one
    /// the state describes, and no amount of care in this method can supply
    /// that.
    /// </para>
    /// </summary>
    private async Task<int> AdoptAsync(
        CommandOutput output,
        Settings settings,
        IReadOnlyList<StateAttribution> found)
    {
        var adoptable = found
            .Where(item => item.Kind is AttributionKind.Project or AttributionKind.Unregistered)
            .ToList();

        output.WriteBlankLine();

        if (adoptable.Count == 0)
        {
            output.WriteLine(
                "[dim]Nothing here can be adopted without a judgement call. See the notes above.[/]");

            return CommandOutput.Success();
        }

        if (!settings.Apply)
        {
            output.WriteLine("[bold]Adopting would:[/]");

            foreach (var item in adoptable)
            {
                output.WriteLine(item.Kind == AttributionKind.Unregistered
                    ? $"  register {Markup.Escape(item.SubjectPath)}, then import {item.Topics} topic(s)"
                    : $"  import up to {item.Topics} topic(s) into {Markup.Escape(item.Slug!)}");
            }

            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Add --apply to do it.[/]");

            return CommandOutput.Success();
        }

        if (!settings.Yes && !settings.AllowsPrompting)
        {
            // Registering repositories and writing into the workspace is not
            // something to do because nobody could be asked (spec section 37).
            return output.Fail(
                "Adopting registers repositories and writes to the workspace, so it needs "
                + "confirmation. Pass --yes to accept every proposal.",
                ExitCode.InvalidArguments);
        }

        var adopted = 0;

        foreach (var item in adoptable)
        {
            var question = item.Kind == AttributionKind.Unregistered
                ? $"Register '{Markup.Escape(item.SubjectPath)}' and import {item.Topics} topic(s)?"
                : $"Import up to {item.Topics} topic(s) into '{Markup.Escape(item.Slug!)}'?";

            // Asked one at a time. These are independent decisions, and somebody
            // may well want one repository adopted and not its neighbour.
            if (!settings.Yes && !_console.Confirm(question, defaultValue: false))
            {
                output.WriteLine("  [dim]skipped[/]");
                continue;
            }

            var slug = item.Slug;

            if (item.Kind == AttributionKind.Unregistered)
            {
                var added = await _projects.AddAsync(item.SubjectPath).ConfigureAwait(false);

                if (added.Failed)
                {
                    output.WriteLine(
                        $"  [red]could not register[/] {Markup.Escape(item.SubjectPath)}  "
                        + $"[dim]{Markup.Escape(added.Error ?? string.Empty)}[/]");

                    continue;
                }

                slug = added.Value!.Entry.Slug;

                output.WriteLine($"  [green]registered[/] {Markup.Escape(slug)}");
            }

            var imported = await ImportAsync(output, slug!, item.StatePath).ConfigureAwait(false);

            if (imported)
            {
                adopted++;
            }
        }

        output.WriteBlankLine();
        output.WriteLine(adopted == 0
            ? "[dim]Nothing was adopted.[/]"
            : $"[green]Adopted {adopted}.[/] [dim]The originals were copied, not moved.[/]");

        return CommandOutput.Success();
    }

    private async Task<bool> ImportAsync(CommandOutput output, string slug, string statePath)
    {
        var preview = await _importer
            .ImportAsync(_workspace.LocalPath, slug, statePath, apply: false)
            .ConfigureAwait(false);

        if (preview.Failed)
        {
            output.WriteLine($"  [red]could not read the memory[/]  "
                + $"[dim]{Markup.Escape(preview.Error ?? string.Empty)}[/]");

            return false;
        }

        if (preview.Value!.Imported.Count == 0)
        {
            output.WriteLine("  [dim]nothing left to bring across[/]");

            foreach (var (name, reason) in preview.Value.Skipped)
            {
                output.WriteLine($"    [dim]{Markup.Escape(name)}: {Markup.Escape(reason)}[/]");
            }

            return false;
        }

        var captured = await _backups.CaptureAsync(
            "project survey adopt",
            slug,
            preview.Value.Imported
                .Select(topic => Path.Combine(
                    _workspace.LocalPath, "projects", slug, "memory", topic.Name + ".md"))
                .ToList()).ConfigureAwait(false);

        if (captured.Failed)
        {
            output.WriteLine(
                "  [red]no backup could be taken, so nothing was imported[/]  "
                + $"[dim]{Markup.Escape(captured.Error ?? string.Empty)}[/]");

            return false;
        }

        var applied = await _importer
            .ImportAsync(_workspace.LocalPath, slug, statePath, apply: true)
            .ConfigureAwait(false);

        if (applied.Failed)
        {
            output.WriteLine($"  [red]import failed[/]  "
                + $"[dim]{Markup.Escape(applied.Error ?? string.Empty)}[/]");

            return false;
        }

        output.WriteLine(
            $"  [green]imported[/] {applied.Value!.Imported.Count} topic(s), "
            + $"{applied.Value.Facts} fact(s)  "
            + $"[dim]undo: loadout backup restore {Markup.Escape(captured.Value!.Id)}[/]");

        foreach (var (name, reason) in applied.Value.Skipped)
        {
            output.WriteLine($"    [dim]{Markup.Escape(name)}: {Markup.Escape(reason)}[/]");
        }

        return true;
    }
}
