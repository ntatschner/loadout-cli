using System.ComponentModel;
using Loadout.Agents.Ideas;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Settings shared by the commands that act on one idea.</summary>
public class IdeaSettings : GlobalSettings
{
    [CommandArgument(0, "<id>")]
    [Description("The idea. 'loadout idea list' shows them.")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--project <SLUG>")]
    [Description("The project whose list it is on. Found by looking when not given.")]
    public string? Project { get; init; }

    [CommandOption("--global")]
    [Description("It is on the workspace-wide list.")]
    public bool Global { get; init; }

    /// <summary>
    /// Which list the idea is on: the one named, or found by looking, the
    /// repository's project first.
    /// </summary>
    internal async Task<OperationResult<IdeaPlace>> PlaceAsync(
        IIdeaService ideas,
        IProjectService projects,
        CancellationToken ct)
    {
        if (Global && Project is { Length: > 0 })
        {
            return OperationResult<IdeaPlace>.Fail(
                "--global and --project name different lists. Give one.", ExitCode.InvalidArguments);
        }

        if (Global || Project is { Length: > 0 })
        {
            var place = new IdeaPlace(Global ? null : Project, Id.Trim());
            var read = await ideas.ReadAsync(place, ct).ConfigureAwait(false);

            return read.Succeeded
                ? OperationResult<IdeaPlace>.Ok(place)
                : OperationResult<IdeaPlace>.Fail(read.Error!, read.ExitCode);
        }

        var here = await ProjectHandle.ResolveAsync(projects, null, Repo, ct).ConfigureAwait(false);

        return await ideas.LocateAsync(Id, here.Succeeded ? here.Value!.Entry.Slug : null, ct).ConfigureAwait(false);
    }
}

/// <summary>Drops an idea in to be fleshed out later.</summary>
[Description("Drop an idea in, to be fleshed out by an agent later.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "idea capture note brainstorm dump later someday plan new",
    Mutates = true,
    Example = "\"A status page for the home lab\"")]
public sealed class IdeaAddCommand : AsyncCommand<IdeaAddCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaAddCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<text>")]
        [Description("The idea, in your own words. Kept verbatim.")]
        public string Text { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Put it on this project's list. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--global")]
        [Description("Put it on the workspace-wide list, for an idea that belongs to no project yet.")]
        public bool Global { get; init; }

        [CommandOption("--id <ID>")]
        [Description("What to call it. Made from the first few words when not given.")]
        public string? IdeaId { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Whose idea it is. Defaults to this machine's user.")]
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

        if (settings.Global && settings.Project is { Length: > 0 })
        {
            return output.Fail("--global and --project name different lists. Give one.", ExitCode.InvalidArguments);
        }

        // Outside a repository an idea goes on the workspace-wide list rather
        // than being refused. Dropping one in has to be cheaper than keeping it
        // in your head, and an idea is exactly the thing that may belong to no
        // project yet. Said, so nobody wonders where it went.
        string? project = null;

        if (!settings.Global)
        {
            var resolution = await ProjectHandle
                .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
                .ConfigureAwait(false);

            if (resolution.Succeeded)
            {
                project = resolution.Value!.Entry.Slug;
            }
            else if (settings.Project is { Length: > 0 })
            {
                return output.Fail(resolution);
            }
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would put the idea on {Markup.Escape(Core.Tasks.TaskPaths.Describe(project))}. Nothing was written.");

            return CommandOutput.Success();
        }

        var captured = await _ideas.CaptureAsync(
            project,
            settings.Text,
            settings.By is { Length: > 0 } who ? who : Environment.UserName,
            settings.IdeaId,
            cancellationToken).ConfigureAwait(false);

        if (captured.Failed)
        {
            return output.Fail(captured);
        }

        var record = captured.Value!;

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(new IdeaPlace(project, record.Id), record));

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(record.Id)} is on {Markup.Escape(Core.Tasks.TaskPaths.Describe(project))}.");
        output.WriteLine(
            $"[dim]  Have an agent flesh it out with: loadout idea refine {Markup.Escape(record.Id)}[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Every idea, and where each one stands.</summary>
[Description("List every idea, on every project and the workspace-wide list, and where each stands.")]
[CommandMeta(CommandCategory.Workspace, Intent = "ideas list backlog someday brainstorm what ideas")]
public sealed class IdeaListCommand : AsyncCommand<IdeaListCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IAnsiConsole _console;

    public IdeaListCommand(IIdeaService ideas, IAnsiConsole console)
    {
        _ideas = ideas;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Only this project's ideas.")]
        public string? Project { get; init; }

        [CommandOption("--global")]
        [Description("Only the workspace-wide list.")]
        public bool Global { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var listed = await _ideas.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var shown = listed.Value!
            .Where(idea => !settings.Global || idea.Place.Project is null)
            .Where(idea => settings.Project is not { Length: > 0 } only
                || string.Equals(idea.Place.Project, only, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                ideas = shown.Select(idea => new
                {
                    id = idea.Place.Id,
                    project = idea.Place.Project,
                    idea.Title,
                    stage = IdeaView.Stage(idea.Stage),
                    state = idea.State.ToString().ToLowerInvariant(),
                    by = idea.DeclaredBy,
                    declared = idea.DeclaredUtc,
                    unanswered = idea.Unanswered,
                }),
            });

            return CommandOutput.Success();
        }

        if (shown.Count == 0)
        {
            output.WriteLine("[dim]No ideas yet. Drop one in with: loadout idea add \"...\"[/]");

            return CommandOutput.Success();
        }

        foreach (var group in shown.GroupBy(idea => idea.Place.Project))
        {
            output.WriteLine($"[bold]{Markup.Escape(Core.Tasks.TaskPaths.Describe(group.Key))}[/]");

            foreach (var idea in group)
            {
                output.WriteLine(
                    $"  {Markup.Escape(idea.Place.Id),-28} {IdeaView.Marked(idea.Stage, idea.Unanswered),-24} "
                    + Markup.Escape(idea.Title));
            }
        }

        return CommandOutput.Success();
    }
}

/// <summary>Shows one idea in full.</summary>
[Description("Show an idea: the questions, the answers, the plan and what you made of each piece.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea show plan questions options layers")]
public sealed class IdeaShowCommand : AsyncCommand<IdeaSettings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaShowCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        IdeaSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var place = await settings.PlaceAsync(_ideas, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        var read = await _ideas.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, read.Value!));
        }
        else
        {
            IdeaView.Write(output, place.Value!, read.Value!);
        }

        return CommandOutput.Success();
    }
}

