using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Making a team of your own.
/// </summary>
/// <remarks>
/// <para>
/// A team has always been a YAML file you write by hand, and the documentation
/// says so. What was missing was the two-line part around it: knowing where the
/// file goes, and getting a copy of one that already works. Both were written
/// down in prose and had to be done from memory, and the one team that exists
/// purely to be copied — <c>product-company</c>, marked <c>template: true</c> —
/// had no command that would copy it, though the model's own documentation had
/// named <c>team new --from</c> for months.
/// </para>
/// <para>
/// It writes a file and stops. It does not ask a series of questions and
/// assemble a team from the answers: the file is the thing, and a second way of
/// describing a team would have to be kept in step with the parser that reads
/// it.
/// </para>
/// </remarks>
[Description("Write a new team of your own, empty or copied from one that already works.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team new create author write own team copy template scaffold", Mutates = true)]
public sealed class TeamNewCommand : AsyncCommand<TeamNewCommand.Settings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TeamNewCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("What to call it. Lowercase and hyphenated, as the built-in ones are.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--from <TEAM>")]
        [Description("Start from this team's file rather than an empty one.")]
        public string? From { get; init; }

        [CommandOption("--for-this-project")]
        [Description("Write it under this project, so only this project sees it. Otherwise all of them do.")]
        public bool ForThisProject { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!TeamFiles.Names(settings.Name))
        {
            return output.Fail(
                $"'{settings.Name}' cannot be a team's name. Lowercase letters, digits and hyphens, "
                + "as the built-in ones are: bug-hunt, docs-crew.",
                ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return output.Fail(
                "There is no workspace to write a team into. Set one up with: loadout setup",
                ExitCode.WorkspaceSyncFailed);
        }

        var (catalogue, _, slug) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (catalogue.Find(settings.Name) is not null)
        {
            return output.Fail(
                $"There is already a team called '{settings.Name}'. See it with: "
                + $"loadout team show {settings.Name}",
                ExitCode.InvalidArguments);
        }

        if (settings.ForThisProject && slug is not { Length: > 0 })
        {
            return output.Fail(
                "--for-this-project needs a project, and this directory is not in a registered one. "
                + "Name one with --project, or leave the option off to write it for all of them.",
                ExitCode.ProjectNotFound);
        }

        string text;

        if (settings.From is { Length: > 0 } from)
        {
            if (catalogue.Find(from) is null)
            {
                return output.Fail(
                    $"No team named '{from}' to copy. See what there is with: loadout team list",
                    ExitCode.ProjectNotFound);
            }

            var read = Copy(from, catalogue);

            if (read.Failed)
            {
                return output.Fail(read);
            }

            text = TeamFiles.Renamed(read.Value!, settings.Name);
        }
        else
        {
            text = TeamFiles.Scaffold(settings.Name);
        }

        var directory = TeamFiles.DirectoryFor(
            _workspace.LocalPath, settings.ForThisProject ? slug : null);

        var path = Path.Combine(directory, settings.Name + ".yaml");

        // Checked even though the catalogue said the name is free: a file whose
        // contents name a different team sits in the same directory under this
        // name, and overwriting it would silently take a team away.
        if (File.Exists(path))
        {
            return output.Fail(
                $"'{path}' is already there. Nothing was written.", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Would write[/] {Markup.Escape(path)}");
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was written.[/]");

            return CommandOutput.Success();
        }

        try
        {
            Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(path, text, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return output.Fail($"'{path}' could not be written: {ex.Message}");
        }

        if (output.IsJson)
        {
            output.WriteJson(new { team = settings.Name, path, from = settings.From });

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Wrote [bold]{Markup.Escape(settings.Name)}[/]");
        output.WriteLine($"  [dim]{Markup.Escape(path)}[/]");
        output.WriteBlankLine();
        output.WriteLine($"[dim]Open it with:[/] loadout team edit {settings.Name}");
        output.WriteLine($"[dim]Check it with:[/] loadout team show {settings.Name}");

        return CommandOutput.Success();
    }

    /// <summary>
    /// The text of the team being copied, from wherever the catalogue read it.
    /// </summary>
    /// <remarks>
    /// A built-in is an embedded resource and everything else is a file, and
    /// the catalogue records which by recording the name it read. Copying the
    /// text rather than re-serialising the definition is what keeps the
    /// comments, the key order and the inline maps — a copy that came back
    /// alphabetised with the comments gone would be a worse starting point than
    /// the file somebody was reading when they decided to copy it.
    /// </remarks>
    private static Loadout.Models.Results.OperationResult<string> Copy(
        string from, TeamCatalogueResult catalogue)
    {
        var source = catalogue.Source(from);

        if (source is not { Length: > 0 })
        {
            return Loadout.Models.Results.OperationResult<string>.Fail(
                $"Nothing recorded where '{from}' was read from, so there is nothing to copy.");
        }

        if (catalogue.Origin(from) == SpecialistOrigin.BuiltIn)
        {
            var assembly = typeof(TeamCatalogue).Assembly;

            using var stream = assembly.GetManifestResourceStream(source);

            if (stream is null)
            {
                return Loadout.Models.Results.OperationResult<string>.Fail(
                    $"'{from}' ships with Loadout but could not be read out of it.");
            }

            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            return Loadout.Models.Results.OperationResult<string>.Ok(reader.ReadToEnd());
        }

        try
        {
            return Loadout.Models.Results.OperationResult<string>.Ok(File.ReadAllText(source));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Loadout.Models.Results.OperationResult<string>.Fail(
                $"'{source}' could not be read: {ex.Message}");
        }
    }
}

/// <summary>Opening a team of yours, or saying where it is.</summary>
[Description("Print the path of a team's file, or open it.")]
[CommandMeta(CommandCategory.Start, Intent = "team edit open change file path yaml")]
public sealed class TeamEditCommand : AsyncCommand<TeamEditCommand.Settings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IApplicationLauncher _launcher;
    private readonly IAnsiConsole _console;

    public TeamEditCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IApplicationLauncher launcher,
        IAnsiConsole console)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _launcher = launcher;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<team>")]
        [Description("The team, as 'team list' names it.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--path-only")]
        [Description("Print the path and open nothing.")]
        public bool PathOnly { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, _, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (catalogue.Find(settings.Name) is null)
        {
            return output.Fail(
                $"No team named '{settings.Name}'. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        var origin = catalogue.Origin(settings.Name);
        var source = catalogue.Source(settings.Name);

        if (TeamFiles.Whyever(settings.Name, origin, source) is { } whyever)
        {
            return output.Fail(whyever, ExitCode.PolicyViolation);
        }

        var path = source!;

        if (settings.PathOnly || output.IsJson)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { team = settings.Name, path, origin = origin.ToString() });
            }
            else
            {
                Console.Out.WriteLine(path);
            }

            return CommandOutput.Success();
        }

        if (!output.CanOpenAWindow)
        {
            // The same reasoning as 'config edit': nobody is watching, so the
            // path is the whole of the useful answer, and handing the file to
            // the desktop here is how a suite run puts a "choose an
            // application" dialog in front of somebody.
            Console.Out.WriteLine(path);

            return CommandOutput.Success();
        }

        var opened = await _launcher.OpenInFileManagerAsync(path).ConfigureAwait(false);

        if (opened.Failed)
        {
            output.WriteLine($"[yellow]Could not open an editor:[/] {Shown.Safely(opened.Error!)}");
            Console.Out.WriteLine(path);
        }

        return CommandOutput.Success();
    }
}

