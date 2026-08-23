using System.ComponentModel;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core.Git;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>
/// Records inside a repository which project it belongs to.
/// <para>
/// Written automatically whenever a project is registered, cloned or relocated,
/// so this exists for repositories that were registered before the mark did,
/// and for correcting one that is wrong.
/// </para>
/// </summary>
[Description("Record inside a repository which project it belongs to.")]
public sealed class ProjectLinkCommand : AsyncCommand<ProjectLinkCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;

    public ProjectLinkCommand(IProjectService projects, IGitManager git, IAnsiConsole console)
    {
        _projects = projects;
        _git = git;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project to link. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--all")]
        [Description("Link every registered project present on this machine.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var listed = await _projects.ListAsync().ConfigureAwait(false);
        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var targets = listed.Value!.Where(p => p.LocalPath is not null).ToList();

        if (!settings.All)
        {
            var resolution = settings.Project is not null
                ? await _projects.ResolveAsync(settings.Project).ConfigureAwait(false)
                : await _projects
                    .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory())
                    .ConfigureAwait(false);

            if (resolution.Failed)
            {
                return output.Fail(resolution);
            }

            if (resolution.Value!.LocalPath is null)
            {
                return output.Fail(
                    $"'{resolution.Value.Entry.Name}' is not on this machine, so there is no "
                    + "repository to mark.",
                    ExitCode.RepositoryUnavailable);
            }

            targets = [resolution.Value];
        }

        var linked = 0;

        foreach (var project in targets)
        {
            var existing = await _git
                .GetConfigValueAsync(IProjectService.ProjectMarker, project.LocalPath!)
                .ConfigureAwait(false);

            if (existing.Succeeded && existing.Value == project.Entry.Slug)
            {
                output.WriteVerbose(
                    $"[dim]{Markup.Escape(project.Entry.Slug)} is already linked.[/]");

                continue;
            }

            var written = await _git.SetLocalConfigValueAsync(
                IProjectService.ProjectMarker,
                project.Entry.Slug,
                project.LocalPath!).ConfigureAwait(false);

            if (written.Failed)
            {
                output.WriteLine(
                    $"  [red]failed[/]  {Markup.Escape(project.Entry.Slug)}  "
                    + $"[dim]{Markup.Escape(written.Error ?? string.Empty)}[/]");

                continue;
            }

            linked++;

            output.WriteLine(
                $"  [green]linked[/]  {Markup.Escape(project.Entry.Slug)}  "
                + $"[dim]{Markup.Escape(project.LocalPath!)}[/]");
        }

        if (output.IsJson)
        {
            output.WriteJson(new { linked, considered = targets.Count });
            return CommandOutput.Success();
        }

        output.WriteBlankLine();

        output.WriteLine(linked == 0
            ? "[dim]Everything was already linked.[/]"
            : $"[green]Linked {linked} repositor{(linked == 1 ? "y" : "ies")}.[/] "
              + "[dim]The mark lives in .git/config and is never committed.[/]");

        return CommandOutput.Success();
    }
}
