using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Platform.Common;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

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
        var json = await BatchInput.ReadAsync(settings.File, cancellationToken).ConfigureAwait(false);

        if (json.Failed)
        {
            return output.Fail(json);
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value?.Entry.Slug;
        var parsed = TaskOperations.Parse(json.Value!, slug, settings.By is { Length: > 0 } who ? who : Environment.UserName);

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

/// <summary>Where a batch is read from, for <c>task apply</c> and <c>idea apply</c> both.</summary>
internal static class BatchInput
{
    /// <summary>The JSON array from a file, or from standard input for <c>-</c> or when it is piped and no file is named.</summary>
    public static async Task<OperationResult<string>> ReadAsync(string? file, CancellationToken ct)
    {
        if (file is "-" || (file is null && Invocation.IsInputRedirected))
        {
            return OperationResult<string>.Ok(await Console.In.ReadToEndAsync(ct).ConfigureAwait(false));
        }

        if (file is not { Length: > 0 } path)
        {
            return OperationResult<string>.Fail("Give a file holding the JSON array, or - for standard input.", ExitCode.InvalidArguments);
        }

        return System.IO.File.Exists(path)
            ? OperationResult<string>.Ok(await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false))
            : OperationResult<string>.Fail($"'{path}' does not exist.", ExitCode.InvalidArguments);
    }
}

/// <summary>Makes several idea changes at once, each idea's all or nothing.</summary>
/// <remarks>
/// Takes the JSON array Studio's idea batch route and the agents' tool take:
/// capture, answer, choose, judge, dismiss and remove. An idea's answers,
/// choices and verdicts are made together or not at all. Refining is not one
/// of them, because a round takes minutes and starts an agent.
/// </remarks>
[Description("Make several idea changes at once from a JSON array, each idea's all or nothing.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "batch apply answer several idea questions at once bulk json",
    Mutates = true,
    Example = "answers.json --project website")]
public sealed class IdeaApplyCommand : AsyncCommand<TaskApplyCommand.Settings>
{
    private readonly Core.Ideas.IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaApplyCommand(Core.Ideas.IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        TaskApplyCommand.Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var json = await BatchInput.ReadAsync(settings.File, cancellationToken).ConfigureAwait(false);

        if (json.Failed)
        {
            return output.Fail(json);
        }

        var resolution = await settings.ListAsync(_projects, cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var parsed = Core.Ideas.IdeaOperations.Parse(
            json.Value!, resolution.Value?.Entry.Slug, settings.By is { Length: > 0 } who ? who : Environment.UserName);

        if (parsed.Failed)
        {
            return output.Fail(parsed);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would make {parsed.Value!.Count} change(s):");

            foreach (var operation in parsed.Value)
            {
                output.WriteLine($"  {Markup.Escape(operation.Op)} {Markup.Escape(operation.Id ?? operation.Text ?? string.Empty)} on {Markup.Escape(TaskPaths.Describe(operation.Project))}");
            }

            output.WriteLine("Nothing was changed.");

            return CommandOutput.Success();
        }

        var applied = (await _ideas.ApplyAsync(parsed.Value!, cancellationToken).ConfigureAwait(false)).Value!;
        var refused = applied.Outcomes.FirstOrDefault(one => !one.Applied && !one.HeldBack);

        if (output.IsJson)
        {
            output.WriteJson(Core.Ideas.IdeaOperations.Json(applied));

            return refused is null ? CommandOutput.Success() : (int)refused.Code;
        }

        foreach (var outcome in applied.Outcomes)
        {
            var what = Markup.Escape($"{outcome.Op} {outcome.Id} on {TaskPaths.Describe(outcome.Project)}");

            output.WriteLine(outcome.Applied ? $"[green]+[/] {what}" : $"[red]x[/] {what}: {Shown.Safely(outcome.Error!)}");
        }

        return refused is null
            ? CommandOutput.Success()
            : output.Fail(
                $"{applied.Outcomes.Count(one => !one.Applied)} of {applied.Outcomes.Count} change(s) were not made. "
                + "The ideas they were for are as they were.",
                refused.Code);
    }
}
