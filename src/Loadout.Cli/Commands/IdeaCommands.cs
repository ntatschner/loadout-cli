using System.ComponentModel;
using Loadout.Agents.Ideas;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
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
    internal Task<OperationResult<IdeaPlace>> PlaceAsync(
        IIdeaService ideas,
        IProjectService projects,
        CancellationToken ct) =>
        Find(ideas, projects, Id, Project, Global, Repo, ct);

    /// <summary>
    /// Which list an idea is on: the one named, or found by looking, the
    /// repository's project first. Shared by every command that names an idea,
    /// whatever it calls the option.
    /// </summary>
    internal static async Task<OperationResult<IdeaPlace>> Find(
        IIdeaService ideas,
        IProjectService projects,
        string id,
        string? project,
        bool global,
        string? repo,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ideas);

        if (global && project is { Length: > 0 })
        {
            return OperationResult<IdeaPlace>.Fail(
                "--global and --project name different lists. Give one.", ExitCode.InvalidArguments);
        }

        if (global || project is { Length: > 0 })
        {
            var place = new IdeaPlace(global ? null : project, id.Trim());
            var read = await ideas.ReadAsync(place, ct).ConfigureAwait(false);

            return read.Succeeded
                ? OperationResult<IdeaPlace>.Ok(place)
                : OperationResult<IdeaPlace>.Fail(read.Error!, read.ExitCode);
        }

        var here = await ProjectHandle.ResolveAsync(projects, null, repo, ct).ConfigureAwait(false);

        return await ideas.LocateAsync(id, here.Succeeded ? here.Value!.Entry.Slug : null, ct).ConfigureAwait(false);
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
    private readonly IIdeaRefiner _refiner;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaAddCommand(IIdeaService ideas, IIdeaRefiner refiner, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _refiner = refiner;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[text]")]
        [Description("The idea, in your own words. Kept verbatim.")]
        public string? Text { get; init; }

        /// <remarks>
        /// The same thing as an option, for text that starts with a dash: a
        /// bullet copied from a list reads to the parser as an option, and the
        /// launcher's screens always pass the idea this way for that reason.
        /// </remarks>
        [CommandOption("--text <TEXT>")]
        [Description("The idea, or what to add to one, as an option. Prose that starts with a dash goes as --text=\"- ...\".")]
        public string? TextOption { get; init; }

        [CommandOption("--to <ID>")]
        [Description("Add the text to this idea instead of dropping in a new one. The next refine reads it.")]
        public string? To { get; init; }

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

        [CommandOption("--refine")]
        [Description("With --to: start a round once the note is saved. Without this or --no-refine, you are asked at a keyboard, Yes by default, and no round starts when nobody is there to ask.")]
        public bool Refine { get; init; }

        [CommandOption("--no-refine")]
        [Description("With --to: save the note and start no round.")]
        public bool NoRefine { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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

        if (settings.Text is { Length: > 0 } && settings.TextOption is { Length: > 0 })
        {
            return output.Fail("Give the idea once: as text, or with --text.", ExitCode.InvalidArguments);
        }

        var text = settings.TextOption is { Length: > 0 } given ? given : settings.Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return output.Fail("Say what the idea is: loadout idea add \"...\"", ExitCode.InvalidArguments);
        }

        if (settings.Refine && settings.NoRefine)
        {
            return output.Fail("--refine and --no-refine say opposite things. Give one.", ExitCode.InvalidArguments);
        }

        if (settings.To is { Length: > 0 })
        {
            return await AddToAsync(output, settings, text, cancellationToken).ConfigureAwait(false);
        }

        if (settings.Refine || settings.NoRefine)
        {
            return output.Fail(
                "--refine and --no-refine go with --to, for adding to an idea already there.", ExitCode.InvalidArguments);
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
            text,
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

    /// <summary>Adds the text to an idea already there, for its next round to read.</summary>
    private async Task<int> AddToAsync(
        CommandOutput output,
        Settings settings,
        string text,
        CancellationToken ct)
    {
        if (settings.IdeaId is { Length: > 0 })
        {
            return output.Fail(
                "--id names a new idea and --to adds to one already there. Give one.", ExitCode.InvalidArguments);
        }

        var found = await IdeaSettings.Find(
            _ideas, _projects, settings.To!, settings.Project, settings.Global, settings.Repo, ct).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(
                $"{found.Error} To look on one list only, name it with --project <SLUG>, or --global for the workspace-wide one.",
                found.ExitCode);
        }

        var place = found.Value!;
        var id = Markup.Escape(place.Id);
        var by = settings.By is { Length: > 0 } who ? who : Environment.UserName;

        if (settings.DryRun)
        {
            var previewed = await _ideas.PreviewAddToAsync(place, text, by, ct).ConfigureAwait(false);

            if (previewed.Failed)
            {
                return output.Fail(previewed);
            }

            output.WriteLine($"Would add {Markup.Escape(previewed.Value!.Notes[^1].Id)} to {id}. Nothing was written.");

            return CommandOutput.Success();
        }

        var added = await _ideas.AddToAsync(place, text, by, ct).ConfigureAwait(false);

        if (added.Failed)
        {
            return output.Fail(added);
        }

        var record = added.Value!;
        var note = record.Notes[^1].Id;
        var next = $"loadout idea refine {place.Id}";

        output.WriteLine($"[green]+[/] Added {Markup.Escape(note)} to {id}.");

        // Asked only of someone at a keyboard. A script or a page that wants
        // a round says --refine, so none ever starts that was not asked for.
        // Nor is it asked while questions are open: the round would be refused
        // until they are answered, and the reminder below says so.
        var refine = settings.Refine
            || (!settings.NoRefine
                && settings.AllowsPrompting
                && IdeaWork.StageOf(record) is IdeaStage.Captured or IdeaStage.Ready
                && AskToRefine(_console));

        if (!refine)
        {
            var reminder = IdeaWork.UnansweredReminder(record);

            if (output.IsJson)
            {
                output.WriteJson(AddedJson(place, record, note, new { requested = false, next }, reminder));

                return CommandOutput.Success();
            }

            output.WriteLine($"[dim]  No round was started. The next one reads it: {Markup.Escape(next)}[/]");
            Remind(output, place, reminder);

            return CommandOutput.Success();
        }

        // Asked for, but a round on an idea with open questions is refused
        // until they are answered. The note is in, which is what was asked
        // for first, so this is not a failure: the round waits, and says so.
        if (IdeaWork.UnansweredReminder(record) is { } waiting)
        {
            var answer = $"loadout idea answer {place.Id} <question> --answer=<text>";

            if (output.IsJson)
            {
                output.WriteJson(AddedJson(
                    place, record, note, new { requested = true, waiting = "questions", next = answer }, waiting));

                return CommandOutput.Success();
            }

            output.WriteLine(
                $"[yellow]![/] {Markup.Escape(waiting)}, so no round was started: it waits until they are answered.");
            output.WriteLine($"[dim]  See them with: loadout idea show {id}[/]");
            output.WriteLine($"[dim]  Answer each with: {Markup.Escape(answer)}[/]");

            return CommandOutput.Success();
        }

        output.Meanwhile(
            $"[dim]Asking an agent about {id}. A round usually takes a minute or two.[/]");

        var refined = await _refiner.RefineAsync(
            new RefineRequest(
                place,
                settings.Agent,
                null,
                DryRun: false,
                Watching: tool =>
                {
                    output.Meanwhile($"[dim]  {Markup.Escape(tool)}[/]");

                    return Task.CompletedTask;
                }),
            ct).ConfigureAwait(false);

        if (refined.Failed)
        {
            // The note is kept whatever the round did, and said to be, so
            // nobody adds the same thing again thinking it was lost.
            var saved = await _ideas.ReadAsync(place, ct).ConfigureAwait(false);
            var reminder = IdeaWork.UnansweredReminder(saved.Succeeded ? saved.Value! : record);
            var error = Core.Security.SecretRedactor.Redact(refined.Error ?? "The round failed.");

            if (output.IsJson)
            {
                output.WriteJson(AddedJson(
                    place,
                    saved.Succeeded ? saved.Value! : record,
                    note,
                    new { requested = true, succeeded = false, error, next },
                    reminder));
            }
            else
            {
                Console.Error.WriteLine(
                    $"{note} was saved on {place.Id}, but the refine failed: {error} Try the round again with: {next}");
                Remind(output, place, reminder);
            }

            return (int)refined.ExitCode;
        }

        var outcome = refined.Value!;
        var after = IdeaWork.UnansweredReminder(outcome.Record);

        if (output.IsJson)
        {
            output.WriteJson(AddedJson(place, outcome.Record, note, new { requested = true, succeeded = true }, after));

            return CommandOutput.Success();
        }

        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(warning)}");
        }

        IdeaView.Write(output, place, outcome.Record);
        Remind(output, place, after);

        return CommandOutput.Success();
    }

    /// <summary>
    /// The idea as <c>idea show --json</c> gives it, with what was added, what
    /// became of the round, and the reminder, so a caller reads all three
    /// from one document.
    /// </summary>
    private static System.Text.Json.Nodes.JsonObject AddedJson(
        IdeaPlace place,
        IdeaRecord record,
        string note,
        object refine,
        string? reminder)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToNode(IdeaView.Of(place, record), CommandOutput.JsonOptions)!.AsObject();

        json["added"] = note;
        json["refine"] = System.Text.Json.JsonSerializer.SerializeToNode(refine, CommandOutput.JsonOptions);
        json["unanswered"] = IdeaWork.Unanswered(record).Count;
        json["reminder"] = reminder;

        return json;
    }

    /// <summary>
    /// Asks whether to refine now, with Yes as the answer Enter gives: someone
    /// adding to an idea at a keyboard usually wants the plan to take it in.
    /// </summary>
    internal static bool AskToRefine(IAnsiConsole console) => console.Confirm("Refine now?", defaultValue: true);

    private static void Remind(CommandOutput output, IdeaPlace place, string? reminder)
    {
        if (reminder is not null)
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(reminder)}. See them with: loadout idea show {Markup.Escape(place.Id)}");
        }
    }
}

