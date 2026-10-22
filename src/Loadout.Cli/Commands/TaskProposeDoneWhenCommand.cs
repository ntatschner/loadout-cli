using System.ComponentModel;
using Loadout.Agents.Tasks;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Asks an agent what a task's done-when should be, and records it only when a person says so.</summary>
/// <remarks>
/// <para>
/// For work that never got a finish line: tasks recorded by hand, and ideas
/// accepted before an idea's plan proposed one. A team's queue takes only a
/// task with a done-when, so without this the only way in was typing them.
/// </para>
/// <para>
/// Nothing is written unless asked: <c>--accept</c>, or a yes at the terminal.
/// A done-when nobody confirmed is a finish line nobody agreed to, and an
/// unattended run is held to it.
/// </para>
/// </remarks>
[Description("Ask an agent to propose a task's done-when from its title, detail and plan. Records them only with --accept, or a yes when asked.")]
[CommandMeta(CommandCategory.Workspace, Intent = "task done-when propose generate suggest finish line criteria", Mutates = true)]
public sealed class TaskProposeDoneWhenCommand : AsyncCommand<TaskProposeDoneWhenCommand.Settings>
{
    private readonly IDoneWhenProposer _proposer;
    private readonly ITaskService _tasks;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TaskProposeDoneWhenCommand(
        IDoneWhenProposer proposer,
        ITaskService tasks,
        IProjectService projects,
        IAnsiConsole console)
    {
        _proposer = proposer;
        _tasks = tasks;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : TaskSettings
    {
        [CommandArgument(0, "<id>")]
        [Description("The task.")]
        public string Id { get; init; } = string.Empty;

        [CommandOption("--accept")]
        [Description("Record what is proposed as the task's done-when, replacing any it has, without asking.")]
        public bool Accept { get; init; }

        [CommandOption("--model <MODEL>")]
        [Description("The model to ask for, as the agent spells it.")]
        public string? Model { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is accepting them. Defaults to this machine's user.")]
        public string? By { get; init; }
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

        var proposed = await _proposer.ProposeAsync(
            new DoneWhenRequest(slug, settings.Id, settings.Agent, settings.Model, settings.DryRun),
            cancellationToken).ConfigureAwait(false);

        if (proposed.Failed)
        {
            return output.Fail(proposed);
        }

        var outcome = proposed.Value!;

        // Not into JSON: a page parses what this prints, and a line before the
        // object would make the whole answer unreadable.
        foreach (var warning in outcome.Warnings.Where(_ => !output.IsJson))
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(warning)}");
        }

        if (outcome.Proposal is not { } proposal)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was asked.[/] An agent would be asked for {Markup.Escape(outcome.Task.Id)}'s done-when, "
                + "and nothing recorded without --accept.");

            return CommandOutput.Success();
        }

        var accepting = settings.Accept;

        if (!output.IsJson)
        {
            output.WriteLine($"Proposed done-when for [bold]{Markup.Escape(outcome.Task.Id)}[/]:");

            foreach (var condition in proposal.DoneWhen)
            {
                output.WriteLine($"  - {Markup.Escape(condition)}");
            }

            if (proposal.Why.Length > 0)
            {
                output.WriteLine($"[dim]{Markup.Escape(proposal.Why)}[/]");
            }

            if (outcome.CostUsd > 0)
            {
                output.WriteLine($"[dim]That cost ${outcome.CostUsd:0.00}.[/]");
            }

            if (outcome.Task.DoneWhen.Count > 0)
            {
                output.WriteLine($"[yellow]![/] It already has {outcome.Task.DoneWhen.Count}, which accepting these replaces.");
            }

            if (!accepting && settings.AllowsPrompting)
            {
                accepting = _console.Confirm("Record these as its done-when?", defaultValue: false);
            }
        }

        if (accepting)
        {
            var who = settings.By is { Length: > 0 } by ? by : Environment.UserName;

            var declared = await _tasks.DeclareAsync(
                slug,
                outcome.Task.Id,
                outcome.Task.State,
                who,
                ct: cancellationToken,
                doneWhen: proposal.DoneWhen,
                reason: "Done-when proposed by an agent and accepted.").ConfigureAwait(false);

            if (declared.Failed)
            {
                return output.Fail(declared);
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                task = outcome.Task.Id,
                project = slug,
                proposed = proposal.DoneWhen,
                why = proposal.Why,
                costUsd = outcome.CostUsd,
                replaces = outcome.Task.DoneWhen,
                recorded = accepting,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(accepting
            ? $"[green]+[/] {Markup.Escape(outcome.Task.Id)} has its done-when."
            : "[dim]Nothing was recorded. To record them as they are:[/] "
              + Markup.Escape(Accepting(outcome.Task.Id, outcome.Task.State.ToString().ToLowerInvariant(), slug, proposal.DoneWhen)));

        return CommandOutput.Success();
    }

    /// <summary>The command that records a proposal as it stands, for copying.</summary>
    internal static string Accepting(string id, string state, string? project, IReadOnlyList<string> doneWhen) =>
        $"loadout task declare {id} {state} "
        + string.Join(" ", doneWhen.Select(one => $"--done-when \"{one.Replace("\"", "'", StringComparison.Ordinal)}\""))
        + (project is null ? " --global" : $" --project {project}");
}
