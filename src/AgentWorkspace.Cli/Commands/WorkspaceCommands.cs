using System.ComponentModel;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core.Configuration;
using AgentWorkspace.Core.Security;
using AgentWorkspace.Core.Workspace;
using AgentWorkspace.Models;
using AgentWorkspace.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>Reports the state of the central workspace clone (spec section 76).</summary>
[Description("Show the state of the central workspace.")]
public sealed class WorkspaceStatusCommand : AsyncCommand<GlobalSettings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public WorkspaceStatusCommand(
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _workspace = workspace;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var config = configResult.Value!;
        var configured = _workspace.IsConfigured(config);
        var cloned = _workspace.IsCloned();

        var manifest = cloned
            ? (await _workspace.ReadManifestAsync().ConfigureAwait(false)).Value
            : null;

        var registry = cloned
            ? (await _workspace.ReadRegistryAsync().ConfigureAwait(false)).Value
            : null;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                configured,
                cloned,
                // The remote can carry a token when HTTPS is used with an
                // embedded credential, so it is redacted even here.
                remote = SecretRedactor.Redact(config.Workspace.Remote),
                localPath = _workspace.LocalPath,
                schema = manifest?.WorkspaceSchema,
                projects = registry?.Projects.Count ?? 0,
            });

            return CommandOutput.Success();
        }

        if (!configured)
        {
            output.WriteLine("[yellow]No central workspace is configured.[/]");
            output.WriteLine("[dim]The launcher works without one; projects stay local to this machine.[/]");
            return CommandOutput.Success();
        }

        output.WriteLine($"Remote     {Markup.Escape(SecretRedactor.Redact(config.Workspace.Remote))}");
        output.WriteLine($"Local      {Markup.Escape(_workspace.LocalPath)}");
        output.WriteLine(cloned
            ? "[green]Cloned[/]     yes"
            : "[yellow]Cloned[/]     no  [dim]run: agentctl workspace sync[/]");

        if (manifest is not null)
        {
            output.WriteLine($"Schema     {manifest.WorkspaceSchema}");
        }

        if (registry is not null)
        {
            output.WriteLine($"Projects   {registry.Projects.Count}");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Synchronises the workspace clone (spec sections 45, 47, 48, 76).</summary>
[Description("Fetch and fast-forward the central workspace.")]
public sealed class WorkspaceSyncCommand : AsyncCommand<GlobalSettings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public WorkspaceSyncCommand(
        IWorkspaceManager workspace,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _workspace = workspace;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);
        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        if (settings.Offline)
        {
            return output.Fail(
                "Cannot synchronise while --offline is set.", ExitCode.InvalidArguments);
        }

        var result = await _workspace.SyncAsync(configResult.Value!).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var sync = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                outcome = sync.Outcome.ToString(),
                detail = sync.Detail,
                cachedAt = sync.CachedAtUtc,
                recoveryBranch = sync.RecoveryBranch,
            });
        }
        else
        {
            var colour = sync.Outcome switch
            {
                WorkspaceSyncOutcome.Synced => "green",
                WorkspaceSyncOutcome.Conflict => "red",
                _ => "yellow",
            };

            output.WriteLine($"[{colour}]{sync.Outcome}[/]  {Markup.Escape(sync.Detail)}");

            if (sync.Outcome == WorkspaceSyncOutcome.Offline && sync.CachedAtUtc is not null)
            {
                output.WriteLine(
                    $"[dim]Cached workspace from {sync.CachedAtUtc:dd MMMM yyyy HH:mm} UTC.[/]");
            }

            if (sync.RecoveryBranch is not null)
            {
                // The single most important line when a conflict happens: the
                // user needs to know their work is still reachable by name.
                output.WriteBlankLine();
                output.WriteLine($"Recovery branch: [bold]{Markup.Escape(sync.RecoveryBranch)}[/]");
                output.WriteLine("[dim]Review it with:[/] git log " + sync.RecoveryBranch);
            }
        }

        // A conflict needs a human decision, so it must not look like success
        // to a script (spec sections 40 and 47).
        return sync.Outcome switch
        {
            WorkspaceSyncOutcome.Conflict => (int)ExitCode.GitConflict,
            _ => CommandOutput.Success(),
        };
    }
}

/// <summary>Opens the workspace clone in the file manager (spec section 73).</summary>
[Description("Open the local workspace clone in the file manager.")]
public sealed class WorkspaceOpenCommand : AsyncCommand<GlobalSettings>
{
    private readonly IWorkspaceManager _workspace;
    private readonly IApplicationLauncher _launcher;
    private readonly IAnsiConsole _console;

    public WorkspaceOpenCommand(
        IWorkspaceManager workspace,
        IApplicationLauncher launcher,
        IAnsiConsole console)
    {
        _workspace = workspace;
        _launcher = launcher;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        if (!_workspace.IsCloned())
        {
            return output.Fail(
                "The workspace has not been cloned yet. Run: agentctl workspace sync",
                ExitCode.ConfigurationInvalid);
        }

        var result = await _launcher.OpenInFileManagerAsync(_workspace.LocalPath).ConfigureAwait(false);

        return result.Succeeded ? CommandOutput.Success() : output.Fail(result);
    }
}
