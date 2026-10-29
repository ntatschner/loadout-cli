using System.ComponentModel;
using System.Globalization;
using Loadout.Agents.Backfill;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Backfill;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Brings open ideas and tasks up to the current contract: fills what an
/// idea's plan lacks, and holds a proposed done-when on each open or blocked
/// task that has none.
/// </summary>
/// <remarks>
/// <para>
/// Started by hand only. A pass asks an agent once for each record it fills,
/// so <c>--dry-run</c> says what it would ask and how many times before
/// anything is spent, and <c>--limit</c> caps a run.
/// </para>
/// <para>
/// A task's proposal is held, not written: it becomes the task's done-when
/// only when somebody records it with <c>task declare --done-when</c>. An
/// idea's is written into its plan, marked as gap-filled, because accepting
/// the idea is already where the person confirms the plan's done-when.
/// </para>
/// </remarks>
[Description("Fill in what open ideas and tasks lack under the current contract. Ideas get the missing plan fields as a new revision; tasks get a proposed done-when held for you to confirm.")]
[CommandMeta(CommandCategory.Workspace, Intent = "backfill contract missing done-when gap fill old ideas tasks bring up to date", Mutates = true)]
public sealed class BackfillContractCommand : AsyncCommand<BackfillContractCommand.Settings>
{
    private readonly IContractBackfiller _backfiller;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public BackfillContractCommand(
        IContractBackfiller backfiller,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _backfiller = backfiller;
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandOption("--all-projects")]
        [Description("Every project's list and the workspace-wide one, rather than one list.")]
        public bool AllProjects { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("Ask an agent about this many records at most.")]
        public int? Limit { get; init; }

        [CommandOption("--model <MODEL>")]
        [Description("The model to ask for, as the agent spells it.")]
        public string? Model { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        IReadOnlyList<string?> lists;

        if (settings.AllProjects)
        {
            if (settings.Global || settings.Project is { Length: > 0 })
            {
                return output.Fail("--all-projects covers every list already. Leave out --project and --global.", ExitCode.InvalidArguments);
            }

            if (!_workspace.IsAvailable())
            {
                return output.Fail("There is no workspace on this machine, so there are no lists to look at.", ExitCode.WorkspaceSyncFailed);
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

        var run = await _backfiller.RunAsync(
            new BackfillRequest(lists, settings.Limit, settings.Agent, settings.Model, settings.DryRun),
            cancellationToken).ConfigureAwait(false);

        if (run.Failed)
        {
            return output.Fail(run);
        }

        var outcome = run.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                lists = lists.Select(TaskOperations.Named),
                dryRun = outcome.DryRun,
                calls = outcome.Calls,
                found = outcome.Found.Select(item => new
                {
                    kind = Kind(item.Kind),
                    project = item.Project,
                    id = item.Id,
                    title = item.Title,
                    missing = item.Missing,
                    asks = outcome.Asking.Contains(item),
                    skipped = item.Skipped,
                    lastFailure = item.LastFailure,
                }),
                results = outcome.Results.Select(result => new
                {
                    kind = Kind(result.Item.Kind),
                    project = result.Item.Project,
                    id = result.Item.Id,
                    filled = result.Filled,
                    doneWhen = result.DoneWhen,
                    error = result.Error,
                    costUsd = result.CostUsd,
                }),
            });

            return outcome.Results.Any(result => !result.Filled) ? (int)ExitCode.GeneralFailure : CommandOutput.Success();
        }

        if (outcome.Found.Count == 0)
        {
            output.WriteLine($"Nothing on {Where(lists)} is short of the current contract.");

            return CommandOutput.Success();
        }

        var skipped = outcome.Found.Count(item => !item.Fills);
        var capped = outcome.Found.Count - skipped - outcome.Asking.Count;

        output.WriteLine(
            $"{Count(outcome.Found.Count, "record")} on {Where(lists)} short of the current contract. "
            + (outcome.DryRun ? "A pass would ask an agent " : "This pass asked an agent ")
            + $"{Count(outcome.Calls, "time")}"
            + (capped > 0 ? $", leaving {capped} for a later pass under --limit" : string.Empty)
            + ".");

        foreach (var item in outcome.Found)
        {
            var line = $"  {Kind(item.Kind),-4}  {Markup.Escape(Named(item))}  [dim]lacks {Markup.Escape(string.Join(", ", item.Missing))}[/]";

            if (item.Skipped is { } why)
            {
                line += $"  [yellow]skipped:[/] {Markup.Escape(why)}";
            }
            else if (!outcome.Asking.Contains(item))
            {
                line += "  [dim]left for a later pass[/]";
            }

            output.WriteLine(line);

            if (item.LastFailure is { } failure)
            {
                output.WriteLine($"        [dim]last attempt failed:[/] {Shown.Safely(failure)}");
            }
        }

        if (outcome.DryRun)
        {
            output.WriteLine("[dim]Dry run: nothing was asked and nothing changed.[/]");

            return CommandOutput.Success();
        }

        foreach (var result in outcome.Results)
        {
            if (result.Error is { } error)
            {
                output.WriteLine($"[red]![/] {Markup.Escape(Named(result.Item))}: {Shown.Safely(error)} The next pass tries it again.");
            }
            else if (result.Item.Kind == BackfillKind.Idea)
            {
                output.WriteLine(
                    $"[green]+[/] {Markup.Escape(Named(result.Item))}: done-when written into a new plan revision, marked gap-filled until you accept it.");
            }
            else
            {
                output.WriteLine(
                    $"[green]+[/] {Markup.Escape(Named(result.Item))}: done-when proposed and held. 'loadout task show {Markup.Escape(result.Item.Id)}' "
                    + "shows it with the command that records it.");
            }
        }

        var cost = outcome.Results.Sum(result => result.CostUsd);

        if (cost > 0)
        {
            output.WriteLine($"[dim]That cost ${cost.ToString("0.00", CultureInfo.InvariantCulture)}.[/]");
        }

        return outcome.Results.Any(result => !result.Filled) ? (int)ExitCode.GeneralFailure : CommandOutput.Success();
    }

    private static string Kind(BackfillKind kind) => kind == BackfillKind.Idea ? "idea" : "task";

    private static string Named(BackfillItem item) =>
        item.Project is { } project ? $"{project}/{item.Id}" : $"{TaskOperations.WorkspaceWide}/{item.Id}";

    private static string Where(IReadOnlyList<string?> lists) =>
        lists.Count == 1 ? TaskPaths.Describe(lists[0]) : $"{lists.Count} lists";

    private static string Count(int count, string what) =>
        $"{count.ToString(CultureInfo.InvariantCulture)} {what}{(count == 1 ? string.Empty : "s")}";
}
