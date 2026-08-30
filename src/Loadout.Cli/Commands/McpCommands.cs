using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Mcp;
using Loadout.Core.Projects;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options shared by the MCP commands.</summary>
public class McpSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Project to act on, instead of the repository in the current directory.")]
    public string? Project { get; init; }

    [CommandOption("--global")]
    [Description("Act on the servers every project loads, rather than one project's.")]
    public bool Global { get; init; }
}

/// <summary>
/// Lists the MCP servers a project loads, and what is wrong with the set.
/// <para>
/// Claude reads servers from an account's connectors, from installed plugins,
/// from a project file and from a user file, and nothing reconciles them.
/// Nobody sees the whole set until something behaves oddly.
/// </para>
/// </summary>
[Description("List the MCP servers a project loads, and any clashes between them.")]
public sealed class McpListCommand : AsyncCommand<McpSettings>
{
    private readonly IMcpService _mcp;
    private readonly IInstalledMcpReader _installed;
    private readonly McpScopeResolver _scope;
    private readonly IAnsiConsole _console;

    public McpListCommand(
        IMcpService mcp,
        IInstalledMcpReader installed,
        McpScopeResolver scope,
        IAnsiConsole console)
    {
        _mcp = mcp;
        _installed = installed;
        _scope = scope;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, McpSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var slug = await _scope.SlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var installed = await _installed
            .ReadAsync(settings.Repo ?? Directory.GetCurrentDirectory())
            .ConfigureAwait(false);

        var resolved = await _mcp.ResolveAsync(slug.Value!, installed).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var resolution = resolved.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug.Value,
                servers = resolution.Servers.Select(e => new
                {
                    name = e.Name,
                    scope = e.Scope.ToString().ToLowerInvariant(),
                    type = e.Server.Type ?? "stdio",
                    reaches = e.Server.Identity,
                }),
                clashes = resolution.Clashes.Select(c => new
                {
                    kind = c.Kind.ToString(),
                    names = c.Names,
                    detail = c.Detail,
                }),
            });

            return CommandOutput.Success();
        }

        if (resolution.Servers.Count == 0)
        {
            output.WriteLine(
                $"[dim]{slug.Value.EscapeMarkup()} declares no MCP servers in the workspace.[/]");
            output.WriteBlankLine();
            output.WriteLine("[dim]Add one with: loadout mcp add <name> <command or url>[/]");

            return CommandOutput.Success();
        }

        foreach (var entry in resolution.Servers)
        {
            output.WriteLine(
                $"[cyan]{entry.Name.EscapeMarkup()}[/] "
                + $"[dim]{entry.Scope.ToString().ToLowerInvariant()}[/]  "
                + $"[dim]{entry.Server.Identity.EscapeMarkup()}[/]");
        }

        Report(output, resolution.Clashes);

        return resolution.Clashes.Count == 0
            ? CommandOutput.Success()
            : (int)ExitCode.Success;
    }

    /// <summary>
    /// Says what is wrong with the set. Shared with the add command, which runs
    /// the same check so a clash is seen when it is introduced rather than the
    /// next time somebody happens to look.
    /// </summary>
    internal static void Report(CommandOutput output, IReadOnlyList<McpClash> clashes)
    {
        if (clashes.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();

        foreach (var clash in clashes)
        {
            var names = string.Join(", ", clash.Names);

            output.WriteLine(
                $"[yellow]{names.EscapeMarkup()}[/] [dim]{clash.Detail.EscapeMarkup()}[/]");
        }
    }
}

/// <summary>Options for adding a server.</summary>
public sealed class McpAddSettings : McpSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Name to register the server under. Its tools carry this as a prefix.")]
    public string Name { get; init; } = string.Empty;

    [CommandArgument(1, "<COMMAND-OR-URL>")]
    [Description("An https:// endpoint, or the command that starts a stdio server.")]
    public string Target { get; init; } = string.Empty;

    [CommandOption("--arg <VALUE>")]
    [Description("Argument for a stdio server. Repeatable.")]
    public string[] Args { get; init; } = [];

    [CommandOption("--force")]
    [Description("Add it even though it clashes with a server already declared.")]
    public bool Force { get; init; }
}