/// <summary>Runs the next round of fleshing an idea out.</summary>
[Description("Have an agent take the idea one round further: ask its questions, propose a plan, or revise it.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea refine flesh out plan design questions agent think")]
public sealed class IdeaRefineCommand : AsyncCommand<IdeaRefineCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IIdeaRefiner _refiner;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaRefineCommand(
        IIdeaService ideas,
        IIdeaRefiner refiner,
        IProjectService projects,
        IAnsiConsole console)
    {
        _ideas = ideas;
        _refiner = refiner;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandOption("--model <MODEL>")]
        [Description("The model to ask for, as the agent spells it.")]
        public string? Model { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var place = await settings.PlaceAsync(_ideas, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        if (!settings.DryRun)
        {
            output.Meanwhile(
                $"[dim]Asking an agent about {Markup.Escape(place.Value!.Id)}. A round usually takes a minute or two.[/]");
        }

        var refined = await _refiner.RefineAsync(
            new RefineRequest(
                place.Value!,
                settings.Agent,
                settings.Model,
                settings.DryRun,
                Watching: tool =>
                {
                    output.Meanwhile($"[dim]  {Markup.Escape(tool)}[/]");

                    return Task.CompletedTask;
                }),
            cancellationToken).ConfigureAwait(false);

        if (refined.Failed)
        {
            return output.Fail(refined);
        }

        var outcome = refined.Value!;

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new
                {
                    executable = outcome.Plan.Executable,
                    arguments = outcome.Plan.Arguments,
                    directory = outcome.Plan.WorkingDirectory,
                    stage = IdeaView.Stage(outcome.Before),
                });

                return CommandOutput.Success();
            }

            output.WriteLine($"Would start {Markup.Escape(outcome.Plan.Executable)} in {Markup.Escape(outcome.Plan.WorkingDirectory)}.");
            output.WriteLine($"[dim]{Markup.Escape(string.Join(' ', outcome.Plan.Arguments))}[/]");
            output.WriteLine("Nothing was started.");

            return CommandOutput.Success();
        }

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, outcome.Record));

            return CommandOutput.Success();
        }

        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(warning)}");
        }

        IdeaView.Write(output, place.Value!, outcome.Record);

        if (outcome.CostUsd > 0)
        {
            output.WriteLine($"[dim]That round cost ${outcome.CostUsd:0.00}.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Answers one of the agent's questions.</summary>
[Description("Answer one of the questions an agent asked about an idea.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea answer question reply", Mutates = true)]
public sealed class IdeaAnswerCommand : AsyncCommand<IdeaAnswerCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaAnswerCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandArgument(1, "<question>")]
        [Description("Which question: Q1, Q2 and so on.")]
        public string Question { get; init; } = string.Empty;

        [CommandArgument(2, "<answer>")]
        [Description("Your answer, in your own words.")]
        public string Answer { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IdeaChange.RunAsync(
            _console, settings, settings.DryRun, _ideas, _projects,
            $"answer {settings.Question}",
            place => _ideas.AnswerAsync(place, settings.Question, settings.Answer, cancellationToken),
            cancellationToken);
    }
}