/// <summary>Forgetting a team of yours.</summary>
[Description("Delete a team you wrote. The ones that ship and the ones from packs are refused.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team remove delete forget drop get rid of team", Mutates = true)]
public sealed class TeamRemoveCommand : AsyncCommand<TeamRemoveCommand.Settings>
{
    private readonly ITeamCatalogue _teams;
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TeamRemoveCommand(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TeamSettings
    {
        [CommandArgument(0, "<team>")]
        [Description("The team to delete.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Do not ask first.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (catalogue, _, _) = await TeamLoading
            .LoadAsync(_teams, _library, _workspace, _projects, settings, cancellationToken)
            .ConfigureAwait(false);

        if (catalogue.Find(settings.Name) is null)
        {
            return output.Fail(
                $"No team named '{settings.Name}'. See what there is with: loadout team list",
                ExitCode.ProjectNotFound);
        }

        var source = catalogue.Source(settings.Name);

        if (TeamFiles.Whyever(settings.Name, catalogue.Origin(settings.Name), source) is { } whyever)
        {
            return output.Fail(whyever, ExitCode.PolicyViolation);
        }

        var path = source!;

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Would delete[/] {Markup.Escape(path)}");
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was deleted.[/]");

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            // Named, and the path with it. A team of your own is a file
            // somebody wrote, and the workspace's history is the only place it
            // survives being wrong about which one this is.
            output.WriteLine($"  {Markup.Escape(path)}");

            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"Deleting '{settings.Name}' needs agreeing to, and nobody is here to agree. "
                    + "Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm($"Delete the team '{settings.Name}'?", false))
            {
                output.WriteLine("[dim]Nothing was deleted.[/]");

                return CommandOutput.Success();
            }
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return output.Fail($"'{path}' could not be deleted: {ex.Message}");
        }

        if (output.IsJson)
        {
            output.WriteJson(new { team = settings.Name, deleted = path });

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Deleted [bold]{Markup.Escape(settings.Name)}[/]");
        output.WriteLine($"  [dim]{Markup.Escape(path)}[/]");

        return CommandOutput.Success();
    }
}
