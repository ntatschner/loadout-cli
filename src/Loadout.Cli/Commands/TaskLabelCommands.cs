using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Adds labels to a task or an idea, or takes them off.</summary>
/// <remarks>
/// Labels are the person's own, beside the type, and nothing acts on them: a
/// type drives what Loadout says about the work, and a label only sorts it.
/// </remarks>
[Description("Add your own labels to a task or idea, or take them off with --remove. Labels trigger nothing; filter on them with label:.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "label tag task idea custom sort group",
    Mutates = true,
    Example = "fix-login homelab urgent")]
public sealed class TaskLabelCommand : AsyncCommand<TaskLabelCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskLabelCommand(ITaskService tasks, IProjectService projects, IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task or idea.")]
        public string Id { get; init; } = string.Empty;

        [CommandArgument(1, "<label>")]
        [Description("One or more labels, each a word or two of at most 32 characters.")]
        public string[] Labels { get; init; } = [];

        [CommandOption("--remove")]
        [Description("Take these labels off rather than adding them.")]
        public bool Remove { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!settings.Remove && settings.Labels.Select(TaskLabels.Rejection).FirstOrDefault(why => why is not null) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;
        var said = string.Join(", ", settings.Labels.Select(one => one.Trim()));

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would {(settings.Remove ? "take" : "put")} {Markup.Escape(said)} {(settings.Remove ? "off" : "on")} "
                + $"[bold]{Markup.Escape(settings.Id)}[/]. Nothing was written.");

            return CommandOutput.Success();
        }

        var labelled = await _tasks.LabelAsync(
            slug,
            settings.Id,
            settings.Remove ? [] : settings.Labels,
            settings.Remove ? settings.Labels : [],
            cancellationToken).ConfigureAwait(false);

        if (labelled.Failed)
        {
            return output.Fail(labelled);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { project = slug, task = TaskOperations.Json(labelled.Value!) });

            return CommandOutput.Success();
        }

        var now = labelled.Value!.Labels;

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(labelled.Value.Id)} is labelled "
            + (now.Count > 0 ? Markup.Escape(string.Join(", ", now)) : "with nothing") + ".");

        return CommandOutput.Success();
    }
}

/// <summary>Every label in the workspace, and how many tasks and ideas carry each.</summary>
[Description("List every label in the workspace, with how many tasks and ideas carry each.")]
[CommandMeta(CommandCategory.Workspace, Intent = "labels list tags custom count")]
public sealed class TaskLabelsListCommand : AsyncCommand<GlobalSettings>
{
    private readonly ITaskService _tasks;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskLabelsListCommand(ITaskService tasks, Core.Workspace.IWorkspaceManager workspace, IAnsiConsole console)
    {
        _tasks = tasks;
        _workspace = workspace;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!_workspace.IsAvailable())
        {
            return output.Fail("There is no workspace on this machine, so there are no labels.", ExitCode.WorkspaceSyncFailed);
        }

        // Counted with the first spelling met, since case does not make two labels.
        var counts = new Dictionary<string, int>(TaskLabels.Same);
        var spelt = new List<string>();

        foreach (var list in TaskPaths.Lists(_workspace.LocalPath))
        {
            var listed = await _tasks.ListAsync(list, cancellationToken).ConfigureAwait(false);

            if (listed.Failed)
            {
                return output.Fail(listed);
            }

            foreach (var label in listed.Value!.SelectMany(item => item.Labels))
            {
                if (counts.TryGetValue(label, out var count))
                {
                    counts[label] = count + 1;
                }
                else
                {
                    counts[label] = 1;
                    spelt.Add(label);
                }
            }
        }

        var labels = spelt.OrderBy(label => label, TaskLabels.Same).Select(label => (Name: label, Count: counts[label])).ToList();

        if (output.IsJson)
        {
            output.WriteJson(new { labels = labels.Select(one => new { name = one.Name, count = one.Count }) });

            return CommandOutput.Success();
        }

        if (labels.Count == 0)
        {
            output.WriteLine("[dim]No task or idea has a label. Add one with: loadout task label <id> <label>[/]");

            return CommandOutput.Success();
        }