/// <summary>Every idea, and where each one stands.</summary>
[Description("List every idea, on every project and the workspace-wide list, and where each stands.")]
[CommandMeta(CommandCategory.Workspace, Intent = "ideas list backlog someday brainstorm what ideas")]
public sealed class IdeaListCommand : AsyncCommand<IdeaListCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;
    private readonly Core.Configuration.IConfigurationService _configuration;

    public IdeaListCommand(IIdeaService ideas, IAnsiConsole console, TimeProvider time, Core.Configuration.IConfigurationService configuration)
    {
        _ideas = ideas;
        _console = console;
        _time = time;
        _configuration = configuration;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Only this project's ideas.")]
        public string? Project { get; init; }

        [CommandOption("--global")]
        [Description("Only the workspace-wide list.")]
        public bool Global { get; init; }

        [CommandOption("--filter <QUERY>")]
        [Description("Keep only what the query says, the query every list takes, such as \"stage:answering login\". One starting with a minus goes as --filter=-stage:accepted.")]
        public string? Filter { get; init; }

        [CommandOption("--sort <FIELDS>")]
        [Description("Sort by these fields, commas between: id, title, project, stage, by, updated, unanswered. Reversed with a minus: --sort=-updated.")]
        public string? Sort { get; init; }

        [CommandOption("--saved")]
        [Description("Use the query saved for this list in Studio. Without --json it is used anyway; --filter sets it aside.")]
        public bool Saved { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("How many ideas a page holds. Without it the whole list is one page.")]
        public int? Limit { get; init; }

        [CommandOption("--page <NUMBER>")]
        [Description("Which page to show, counted from 1. Needs --limit.")]
        public int Page { get; init; } = 1;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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

        var placed = listed.Value!
            .Where(idea => !settings.Global || idea.Place.Project is null)
            .Where(idea => settings.Project is not { Length: > 0 } only
                || string.Equals(idea.Place.Project, only, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var config = await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var (filter, fromSaved) = ListOptions.Filter(
            settings.Filter, Core.Lists.ListViews.Saved(config.Value?.Studio, "ideas"), settings.Saved, output.IsJson);
        var queried = ListOptions.Apply(Core.Lists.IdeaFields.All, filter, settings.Sort, placed, _time.GetUtcNow());

        if (queried.Failed)
        {
            return output.Fail(queried);
        }

        var paged = Core.Lists.ListPages.Of(queried.Value!, settings.Page, settings.Limit);

        if (paged.Failed)
        {
            return output.Fail(paged);
        }

        var page = paged.Value!;
        var shown = page.Items.ToList();

        // Ideas that are work on another list now, shown under the list they
        // left: that is where somebody would look for one and not find it.
        var movedOn = await _ideas.MovedOnAsync(cancellationToken).ConfigureAwait(false);
        var gone = movedOn.Succeeded
            ? [.. movedOn.Value!
                .Where(one => !settings.Global || one.From is null)
                .Where(one => settings.Project is not { Length: > 0 } only
                    || string.Equals(one.From, only, StringComparison.OrdinalIgnoreCase))]
            : new List<IdeaMovedOn>();

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
                    outdated = idea.Outdated,
                    running = idea.Running,
                    failure = IdeaView.Failure(idea.Failure),
                    moved = TaskTrace.Json(idea.Moved),
                }),
                movedOn = gone.Select(one => new { id = one.Id, title = one.Title, from = one.From, to = one.Where, at = one.Utc }),
                recorded = placed.Count,
                total = page.Total,
                page = page.Page,
                pages = page.Pages,
                limit = page.Limit,
                query = ListOptions.Shape(Core.Lists.IdeaFields.All, filter, settings.Sort),
            });

            return CommandOutput.Success();
        }

        // A list someone else's filter has narrowed says so, so it never
        // looks short for no reason.
        if (fromSaved && !output.IsJson)
        {
            output.WriteLine($"[dim]Filtered by the saved query {Markup.Escape(filter!)}. --filter sets it aside.[/]");
        }

        if (shown.Count == 0 && gone.Count == 0)
        {
            output.WriteLine(placed.Count > 0
                ? "[dim]Nothing matches the filter.[/]"
                : "[dim]No ideas yet. Drop one in with: loadout idea add \"...\"[/]");

            return CommandOutput.Success();
        }

        var lists = shown.Select(idea => idea.Place.Project)
            .Concat(gone.Select(one => one.From))
            .Distinct()
            .OrderBy(list => list is null ? 0 : 1)
            .ThenBy(list => list, StringComparer.Ordinal);

        foreach (var list in lists)
        {
            output.WriteLine($"[bold]{Markup.Escape(Core.Tasks.TaskPaths.Describe(list))}[/]");

            foreach (var idea in shown.Where(idea => idea.Place.Project == list))
            {
                output.WriteLine(
                    $"  {Markup.Escape(idea.Place.Id),-28} {IdeaView.Marked(idea),-24} "
                    + Markup.Escape(idea.Title)
                    + (idea.Moved is { } came ? $"  [dim]{Markup.Escape(TaskTrace.From(came))}[/]" : string.Empty));
            }

            foreach (var one in gone.Where(one => one.From == list))
            {
                output.WriteLine(
                    $"  [dim]{Markup.Escape(one.Id),-28} {"moved on",-24} {Markup.Escape(one.Title)}, "
                    + $"a task on {Markup.Escape(Core.Tasks.TaskPaths.Describe(one.Where))} now[/]");
            }
        }

        if (page.Limit is not null)
        {
            output.WriteLine(
                $"[dim]Page {page.Page} of {page.Pages}, {page.Total} in all."
                + (page.More ? $" --page {page.Page + 1} for the next.[/]" : "[/]"));
        }

        return CommandOutput.Success();
    }
}

