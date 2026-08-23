using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Lists a project's working trees (spec section 71).
/// <para>
/// Worktrees are how one repository holds several branches checked out at once,
/// and each is a legitimate place to launch an agent. Listing them is what
/// makes <c>--worktree</c> discoverable rather than something a user has to
/// already know the name of.
/// </para>
/// </summary>
[Description("List the working trees available for a project.")]
public sealed class WorktreeListCommand : AsyncCommand<WorktreeListCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;

    public WorktreeListCommand(IProjectService projects, IGitManager git, IAnsiConsole console)
    {
        _projects = projects;
        _git = git;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the current repository.")]
        public string? Project { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolution = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        if (project.LocalPath is null)
        {
            return output.Fail(
                $"'{project.Entry.Name}' is not present on this machine.",
                ExitCode.RepositoryUnavailable);
        }

        var result = await _git.ListWorktreesAsync(project.LocalPath).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var worktrees = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = project.Entry.Slug,
                worktrees = worktrees.Select(w => new
                {
                    path = w.Path,
                    branch = w.Branch,
                    primary = w.IsPrimary,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(project.Entry.Name)}[/]");
        output.WriteBlankLine();

        foreach (var worktree in worktrees)
        {
            var label = worktree.Branch ?? Path.GetFileName(worktree.Path);
            var marker = worktree.IsPrimary ? "[green]*[/]" : " ";

            output.WriteLine(
                $"{marker} {Markup.Escape(label)}  [dim]{Markup.Escape(worktree.Path)}[/]");
        }

        if (worktrees.Count > 1)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[dim]Launch in one with:[/] loadout {Markup.Escape(project.Entry.Slug)} "
                + "--worktree <name>");
        }

        return CommandOutput.Success();
    }
}
