using System.ComponentModel;
using System.Diagnostics;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>
/// Installs the graphical entry point for the launcher (spec section 44).
/// <para>
/// A Start Menu shortcut on Windows, a .desktop entry on Linux. On macOS the
/// application bundle is not built yet, and the command says so rather than
/// pretending: spec section 5 requires a gap to be visible.
/// </para>
/// </summary>
[Description("Install or remove the desktop entry for the launcher.")]
public sealed class DesktopCommand : AsyncCommand<DesktopCommand.Settings>
{
    private readonly IDesktopIntegration _desktop;
    private readonly IAnsiConsole _console;

    public DesktopCommand(IDesktopIntegration desktop, IAnsiConsole console)
    {
        _desktop = desktop;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--remove")]
        [Description("Remove the desktop entry instead of installing it.")]
        public bool Remove { get; init; }

        [CommandOption("--executable <PATH>")]
        [Description("Path to record in the entry. Defaults to the running executable.")]
        public string? Executable { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        if (settings.Remove)
        {
            var removed = await _desktop.UninstallAsync().ConfigureAwait(false);

            if (removed.Failed)
            {
                return output.Fail(removed);
            }

            output.WriteLine("[green]Removed[/] the desktop entry.");

            return CommandOutput.Success();
        }

        var executable = settings.Executable ?? ResolveExecutable();

        if (executable is null)
        {
            return output.Fail(
                "The launcher's own path could not be determined. Pass --executable.",
                ExitCode.ConfigurationInvalid);
        }

        var result = await _desktop.InstallAsync(executable).ConfigureAwait(false);

        if (result.Failed)
        {
            // On macOS this is the deferred bundle, which is a known gap rather
            // than a fault, so it reads as information rather than an error.
            output.WriteLine($"[yellow]Not installed:[/] {Markup.Escape(result.Error!)}");

            return CommandOutput.Success();
        }

        var installed = _desktop.IsInstalled();

        output.WriteLine(installed.Succeeded && installed.Value
            ? "[green]Installed[/] the desktop entry."
            : "[yellow]The entry was written but could not be confirmed.[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// Finds the running executable so the entry points at this install rather
    /// than at whatever happens to be on PATH later.
    /// </summary>
    private static string? ResolveExecutable()
    {
        var path = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return path;
        }

        // ProcessPath is empty in a few hosting arrangements; the main module is
        // the fallback rather than guessing at a name.
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null;
        }
    }
}

/// <summary>Commits and optionally pushes workspace changes (spec sections 45, 46, 76).</summary>
[Description("Commit workspace changes, and push them.")]
public sealed class WorkspaceSaveCommand : AsyncCommand<WorkspaceSaveCommand.Settings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public WorkspaceSaveCommand(IWorkspaceManager workspace, IAnsiConsole console)
    {
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--message <MESSAGE>")]
        [Description("Project name to record in the commit message.")]
        public string? Project { get; init; }

        [CommandOption("--local")]
        [Description("Commit without pushing.")]
        public bool Local { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var pending = await _workspace.GetPendingChangesAsync().ConfigureAwait(false);
        if (pending.Failed)
        {
            return output.Fail(pending);
        }

        if (pending.Value!.Count == 0)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { committed = false, changes = 0 });
            }
            else
            {
                output.WriteLine("[dim]Nothing to save.[/]");
            }

            return CommandOutput.Success();
        }

        var result = await _workspace.SaveAsync(
            settings.Project ?? "workspace",
            "manual",
            push: !settings.Local).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                committed = result.Value,
                changes = pending.Value.Count,
                pushed = result.Value && !settings.Local,
            });
        }
        else
        {
            output.WriteLine(settings.Local
                ? $"[green]Saved[/] {pending.Value.Count} change(s) locally."
                : $"[green]Saved and pushed[/] {pending.Value.Count} change(s).");
        }

        return CommandOutput.Success();
    }
}