/// <summary>Shows one idea in full.</summary>
[Description("Show an idea: the questions, the answers, the plan and what you made of each piece.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea show plan questions options layers")]
public sealed class IdeaShowCommand : AsyncCommand<IdeaShowCommand.Settings>
{
    public sealed class Settings : IdeaSettings
    {
        [CommandOption("--full")]
        [Description("Every option's detail, pros and cons, not only the chosen option's detail.")]
        public bool Full { get; init; }
    }

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
    public override async Task<int> ExecuteAsync(
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

        var read = await _ideas.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        // JSON carries every option in full already; --full is for reading.
        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, read.Value!));
        }
        else
        {
            IdeaView.Write(output, place.Value!, read.Value!, settings.Full);
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
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public IdeaRefineCommand(
        IIdeaService ideas,
        IIdeaRefiner refiner,
        IProjectService projects,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _ideas = ideas;
        _refiner = refiner;
        _projects = projects;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandOption("--model <MODEL>")]
        [Description("The model to ask for, as the agent spells it.")]
        public string? Model { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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

        // Said before anything is spent, by the same rule the refiner applies,
        // so a change of agent or model is never one nobody saw. Only for an
        // idea a round would run on: the rest are refused, and announcing an
        // agent first would read as though one had started.
        if (!output.IsJson
            && (await _ideas.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false)) is { Succeeded: true } read
            && IdeaWork.StageOf(read.Value!) is IdeaStage.Captured or IdeaStage.Ready)
        {
            var config = await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
            var fallback = config.Succeeded ? config.Value!.DefaultAgent : "the default agent";

            IdeaView.Choice(output, IdeaWork.Choose(read.Value!, settings.Agent, settings.Model), fallback);
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
                    agent = outcome.Agent,
                    model = outcome.Choice.Model,
                    fromLastRound = outcome.Choice.FromLastRound,
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

        [CommandArgument(2, "[answer]")]
        [Description("Your answer, in your own words.")]
        public string? Answer { get; init; }

        [CommandOption("--answer <ANSWER>")]
        [Description("The answer, as an option. One that starts with a dash goes as --answer=\"- ...\".")]
        public string? AnswerOption { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IdeaChange.Once(settings.Answer, settings.AnswerOption) is not { } answer)
        {
            return new CommandOutput(_console, settings).Fail(
                "Give the answer once: after the question, or with --answer.", ExitCode.InvalidArguments);
        }

        return await IdeaChange.RunAsync(
            _console, settings, settings.DryRun, _ideas, _projects,
            $"answer {settings.Question}",
            place => _ideas.AnswerAsync(place, settings.Question, answer, cancellationToken),
            cancellationToken).ConfigureAwait(false);
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
    public override Task<int> ExecuteAsync(
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
    public override Task<int> ExecuteAsync(
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
    public override Task<int> ExecuteAsync(
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

        [CommandArgument(2, "[request]")]
        [Description("What you want changed.")]
        public string? Request { get; init; }

        [CommandOption("--request <REQUEST>")]
        [Description("The request, as an option. One that starts with a dash goes as --request=\"- ...\".")]
        public string? RequestOption { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (IdeaChange.Once(settings.Request, settings.RequestOption) is not { } request)
        {
            return new CommandOutput(_console, settings).Fail(
                "Say what to change once: after the piece, or with --request.", ExitCode.InvalidArguments);
        }

        return await IdeaChange.RunAsync(
            _console, settings, settings.DryRun, _ideas, _projects,
            $"ask for {settings.Piece} to be improved",
            place => _ideas.JudgeAsync(place, settings.Piece, IdeaVerdict.Improve, request, cancellationToken),
            cancellationToken).ConfigureAwait(false);
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

        /// <summary>The finish line the person confirms, replacing what the plan proposed.</summary>
        /// <remarks>
        /// Repeated rather than one comma-separated string, as on
        /// <c>task declare</c>, because a condition is a sentence.
        /// </remarks>
        [CommandOption("--done-when <CONDITION>")]
        [Description(
            "Something that must be true for the work to be finished. Repeat it for each; together they replace "
            + "what the plan proposed. Without it, accepting confirms the plan's own.")]
        public string[] DoneWhen { get; init; } = [];

        [CommandOption("--no-done-when")]
        [Description("Accept it with no done-when, leaving out what the plan proposed. A team queue will refuse the task until it has some.")]
        public bool NoDoneWhen { get; init; }

        /// <summary>The done-when to confirm: null to take the plan's, empty for none.</summary>
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

        if (settings.To is { Length: > 0 } && settings.NewProject is { Length: > 0 })
        {
            return output.Fail("--to and --new-project say different things. Give one.", ExitCode.InvalidArguments);
        }

        if (settings.NoDoneWhen && settings.DoneWhen.Length > 0)
        {
            return output.Fail("--done-when and --no-done-when say opposite things. Give one or the other.", ExitCode.InvalidArguments);
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
            output.WriteLine(Markup.Escape(Confirming(settings.DoneWhenGiven, plan.DoneWhen, would: true)));
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
            cancellationToken,
            settings.DoneWhenGiven).ConfigureAwait(false);

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
                from = accepted.Value.From,
                doneWhen = accepted.Value.Task.DoneWhen,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(accepted.Value!.Task.Id)} is now a task on "
            + $"[bold]{Markup.Escape(accepted.Value.Project)}[/]. {Markup.Escape(reason)}");

        if (accepted.Value.Moved)
        {
            var left = Core.Tasks.TaskPaths.Describe(accepted.Value.From);

            output.WriteLine(
                $"[dim]  It moved from {Markup.Escape(left)} to {Markup.Escape(accepted.Value.Project)}, "
                + $"and {Markup.Escape(left)} still shows where it went.[/]");
        }

        output.WriteLine($"[dim]  The plan is at {Markup.Escape(accepted.Value.PlanPath)} in the workspace.[/]");
        output.WriteLine(Markup.Escape(Confirming(settings.DoneWhenGiven, accepted.Value.Task.DoneWhen, would: false)));

        foreach (var condition in accepted.Value.Task.DoneWhen)
        {
            output.WriteLine($"  - {Markup.Escape(condition)}");
        }

        return CommandOutput.Success();
    }

    /// <summary>What accepting does with the done-when, in a line.</summary>
    /// <remarks>
    /// Said every time, because accepting without --done-when is itself a
    /// confirmation of the plan's conditions, and nobody should find that out
    /// from a queue holding a run to them.
    /// </remarks>
    /// <param name="given">What the person gave: null to take the plan's, empty for none.</param>
    /// <param name="proposed">The conditions the task ends up with, or would.</param>
    /// <param name="would">Whether this is a dry run.</param>
    internal static string Confirming(IReadOnlyList<string>? given, IReadOnlyList<string> proposed, bool would)
    {
        var count = given is null
            ? proposed.Count
            : given.Count(one => one.Trim().Length > 0);

        if (count == 0)
        {
            return (would ? "It would have" : "It has")
                + " no done-when, so a team queue will refuse it until it has some: "
                + "loadout task declare <id> open --done-when \"...\"";
        }

        return given is not null
            ? $"{(would ? "Would hold" : "Holds")} the task to the {count} done-when you gave, in place of the plan's."
            : $"Accepting confirms the plan's {count} done-when, which the task {(would ? "would be" : "is now")} held to.";
    }
}

/// <summary>Clears the record of an idea's latest failed round.</summary>
/// <remarks>
/// For an idea somebody has stopped refining. Only the next round that comes
/// back clears a failure otherwise, so an abandoned idea would say its last
/// round failed for good, with nothing to do about it but pay for another.
/// </remarks>
[Description("Clear the 'last round failed' marker on an idea, without running another round. Answers and the plan stay.")]
[CommandMeta(CommandCategory.Workspace, Intent = "idea dismiss clear failed round error marker", Mutates = true)]
public sealed class IdeaDismissCommand : AsyncCommand<IdeaSettings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDismissCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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
            var read = await _ideas.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

            if (read.Failed)
            {
                return output.Fail(read);
            }

            output.WriteLine(IdeaWork.FailureOf(read.Value!) is { } failed
                ? $"Would clear the failed round on {Markup.Escape(place.Value!.Id)}: {Shown.Safely(failed.Error)}. Nothing was changed."
                : $"{Markup.Escape(place.Value!.Id)} has no failed round to clear. Nothing was changed.");

            return CommandOutput.Success();
        }

        var dismissed = await _ideas.DismissFailureAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (dismissed.Failed)
        {
            return output.Fail(dismissed);
        }

        if (output.IsJson)
        {
            output.WriteJson(IdeaView.Of(place.Value!, dismissed.Value!));

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] Cleared the failed round on {Markup.Escape(place.Value!.Id)}.");

        return CommandOutput.Success();
    }
}

