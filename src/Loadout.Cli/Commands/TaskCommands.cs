using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Git;
using Loadout.Core.Lists;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Results;
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

    [CommandOption("--global")]
    [Description("The workspace-wide list, for what belongs to no project yet.")]
    public bool Global { get; init; }

    /// <summary>
    /// The list these settings name: a project, or null for the workspace-wide one.
    /// </summary>
    /// <remarks>
    /// Asked for, never fallen back to. A command run outside any repository
    /// fails as it always did rather than quietly writing to the workspace-wide
    /// list, because a task recorded somewhere nobody looks is worse than one
    /// refused with the reason.
    /// </remarks>
    internal async Task<OperationResult<Models.Projects.ProjectResolution?>> ListAsync(
        IProjectService projects,
        CancellationToken ct)
    {
        if (Global)
        {
            return Project is { Length: > 0 }
                ? OperationResult<Models.Projects.ProjectResolution?>.Fail(
                    "--global and --project name different lists. Give one.", ExitCode.InvalidArguments)
                : OperationResult<Models.Projects.ProjectResolution?>.Ok(null);
        }

        var resolution = await ProjectHandle.ResolveAsync(projects, Project, Repo, ct).ConfigureAwait(false);

        return resolution.Succeeded
            ? OperationResult<Models.Projects.ProjectResolution?>.Ok(resolution.Value)
            : OperationResult<Models.Projects.ProjectResolution?>.Fail(resolution.Error!, resolution.ExitCode);
    }
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
    private readonly IConfigurationService _configuration;
    private readonly ITaskActivityReader _activity;

    public TaskListCommand(
        ITaskService tasks,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider time,
        IConfigurationService configuration,
        ITaskActivityReader activity)
    {
        _tasks = tasks;
        _projects = projects;
        _git = git;
        _console = console;
        _time = time;
        _configuration = configuration;
        _activity = activity;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandOption("--all")]
        [Description("Include what is done and dropped, rather than only what is open.")]
        public bool All { get; init; }

        [CommandOption("--filter <QUERY>")]
        [Description("Keep only what the query says, such as \"state:doing,open login\". It replaces the default of hiding done and dropped work. A query starting with a minus is given as --filter=-state:done.")]
        public string? Filter { get; init; }

        [CommandOption("--sort <FIELDS>")]
        [Description("Sort by these fields, commas between: id, title, state, kind, by, updated, note, runs, launches. Newest first is --sort=-updated.")]
        public string? Sort { get; init; }

        [CommandOption("--saved")]
        [Description("Use the query saved for the task list in Studio or the TUI. Without --json it is used anyway; --filter or --all set it aside.")]
        public bool Saved { get; init; }
    }

    /// <summary>
    /// The query the list is put through: what --filter says; or else the
    /// saved query, when there is one and it applies; or else the default of
    /// hiding done and dropped work unless --all. --sort goes after.
    /// </summary>
    /// <param name="settings">What the command was given.</param>
    /// <param name="saved">The query saved for the task list, or null.</param>
    /// <param name="json">Whether the output is JSON, which takes the saved query only when asked.</param>
    /// <remarks>
    /// A script reading --json gets the same list whoever last changed a
    /// filter on a screen, unless it asks for --saved. Somebody reading the
    /// list gets the one their screens show.
    /// </remarks>
    internal static OperationResult<ListQuery> Query(Settings settings, string? saved, bool json)
    {
        var useSaved = saved is not null && !settings.All && (settings.Saved || !json);
        var filter = settings.Filter is { Length: > 0 } given
            ? given
            : useSaved ? saved! : settings.All ? string.Empty : TaskFields.DefaultQuery;
        var sort = settings.Sort is { Length: > 0 } fields ? $" {ListQuery.SortKey}:{fields}" : string.Empty;

        return ListQuery.Parse(filter + sort);
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var saved = ListViews.Saved(
            (await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false)).Value?.Studio, "tasks");
        var query = Query(settings, saved, output.IsJson);

        if (query.Failed)
        {
            return output.Fail(query);
        }

        var project = resolution.Value;
        var slug = project?.Entry.Slug;

        // Read with its revision, which the JSON carries so a page or a
        // script changing a task from this read can say which read it was.
        var read = await _tasks.ReadAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var listed = OperationResult<IReadOnlyList<TaskItem>>.Ok(read.Value!.Items);

        var now = _time.GetUtcNow();

        // Only work is checked against the repository and turned into
        // suggestions. An idea has no commits to show for itself and is not
        // something to start, so both would be noise about it.
        var disagreements = project is null
            ? []
            : await CheckAsync(project, Work(listed.Value!), now, cancellationToken).ConfigureAwait(false);

        // Runs and launches are counted from the run journal and the launch
        // ledger, read whole, so only for a query that names one.
        var counted = slug is not null && TaskFields.Counts(query.Value!);
        var activity = counted
            ? await _activity.ReadAsync(slug!, cancellationToken).ConfigureAwait(false)
            : TaskActivity.None;
        var fields = TaskFields.For(activity);

        var applied = fields.Apply(query.Value!, listed.Value!, now);

        if (applied.Failed)
        {
            return output.Fail(applied);
        }

        var shown = applied.Value!;

        // What left this list, kept apart from its tasks so nothing counting
        // them, or suggesting what to do next, ever counts one of these.
        var moved = await _tasks.MovedAsync(cancellationToken).ConfigureAwait(false);
        var away = moved.Succeeded
            ? [.. moved.Value!
                .Where(one => one.Left(slug))
                .Where(one => settings.All || one.Task.State is not (TaskState.Done or TaskState.Dropped))
                .OrderBy(one => one.Task.Id, StringComparer.Ordinal)]
            : new List<TaskMoved>();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                tasks = shown.Select(item => new
                {
                    item.Id,
                    item.Title,
                    state = item.State.ToString().ToLowerInvariant(),
                    kind = item.Kind.ToString().ToLowerInvariant(),
                    item.DeclaredBy,
                    declared = item.DeclaredUtc,
                    item.Note,
                    moved = TaskTrace.Json(item.Moved),
                    runs = counted ? activity.RunsOf(item.Id) : (int?)null,
                    launches = counted ? activity.LaunchesOf(item.Id) : (int?)null,
                }),
                movedAway = away.Select(one => new
                {
                    id = one.Task.Id,
                    title = one.Task.Title,
                    kind = one.Task.Kind.ToString().ToLowerInvariant(),
                    to = one.Where,
                    at = one.Task.Moved!.Utc,
                }),
                recorded = listed.Value!.Count,
                revision = read.Value.Revision,
                query = fields.Shape(query.Value!),
                unsupported = disagreements.Select(d => new { task = d.TaskId, d.Detail }),
                suggested = Suggestions.Compose(Work(shown), disagreements)
                    .Select(s => new { s.Text, source = s.Source.ToString().ToLowerInvariant() }),
            });

            return CommandOutput.Success();
        }

        // A list someone else's filter has narrowed says so, so it never looks
        // short for no reason.
        if (settings.Filter is not { Length: > 0 } && !settings.All && saved is not null)
        {
            output.WriteLine($"[dim]Filtered by the saved query {Markup.Escape(saved)}. --all or --filter sets it aside.[/]");
        }

        if (shown.Count == 0)
        {
            output.WriteLine(listed.Value!.Count == 0
                ? $"[dim]{Markup.Escape(TaskPaths.Describe(slug))} has no tasks recorded.[/]"
                : settings.Filter is { Length: > 0 } filter
                    ? $"[dim]Nothing matches {Markup.Escape(filter)}. {listed.Value!.Count} recorded in total; without --filter, the open ones show.[/]"
                    : $"[dim]Nothing open. {listed.Value!.Count} recorded in total; --all shows them.[/]");

            TaskTrace.Write(output, away);

            return CommandOutput.Success();
        }

        foreach (var item in shown)
        {
            output.WriteLine(
                $"{Markup.Escape(item.Id),-22} "
                + $"{State(item.State),-9} "
                + (item.Kind == TaskKind.Idea ? "[blue]idea[/] " : string.Empty)
                + $"{Markup.Escape(item.Title)}");

            output.WriteLine(
                $"  [dim]{Markup.Escape(item.DeclaredBy.Length > 0 ? item.DeclaredBy : "nobody named")}"
                + $", {item.DeclaredUtc:yyyy-MM-dd}"
                + (item.Moved is { } came ? $" · {Markup.Escape(TaskTrace.From(came))}" : string.Empty)
                + "[/]");
        }

        TaskTrace.Write(output, away);

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

        var suggested = Suggestions.Compose(Work(shown), disagreements);

        if (suggested.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Next[/]  [dim]composed from the record above[/]");

            foreach (var suggestion in suggested)
            {
                output.WriteLine($"  {Markup.Escape(suggestion.Text)}");
            }
        }

        return CommandOutput.Success();
    }

    private static List<TaskItem> Work(IEnumerable<TaskItem> items) =>
        [.. items.Where(item => item.Kind == TaskKind.Task)];

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
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskDeclareCommand(
        ITaskService tasks,
        IProjectService projects,
        Core.Workspace.IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _workspace = workspace;
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
    public override async Task<int> ExecuteAsync(
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

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would record [bold]{Markup.Escape(settings.Id)}[/] as "
                + $"{state.ToString().ToLowerInvariant()} for {Markup.Escape(TaskPaths.Describe(slug))}. "
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

        if (slug is not null)
        {
            await SayIfNobodyWillSeeItAsync(output, slug, cancellationToken).ConfigureAwait(false);
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Says so when the project does not carry its tasks into a session.
    /// </summary>
    /// <remarks>
    /// Said rather than fixed. Turning the switch on from here would rewrite the
    /// manifest as a side effect of recording a task, which is the kind of
    /// surprise preview-before-mutation exists to prevent — and the switch is
    /// paid for on every launch of the project, not only this one.
    /// <para>
    /// Not a failure: the record is worth keeping either way, and the audit
    /// trail reads it whether or not a session is shown it.
    /// </para>
    /// </remarks>
    private async Task SayIfNobodyWillSeeItAsync(
        CommandOutput output,
        string slug,
        CancellationToken ct)
    {
        var manifest = await _workspace.ReadProjectAsync(slug, ct).ConfigureAwait(false);

        if (manifest.Failed || manifest.Value!.Context.Tasks)
        {
            return;
        }

        output.WriteLine(
            $"[yellow]![/] {Markup.Escape(slug)} does not carry its tasks into a session, "
            + "so nothing will show this to one.");

        output.WriteLine(
            "[dim]  Turn it on with: loadout project context tasks on "
            + $"--project {Markup.Escape(slug)}[/]");
    }
}

/// <summary>How a moved task's trace is said, on the command line and in JSON.</summary>
internal static class TaskTrace
{
    /// <summary>Where a task came from, as the list it is on now says it.</summary>
    public static string From(TaskMove moved) =>
        $"from {TaskPaths.Describe(moved.From.Length > 0 ? moved.From : null)}, {moved.Utc:yyyy-MM-dd}";

    /// <summary>Where a moved task came from, for JSON, or null when it never moved.</summary>
    public static object? Json(TaskMove? moved) =>
        moved is null ? null : new { from = moved.From.Length > 0 ? moved.From : null, at = moved.Utc };

    /// <summary>
    /// What left this list, after its own tasks and apart from them, with the
    /// command that shows each where it is now.
    /// </summary>
    public static void Write(CommandOutput output, IReadOnlyList<TaskMoved> away)
    {
        if (away.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Moved away[/]  [dim]not on this list any more[/]");

        foreach (var one in away)
        {
            var where = TaskPaths.Describe(one.Where);
            var look = one.Where is { Length: > 0 } project ? $"--project {project}" : "--global";

            output.WriteLine(
                $"[dim]{Markup.Escape(one.Task.Id),-22} moved to {Markup.Escape(where)}, "
                + $"{one.Task.Moved!.Utc:yyyy-MM-dd}  {Markup.Escape(one.Task.Title)}[/]");
            output.WriteLine($"[dim]  loadout task list {Markup.Escape(look)}[/]");
        }
    }
}

/// <summary>Where a move sends something: a project, or the workspace-wide list.</summary>
internal static class MoveDestination
{
    /// <summary>
    /// The list named by <c>--to</c> or <c>--to-global</c>, or a refusal when
    /// neither or both was given.
    /// </summary>
    /// <remarks>
    /// Asked for outright rather than defaulted. A move with no destination
    /// that quietly went to the workspace-wide list would take a task off the
    /// project somebody was looking at and put it where nobody looks.
    /// </remarks>
    public static OperationResult<string?> Of(string? to, bool toGlobal)
    {
        var named = to is { Length: > 0 } && to.Trim().Length > 0;

        return (named, toGlobal) switch
        {
            (true, true) => OperationResult<string?>.Fail(
                "--to and --to-global name different lists. Give one.", ExitCode.InvalidArguments),
            (false, false) => OperationResult<string?>.Fail(
                "Say where it goes: --to <project>, or --to-global for the workspace-wide list.",
                ExitCode.InvalidArguments),
            (true, false) => OperationResult<string?>.Ok(to!.Trim()),
            _ => OperationResult<string?>.Ok(null),
        };
    }
}

/// <summary>Moves a task to the list it belongs on.</summary>
/// <remarks>
/// An idea's task moves its record with it, and an accepted one's plan, so
/// this is the same move as <c>idea move</c>: one implementation, reached
/// from whichever list somebody was looking at.
/// </remarks>
[Description("Move a task to another project's list, or to the workspace-wide one. Its plan and record go with it.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "move task wrong project misplaced reassign transfer",
    Mutates = true,
    Example = "fix-login --project website --to homelab")]
public sealed class TaskMoveCommand : AsyncCommand<TaskMoveCommand.Settings>
{
    private readonly Core.Ideas.IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskMoveCommand(
        Core.Ideas.IIdeaService ideas,
        IProjectService projects,
        IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task to move.")]
        public string Id { get; init; } = string.Empty;

        [CommandOption("--to <SLUG>")]
        [Description("The project it belongs to.")]
        public string? To { get; init; }

        [CommandOption("--to-global")]
        [Description("Move it to the workspace-wide list, for what belongs to no project yet.")]
        public bool ToGlobal { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var destination = MoveDestination.Of(settings.To, settings.ToGlobal);

        if (destination.Failed)
        {
            return output.Fail(destination);
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        return await Move.RunAsync(
            output, _ideas, resolution.Value?.Entry.Slug, destination.Value, settings.Id, settings.DryRun, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>The shared run of a move, for the task and idea commands both.</summary>
internal static class Move
{
    public static async Task<int> RunAsync(
        CommandOutput output,
        Core.Ideas.IIdeaService ideas,
        string? from,
        string? to,
        string id,
        bool dryRun,
        CancellationToken ct)
    {
        if (dryRun)
        {
            output.WriteLine(
                $"Would move [bold]{Markup.Escape(id)}[/] from {Markup.Escape(TaskPaths.Describe(from))} "
                + $"to {Markup.Escape(TaskPaths.Describe(to))}. Nothing was moved.");

            return CommandOutput.Success();
        }

        var moved = await ideas.MoveAsync(from, to, id, ct).ConfigureAwait(false);

        if (moved.Failed)
        {
            return output.Fail(moved);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = moved.Value!.Id,
                from,
                to,
                kind = moved.Value.Kind.ToString().ToLowerInvariant(),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            string.Equals(from, to, StringComparison.Ordinal)
                ? $"{Markup.Escape(moved.Value!.Id)} is on {Markup.Escape(TaskPaths.Describe(to))} already. Nothing was moved."
                : $"[green]+[/] Moved {Markup.Escape(moved.Value!.Id)} from {Markup.Escape(TaskPaths.Describe(from))} "
                    + $"to {Markup.Escape(TaskPaths.Describe(to))}. {Markup.Escape(TaskPaths.Describe(from))} "
                    + "still shows where it went.");

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
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;

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
