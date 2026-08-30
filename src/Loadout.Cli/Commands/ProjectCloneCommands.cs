using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Clones a project that is registered centrally but absent here
/// (spec sections 28 and 75).
/// <para>
/// This is what makes a project definition travelling through the workspace
/// actually useful on a new machine: the definition arrives by sync, and the
/// source arrives by this command, without the user having to go and find the
/// remote URL themselves.
/// </para>
/// </summary>
[Description("Clone a registered project that is not yet on this machine.")]
public sealed class ProjectCloneCommand : AsyncCommand<ProjectCloneCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectCloneCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandArgument(1, "[destination]")]
        [Description("Where to clone to. Defaults to this machine's clone root plus the slug.")]
        public string? Destination { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _projects.CloneAsync(settings.Project, settings.Destination, cancellationToken)
            .ConfigureAwait(false);

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
                name = project.Entry.Name,
                path = project.LocalPath,
            });
        }
        else
        {
            output.WriteLine(
                $"[green]Cloned[/] {Markup.Escape(project.Entry.Name)} "
                + $"[dim]to {Markup.Escape(project.LocalPath!)}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Points a project at a different local path on this machine
/// (spec section 75).
/// <para>
/// The counterpart to cloning: when a repository already exists somewhere the
/// launcher does not know about, telling it where is better than cloning a
/// second copy.
/// </para>
/// </summary>
[Description("Point a project at a different local path on this machine.")]
public sealed class ProjectRelocateCommand : AsyncCommand<ProjectRelocateCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public ProjectRelocateCommand(IProjectService projects, IAnsiConsole console)
    {
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandArgument(1, "<path>")]
        [Description("Path to the existing clone.")]
        public string Path { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _projects.RelocateAsync(settings.Project, settings.Path)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        output.WriteLine(
            $"[green]Relocated[/] {Markup.Escape(settings.Project)} "
            + $"[dim]to {Markup.Escape(settings.Path)}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Shows everything the launcher knows about one project (spec section 75).</summary>
[Description("Show the details of one project.")]
public sealed class ProjectShowCommand : AsyncCommand<ProjectShowCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly Core.Git.IGitManager _git;
    private readonly IAnsiConsole _console;

    public ProjectShowCommand(
        IProjectService projects,
        Core.Workspace.IWorkspaceManager workspace,
        Core.Git.IGitManager git,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _git = git;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _projects.ResolveAsync(settings.Project).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var project = result.Value!;
        var manifest = (await _workspace.ReadProjectAsync(project.Entry.Slug).ConfigureAwait(false)).Value;

        var state = project.LocalPath is null
            ? null
            : (await _git.GetStateAsync(project.LocalPath).ConfigureAwait(false)).Value;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = project.Entry.Slug,
                uuid = project.Entry.Id,
                name = project.Entry.Name,
                remote = project.Entry.Remote,
                aliases = project.Entry.Aliases,
                defaultAgent = project.Entry.DefaultAgent,
                available = project.IsAvailableLocally,
                path = project.LocalPath,
                branch = state?.Branch,
                clean = state?.IsClean,
                launchCount = project.LaunchCount,
                lastLaunched = project.LastLaunchedUtc,
                profiles = manifest?.Profiles.Keys,
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(project.Entry.Name)}[/]");
        output.WriteBlankLine();
        output.WriteLine($"Slug       {Markup.Escape(project.Entry.Slug)}");
        output.WriteLine($"Id         {Markup.Escape(project.Entry.Id)}");
        output.WriteLine($"Remote     {Markup.Escape(project.Entry.Remote)}");
        output.WriteLine($"Agent      {Markup.Escape(project.Entry.DefaultAgent)}");

        output.WriteLine(project.LocalPath is null
            ? "Local      [yellow]not on this machine[/]"
            : $"Local      {Markup.Escape(project.LocalPath)}");

        if (state is not null)
        {
            output.WriteLine($"Branch     {Markup.Escape(state.Branch ?? "detached HEAD")}");
            output.WriteLine(state.IsClean ? "Status     clean" : "Status     [yellow]modified[/]");
        }

        if (project.LaunchCount > 0)
        {
            output.WriteLine(
                $"Launched   {project.LaunchCount} time(s), last "
                + $"{project.LastLaunchedUtc:dd MMM yyyy HH:mm} UTC");
        }

        if (manifest is not null && manifest.Profiles.Count > 0)
        {
            output.WriteLine($"Profiles   {string.Join(", ", manifest.Profiles.Keys)}");
        }

        return CommandOutput.Success();
    }
}