/// <summary>Takes off a note no round has read yet.</summary>
[Description("Take a note off an idea, by its number as 'idea show' lists it, while no round has read it.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "idea note remove delete take back undo addition added",
    Mutates = true,
    Example = "sync 2")]
public sealed class IdeaNoteRemoveCommand : AsyncCommand<IdeaNoteRemoveCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaNoteRemoveCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandArgument(1, "<note>")]
        [Description("The note's number, as 'idea show' lists it: 2 or N2.")]
        public string Note { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
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

        var id = Markup.Escape(place.Value!.Id);

        var removed = settings.DryRun
            ? await _ideas.PreviewRemoveNoteAsync(place.Value!, settings.Note, cancellationToken).ConfigureAwait(false)
            : await _ideas.RemoveNoteAsync(place.Value!, settings.Note, cancellationToken).ConfigureAwait(false);

        if (removed.Failed)
        {
            return output.Fail(removed);
        }

        var note = removed.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = place.Value!.Id,
                project = place.Value!.Project,
                removed = note.Id,
                text = note.Text,
                dryRun = settings.DryRun,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(settings.DryRun
            ? $"Would remove {Markup.Escape(note.Id)} from {id}. Nothing was changed."
            : $"[green]+[/] Removed {Markup.Escape(note.Id)} from {id}. Its number is not given to another note.");

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
    public override async Task<int> ExecuteAsync(
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

/// <summary>Moves an idea to the list it belongs on.</summary>
[Description("Move an idea to another project's list, or to the workspace-wide one, with its answers and plan.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "idea move wrong project misplaced reassign transfer",
    Mutates = true,
    Example = "status-page --to homelab")]
public sealed class IdeaMoveCommand : AsyncCommand<IdeaMoveCommand.Settings>
{
    private readonly IIdeaService _ideas;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaMoveCommand(IIdeaService ideas, IProjectService projects, IAnsiConsole console)
    {
        _ideas = ideas;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaSettings
    {
        [CommandOption("--to <SLUG>")]
        [Description("The project it belongs to.")]
        public string? To { get; init; }

        [CommandOption("--to-global")]
        [Description("Move it to the workspace-wide list, for an idea that belongs to no project yet.")]
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

        var place = await settings.PlaceAsync(_ideas, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        return await Move.RunAsync(
            output, _ideas, place.Value!.Project, destination.Value, place.Value.Id, settings.DryRun, cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>The shared run of a command that changes one idea and shows it after.</summary>
internal static class IdeaChange
{
    /// <summary>
    /// A value given once, positionally or as its option, or null when it was
    /// given twice or not at all.
    /// </summary>
    /// <remarks>
    /// Both, because the parser refuses a positional value that starts with a
    /// dash, and prose often does: the option written as <c>--name=value</c> is
    /// the one form it always takes. The launcher's screens use that.
    /// </remarks>
    public static string? Once(string? positional, string? option)
    {
        var given = new[] { positional, option }.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

        return given.Count == 1 ? given[0] : null;
    }

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
