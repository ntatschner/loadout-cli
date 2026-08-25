using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Lists registered projects and whether each is available here (spec section 75).</summary>
[Description("List registered projects.")]
[CommandMeta(CommandCategory.Projects, Intent = "show all registered repositories")]
public sealed class ProjectListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectListCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _projects.ListAsync().ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var projects = result.Value!;

        if (output.IsJson)
        {
            // The shape here is a public contract (spec section 38); renaming
            // a field breaks somebody's script.
            output.WriteJson(new
            {
                projects = projects.Select(p => new
                {
                    id = p.Entry.Slug,
                    uuid = p.Entry.Id,
                    name = p.Entry.Name,
                    available = p.IsAvailableLocally,
                    path = p.LocalPath,
                    defaultAgent = p.Entry.DefaultAgent,
                    pinned = p.Pinned,
                    lastLaunched = p.LastLaunchedUtc,
                }),
            });

            return CommandOutput.Success();
        }

        if (projects.Count == 0)
        {
            output.WriteLine("[dim]No projects are registered. Add one with: loadout project add <path>[/]");
            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn(string.Empty);
        table.AddColumn("Project");
        table.AddColumn("Agent");
        table.AddColumn("Location");

        foreach (var project in projects)
        {
            var location = project.IsAvailableLocally
                ? Markup.Escape(project.LocalPath!)
                : "[yellow]not on this machine[/]";

            table.AddRow(
                project.Pinned ? "[yellow]*[/]" : " ",
                Markup.Escape(project.Entry.Name),
                Markup.Escape(project.Entry.DefaultAgent),
                location);
        }

        output.Write(table);

        return CommandOutput.Success();
    }
}

/// <summary>Registers an existing local repository (spec sections 25 and 26).</summary>
[Description("Register an existing local Git repository as a project.")]
public sealed class ProjectAddCommand : AsyncCommand<ProjectAddCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectAddCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[path]")]
        [Description("Repository path. Defaults to the current directory.")]
        public string? Path { get; init; }

        [CommandOption("--slug <SLUG>")]
        [Description("Project slug. Inferred from the remote or directory name when omitted.")]
        public string? Slug { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);
        var path = settings.Path ?? settings.Repo ?? Directory.GetCurrentDirectory();

        var result = await _projects.AddAsync(path, settings.Slug).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var project = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = project.Entry.Slug,
                uuid = project.Entry.Id,
                name = project.Entry.Name,
                path = project.LocalPath,
                remote = project.Entry.Remote,
            });
        }
        else
        {
            output.WriteLine(
                $"[green]Registered[/] {Markup.Escape(project.Entry.Name)} "
                + $"[dim]({Markup.Escape(project.Entry.Slug)})[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Removes a project registration without touching its source (spec section 75).</summary>
[Description("Remove a project registration. Never deletes source code.")]
public sealed class ProjectRemoveCommand : AsyncCommand<ProjectRemoveCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectRemoveCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandOption("--from-workspace")]
        [Description("Also remove the shared definition, affecting every machine.")]
        public bool FromWorkspace { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        // Removing the shared definition affects every machine that uses the
        // workspace, so it is confirmed rather than assumed. In non-interactive
        // mode the flag itself is taken as the confirmation.
        if (settings.FromWorkspace && settings.AllowsPrompting)
        {
            var confirmed = _console.Confirm(
                $"Remove '{Markup.Escape(settings.Project)}' from the shared workspace for all machines?",
                defaultValue: false);

            if (!confirmed)
            {
                output.WriteLine("[dim]Cancelled.[/]");
                return CommandOutput.Success();
            }
        }

        var result = await _projects.RemoveAsync(settings.Project, settings.FromWorkspace)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine(
            $"[green]Removed[/] {Markup.Escape(settings.Project)} "
            + "[dim](the repository itself was not touched)[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Scans the configured discovery roots (spec section 64).</summary>
[Description("Scan the configured roots for Git repositories.")]
public sealed class ProjectDiscoverCommand : AsyncCommand<GlobalSettings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectDiscoverCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _projects.DiscoverAsync().ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var found = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                repositories = found.Select(r => new
                {
                    path = r.Path,
                    name = r.Name,
                    remote = r.RemoteUrl,
                    registered = r.IsRegistered,
                    slug = r.MatchedSlug,
                }),
            });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine(
                "[dim]No repositories found. Check the discovery roots with: loadout doctor[/]");
            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]Repositories discovered[/] [dim]({found.Count})[/]");
        output.WriteBlankLine();

        foreach (var repository in found)
        {
            var marker = repository.IsRegistered ? "[green]+[/]" : "[yellow]?[/]";
            var suffix = repository.IsRegistered
                ? $"[dim]registered as {Markup.Escape(repository.MatchedSlug!)}[/]"
                : "[dim]not registered[/]";

            output.WriteLine($"{marker} {Markup.Escape(repository.Path)}  {suffix}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Opens a project directory in the platform file manager (spec section 73).</summary>
[Description("Open a project directory in the file manager.")]
public sealed class ProjectOpenCommand : AsyncCommand<ProjectOpenCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IApplicationLauncher _launcher;
    private readonly IAnsiConsole _console;

    public ProjectOpenCommand(
        IProjectService projects,
        IApplicationLauncher launcher,
        IAnsiConsole console)
    {
        _projects = projects;
        _launcher = launcher;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var resolveResult = await _projects.ResolveAsync(settings.Project).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return output.Fail(resolveResult);
        }

        var project = resolveResult.Value!;

        if (project.LocalPath is null)
        {
            return output.Fail(
                $"'{project.Entry.Name}' is not present on this machine.",
                ExitCode.RepositoryUnavailable);
        }

        var openResult = await _launcher.OpenInFileManagerAsync(project.LocalPath).ConfigureAwait(false);

        return openResult.Succeeded
            ? CommandOutput.Success()
            : output.Fail(openResult);
    }
}
