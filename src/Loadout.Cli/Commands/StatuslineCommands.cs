using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Statusline;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Prints the one line Claude renders at the bottom of its screen.
/// <para>
/// This is the command Claude runs, not one a person types, and that shapes
/// every decision in it. It gets a JSON payload on stdin and must answer on
/// stdout quickly, quietly, and without ever failing: a status line command
/// that errors leaves the bottom of somebody's terminal blank with nothing to
/// explain why, and one that hangs stalls the display. So it has a hard
/// deadline, it swallows everything, and it prints whatever it managed to
/// work out — even if that is only the directory.
/// </para>
/// </summary>
[Description("Render the agent status line. Claude runs this; you do not need to.")]
[CommandMeta(CommandCategory.Integration, Intent = "prompt status bar agent display context")]
public sealed class StatuslineRenderCommand : AsyncCommand<GlobalSettings>
{
    /// <summary>
    /// How long the whole thing may take. Claude redraws the status line
    /// frequently, so a slow answer is worse than a partial one — this bounds
    /// the git call, which is the only part that can block.
    /// </summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    private readonly IConfigurationService _configuration;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;

    public StatuslineRenderCommand(
        IConfigurationService configuration,
        IProjectService projects,
        IGitManager git)
    {
        _configuration = configuration;
        _projects = projects;
        _git = git;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        using var deadline = new CancellationTokenSource(Deadline);

        var payload = StatuslinePayload.Parse(
            await ReadStdinAsync(deadline.Token).ConfigureAwait(false));

        // No payload, or one this build could not read, still leaves a useful
        // line to draw: Claude runs the command inside the session directory,
        // so the working directory alone yields the folder and the branch.
        // An empty status line would be indistinguishable from a broken one.
        payload ??= new StatuslinePayload { Cwd = CurrentDirectory() };

        var options = new Models.Configuration.StatuslineSettings();

        string? slug = null;
        string? root = null;
        GitRepositoryState? git = null;

        try
        {
            var configResult = await _configuration.LoadConfigAsync(deadline.Token).ConfigureAwait(false);

            if (configResult.Succeeded)
            {
                options = configResult.Value!.Statusline;
            }

            var directory = payload?.Workspace?.ProjectDir
                ?? payload?.Workspace?.CurrentDir
                ?? payload?.Cwd;

            if (directory is { Length: > 0 })
            {
                // Not being a registered project is entirely normal — somebody
                // may have opened Claude anywhere — so a failure here just
                // drops the segment.
                var resolved = await _projects
                    .ResolveFromDirectoryAsync(directory, deadline.Token)
                    .ConfigureAwait(false);

                if (resolved.Succeeded && resolved.Value is { } project)
                {
                    slug = project.Entry.Slug;
                    root = project.LocalPath;
                }

                var state = await _git.GetStateAsync(directory, deadline.Token).ConfigureAwait(false);

                git = state.Value;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || deadline.IsCancellationRequested)
        {
            // Deliberately broad. Whatever went wrong, the remedy is the same:
            // print what is known. There is nowhere to report it to.
        }

        var line = StatuslineRenderer.Render(
            new StatuslineInputs(payload, slug, root, git),
            options);

        Console.Out.WriteLine(line);

        return (int)ExitCode.Success;
    }

    /// <summary>The working directory, or null on the rare systems that refuse to say.</summary>
    private static string? CurrentDirectory()
    {
        try
        {
            return Directory.GetCurrentDirectory();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads the payload, tolerating not being given one. Run by hand from a
    /// terminal there is no stdin to read, and blocking forever waiting for it
    /// would look like a hang.
    /// </summary>
    private static async Task<string?> ReadStdinAsync(CancellationToken ct)
    {
        try
        {
            if (Console.IsInputRedirected)
            {
                return await Console.In.ReadToEndAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // No payload; the line is drawn from what the launcher knows.
        }

        return null;
    }
}

/// <summary>Which settings file a status line command applies to.</summary>
public class StatuslineTargetSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Apply to this registered project instead of the current directory.")]
    public string? Project { get; init; }

    [CommandOption("--all")]
    [Description("Apply to every registered project.")]
    public bool All { get; init; }

    [CommandOption("--global")]
    [Description("Apply to the Claude user settings, so it shows in every session on this machine.")]
    public bool Global { get; init; }
}

/// <summary>
/// Adds the status line to a Claude settings file (spec sections 9 and 31).
/// <para>
/// Two scopes, which do genuinely different things. A project install writes
/// into the workspace repository, so it travels to every machine that clones
/// it and applies only when the launcher starts the session. A global install
/// writes to the Claude user settings, so it applies to every session on this
/// machine including ones started by hand — at the cost of being machine-local.
/// </para>
/// </summary>
[Description("Show the project, directory, branch and context usage in the agent status line.")]
public sealed class StatuslineInstallCommand : AsyncCommand<StatuslineTargetSettings>
{
    private readonly StatuslineTargets _targets;
    private readonly IAnsiConsole _console;

    public StatuslineInstallCommand(StatuslineTargets targets, IAnsiConsole console)
    {
        _targets = targets;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        if (targetsResult.Failed)
        {
            return output.Fail(targetsResult);
        }

        var executable = StatuslineTargets.ExecutablePath();

        if (executable is null)
        {
            return output.Fail(
                "Could not work out where this launcher is installed, so there is no command to install.",
                ExitCode.GeneralFailure);
        }

        var installed = new List<StatuslineInstallation>();

        foreach (var target in targetsResult.Value!)
        {
            var result = await StatuslineInstaller
                .InstallAsync(target.SettingsPath, executable)
                .ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            installed.Add(result.Value!);
        }

        if (output.IsJson)
        {
            output.WriteJson(installed.Select(i => new
            {
                path = i.Path,
                command = i.Command,
                replaced = i.Replaced,
            }));

            return CommandOutput.Success();
        }

        foreach (var (target, installation) in targetsResult.Value!.Zip(installed))
        {
            output.WriteLine($"[green]Installed[/] for {target.Description.EscapeMarkup()}");
            output.WriteLine($"  [dim]{installation.Path.EscapeMarkup()}[/]");

            if (installation.Replaced is { Length: > 0 } previous
                && previous != installation.Command)
            {
                // Silently discarding somebody's own status line would be the
                // kind of thing found out weeks later.
                output.WriteLine(
                    $"  [yellow]Replaced[/] [dim]{previous.EscapeMarkup()}[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Start a new session to see it. Claude reads settings at startup.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Removes the status line, leaving the rest of the settings file alone.</summary>
[Description("Remove the agent status line.")]
public sealed class StatuslineUninstallCommand : AsyncCommand<StatuslineTargetSettings>
{
    private readonly StatuslineTargets _targets;
    private readonly IAnsiConsole _console;

    public StatuslineUninstallCommand(StatuslineTargets targets, IAnsiConsole console)
    {
        _targets = targets;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        if (targetsResult.Failed)
        {
            return output.Fail(targetsResult);
        }

        foreach (var target in targetsResult.Value!)
        {
            var result = await StatuslineInstaller
                .UninstallAsync(target.SettingsPath)
                .ConfigureAwait(false);

            if (result.Failed)
            {
                return output.Fail(result);
            }

            output.WriteLine(result.Value
                ? $"[green]Removed[/] from {target.Description.EscapeMarkup()}"
                : $"[dim]Nothing to remove for {target.Description.EscapeMarkup()}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Says where the status line is installed and what it currently looks like.
/// <para>
/// The preview matters more than it sounds: the line is otherwise only visible
/// by starting an agent session, which is a slow way to find out that a segment
/// is switched off.
/// </para>
/// </summary>
[Description("Show where the status line is installed and what it looks like.")]
public sealed class StatuslineShowCommand : AsyncCommand<StatuslineTargetSettings>
{
    private readonly StatuslineTargets _targets;
    private readonly IConfigurationService _configuration;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;

    public StatuslineShowCommand(
        StatuslineTargets targets,
        IConfigurationService configuration,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
    {
        _targets = targets;
        _configuration = configuration;
        _projects = projects;
        _git = git;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, StatuslineTargetSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);

        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var options = configResult.Value!.Statusline;

        var targetsResult = await _targets.ResolveAsync(settings).ConfigureAwait(false);

        // Unlike installing, showing changes nothing, so being outside a
        // registered project is no reason to refuse. Fall back to the
        // machine-wide file, which is the one that would apply here anyway.
        var targets = targetsResult.Succeeded
            ? targetsResult.Value!
            : [new StatuslineTarget("every session on this machine", _targets.GlobalSettingsPath())];

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolved = await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);
        var state = await _git.GetStateAsync(directory).ConfigureAwait(false);

        // A payload the way Claude would send one, so the preview shows the
        // model and context segments rather than quietly omitting them.
        var payload = new StatuslinePayload
        {
            Cwd = directory,
            Workspace = new StatuslineWorkspace
            {
                CurrentDir = directory,
                ProjectDir = resolved.Value?.LocalPath ?? state.Value?.Root,
            },
            Model = new StatuslineModel { DisplayName = "Opus 5" },
            ContextWindow = new StatuslineContextWindow
            {
                TotalInputTokens = 84_000,
                ContextWindowSize = 200_000,
            },
        };

        var preview = StatuslineRenderer.Render(
            new StatuslineInputs(
                payload,
                resolved.Value?.Entry.Slug,
                resolved.Value?.LocalPath,
                state.Value),
            options);

        var installations = new List<(string Description, string Path, string? Command)>();

        foreach (var target in targets)
        {
            var command = await StatuslineInstaller
                .ReadCommandAsync(target.SettingsPath)
                .ConfigureAwait(false);

            installations.Add((target.Description, target.SettingsPath, command.Value));
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                preview = StatuslineRenderer.Render(
                    new StatuslineInputs(
                        payload,
                        resolved.Value?.Entry.Slug,
                        resolved.Value?.LocalPath,
                        state.Value),
                    new Models.Configuration.StatuslineSettings
                    {
                        ShowProject = options.ShowProject,
                        ShowDirectory = options.ShowDirectory,
                        ShowGit = options.ShowGit,
                        ShowModel = options.ShowModel,
                        ShowContext = options.ShowContext,
                        Separator = options.Separator,

                        // JSON is read by programs, and escape codes in a
                        // string field are noise to every one of them.
                        Colour = false,
                    }),
                segments = new
                {
                    project = options.ShowProject,
                    directory = options.ShowDirectory,
                    git = options.ShowGit,
                    model = options.ShowModel,
                    context = options.ShowContext,
                },
                installed = installations.Select(i => new
                {
                    scope = i.Description,
                    path = i.Path,
                    command = i.Command,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine("[bold]Preview[/]");

        // Written raw: the line carries its own escape codes, and putting it
        // through markup rendering would print them rather than apply them.
        _console.WriteLine();
        Console.Out.WriteLine("  " + preview);
        _console.WriteLine();

        output.WriteLine("[bold]Segments[/]");
        output.WriteLine($"  project    {OnOff(options.ShowProject)}");
        output.WriteLine($"  directory  {OnOff(options.ShowDirectory)}");
        output.WriteLine($"  git        {OnOff(options.ShowGit)}");
        output.WriteLine($"  model      {OnOff(options.ShowModel)}");
        output.WriteLine($"  context    {OnOff(options.ShowContext)}");
        output.WriteBlankLine();
        output.WriteLine("[dim]Change these with loadout config set statusline-git false[/]");
        output.WriteBlankLine();

        output.WriteLine("[bold]Installed[/]");

        foreach (var (description, path, command) in installations)
        {
            output.WriteLine(command is null
                ? $"  [dim]not installed[/]  {description.EscapeMarkup()}"
                : $"  [green]installed[/]      {description.EscapeMarkup()}");

            output.WriteLine($"    [dim]{path.EscapeMarkup()}[/]");
        }

        if (installations.TrueForAll(i => i.Command is null))
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Turn it on with loadout statusline install[/]");
        }

        return CommandOutput.Success();
    }

    private static string OnOff(bool value) => value ? "[green]on[/]" : "[dim]off[/]";
}