/// <summary>Picks one of a layer's options.</summary>
[Description("Pick one of the options the plan offers for a layer.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea choose option layer pick", Mutates = true)]
public sealed class IdeaChooseCommand : AsyncCommand<IdeaChooseCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaChooseCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandArgument(1, "<layer>")]
        [Description("Which layer: L1, L2 and so on.")]
        public string Layer { get; init; } = string.Empty;

        [CommandArgument(2, "<option>")]
        [Description("Which option: L1b, or just b.")]
        public string Option { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IdeaChange.RunAsync(
            _console, settings, settings.DryRun, _ideas, _projects,
            $"choose {settings.Option} for {settings.Layer}",
            place => _ideas.ChooseAsync(place, settings.Layer, settings.Option, cancellationToken),
            cancellationToken);
    }
}

/// <summary>Settings for keeping or dropping pieces of a plan.</summary>
public sealed class IdeaPiecesSettings : IdeaSettings
{
    [CommandArgument(1, "<pieces>")]
    [Description("The layers and additions: L2, A1 and so on.")]
    public string[] Pieces { get; init; } = [];
}

/// <summary>Keeps pieces of the plan.</summary>
[Description("Keep layers or additions in an idea's plan. An addition is only in the plan once kept.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea keep include add piece layer addition", Mutates = true)]
public sealed class IdeaKeepCommand : AsyncCommand<IdeaPiecesSettings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaKeepCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        IdeaPiecesSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IdeaChange.JudgeAllAsync(
            _console, settings, settings.DryRun, _ideas, _projects, IdeaVerdict.Keep, cancellationToken);
    }
}

/// <summary>Drops pieces of the plan.</summary>
[Description("Drop layers or additions from an idea's plan.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea drop remove exclude piece layer addition", Mutates = true)]
public sealed class IdeaDropCommand : AsyncCommand<IdeaPiecesSettings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDropCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        IdeaPiecesSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IdeaChange.JudgeAllAsync(
            _console, settings, settings.DryRun, _ideas, _projects, IdeaVerdict.Drop, cancellationToken);
    }
}

/// <summary>Asks for a piece of the plan, or the whole of it, to be reworked.</summary>
[Description("Ask for a layer, an addition or the whole plan to be reworked in the next round.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea improve rework change revise request feedback", Mutates = true)]
public sealed class IdeaImproveCommand : AsyncCommand<IdeaImproveCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaImproveCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandArgument(1, "<piece>")]
        [Description("A layer or addition (L2, A1), or 'plan' for the whole of it.")]
        public string Piece { get; init; } = string.Empty;

        [CommandArgument(2, "<request>")]
        [Description("What you want changed.")]
        public string Request { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IdeaChange.RunAsync(
            _console, settings, settings.DryRun, _ideas, _projects,
            $"ask for {settings.Piece} to be improved",
            place => _ideas.JudgeAsync(place, settings.Piece, IdeaVerdict.Improve, settings.Request, cancellationToken),
            cancellationToken);
    }
}

