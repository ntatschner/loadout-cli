using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Context;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Creates a handoff document for a project (spec section 69).
/// <para>
/// The point of a handoff is that Claude today and Codex tomorrow read the same
/// artefact. Spec section 99 rules out unifying proprietary session formats, so
/// the durable thing is a Markdown document in the workspace, which both agents
/// and the person can read.
/// </para>
/// </summary>
[Description("Create a cross-agent handoff document for a project.")]
public sealed class HandoffCreateCommand : AsyncCommand<HandoffCreateCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IHandoffService _handoffs;
    private readonly IClipboardProvider _clipboard;
    private readonly IAnsiConsole _console;

    public HandoffCreateCommand(
        IProjectService projects,
        IHandoffService handoffs,
        IClipboardProvider clipboard,
        IAnsiConsole console)
    {
        _projects = projects;
        _handoffs = handoffs;
        _clipboard = clipboard;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandOption("--name <NAME>")]
        [Description("Name for the handoff. Defaults to a timestamp.")]
        public string? Name { get; init; }

        [CommandOption("--show")]
        [Description("Print the most recent handoff instead of creating one.")]
        public bool Show { get; init; }

        [CommandOption("--list")]
        [Description("List this project's handoffs.")]
        public bool List { get; init; }

        [CommandOption("--clipboard")]
        [Description("Copy the handoff to the clipboard.")]
        public bool Clipboard { get; init; }
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

        var slug = resolveResult.Value!.Entry.Slug;

        if (settings.List)
        {
            return await ListAsync(output, slug).ConfigureAwait(false);
        }

        if (settings.Show || settings.Clipboard)
        {
            return await ShowAsync(output, slug, settings).ConfigureAwait(false);
        }

        var created = await _handoffs.CreateAsync(slug, settings.Name).ConfigureAwait(false);
        if (created.Failed)
        {
            return output.Fail(created);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { name = created.Value!.Name, path = created.Value.Path });
        }
        else
        {
            output.WriteLine($"[green]Created[/] {Markup.Escape(created.Value!.Path)}");
            output.WriteLine("[dim]Fill it in, then commit it with the workspace.[/]");
        }

        return CommandOutput.Success();
    }

    private async Task<int> ListAsync(CommandOutput output, string slug)
    {
        var result = await _handoffs.ListAsync(slug).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var handoffs = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                handoffs = handoffs.Select(h => new { name = h.Name, written = h.WrittenUtc }),
            });

            return CommandOutput.Success();
        }

        if (handoffs.Count == 0)
        {
            output.WriteLine($"[dim]'{Markup.Escape(slug)}' has no handoffs yet.[/]");
            return CommandOutput.Success();
        }

        foreach (var handoff in handoffs)
        {
            output.WriteLine(
                $"{Markup.Escape(handoff.Name)}  [dim]{handoff.WrittenUtc:dd MMM yyyy HH:mm} UTC[/]");
        }

        return CommandOutput.Success();
    }

    private async Task<int> ShowAsync(CommandOutput output, string slug, Settings settings)
    {
        var result = await _handoffs.ReadAsync(slug, settings.Name).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        if (settings.Clipboard)
        {
            var copied = await _clipboard.SetTextAsync(result.Value!).ConfigureAwait(false);

            if (copied.Failed)
            {
                // The clipboard is optional (spec section 74), so a headless
                // machine falls back to printing rather than failing.
                output.WriteLine($"[yellow]Clipboard unavailable:[/] {Markup.Escape(copied.Error!)}");
                Console.Out.WriteLine(result.Value);

                return CommandOutput.Success();
            }

            output.WriteLine("[green]Copied to the clipboard.[/]");
            return CommandOutput.Success();
        }

        // Written raw so the Markdown survives redirection into a file.
        Console.Out.WriteLine(result.Value);

        return CommandOutput.Success();
    }
}

/// <summary>Lists a project's context profiles (spec section 34).</summary>
[Description("List the context profiles available for a project.")]
public sealed class ProfileListCommand : AsyncCommand<ProfileListCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IContextCompiler _compiler;
    private readonly IAnsiConsole _console;

    public ProfileListCommand(
        IProjectService projects,
        Core.Workspace.IWorkspaceManager workspace,
        IContextCompiler compiler,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _compiler = compiler;
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

        var manifestResult = await _workspace
            .ReadProjectAsync(resolveResult.Value!.Entry.Slug)
            .ConfigureAwait(false);

        if (manifestResult.Failed)
        {
            return output.Fail(manifestResult);
        }

        var manifest = manifestResult.Value!;
        var agent = settings.Agent ?? manifest.Agents.Default;
        var profiles = _compiler.ListProfiles(manifest, agent);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                agent,
                profiles = profiles.Select(name => new
                {
                    name,
                    description = manifest.Profiles.TryGetValue(name, out var profile)
                        ? profile.Description
                        : "The project's base context.",
                }),
            });

            return CommandOutput.Success();
        }

        foreach (var name in profiles)
        {
            var description = manifest.Profiles.TryGetValue(name, out var profile)
                ? profile.Description
                : "The project's base context.";

            output.WriteLine($"{Markup.Escape(name)}  [dim]{Markup.Escape(description)}[/]");
        }

        return CommandOutput.Success();
    }
}