/// <summary>
/// Adds an MCP server to the workspace, warning about anything it clashes with.
/// </summary>
[Description("Add an MCP server for a project, or for every project.")]
public sealed class McpAddCommand : AsyncCommand<McpAddSettings>
{
    private readonly IMcpService _mcp;
    private readonly IInstalledMcpReader _installed;
    private readonly McpScopeResolver _scope;
    private readonly IAnsiConsole _console;

    public McpAddCommand(
        IMcpService mcp,
        IInstalledMcpReader installed,
        McpScopeResolver scope,
        IAnsiConsole console)
    {
        _mcp = mcp;
        _installed = installed;
        _scope = scope;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, McpAddSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var slug = await _scope.SlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var server = settings.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? new McpServer { Type = "http", Url = settings.Target }
            : new McpServer { Command = settings.Target, Args = [.. settings.Args] };

        // Read before writing, so the answer covers the connectors and plugins
        // the workspace cannot see. That is where the duplicates come from.
        var installed = await _installed
            .ReadAsync(settings.Repo ?? Directory.GetCurrentDirectory())
            .ConfigureAwait(false);

        var resolved = await _mcp.ResolveAsync(slug.Value!, installed).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var scope = settings.Global ? McpScope.Global : McpScope.Project;

        // Checked against what is already declared, before writing. A clash
        // found now is a question; found later it is a session quietly loading
        // the same tools twice.
        var proposed = resolved.Value!.Servers
            .Concat([new McpEntry(settings.Name, scope, server)])
            .ToList();

        var introduced = McpService.Inspect(proposed)
            .Where(c => c.Names.Contains(settings.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (introduced.Count > 0)
        {
            McpListCommand.Report(output, introduced);

            if (!settings.Force)
            {
                output.WriteBlankLine();

                return output.Fail(
                    "Nothing was added. Use --force to add it anyway, or pick a different name.",
                    ExitCode.PolicyViolation);
            }

            output.WriteLine("[yellow]Added anyway, as asked.[/]");
        }

        var added = await _mcp
            .AddAsync(slug.Value!, scope, settings.Name, server)
            .ConfigureAwait(false);

        if (added.Failed)
        {
            return output.Fail(added);
        }

        output.WriteLine(
            $"[green]Added[/] {settings.Name.EscapeMarkup()} "
            + $"[dim]to {(settings.Global ? "every project" : slug.Value!.EscapeMarkup())}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Removes a server from the workspace.</summary>
[Description("Remove an MCP server from a project, or from every project.")]
public sealed class McpRemoveCommand : AsyncCommand<McpRemoveSettings>
{
    private readonly IMcpService _mcp;
    private readonly McpScopeResolver _scope;
    private readonly IAnsiConsole _console;

    public McpRemoveCommand(IMcpService mcp, McpScopeResolver scope, IAnsiConsole console)
    {
        _mcp = mcp;
        _scope = scope;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, McpRemoveSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var slug = await _scope.SlugAsync(settings).ConfigureAwait(false);

        if (slug.Failed)
        {
            return output.Fail(slug);
        }

        var removed = await _mcp
            .RemoveAsync(
                slug.Value!,
                settings.Global ? McpScope.Global : McpScope.Project,
                settings.Name)
            .ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine(removed.Value
            ? $"[green]Removed[/] {settings.Name.EscapeMarkup()}"
            : $"[dim]{settings.Name.EscapeMarkup()} was not declared there.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Options for removing a server.</summary>
public sealed class McpRemoveSettings : McpSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Server to remove.")]
    public string Name { get; init; } = string.Empty;
}
