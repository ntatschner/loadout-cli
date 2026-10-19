using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Tasks;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Shows one task, rather than the whole list it is on.</summary>
/// <remarks>
/// For an agent or a script that knows which task it wants: the list is read
/// once and one entry comes back, with the list's revision, which is what
/// <c>task apply</c> takes to refuse a change made from an older read.
/// </remarks>
[Description("Show one task, with the revision of the list it is on.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "show one task detail status of a task",
    Example = "fix-login --project website --json")]
public sealed class TaskShowCommand : AsyncCommand<TaskShowCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskShowCommand(ITaskService tasks, IProjectService projects, IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
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
        var read = await _tasks.ReadAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (read.Value!.Items.FirstOrDefault(one => string.Equals(one.Id, settings.Id.Trim(), StringComparison.OrdinalIgnoreCase))
            is not { } task)
        {
            return output.Fail($"'{settings.Id.Trim()}' is not a task of {TaskPaths.Describe(slug)}.", ExitCode.InvalidArguments);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { project = slug, revision = read.Value.Revision, task = TaskOperations.Json(task) });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(task.Id)}[/]  {task.State.ToString().ToLowerInvariant()}"
            + (task.Kind == TaskKind.Idea ? "  [blue]idea[/]" : string.Empty));
        output.WriteLine(Markup.Escape(task.Title));

        if (task.Note.Length > 0)
        {
            output.WriteBlankLine();
            output.WriteLine(Markup.Escape(task.Note));
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{Markup.Escape(task.DeclaredBy.Length > 0 ? task.DeclaredBy : "nobody named")}, {task.DeclaredUtc:yyyy-MM-dd HH:mm}"
            + (task.Moved is { } came ? $" · {Markup.Escape(TaskTrace.From(came))}" : string.Empty)
            + $" · on {Markup.Escape(TaskPaths.Describe(slug))} at revision {read.Value.Revision}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Makes several task changes at once, each list all or nothing.</summary>
/// <remarks>
/// <para>
/// Takes the JSON array Studio's batch route and the agents' tool take, so a
/// batch that works through one works through the others: add, declare,
/// remove and move, each as <c>{"op", "id", "state", "title", "note", "kind",
/// "project", "to", "revision"}</c>. An operation naming no project is made
/// on the list this command names, as every task command does.
/// </para>
/// <para>
/// The changes to one list are made together or not at all. Every operation
/// gets a result, so a refusal says which one and why, beside the ones it
/// held back.
/// </para>
/// </remarks>
[Description("Make several task changes at once from a JSON array, each list all or nothing.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "batch apply several task changes at once bulk json",
    Mutates = true,
    Example = "changes.json --project website")]
public sealed class TaskApplyCommand : AsyncCommand<TaskApplyCommand.Settings>
{
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskApplyCommand(ITaskService tasks, IProjectService projects, IAnsiConsole console)
    {
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "[file]")]
        [Description("A file holding the JSON array, or - for standard input.")]
        public string? File { get; init; }

        [CommandOption("--by <NAME>")]
        [Description("Who is saying so, for operations that name nobody. Defaults to you.")]
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
        string json;

        if (settings.File is "-" || (settings.File is null && Console.IsInputRedirected))
        {
            json = await Console.In.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (settings.File is not { Length: > 0 } path)
        {
            return output.Fail("Give a file holding the JSON array, or - for standard input.", ExitCode.InvalidArguments);
        }
        else if (!System.IO.File.Exists(path))
        {
            return output.Fail($"'{path}' does not exist.", ExitCode.InvalidArguments);
        }
        else
        {
            json = await System.IO.File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;
        var parsed = TaskOperations.Parse(json, slug, settings.By is { Length: > 0 } who ? who : Environment.UserName);

        if (parsed.Failed)
        {
            return output.Fail(parsed);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would make {parsed.Value!.Count} change(s):");

            foreach (var operation in parsed.Value)
            {
                output.WriteLine($"  {Markup.Escape(Described(operation.Op, operation.Id ?? operation.Title ?? string.Empty, operation.Project, operation.To))}");
            }

            output.WriteLine("Nothing was changed.");

            return CommandOutput.Success();
        }

        var applied = (await _tasks.ApplyAsync(parsed.Value!, cancellationToken).ConfigureAwait(false)).Value!;
        var refused = applied.Outcomes.FirstOrDefault(one => !one.Applied && !one.HeldBack);

        if (output.IsJson)
        {
            output.WriteJson(TaskOperations.Json(applied));

            return refused is null ? CommandOutput.Success() : (int)refused.Code;
        }

        foreach (var outcome in applied.Outcomes)
        {
            var what = Markup.Escape(Described(outcome.Op, outcome.Id, outcome.Project, outcome.To));

            output.WriteLine(outcome.Applied
                ? $"[green]+[/] {what}"
                : $"[red]x[/] {what}: {Shown.Safely(outcome.Error!)}");
        }

        return refused is null
            ? CommandOutput.Success()
            : output.Fail(
                $"{applied.Outcomes.Count(one => !one.Applied)} of {applied.Outcomes.Count} change(s) were not made. "
                + "The lists they were on are as they were.",
                refused.Code);
    }

    private static string Described(string op, string id, string? project, string? to) =>
        op == TaskOperation.Move
            ? $"move {id} from {TaskPaths.Describe(project)} to {TaskPaths.Describe(to)}"
            : $"{op} {id} on {TaskPaths.Describe(project)}";
}
