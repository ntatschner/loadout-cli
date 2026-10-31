using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What a task requires, what requires it, and changes to the first.</summary>
/// <remarks>
/// <para>
/// A task that requires another is held out of team queues until that one is
/// declared done, and a person starting it by hand is asked first. The link is
/// kept on the task that waits, and may name a task on any list in the workspace.
/// </para>
/// <para>
/// With no task named it changes nothing and lists both directions: a link
/// seen only from the waiting end is easy to break by dropping the other.
/// </para>
/// </remarks>
[Description("List what a task requires and what requires it, or add tasks it must wait for. --remove takes them off.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "requires depends on prerequisite before blocked by order link wait",
    Mutates = true,
    Example = "release-notes release-workflow")]
public sealed class TaskRequiresCommand : AsyncCommand<TaskRequiresCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskRequiresCommand(ITaskService tasks, IProjectService projects, IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task that waits.")]
        public string Id { get; init; } = string.Empty;

        [CommandArgument(1, "[task]")]
        [Description("Tasks it must wait for: an id on the same list, project/id, or global/id for the workspace-wide list.")]
        public string[] Targets { get; init; } = [];

        [CommandOption("--remove")]
        [Description("Stop waiting for these tasks instead.")]
        public bool Remove { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;
        var links = new List<TaskLink>();

        foreach (var said in settings.Targets)
        {
            var (link, refusal) = TaskRequires.Parse(said, slug);

            if (link is null)
            {
                return output.Fail(refusal!, ExitCode.InvalidArguments);
            }

            links.Add(link);
        }

        if (settings.Remove && links.Count == 0)
        {
            return output.Fail("--remove needs the tasks to stop waiting for.", ExitCode.InvalidArguments);
        }

        if (links.Count > 0 && settings.DryRun)
        {
            if (!settings.Remove && await _tasks.RequiresRefusalAsync(slug, settings.Id, links, cancellationToken).ConfigureAwait(false) is { } refused)
            {
                return output.Fail(refused, ExitCode.InvalidArguments);
            }

            output.WriteLine(
                $"Would {(settings.Remove ? "stop" : "make")} [bold]{Markup.Escape(settings.Id)}[/] "
                + $"{(settings.Remove ? "waiting for" : "wait for")} {Markup.Escape(string.Join(", ", links.Select(TaskRequires.Name)))}. Nothing was written.");

            return CommandOutput.Success();
        }

        if (links.Count > 0)
        {
            var changed = await _tasks.RequireAsync(
                slug,
                settings.Id,
                settings.Remove ? [] : links,
                settings.Remove ? links : [],
                cancellationToken).ConfigureAwait(false);

            if (changed.Failed)
            {
                return output.Fail(changed);
            }
        }

        return await ShowAsync(output, slug, settings.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Both directions of a task's links, with where each one it waits for stands.</summary>
    private async Task<int> ShowAsync(CommandOutput output, string? slug, string id, CancellationToken ct)
    {
        var every = await _tasks.EveryListAsync(ct).ConfigureAwait(false);

        if (every.Failed)
        {
            return output.Fail(every);
        }

        TaskRequires.Lists lists = key => every.Value!.TryGetValue(key, out var list) ? list : null;
        var here = TaskRequires.Key(slug);
        var task = TaskRequires.Find(new TaskLink { Project = here, Id = id.Trim() }, lists);

        if (task is null)
        {
            return output.Fail($"'{id.Trim()}' is not a task of {TaskPaths.Describe(slug)}.", ExitCode.InvalidArguments);
        }

        var unmet = TaskRequires.Unmet(task, lists);
        var requiredBy = TaskLinks.RequiredBy(new TaskLink { Project = here, Id = task.Id }, every.Value!);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug,
                id = task.Id,
                held = unmet.Count > 0,
                requires = task.Requires.Select(link => new
                {
                    task = TaskRequires.Name(link),
                    state = TaskRequires.Find(link, lists)?.State.ToString().ToLowerInvariant(),
                    met = unmet.All(one => !TaskRequires.Same(one.Link, link)),
                    why = unmet.FirstOrDefault(one => TaskRequires.Same(one.Link, link))?.Why,
                }),
                requiredBy = requiredBy.Select(TaskRequires.Name),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(task.Id)}[/]  [dim]{Markup.Escape(TaskPaths.Describe(slug))}[/]");

        if (task.Requires.Count == 0)
        {
            output.WriteLine("[dim]  Requires nothing.[/]");
        }

        foreach (var link in task.Requires)
        {
            var why = unmet.FirstOrDefault(one => TaskRequires.Same(one.Link, link))?.Why;

            output.WriteLine(
                $"  requires {Markup.Escape(TaskRequires.Name(link, here))}  "
                + (why is null ? "[green]done[/]" : $"[yellow]{Markup.Escape(why)}[/]"));
        }

        foreach (var link in requiredBy)
        {
            output.WriteLine($"  required by {Markup.Escape(TaskRequires.Name(link, here))}");
        }

        if (unmet.Count > 0)
        {
            output.WriteLine("[dim]  Held out of team queues until each is done; a person starting it by hand is asked first.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Links read across every list.</summary>
internal static class TaskLinks
{
    /// <summary>Every task, on any list, that requires the one named.</summary>
    public static IReadOnlyList<TaskLink> RequiredBy(TaskLink target, IReadOnlyDictionary<string, IReadOnlyList<TaskItem>> every) =>
    [
        .. every
            .SelectMany(list => list.Value
                .Where(task => task.Requires.Any(link => TaskRequires.Same(link, target)))
                .Select(task => new TaskLink { Project = list.Key, Id = task.Id }))
            .OrderBy(link => link.Project, StringComparer.Ordinal)
            .ThenBy(link => link.Id, StringComparer.Ordinal),
    ];

    /// <summary>
    /// The tasks still waiting on one about to be dropped or removed, said as a
    /// warning, or null when nothing is.
    /// </summary>
    /// <remarks>
    /// A dropped or removed task keeps holding what requires it, so saying so
    /// at the moment it happens is the one chance to avoid a surprise later.
    /// </remarks>
    public static async Task<string?> StillWaitingAsync(ITaskService tasks, string? slug, string id, CancellationToken ct)
    {
        var every = await tasks.EveryListAsync(ct).ConfigureAwait(false);

        if (every.Failed)
        {
            return null;
        }

        var waiting = RequiredBy(new TaskLink { Project = TaskRequires.Key(slug), Id = id.Trim() }, every.Value!)
            .Where(link => TaskRequires.Find(link, key => every.Value!.TryGetValue(key, out var list) ? list : null)
                is { State: not (TaskState.Done or TaskState.Dropped) })
            .ToList();

        return waiting.Count == 0
            ? null
            : $"{string.Join(", ", waiting.Select(link => TaskRequires.Name(link, TaskRequires.Key(slug))))} "
                + $"{(waiting.Count == 1 ? "requires" : "require")} {id.Trim()}, and will keep waiting: a requirement is met only when it is done. "
                + $"loadout task requires <id> {id.Trim()} --remove releases one.";
    }
}