/// <summary>Turns an idea's plan into a task on a project.</summary>
[Description("Accept an idea's plan: write it out and turn the idea into a task on its project.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea accept approve done plan task promote project", Mutates = true)]
public sealed class IdeaAcceptCommand : AsyncCommand<IdeaAcceptCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IProjectTemplateService _templates;
    private readonly IAnsiConsole _console;

    public IdeaAcceptCommand(
        IIdeaService ideas,
        IProjectService projects,
        IProjectTemplateService templates,
        IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _templates = templates;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandOption("--to <SLUG>")]
        [Description("The project the work belongs to, overriding where it is and where the agent placed it.")]
        public string? To { get; init; }

        [CommandOption("--new-project <NAME>")]
        [Description("Make a new project for it, with this name, and put the work there.")]
        public string? NewProject { get; init; }

        [CommandOption("--path <PATH>")]
        [Description("Where to make the new project. Defaults to the clone root.")]
        public string? Path { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is accepting it. Defaults to this machine's user.")]
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

        if (settings.To is { Length: > 0 } && settings.NewProject is { Length: > 0 })
        {
            return output.Fail("--to and --new-project say different things. Give one.", ExitCode.InvalidArguments);
        }

        var place = await settings.PlaceAsync(_ideas, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        var read = await _ideas.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (read.Value!.Plan is not { } plan)
        {
            return output.Fail(
                $"{place.Value!.Id} has no plan to accept yet. Refine it to get one.", ExitCode.InvalidArguments);
        }

        string? destination = settings.To;
        string reason = destination is null ? string.Empty : "You named it.";

        if (destination is null && settings.NewProject is null)
        {
            var registered = await _projects.ListAsync(cancellationToken).ConfigureAwait(false);

            var settled = IdeaWork.Destination(
                place.Value!,
                plan,
                registered.Succeeded ? [.. registered.Value!.Select(p => p.Entry.Slug)] : []);

            if (settled.Unsettled)
            {
                var name = plan.Project.Name.Length > 0 ? plan.Project.Name : plan.Title;

                return output.Fail(
                    $"{settled.Reason} Accept it with --to <project>, or make a project for it with "
                    + $"--new-project \"{name}\".",
                    ExitCode.InvalidArguments);
            }

            destination = settled.Project;
            reason = settled.Reason;
        }

        if (settings.DryRun)
        {
            output.WriteLine(settings.NewProject is { Length: > 0 } made
                ? $"Would make the project {Markup.Escape(made)} and put {Markup.Escape(place.Value!.Id)} on it as a task."
                : $"Would put {Markup.Escape(place.Value!.Id)} on {Markup.Escape(destination!)} as a task. {Markup.Escape(reason)}");
            output.WriteLine("Nothing was changed.");

            return CommandOutput.Success();
        }

        if (settings.NewProject is { Length: > 0 } newName)
        {
            var created = await _templates
                .CreateAsync(newName, null, settings.Path, null, cancellationToken)
                .ConfigureAwait(false);

            if (created.Failed)
            {
                return output.Fail(created);
            }

            destination = created.Value!.Slug;
            reason = $"Made for it at {created.Value.TargetPath}.";
        }

        var accepted = await _ideas.AcceptAsync(
            place.Value!,
            destination!,
            settings.By is { Length: > 0 } who ? who : Environment.UserName,
            cancellationToken).ConfigureAwait(false);

        if (accepted.Failed)
        {
            return output.Fail(accepted);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = accepted.Value!.Task.Id,
                project = accepted.Value.Project,
                plan = accepted.Value.PlanPath,
                title = accepted.Value.Task.Title,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(accepted.Value!.Task.Id)} is now a task on "
            + $"[bold]{Markup.Escape(accepted.Value.Project)}[/]. {Markup.Escape(reason)}");
        output.WriteLine($"[dim]  The plan is at {Markup.Escape(accepted.Value.PlanPath)} in the workspace.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Forgets an idea.</summary>
[Description("Forget an idea entirely: its task and its record.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea remove delete forget discard", Mutates = true)]
public sealed class IdeaRemoveCommand : AsyncCommand<IdeaSettings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaRemoveCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        IdeaSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var place = await settings.PlaceAsync(_ideas, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would forget {Markup.Escape(place.Value!.Id)} on {Markup.Escape(place.Value.Where)}. Nothing was removed.");

            return CommandOutput.Success();
        }

        var removed = await _ideas.RemoveAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        output.WriteLine($"[green]+[/] Forgot {Markup.Escape(place.Value!.Id)}.");

        return CommandOutput.Success();
    }
}

