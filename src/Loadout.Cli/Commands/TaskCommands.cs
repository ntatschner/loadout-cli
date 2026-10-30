using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backfill;
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
    private readonly Core.Workspace.IWorkspaceManager _workspace;

    public TaskListCommand(
        ITaskService tasks,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider time,
        IConfigurationService configuration,
        ITaskActivityReader activity,
        Core.Workspace.IWorkspaceManager workspace)
    {
        _tasks = tasks;
        _projects = projects;
        _git = git;
        _console = console;
        _time = time;
        _configuration = configuration;
        _activity = activity;
        _workspace = workspace;
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

        [CommandOption("--every-list")]
        [Description("Every project's tasks and the workspace-wide list's, as one list. Narrow it with --filter project:<slug>.")]
        public bool EveryList { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("How many tasks a page holds. Without it the whole list is one page.")]
        public int? Limit { get; init; }

        [CommandOption("--page <NUMBER>")]
        [Description("Which page to show, counted from 1. Needs --limit.")]
        public int Page { get; init; } = 1;

        [CommandOption("--missing")]
        [Description("Only open and blocked tasks without a done-when: those marked missing, and those with a proposal waiting for you.")]
        public bool Missing { get; init; }
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

        var lists = await ListsAsync(settings, cancellationToken).ConfigureAwait(false);

        if (lists.Failed)
        {
            return output.Fail(lists);
        }

        var saved = ListViews.Saved(
            (await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false)).Value?.Studio, "tasks");
        var query = Query(settings, saved, output.IsJson);

        if (query.Failed)
        {
            return output.Fail(query);
        }

        // One project, when the list is one project's: only then is there a
        // repository to check claims against, or a list for tasks to have left.
        var project = lists.Value!.Project;
        var slug = project?.Entry.Slug;

        // Which list each task came from, by the task itself rather than its
        // id, since two projects can each have a task by the same id. Each
        // list is read with its revision; the JSON carries it for one list,
        // so a page or a script changing a task from this read can say which
        // read it was. Several lists have no one revision to give.
        var where = new Dictionary<TaskItem, string?>(ReferenceEqualityComparer.Instance);
        string? revision = null;

        foreach (var list in lists.Value.Slugs)
        {
            var read = await _tasks.ReadAsync(list, cancellationToken).ConfigureAwait(false);

            if (read.Failed)
            {
                return output.Fail(read);
            }

            revision = lists.Value.Slugs.Count == 1 ? read.Value!.Revision : null;

            foreach (var item in read.Value!.Items)
            {
                where[item] = list;
            }
        }

        var recorded = where.Keys.ToList();
        var now = _time.GetUtcNow();

        // Only work is checked against the repository and turned into
        // suggestions. An idea has no commits to show for itself and is not
        // something to start, so both would be noise about it.
        var disagreements = project is null
            ? []
            : await CheckAsync(project, Work(recorded), now, cancellationToken).ConfigureAwait(false);

        // Runs and launches are counted from the run journal and the launch
        // ledger, read whole, so only for a query that names one.
        var counted = TaskFields.Counts(query.Value!);
        var activity = new Dictionary<string, TaskActivity>(StringComparer.OrdinalIgnoreCase);

        if (counted)
        {
            foreach (var list in lists.Value.Slugs.OfType<string>())
            {
                activity[list] = await _activity.ReadAsync(list, cancellationToken).ConfigureAwait(false);
            }
        }

        TaskActivity ActivityOf(TaskItem item) =>
            where[item] is { } list && activity.TryGetValue(list, out var found) ? found : TaskActivity.None;

        var fields = TaskFields.For(
            item => ActivityOf(item).RunsOf(item.Id),
            item => ActivityOf(item).LaunchesOf(item.Id),
            item => where[item]);

        var applied = fields.Apply(query.Value!, recorded, now);

        if (applied.Failed)
        {
            return output.Fail(applied);
        }

        // After the query, so --missing narrows whatever it kept rather than
        // replacing it.
        IReadOnlyList<TaskItem> kept = settings.Missing
            ? [.. applied.Value!.Where(item => ContractBackfill.MarkOf(item) != ContractMark.None)]
            : applied.Value!;

        var paged = ListPages.Of(kept, settings.Page, settings.Limit);

        if (paged.Failed)
        {
            return output.Fail(paged);
        }

        var page = paged.Value!;
        var shown = page.Items;
        var every = settings.EveryList;

        // What left this list, kept apart from its tasks so nothing counting
        // them, or suggesting what to do next, ever counts one of these. Not
        // for every list at once, where each task is shown on the list it is on.
        var moved = every ? null : await _tasks.MovedAsync(cancellationToken).ConfigureAwait(false);
        var away = moved is { Succeeded: true }
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
                    type = item.Type is { } type ? TaskTypes.Name(type) : null,
                    typeBy = item.TypeBy is { } typeBy ? TaskTypes.Name(typeBy) : null,
                    labels = item.Labels,
                    project = where[item],
                    item.DeclaredBy,
                    declared = item.DeclaredUtc,
                    item.Note,
                    hasDetail = item.Detail.Length > 0,
                    doneWhen = item.DoneWhen,
                    mark = ContractBackfill.Name(ContractBackfill.MarkOf(item)),
                    moved = TaskTrace.Json(item.Moved),
                    runs = counted ? ActivityOf(item).RunsOf(item.Id) : (int?)null,
                    launches = counted ? ActivityOf(item).LaunchesOf(item.Id) : (int?)null,
                }),
                movedAway = away.Select(one => new
                {
                    id = one.Task.Id,
                    title = one.Task.Title,
                    kind = one.Task.Kind.ToString().ToLowerInvariant(),
                    to = one.Where,
                    at = one.Task.Moved!.Utc,
                }),
                recorded = recorded.Count,
                revision,
                total = page.Total,
                page = page.Page,
                pages = page.Pages,
                limit = page.Limit,
                query = fields.Shape(query.Value!),
                unsupported = disagreements.Select(d => new { task = d.TaskId, d.Detail }),
                suggested = Suggestions.Compose(Work(applied.Value!), disagreements)
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

        var described = every ? "No list" : TaskPaths.Describe(slug);

        if (shown.Count == 0)
        {
            output.WriteLine(recorded.Count == 0
                ? $"[dim]{Markup.Escape(described)} has no tasks recorded.[/]"
                : settings.Missing
                    ? "[dim]No open or blocked task here lacks a done-when or has a proposal waiting.[/]"
                : settings.Filter is { Length: > 0 } filter
                    ? $"[dim]Nothing matches {Markup.Escape(filter)}. {recorded.Count} recorded in total; without --filter, the open ones show.[/]"
                    : $"[dim]Nothing open. {recorded.Count} recorded in total; --all shows them.[/]");

            TaskTrace.Write(output, away);

            return CommandOutput.Success();
        }

        var rows = new TaskRows(
            _console.Profile.Out.IsTerminal ? _console.Profile.Width : null,
            every ? shown.Select(item => where[item]) : null);

        foreach (var item in shown)
        {
            output.WriteLine(rows.Line(item, where[item]));

            var mark = ContractBackfill.Label(ContractBackfill.MarkOf(item));

            output.WriteLine(
                (mark is null ? "  [dim]" : $"  [yellow]{mark}[/] [dim]· ")
                + $"{Markup.Escape(item.DeclaredBy.Length > 0 ? item.DeclaredBy : "nobody named")}"
                + $", {item.DeclaredUtc:yyyy-MM-dd}"
                + (item.Moved is { } came ? $" · {Markup.Escape(TaskTrace.From(came))}" : string.Empty)
                + (item.Detail.Length > 0 ? " · more with task show" : string.Empty)
                + "[/]");
        }

        if (page.Limit is not null)
        {
            output.WriteLine(
                $"[dim]Page {page.Page} of {page.Pages}, {page.Total} in all."
                + (page.More ? $" --page {page.Page + 1} for the next.[/]" : "[/]"));
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

        var suggested = Suggestions.Compose(Work(applied.Value!), disagreements);

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

    /// <summary>The lists to read: every one with --every-list, or the one the settings name.</summary>
    private async Task<OperationResult<(Models.Projects.ProjectResolution? Project, IReadOnlyList<string?> Slugs)>> ListsAsync(
        Settings settings,
        CancellationToken ct)
    {
        if (!settings.EveryList)
        {
            var resolution = await settings.ListAsync(_projects, ct).ConfigureAwait(false);

            return resolution.Succeeded
                ? OperationResult<(Models.Projects.ProjectResolution?, IReadOnlyList<string?>)>.Ok(
                    (resolution.Value, [resolution.Value?.Entry.Slug]))
                : OperationResult<(Models.Projects.ProjectResolution?, IReadOnlyList<string?>)>.Fail(
                    resolution.Error!, resolution.ExitCode);
        }

        if (settings.Project is { Length: > 0 } || settings.Global)
        {
            return OperationResult<(Models.Projects.ProjectResolution?, IReadOnlyList<string?>)>.Fail(
                "--every-list already covers every list. Leave out --project and --global, or narrow it with --filter project:<slug>.",
                ExitCode.InvalidArguments);
        }

        if (!_workspace.IsAvailable())
        {
            return OperationResult<(Models.Projects.ProjectResolution?, IReadOnlyList<string?>)>.Fail(
                "There is no workspace on this machine, so there is nowhere to keep tasks.",
                ExitCode.WorkspaceSyncFailed);
        }

        return OperationResult<(Models.Projects.ProjectResolution?, IReadOnlyList<string?>)>.Ok(
            (null, TaskPaths.Lists(_workspace.LocalPath)));
    }

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

/// <summary>A task list's first line per task, laid out to fit the terminal.</summary>
/// <remarks>
/// Columns are padded by what shows, not by the markup that colours it, so
/// they line up. The title is cut only on a terminal: output going to a file
/// or another program has no width to fit, and a cut title there would be
/// text a script then reads as the title.
/// </remarks>
internal sealed class TaskRows
{
    private const int IdWidth = 22;
    private const int StateWidth = 7;
    private static readonly int TypeWidth = TaskTypes.Names.Max(name => name.Length);
    private const int ProjectLimit = 16;

    private readonly int? _width;
    private readonly int _project;

    /// <param name="width">The terminal's width, or null when there is no terminal.</param>
    /// <param name="projects">Each row's project, when rows come from more than one list, or null for one list.</param>
    public TaskRows(int? width, IEnumerable<string?>? projects)
    {
        _width = width;
        _project = projects is null
            ? 0
            : Math.Min(ProjectLimit, projects.Select(one => (one ?? TaskFields.WorkspaceWide).Length).DefaultIfEmpty(0).Max());
    }

    /// <summary>The line for one task, as markup.</summary>
    public string Line(TaskItem item, string? project)
    {
        var state = item.State.ToString().ToLowerInvariant();
        var line = new System.Text.StringBuilder();

        line.Append(Markup.Escape(item.Id.PadRight(IdWidth))).Append(' ');
        line.Append(Coloured(item.State, state)).Append(new string(' ', Math.Max(0, StateWidth - state.Length))).Append(' ');

        var used = Math.Max(IdWidth, item.Id.Length) + 1 + Math.Max(StateWidth, state.Length) + 1;

        if (_project > 0)
        {
            var named = Fit(project ?? TaskFields.WorkspaceWide, _project).PadRight(_project);

            line.Append("[dim]").Append(Markup.Escape(named)).Append("[/] ");
            used += _project + 1;
        }

        if (item.Kind == TaskKind.Idea)
        {
            line.Append("[blue]idea[/] ");
            used += 5;
        }

        // In its own column, padded to the longest name, so titles still line
        // up; a task recorded before there were types leaves it blank.
        var type = item.Type is { } typed ? TaskTypes.Name(typed) : string.Empty;

        line.Append("[dim]").Append(Markup.Escape(type.PadRight(TypeWidth))).Append("[/] ");
        used += TypeWidth + 1;

        // After the title, and taken from its room, so a row with labels
        // still fits rather than wrapping its labels onto a line of their own.
        var labels = item.Labels.Count > 0 ? " " + string.Join(' ', item.Labels.Select(label => $"#{label}")) : string.Empty;

        // Less room than this and the title is left to wrap rather than cut
        // to a word or two, which would say nothing.
        var title = _width is { } width && width - used - labels.Length >= 20
            ? Fit(item.Title, width - used - labels.Length)
            : item.Title;

        line.Append(Markup.Escape(title));

        return (labels.Length > 0 ? line.Append("[dim]").Append(Markup.Escape(labels)).Append("[/]") : line).ToString();
    }

    private static string Fit(string text, int room) =>
        text.Length <= room ? text : text[..(room - 1)].TrimEnd() + "…";

    private static string Coloured(TaskState state, string said) => state switch
    {
        TaskState.Doing => $"[green]{said}[/]",
        TaskState.Blocked => $"[yellow]{said}[/]",
        TaskState.Done or TaskState.Dropped => $"[dim]{said}[/]",
        _ => said,
    };
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
        [Description("What the work is, in a line of 72 characters at most. A longer one is shortened and the whole of it kept in the detail. Kept as it was when not given.")]
        public string? Title { get; init; }

        [CommandOption("--note <NOTE>")]
        [Description("A short aside worth adding.")]
        public string? Note { get; init; }

        [CommandOption("--detail <TEXT>")]
        [Description("The full description, which task show prints and lists leave out. Kept as it was when not given.")]
        public string? Detail { get; init; }

        /// <summary>The task's finish line, replacing whatever it had.</summary>
        /// <remarks>
        /// Repeated rather than one comma-separated string, as on
        /// <c>team run</c>, because a condition is a sentence and sentences
        /// contain commas.
        /// </remarks>
        [CommandOption("--done-when <CONDITION>")]
        [Description(
            "Something that must be true for the task to be finished. Repeat it for each; together they replace "
            + "what the task had. A team run for the task is held to them, and a team queue takes only tasks that have some.")]
        public string[] DoneWhen { get; init; } = [];

        [CommandOption("--no-done-when")]
        [Description("Clear the task's done-when.")]
        public bool NoDoneWhen { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is saying so. Defaults to this machine's user.")]
        public string? By { get; init; }

        [CommandOption("--type <TYPE>")]
        [Description(
            "What the work is about: new-project, feature, bug-fix, ci-cd, release, docs or maintenance. "
            + "Your choice is kept until you change it. Left out, a new task's type is guessed from its words.")]
        public string? Type { get; init; }

        /// <summary>The done-when to record: null to leave it, empty to clear it.</summary>
        internal IReadOnlyList<string>? DoneWhenGiven =>
            NoDoneWhen ? [] : DoneWhen.Length > 0 ? DoneWhen : null;
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

        if (settings.NoDoneWhen && settings.DoneWhen.Length > 0)
        {
            return output.Fail(
                "--done-when and --no-done-when say opposite things. Give one or the other.",
                ExitCode.InvalidArguments);
        }

        TaskType? type = null;

        if (settings.Type is { } typed)
        {
            if (TaskTypes.Parse(typed) is not { } parsed)
            {
                return output.Fail(
                    $"'{typed}' is not a type. Use {string.Join(", ", TaskTypes.Names)}.",
                    ExitCode.InvalidArguments);
            }

            type = parsed;
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;

        var shortens = settings.Title is { Length: > 0 } title && TaskTitles.Shortens(title);

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would record [bold]{Markup.Escape(settings.Id)}[/] as "
                + $"{state.ToString().ToLowerInvariant()} for {Markup.Escape(TaskPaths.Describe(slug))}. "
                + "Nothing was written.");

            if (shortens)
            {
                output.WriteLine(
                    $"[yellow]![/] The title would be shortened to "
                    + $"\"{Markup.Escape(TaskTitles.Shorten(settings.Title!))}\", with the whole of it kept in the detail.");
            }

            return CommandOutput.Success();
        }

        var declared = await _tasks.DeclareAsync(
            slug,
            settings.Id,
            state,
            settings.By is { Length: > 0 } who ? who : Environment.UserName,
            settings.Title,
            settings.Note,
            cancellationToken,
            detail: settings.Detail,
            doneWhen: settings.DoneWhenGiven,
            type: type).ConfigureAwait(false);

        if (declared.Failed)
        {
            return output.Fail(declared);
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(declared.Value!.Id)} is "
            + $"{declared.Value.State.ToString().ToLowerInvariant()}"
            + (declared.Value.Type is { } kept ? $", {Markup.Escape(TaskTypeLine.Said(kept, declared.Value.TypeBy))}." : "."));

        // Said where the title is written, or long titles come back: the
        // tidying pass only ever cleans up what is already there.
        if (shortens)
        {
            output.WriteLine(
                $"[yellow]![/] The title was over {TaskTitles.Length} characters, so it is now "
                + $"\"{Markup.Escape(declared.Value.Title)}\" and the whole of it is at the start of the detail.");
            output.WriteLine("[dim]  Give a shorter --title, and the rest as --detail, to choose the words yourself.[/]");
        }

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

/// <summary>How a task's type is said on the command line.</summary>
internal static class TaskTypeLine
{
    /// <summary>The type, and whether it was a guess: <c>type bug fix (guessed)</c>.</summary>
    /// <remarks>
    /// Said beside the type every time, because a guess and a choice are
    /// worth different amounts, and only a choice is left alone by later guesses.
    /// </remarks>
    public static string Said(TaskType type, TaskTypeSource? by) =>
        $"type {TaskTypes.Words(type)}" + by switch
        {
            TaskTypeSource.Rule => " (guessed)",
            TaskTypeSource.Agent => " (the agent's guess)",
            _ => string.Empty,
        };
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

/// <summary>Everything recorded about one task.</summary>
/// <remarks>
/// The other half of keeping lists short: a list row carries the title, and
/// this is where the detail, the note and every claim made about it are read.
/// </remarks>
[Description("Show one task in full: its detail, note and every state declared for it.")]
[CommandMeta(CommandCategory.Workspace, Intent = "show task detail history description")]
public sealed class TaskShowCommand : AsyncCommand<TaskShowCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly Core.Ideas.IIdeaService _ideas;
    private readonly Core.Teams.IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TaskShowCommand(
        ITaskService tasks,
        IProjectService projects,
        Core.Ideas.IIdeaService ideas,
        Core.Teams.IRunJournal journal,
        IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _ideas = ideas;
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task to show.")]
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

        // Read with the list's revision, which 'task apply' takes to refuse a
        // change made from an older read.
        var read = await _tasks.ReadAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (read.Value!.Items.FirstOrDefault(
                item => string.Equals(item.Id, settings.Id.Trim(), StringComparison.OrdinalIgnoreCase))
            is not { } task)
        {
            return output.Fail(
                $"'{settings.Id.Trim()}' is not a task of {TaskPaths.Describe(slug)}.",
                ExitCode.InvalidArguments);
        }

        // The idea it came from, worked out from the id they share rather than
        // kept on the task, where a second copy could drift from the record.
        // A record that cannot be read is no idea, not a failure: most tasks
        // never were one.
        var record = task.Kind == TaskKind.Task
            ? await _ideas.ReadAsync(new Core.Ideas.IdeaPlace(slug, task.Id), cancellationToken).ConfigureAwait(false)
            : null;
        var accepted = record is { Succeeded: true } ? record.Value!.Accepted : null;

        // The team runs started for it, as the chains that carrying on from a
        // run that ended short makes: each run beside the one it followed.
        var chains = slug is null
            ? []
            : new Core.Teams.BlockedRunReader(_journal).ChainsFor(slug, task.Id);

        if (output.IsJson)
        {
            // The task as every surface writes it, beside the list's revision,
            // and the idea it came from, which is a fact about the pair.
            output.WriteJson(new
            {
                project = slug,
                revision = read.Value.Revision,
                task = TaskOperations.Json(task),
                idea = accepted is null ? null : new { plan = accepted.PlanPath, accepted = accepted.AcceptedUtc, by = accepted.AcceptedBy },
                runs = chains.Select(chain => chain.Select(run => new
                {
                    id = run.RunId,
                    team = run.Team,
                    started = run.Started,
                    ended = run.Ended,
                    outcome = Spelt(run),
                    follows = run.Follows,
                    resolved = run.Resolved?.Action,
                    next = run.Resolved?.Next,
                })),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[bold]{Markup.Escape(task.Id)}[/]  {task.State.ToString().ToLowerInvariant()}"
            + (task.Kind == TaskKind.Idea ? "  [blue]idea[/]" : string.Empty)
            + (task.Type is { } type ? $"  {Markup.Escape(TaskTypeLine.Said(type, task.TypeBy))}" : string.Empty)
            + $"  [dim]{Markup.Escape(TaskPaths.Describe(slug))}[/]");
        output.WriteLine(Markup.Escape(task.Title.Length > 0 ? task.Title : task.Id));
        output.WriteLine(
            $"[dim]{Markup.Escape(task.DeclaredBy.Length > 0 ? task.DeclaredBy : "nobody named")}"
            + $", {task.DeclaredUtc:yyyy-MM-dd}"
            + (task.Moved is { } came ? $" · {Markup.Escape(TaskTrace.From(came))}" : string.Empty)
            + (read.Value.Revision is { } revision ? $" · revision {revision}" : string.Empty)
            + "[/]");

        if (accepted is not null)
        {
            output.WriteLine(
                $"[dim]From the idea {Markup.Escape(task.Id)}, accepted {accepted.AcceptedUtc:yyyy-MM-dd}. "
                + $"The plan is at {Markup.Escape(accepted.PlanPath)}; "
                + $"loadout idea show {Markup.Escape(task.Id)} {(slug is null ? "--global" : $"--project {Markup.Escape(slug)}")} --full shows it.[/]");
        }

        Section(output, "Labels", string.Join(", ", task.Labels));
        Section(output, "Note", task.Note);
        Section(output, "Detail", task.Detail);
        Section(output, "Done when", string.Join('\n', task.DoneWhen.Select(one => $"- {one}")));
        Proposal(output, task, slug);
        Section(output, "Runs", string.Join('\n', chains.Select(chain => string.Join(
            " -> ",
            chain.Select(run => $"{run.RunId} ({Spelt(run) ?? "?"}" + (run.Resolved is { } dealt ? $", {dealt.Action}" : string.Empty) + ")")))));

        output.WriteBlankLine();
        output.WriteLine("[bold]History[/]");

        // A task recorded before there was a history has only its latest
        // claim, and says so rather than looking as if it had none.
        if (task.History.Count == 0)
        {
            output.WriteLine(
                $"  {task.DeclaredUtc:yyyy-MM-dd HH:mm}  {task.State.ToString().ToLowerInvariant(),-8} "
                + $"{Markup.Escape(task.DeclaredBy)}  [dim]the latest claim; nothing earlier was kept[/]");

            return CommandOutput.Success();
        }

        foreach (var one in task.History)
        {
            output.WriteLine(
                $"  {one.Utc:yyyy-MM-dd HH:mm}  {one.State.ToString().ToLowerInvariant(),-8} "
                + Markup.Escape(one.By)
                + (one.Reason.Length > 0 ? $"  [dim]{Markup.Escape(one.Reason)}[/]" : string.Empty));
        }

        return CommandOutput.Success();
    }

    /// <summary>A held proposal, with the command that records it, or why the last pass could not make one.</summary>
    private static void Proposal(CommandOutput output, TaskItem task, string? slug)
    {
        if (task.Proposal is not { } proposal || ContractBackfill.MarkOf(task) == ContractMark.None)
        {
            return;
        }

        if (proposal.DoneWhen.Count == 0)
        {
            Section(output, "Missing", $"It has no done-when. The last backfill pass, {proposal.Utc:yyyy-MM-dd}, could not propose one: {Shown.Plainly(proposal.Error)}");

            return;
        }

        Section(
            output,
            "Proposed done-when, waiting for you",
            string.Join('\n', proposal.DoneWhen.Select(one => $"- {one}"))
            + (proposal.Why.Length > 0 ? $"\n{proposal.Why}" : string.Empty)
            + "\nNothing is recorded until you say so. To record them as they are, or edit them first:\n"
            + TaskProposeDoneWhenCommand.Accepting(task.Id, task.State.ToString().ToLowerInvariant(), slug, proposal.DoneWhen));
    }

    private static string? Spelt(Core.Teams.RunSummary run) =>
        Core.Teams.RunOutcomes.Of(run) is { } outcome ? Core.Teams.RunOutcomes.Spell(outcome) : null;

    private static void Section(CommandOutput output, string heading, string text)
    {
        if (text.Length == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine($"[bold]{heading}[/]");

        foreach (var line in text.Split('\n'))
        {
            output.WriteLine($"  {Markup.Escape(line.TrimEnd())}");
        }
    }
}

/// <summary>Shortens the long titles written before there was a limit.</summary>
[Description("Shorten every task title over 72 characters, keeping the whole of each in its detail.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "shorten long task titles tidy migrate detail", Mutates = true)]
public sealed class TaskShortenCommand : AsyncCommand<TaskShortenCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskShortenCommand(
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
        [CommandOption("--every-list")]
        [Description("Every project's list and the workspace-wide one, rather than one list.")]
        public bool EveryList { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who the history says did it. Defaults to this machine's user.")]
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

        IReadOnlyList<string?> lists;

        if (settings.EveryList)
        {
            if (settings.Project is { Length: > 0 } || settings.Global)
            {
                return output.Fail(
                    "--every-list already covers every list. Leave out --project and --global.",
                    ExitCode.InvalidArguments);
            }

            if (!_workspace.IsAvailable())
            {
                return output.Fail(
                    "There is no workspace on this machine, so there are no tasks to shorten.",
                    ExitCode.WorkspaceSyncFailed);
            }

            lists = TaskPaths.Lists(_workspace.LocalPath);
        }
        else
        {
            var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

            if (resolution.Failed)
            {
                return output.Fail(resolution);
            }

            lists = [resolution.Value?.Entry.Slug];
        }

        var by = settings.By is { Length: > 0 } who ? who : Environment.UserName;
        var found = new List<(string? List, TaskShortened Title)>();

        foreach (var list in lists)
        {
            var shortened = await _tasks
                .ShortenTitlesAsync(list, by, apply: !settings.DryRun, cancellationToken)
                .ConfigureAwait(false);

            if (shortened.Failed)
            {
                return output.Fail(shortened);
            }

            found.AddRange(shortened.Value!.Select(one => (list, one)));
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                written = !settings.DryRun,
                shortened = found.Select(one => new
                {
                    project = one.List,
                    id = one.Title.Id,
                    before = one.Title.Before,
                    after = one.Title.After,
                }),
            });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine($"[dim]No title is over {TaskTitles.Length} characters. Nothing to do.[/]");

            return CommandOutput.Success();
        }

        foreach (var (list, title) in found)
        {
            output.WriteLine(
                $"{Markup.Escape(TaskPaths.Describe(list))}  [bold]{Markup.Escape(title.Id)}[/]  "
                + Markup.Escape(title.After));
        }

        output.WriteBlankLine();
        output.WriteLine(settings.DryRun
            ? $"Would shorten {found.Count} title(s). Nothing was written."
            : $"[green]+[/] Shortened {found.Count} title(s). Each one's whole text is at the start of its "
                + "detail, and its history says so.");

        return CommandOutput.Success();
    }
}

/// <summary>Gives a type to the tasks and ideas recorded before there were types.</summary>
/// <remarks>
/// Run only when asked, and worth running with --dry-run first: typing a whole
/// list unasked is a surprise, even when every type is only a guess.
/// </remarks>
[Description("Give every task and idea with no type the one its words suggest, recorded as a guess.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "classify type tasks ideas backlog guess bug feature migrate", Mutates = true)]
public sealed class TaskClassifyCommand : AsyncCommand<TaskClassifyCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly Core.Workspace.IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public TaskClassifyCommand(
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
        [CommandOption("--every-list")]
        [Description("Every project's list and the workspace-wide one, rather than one list.")]
        public bool EveryList { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who the history says did it. Defaults to this machine's user.")]
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

        IReadOnlyList<string?> lists;

        if (settings.EveryList)
        {
            if (settings.Project is { Length: > 0 } || settings.Global)
            {
                return output.Fail(
                    "--every-list already covers every list. Leave out --project and --global.",
                    ExitCode.InvalidArguments);
            }

            if (!_workspace.IsAvailable())
            {
                return output.Fail(
                    "There is no workspace on this machine, so there are no tasks to classify.",
                    ExitCode.WorkspaceSyncFailed);
            }

            lists = TaskPaths.Lists(_workspace.LocalPath);
        }
        else
        {
            var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

            if (resolution.Failed)
            {
                return output.Fail(resolution);
            }

            lists = [resolution.Value?.Entry.Slug];
        }

        var by = settings.By is { Length: > 0 } who ? who : Environment.UserName;
        var found = new List<(string? List, TaskClassified Task)>();

        foreach (var list in lists)
        {
            var classified = await _tasks
                .ClassifyAsync(list, by, apply: !settings.DryRun, cancellationToken)
                .ConfigureAwait(false);

            if (classified.Failed)
            {
                return output.Fail(classified);
            }

            found.AddRange(classified.Value!.Select(one => (list, one)));
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                written = !settings.DryRun,
                classified = found.Select(one => new
                {
                    project = one.List,
                    id = one.Task.Id,
                    title = one.Task.Title,
                    type = TaskTypes.Name(one.Task.Type),
                }),
            });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine("[dim]Every task already has a type. Nothing to do.[/]");

            return CommandOutput.Success();
        }

        var width = TaskTypes.Names.Max(name => name.Length);

        foreach (var (list, task) in found)
        {
            output.WriteLine(
                $"{Markup.Escape(TaskPaths.Describe(list))}  [bold]{Markup.Escape(task.Id)}[/]  "
                + $"{Markup.Escape(TaskTypes.Name(task.Type).PadRight(width))}  [dim]{Markup.Escape(task.Title)}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(settings.DryRun
            ? $"Would type {found.Count} task(s). Nothing was written."
            : $"[green]+[/] Typed {found.Count} task(s). Each type is recorded as a guess, so refining an idea may "
                + "still change it; give one --type with task declare to choose it yourself.");

        return CommandOutput.Success();
    }
}
