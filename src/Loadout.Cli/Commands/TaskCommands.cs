using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shared settings for the task commands.</summary>
public class TaskSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Project the tasks belong to. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>
/// What is being worked on, and what the record makes of it.
/// </summary>
/// <remarks>
/// Kept apart from memory because the two answer different questions. A memory
/// is something that stays true; a task is true today and stops being true, and
/// mixing them fills the durable store with things that expire.
/// </remarks>
[Description("List what is being worked on, and what the record does not back up.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "tasks backlog what is open todo status where were we")]
public sealed class TaskListCommand : AsyncCommand<TaskListCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public TaskListCommand(
        ITaskService tasks,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider time)
    {
        _tasks = tasks;
        _projects = projects;
        _git = git;
        _console = console;
        _time = time;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandOption("--all")]
        [Description("Include what is done and dropped, rather than only what is open.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await TaskResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        var listed = await _tasks
            .ListAsync(project.Entry.Slug, cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var now = _time.GetUtcNow();
        var disagreements = await CheckAsync(project, listed.Value!, now, cancellationToken)
            .ConfigureAwait(false);

        var shown = settings.All
            ? listed.Value!
            : [.. listed.Value!.Where(item =>
                item.State is not (TaskState.Done or TaskState.Dropped))];

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                tasks = shown.Select(item => new
                {
                    item.Id,
                    item.Title,
                    state = item.State.ToString().ToLowerInvariant(),
                    item.DeclaredBy,
                    declared = item.DeclaredUtc,
                    item.Note,
                }),
                unsupported = disagreements.Select(d => new { task = d.TaskId, d.Detail }),
            });

            return CommandOutput.Success();
        }

        if (shown.Count == 0)
        {
            output.WriteLine(listed.Value!.Count == 0
                ? $"[dim]{Markup.Escape(project.Entry.Slug)} has no tasks recorded.[/]"
                : $"[dim]Nothing open. {listed.Value!.Count} recorded in total; --all shows them.[/]");

            return CommandOutput.Success();
        }

        foreach (var item in shown.OrderBy(i => i.State).ThenBy(i => i.Id, StringComparer.Ordinal))
        {
            output.WriteLine(
                $"{Markup.Escape(item.Id),-22} "
                + $"{State(item.State),-9} "
                + $"{Markup.Escape(item.Title)}");

            output.WriteLine(
                $"  [dim]{Markup.Escape(item.DeclaredBy.Length > 0 ? item.DeclaredBy : "nobody named")}"
                + $", {item.DeclaredUtc:yyyy-MM-dd}[/]");
        }

        if (disagreements.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]What the record does not back up[/]");

            foreach (var disagreement in disagreements)
            {
                output.WriteLine(
                    $"  [yellow]{Markup.Escape(disagreement.TaskId)}[/] "
                    + $"{Markup.Escape(disagreement.Detail)}");
            }

            // Said once, plainly, under the list it qualifies. Without it the
            // section reads as a list of errors, and it is not one.
            output.WriteBlankLine();
            output.WriteLine(
                "[dim]These are observations, not verdicts. Corroboration can say a claim is "
                + "unsupported; it can never say one is wrong.[/]");
        }

        return CommandOutput.Success();
    }

    private static string State(TaskState state) => state switch
    {
        TaskState.Doing => "[green]doing[/]",
        TaskState.Blocked => "[yellow]blocked[/]",
        TaskState.Done => "[dim]done[/]",
        TaskState.Dropped => "[dim]dropped[/]",
        _ => "open",
    };

    /// <summary>
    /// What the repository has to say about the claims.
    /// </summary>
    /// <remarks>
    /// A repository that cannot be read means no commits, which means the
    /// "nothing committed since" observation would fire on everything. So a
    /// failure here returns nothing at all rather than an empty history: no
    /// answer beats a confidently wrong one.
    /// </remarks>
    private async Task<IReadOnlyList<TaskDisagreement>> CheckAsync(
        Models.Projects.ProjectResolution project,
        IReadOnlyList<TaskItem> tasks,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (project.LocalPath is not { Length: > 0 } path || !Directory.Exists(path))
        {
            return [];
        }

        var oldest = tasks
            .Where(item => item.State is TaskState.Done or TaskState.Doing)
            .Select(item => item.DeclaredUtc)
            .DefaultIfEmpty(now)
            .Min();

        var commits = await _git.ListCommitsAsync(path, oldest, ct).ConfigureAwait(false);

        return commits.Succeeded
            ? TaskCorroboration.Check(tasks, commits.Value!, now)
            : [];
    }
}

/// <summary>Records where a task stands.</summary>
[Description("Say where a task stands. Adds it when the id is new.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "declare task state done doing blocked backlog", Mutates = true)]
public sealed class TaskDeclareCommand : AsyncCommand<TaskDeclareCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskDeclareCommand(
        ITaskService tasks,
        IProjectService projects,
        IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("Short identifier for the task.")]
        public string Id { get; init; } = string.Empty;

        [CommandArgument(1, "<state>")]
        [Description("open, doing, done, blocked or dropped.")]
        public string State { get; init; } = string.Empty;

        [CommandOption("--title <TITLE>")]
        [Description("What the work is. Kept as it was when not given.")]
        public string? Title { get; init; }

        [CommandOption("--note <NOTE>")]
        [Description("Anything worth adding.")]
        public string? Note { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is saying so. Defaults to this machine's user.")]
        public string? By { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!Enum.TryParse<TaskState>(settings.State, ignoreCase: true, out var state))
        {
            return output.Fail(
                $"'{settings.State}' is not a state. Use open, doing, done, blocked or dropped.",
                ExitCode.InvalidArguments);
        }

        var resolution = await TaskResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would record [bold]{Markup.Escape(settings.Id)}[/] as "
                + $"{state.ToString().ToLowerInvariant()} for {Markup.Escape(slug)}. "
                + "Nothing was written.");

            return CommandOutput.Success();
        }

        var declared = await _tasks.DeclareAsync(
            slug,
            settings.Id,
            state,
            settings.By is { Length: > 0 } who ? who : Environment.UserName,
            settings.Title,
            settings.Note,
            cancellationToken).ConfigureAwait(false);

        if (declared.Failed)
        {
            return output.Fail(declared);
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(declared.Value!.Id)} is "
            + $"{declared.Value.State.ToString().ToLowerInvariant()}.");

        return CommandOutput.Success();
    }
}

/// <summary>Forgets a task.</summary>
[Description("Forget a task entirely.")]
[CommandMeta(CommandCategory.Workspace, Intent = "remove delete forget task", Mutates = true)]
public sealed class TaskRemoveCommand : AsyncCommand<TaskRemoveCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskRemoveCommand(
        ITaskService tasks,
        IProjectService projects,
        IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task to forget.")]
        public string Id { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await TaskResolution
            .ResolveAsync(_projects, settings, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would forget [bold]{Markup.Escape(settings.Id)}[/]. Nothing was removed.");

            return CommandOutput.Success();
        }

        var removed = await _tasks
            .RemoveAsync(slug, settings.Id, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine($"[green]+[/] Forgot {Markup.Escape(settings.Id)}.");

        return CommandOutput.Success();
    }
}

/// <summary>Working out which project a task command is about.</summary>
internal static class TaskResolution
{
    internal static Task<Loadout.Models.Results.OperationResult<Models.Projects.ProjectResolution>> ResolveAsync(
        IProjectService projects,
        TaskSettings settings,
        CancellationToken ct) =>
        settings.Project is { Length: > 0 } handle
            ? projects.ResolveAsync(handle, ct)
            : projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), ct);
}
