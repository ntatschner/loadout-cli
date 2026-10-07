using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Tui;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>
/// The tasks page of the dashboard: what it reads, and the command each of its
/// buttons types.
/// </summary>
/// <remarks>
/// <para>
/// Every change is the <c>task</c> command somebody would have typed, and
/// running a team on one is <c>team run --task</c>, started exactly as the
/// Run a team form starts one. The page decides nothing.
/// </para>
/// <para>
/// Running a team takes no choices on purpose. The team is this machine's
/// <c>team-for-tasks</c>, the goal is the task's own title and note, and
/// everything else is the team's. Somebody who wants to choose has the form.
/// </para>
/// </remarks>
internal sealed class DashboardTasks
{
    /// <summary>The team a task is run with when this machine has not named one.</summary>
    internal const string DefaultTeam = "iterating-project";

    private readonly ICommandCatalogue _commands;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly CommandOutput _output;

    public DashboardTasks(
        ICommandCatalogue commands,
        ITaskService tasks,
        IProjectService projects,
        IConfigurationService configuration,
        TimeProvider time,
        CommandOutput output)
    {
        _commands = commands;
        _tasks = tasks;
        _projects = projects;
        _configuration = configuration;
        _time = time;
        _output = output;
    }

    /// <summary>Every verb <see cref="Maps"/> answers, for the tests that run each one.</summary>
    internal static IReadOnlyList<string> Verbs => ["add", "state", "edit", "remove"];

    /// <summary>
    /// What the page draws: every project's tasks, the workspace-wide list,
    /// and the team a task would be run with.
    /// </summary>
    /// <remarks>
    /// Tasks only. Ideas are on the list too, and have a page of their own
    /// that knows what to do with one; drawn here they would be rows with no
    /// button that means anything for them.
    /// </remarks>
    public async Task<object> ReadAsync(CancellationToken ct)
    {
        var lists = new List<object>();
        var projects = await _projects.ListAsync(ct).ConfigureAwait(false);

        foreach (var slug in (projects.Succeeded ? projects.Value! : []).Select(one => one.Entry.Slug))
        {
            lists.Add(await ListAsync(slug, ct).ConfigureAwait(false));
        }

        lists.Add(await ListAsync(null, ct).ConfigureAwait(false));

        return new { lists, team = await TeamAsync(ct).ConfigureAwait(false) };
    }

    private async Task<object> ListAsync(string? slug, CancellationToken ct)
    {
        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        return new
        {
            project = slug,
            tasks = listed.Succeeded
                ? listed.Value!
                    .Where(one => one.Kind == TaskKind.Task)
                    .Select(one => new
                    {
                        id = one.Id,
                        title = one.Title,
                        state = one.State.ToString().ToLowerInvariant(),
                        note = one.Note,
                        by = one.DeclaredBy,
                        declared = one.DeclaredUtc,
                    })
                    .ToList<object>()
                : [],
            error = listed.Succeeded ? null : listed.Error,
        };
    }

    /// <summary>This machine's team for tasks, or the built-in one.</summary>
    private async Task<string> TeamAsync(CancellationToken ct)
    {
        var machine = await _configuration.LoadMachineAsync(ct).ConfigureAwait(false);

        return machine.Value?.Teams.ForTasks is { Length: > 0 } named ? named.Trim() : DefaultTeam;
    }

    /// <summary>Does what the page asked, by typing the command for it.</summary>
    public async Task<OperationResult> DoAsync(TaskAction asking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(asking);

        if (asking.Verb == "run")
        {
            return await RunAsync(asking, ct).ConfigureAwait(false);
        }

        if (asking.Verb == "add" && string.IsNullOrWhiteSpace(asking.Id))
        {
            if (string.IsNullOrWhiteSpace(asking.Title))
            {
                return OperationResult.Fail("A task needs a title: what is to be done.", ExitCode.InvalidArguments);
            }

            // Named here rather than by the command, which takes an id it is
            // given: the page asks for what the task is, not what to call it.
            var listed = await _tasks.ListAsync(asking.Project, ct).ConfigureAwait(false);

            asking = asking with
            {
                Id = TaskIds.From(asking.Title, listed.Succeeded ? listed.Value!.Select(one => one.Id) : []),
            };
        }

        var (command, arguments) = Maps(asking);

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{asking.Verb}' to do to a task.", ExitCode.InvalidArguments);
        }

        _output.WriteLine(
            $"[dim]{_time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(asking.Id ?? string.Empty)}");