        foreach (var (name, count) in labels)
        {
            output.WriteLine($"{Markup.Escape(name)}  [dim]{count}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>What renaming and removing a label both take.</summary>
public class LabelEverywhereSettings : GlobalSettings
{
    [CommandOption("--yes")]
    [Description("Do not ask first.")]
    public bool Yes { get; init; }
}

/// <summary>What renaming and removing a label share: finding it everywhere, asking, then changing it.</summary>
internal static class LabelEverywhere
{
    /// <summary>Renames <paramref name="from"/> to <paramref name="to"/> on every list, or with null takes it off.</summary>
    /// <remarks>
    /// The command's --dry-run comes in as <c>dryRun</c>, passed by name so
    /// each command is seen reading it where it arrives, rather than only in here.
    /// </remarks>
    public static async Task<int> RunAsync(
        ITaskService tasks,
        Core.Workspace.IWorkspaceManager workspace,
        IAnsiConsole console,
        LabelEverywhereSettings settings,
        string from,
        string? to,
        bool dryRun,
        CancellationToken ct)
    {
        var output = new CommandOutput(console, settings);

        if (from.Trim().Length == 0)
        {
            return output.Fail("Say which label.", ExitCode.InvalidArguments);
        }

        if (to is not null && TaskLabels.Rejection(to) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        if (!workspace.IsAvailable())
        {
            return output.Fail("There is no workspace on this machine, so there are no labels.", ExitCode.WorkspaceSyncFailed);
        }

        var lists = TaskPaths.Lists(workspace.LocalPath);
        var found = new List<(string? List, string Id)>();

        foreach (var list in lists)
        {
            var carrying = await tasks.RelabelAsync(list, from, to, apply: false, ct).ConfigureAwait(false);

            if (carrying.Failed)
            {
                return output.Fail(carrying);
            }

            found.AddRange(carrying.Value!.Select(id => (list, id)));
        }

        var name = from.Trim();
        var change = to is { } renamed ? $"rename '{name}' to '{renamed.Trim()}' on" : $"take '{name}' off";
        var count = $"{found.Count} task(s) and idea(s)";

        if (found.Count == 0)
        {
            return output.Fail($"No task or idea has the label '{name}'. loadout task labels list shows the labels there are.", ExitCode.InvalidArguments);
        }

        if (dryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { written = false, from = name, to = to?.Trim(), tasks = found.Select(one => new { project = one.List, id = one.Id }) });

                return CommandOutput.Success();
            }

            output.WriteLine($"Would {Markup.Escape(change)} {count}. Nothing was written.");

            return CommandOutput.Success();
        }

        // A count, not a list: a label on fifty items is fifty lines nobody
        // reads, and the number is what tells somebody this reaches further
        // than the item they were looking at.
        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That would {change} {count} across the workspace, and nobody is here to agree to it. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!console.Confirm($"{Markup.Escape(char.ToUpperInvariant(change[0]) + change[1..])} on {count}, on every list?", false))
            {
                output.WriteLine("[dim]Nothing was changed.[/]");

                return CommandOutput.Success();
            }
        }

        foreach (var list in found.Select(one => one.List).Distinct())
        {
            var changed = await tasks.RelabelAsync(list, from, to, apply: true, ct).ConfigureAwait(false);

            if (changed.Failed)
            {
                return output.Fail(changed);
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new { written = true, from = name, to = to?.Trim(), tasks = found.Select(one => new { project = one.List, id = one.Id }) });

            return CommandOutput.Success();
        }

        output.WriteLine(to is null
            ? $"[green]+[/] Took '{Markup.Escape(name)}' off {count}."
            : $"[green]+[/] Renamed '{Markup.Escape(name)}' to '{Markup.Escape(to.Trim())}' on {count}.");

        return CommandOutput.Success();
    }
}

/// <summary>Renames a label on every task and idea that carries it.</summary>
[Description("Rename a label on every task and idea that carries it, after saying how many that is.")]
[CommandMeta(CommandCategory.Workspace, Intent = "labels rename tag change every", Mutates = true, Example = "homelab home-lab")]
public sealed class TaskLabelsRenameCommand : AsyncCommand<TaskLabelsRenameCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskLabelsRenameCommand(ITaskService tasks, Core.Workspace.IWorkspaceManager workspace, IAnsiConsole console)
    {
        _tasks = tasks;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : LabelEverywhereSettings
    {
        [CommandArgument(0, "<label>")]
        [Description("The label as it is now.")]
        public string From { get; init; } = string.Empty;

        [CommandArgument(1, "<new>")]
        [Description("What to call it.")]
        public string To { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return LabelEverywhere.RunAsync(_tasks, _workspace, _console, settings, settings.From, settings.To, dryRun: settings.DryRun, cancellationToken);
    }
}

/// <summary>Takes a label off every task and idea that carries it.</summary>
[Description("Take a label off every task and idea that carries it, after saying how many that is.")]
[CommandMeta(CommandCategory.Workspace, Intent = "labels remove delete tag every", Mutates = true, Example = "someday")]
public sealed class TaskLabelsRemoveCommand : AsyncCommand<TaskLabelsRemoveCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskLabelsRemoveCommand(ITaskService tasks, Core.Workspace.IWorkspaceManager workspace, IAnsiConsole console)
    {
        _tasks = tasks;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : LabelEverywhereSettings
    {
        [CommandArgument(0, "<label>")]
        [Description("The label to take off.")]
        public string Label { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return LabelEverywhere.RunAsync(_tasks, _workspace, _console, settings, settings.Label, null, dryRun: settings.DryRun, cancellationToken);
    }
}