/// <summary>The shared run of a command that changes one idea and shows it after.</summary>
internal static class IdeaChange
{
    public static async Task<int> RunAsync(
        IAnsiConsole console,
        IdeaSettings settings,
        bool dryRun,
        IIdeaService ideas,
        IProjectService projects,
        string doing,
        Func<IdeaPlace, Task<OperationResult<IdeaRecord>>> change,
        CancellationToken ct)
    {
        var output = new CommandOutput(console, settings);
        var place = await settings.PlaceAsync(ideas, projects, ct).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        if (dryRun)
        {
            output.WriteLine($"Would {Markup.Escape(doing)} on {Markup.Escape(place.Value!.Id)}. Nothing was changed.");

            return CommandOutput.Success();
        }

        var changed = await change(place.Value!).ConfigureAwait(false);

        if (changed.Failed)
        {
            return output.Fail(changed);
        }

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, changed.Value!));

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Done: {Markup.Escape(doing)}.");
        IdeaView.WriteNext(output, place.Value!, changed.Value!);

        return CommandOutput.Success();
    }

    /// <summary>
    /// One verdict for several pieces, all or none: every piece is checked
    /// before any is changed, so a typo in the third does not leave the first
    /// two judged and the command reported as failed.
    /// </summary>
    public static async Task<int> JudgeAllAsync(
        IAnsiConsole console,
        IdeaPiecesSettings settings,
        bool dryRun,
        IIdeaService ideas,
        IProjectService projects,
        IdeaVerdict verdict,
        CancellationToken ct)
    {
        var output = new CommandOutput(console, settings);

        if (settings.Pieces.Length == 0)
        {
            return output.Fail("Name at least one piece: L1, A2 and so on.", ExitCode.InvalidArguments);
        }

        var place = await settings.PlaceAsync(ideas, projects, ct).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        var read = await ideas.ReadAsync(place.Value!, ct).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var unknown = settings.Pieces
            .Where(piece => read.Value!.Plan is not { } plan
                || !plan.Layers.Any(l => string.Equals(l.Id, piece, StringComparison.OrdinalIgnoreCase))
                    && !plan.Additions.Any(a => string.Equals(a.Id, piece, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (read.Value!.Plan is null)
        {
            return output.Fail("There is no plan yet. Refine the idea to get one.", ExitCode.InvalidArguments);
        }

        if (unknown.Count > 0)
        {
            return output.Fail(
                $"{string.Join(", ", unknown)} {(unknown.Count == 1 ? "is not a piece" : "are not pieces")} of "
                + "the plan, so nothing was changed. 'loadout idea show' lists them.",
                ExitCode.InvalidArguments);
        }

        var word = verdict.ToString().ToLowerInvariant();

        if (dryRun)
        {
            output.WriteLine($"Would {word} {string.Join(", ", settings.Pieces)}. Nothing was changed.");

            return CommandOutput.Success();
        }

        OperationResult<IdeaRecord>? last = null;

        foreach (var piece in settings.Pieces)
        {
            last = await ideas.JudgeAsync(place.Value!, piece, verdict, null, ct).ConfigureAwait(false);

            if (last.Failed)
            {
                return output.Fail(last);
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, last!.Value!));

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Marked {Markup.Escape(string.Join(", ", settings.Pieces))} {word}.");
        IdeaView.WriteNext(output, place.Value!, last!.Value!);

        return CommandOutput.Success();
    }
}