        return Said(command, await _commands.RunAsync(command, arguments, ct).ConfigureAwait(false));
    }

    /// <summary>
    /// Starts this machine's team for tasks on one, as the Run a team form would.
    /// </summary>
    /// <remarks>
    /// The rule <c>team run --task</c> applies is checked here first, through
    /// the same function, so a refusal reaches the page in the command's own
    /// words. What comes back from the command itself is only an exit code.
    /// </remarks>
    private async Task<OperationResult> RunAsync(TaskAction asking, CancellationToken ct)
    {
        if (asking.Project is not { Length: > 0 } project)
        {
            return OperationResult.Fail(
                "A run works in a project's repository, and this task is on the workspace-wide list. "
                + "Add it to a project's list to run a team on it.",
                ExitCode.InvalidArguments);
        }

        var id = asking.Id ?? string.Empty;
        var listed = await _tasks.ListAsync(project, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return OperationResult.Fail(listed.Error!, listed.ExitCode);
        }

        if (TaskRuns.Rejection(id, project, listed.Value!) is { } rejected)
        {
            return OperationResult.Fail(rejected, ExitCode.InvalidArguments);
        }

        var task = listed.Value!.First(one => string.Equals(one.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

        return await DashboardActions.BeganAsync(
            _commands, _time, Running(task, project, await TeamAsync(ct).ConfigureAwait(false)), _output, ct)
            .ConfigureAwait(false);
    }

    /// <summary>The start a task's run button stands for.</summary>
    /// <remarks>
    /// Its own function so a test can read every field of it: the run must
    /// name the task, or the run adds a task of its own and the one somebody
    /// pressed the button on never moves.
    /// </remarks>
    internal static StartRequest Running(TaskItem task, string project, string team)
    {
        ArgumentNullException.ThrowIfNull(task);

        return new StartRequest(team, TaskRuns.Goal(task), project, Task: task.Id);
    }

    /// <summary>
    /// What a finished command's exit code means for the page.
    /// </summary>
    /// <remarks>
    /// Guessed from the code, because the code is all that comes back: the
    /// command's own words go to the terminal serving the page, and the page
    /// says so rather than inventing a reason it cannot know.
    /// </remarks>
    private static OperationResult Said(string command, int code) =>
        code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(
                (ExitCode)code switch
                {
                    ExitCode.PolicyViolation =>
                        "That looks like it contains a credential, so nothing was recorded. Say it without the value.",
                    ExitCode.ProjectNotFound =>
                        "That project is not one this workspace knows about.",
                    ExitCode.InvalidArguments =>
                        "The task could not take that. The terminal serving this page has the exact reason.",
                    _ => $"'{command}' ended with exit code {code}. The terminal serving this page has the reason.",
                },
                (ExitCode)code);

    /// <summary>The command line a verb stands for, or an empty command for one there is none of.</summary>
    /// <remarks>
    /// <para>
    /// Its own function so a test can put every verb through the real parser.
    /// </para>
    /// <para>
    /// Prose goes joined to its option, <c>--title=...</c>, because the parser
    /// refuses a value that starts with a dash anywhere else. The list goes as
    /// <c>--project</c> or <c>--global</c>, named outright, because the
    /// command never falls back from one to the other.
    /// </para>
    /// <para>
    /// An edit sends the note even when it is blank, which clears it: the box
    /// on the page showed the note, and emptying it is somebody saying it
    /// should go. A change of state sends one only when it was given, so
    /// moving a task to done does not wipe what it said.
    /// </para>
    /// </remarks>
    internal static (string Command, IReadOnlyList<string> Arguments) Maps(TaskAction asking)
    {
        ArgumentNullException.ThrowIfNull(asking);

        string[] list = asking.Project is { Length: > 0 } project ? ["--project", project] : ["--global"];
        var id = asking.Id ?? string.Empty;
        string[] note = asking.Note is { Length: > 0 } said && said.Trim().Length > 0
            ? [DashboardActions.Joined("--note", said.Trim())]
            : [];

        (string, IReadOnlyList<string>) Line(string command, params IEnumerable<string>[] parts) =>
            (command, [.. parts.SelectMany(part => part), "--non-interactive"]);

        return asking.Verb switch
        {
            "add" => Line(
                "task declare", [id, "open", DashboardActions.Joined("--title", asking.Title?.Trim())], note, list),
            "state" => Line("task declare", [id, asking.State ?? string.Empty], note, list),
            "edit" => Line(
                "task declare",
                [
                    id,
                    asking.State ?? string.Empty,
                    DashboardActions.Joined("--title", asking.Title?.Trim()),
                    DashboardActions.Joined("--note", asking.Note?.Trim()),
                ],
                list),
            "remove" => Line("task remove", [id], list),
            _ => (string.Empty, []),
        };
    }
}
