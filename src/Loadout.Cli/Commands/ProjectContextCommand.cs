using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// The switches deciding what a project puts in front of every session.
/// </summary>
/// <remarks>
/// A table rather than a branch per switch, for the reason the configuration
/// registry gives: reading, listing and writing cannot disagree about which
/// switches exist when there is only one list of them.
/// </remarks>
internal sealed record ContextSwitch(
    string Key,
    string Summary,
    string Cost,
    Func<ProjectContext, bool> Read,
    Action<ProjectContext, bool> Write)
{
    internal static IReadOnlyList<ContextSwitch> All =>
    [
        new("tasks",
            "Put the project's open tasks in front of every session",
            "A heading and a line per open task.",
            context => context.Tasks,
            (context, value) => context.Tasks = value),

        new("code-map",
            "Inline a map of the project's code into every session",
            "A few thousand tokens on a mid-sized repository, every launch.",
            context => context.CodeMap,
            (context, value) => context.CodeMap = value),
    ];

    /// <summary>
    /// Finds a switch by what somebody typed.
    /// </summary>
    /// <remarks>
    /// Underscores are accepted because the YAML spells this key
    /// <c>code_map</c>, and somebody who has just read the manifest will type
    /// what they saw there.
    /// </remarks>
    internal static ContextSwitch? Find(string key)
    {
        var wanted = key.Trim().Replace('_', '-');

        return All.FirstOrDefault(
            entry => string.Equals(entry.Key, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The switches, named the way a sentence would name them.</summary>
    internal static string Names
    {
        get
        {
            var keys = All.Select(entry => entry.Key).ToList();

            return keys.Count switch
            {
                0 => "none",
                1 => keys[0],
                _ => string.Join(", ", keys.Take(keys.Count - 1)) + " and " + keys[^1],
            };
        }
    }
}

/// <summary>Reads and changes what a project carries into every session.</summary>
/// <remarks>
/// These switches existed with nowhere to set them. Both default to off and the
/// only assignment anywhere was at registration — <c>Tasks = !state.Versioned</c>
/// — so a project registered from a repository, which is nearly all of them,
/// could only be changed by hand-editing the manifest in the workspace. The
/// task record was the visible casualty: sessions could write to it and nothing
/// ever showed it to them.
/// </remarks>
[Description("Show or change what this project puts in front of every session.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "context tasks code map carry into session switch on off per project",
    Mutates = true,
    Example = "project context tasks on")]
public sealed class ProjectContextCommand : AsyncCommand<ProjectContextCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public ProjectContextCommand(
        IProjectService projects,
        Core.Workspace.IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Project to read or change. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandArgument(0, "[key]")]
        [Description("tasks or code-map. Omit to show both.")]
        public string? Key { get; init; }

        [CommandArgument(1, "[value]")]
        [Description("on or off. Omit to read the switch rather than change it.")]
        public string? Value { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var read = await _workspace.ReadProjectAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var manifest = read.Value!;

        if (settings.Key is not { Length: > 0 } key)
        {
            return Show(output, slug, manifest, ContextSwitch.All);
        }

        if (ContextSwitch.Find(key) is not { } entry)
        {
            return output.Fail(
                $"'{key}' is not a context switch. The switches are {ContextSwitch.Names}.",
                ExitCode.InvalidArguments);
        }

        if (settings.Value is not { Length: > 0 } value)
        {
            return Show(output, slug, manifest, [entry]);
        }

        bool wanted;

        try
        {
            wanted = ConfigKeys.Flag(value);
        }
        catch (FormatException)
        {
            return output.Fail(
                $"'{value}' is not a yes or no. Use on or off.", ExitCode.InvalidArguments);
        }

        // Read before the dry run is considered, so that "would change nothing"
        // and "changed nothing" are the same sentence rather than two claims
        // about a file only one of them looked at.
        var current = entry.Read(manifest.Context);

        if (current == wanted)
        {
            output.WriteLine(
                $"{Markup.Escape(entry.Key)} is already "
                + $"{State(wanted)} for {Markup.Escape(slug)}. Nothing to change.");

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would turn[/] {Markup.Escape(entry.Key)} {State(wanted)} "
                + $"for {Markup.Escape(slug)}. Nothing was written.");

            return CommandOutput.Success();
        }

        entry.Write(manifest.Context, wanted);

        var written = await _workspace
            .WriteProjectAsync(manifest, cancellationToken).ConfigureAwait(false);

        if (written.Failed)
        {
            return output.Fail(written);
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(entry.Key)} is {State(wanted)} "
            + $"for {Markup.Escape(slug)}.");

        // Said rather than left to be discovered. The context is compiled when a
        // session starts, so a switch changed inside one does not reach the
        // session that changed it.
        output.WriteLine("[dim]Takes effect at the next launch.[/]");

        return CommandOutput.Success();
    }

    private static int Show(
        CommandOutput output,
        string slug,
        ProjectManifest manifest,
        IReadOnlyList<ContextSwitch> switches)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug,
                context = switches.ToDictionary(
                    entry => entry.Key,
                    entry => entry.Read(manifest.Context)),
            });

            return CommandOutput.Success();
        }

        foreach (var entry in switches)
        {
            var on = entry.Read(manifest.Context);

            output.WriteLine(
                $"{Markup.Escape(entry.Key).PadRight(10)} {State(on)}"
                + $"  [dim]{Markup.Escape(entry.Summary)}[/]");

            output.WriteVerbose($"           [dim]{Markup.Escape(entry.Cost)}[/]");
        }

        return CommandOutput.Success();
    }

    private static string State(bool on) => on ? "[green]on[/]" : "[dim]off[/]";
}
