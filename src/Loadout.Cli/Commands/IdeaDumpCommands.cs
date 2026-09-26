using System.ComponentModel;
using System.Globalization;
using Loadout.Agents.Ideas;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Ideas;
using Loadout.Core.Projects;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Settings shared by the commands that act on one dump.</summary>
public class IdeaDumpSettings : GlobalSettings
{
    [CommandArgument(0, "<id>")]
    [Description("The dump. 'loadout idea dump list' shows them.")]
    public string Id { get; init; } = string.Empty;

    [CommandOption("--project <SLUG>")]
    [Description("The project whose list it was dropped on. Found by looking when not given.")]
    public string? Project { get; init; }

    [CommandOption("--global")]
    [Description("It was dropped on the workspace-wide list.")]
    public bool Global { get; init; }

    internal async Task<OperationResult<DumpPlace>> PlaceAsync(
        IIdeaDumps dumps,
        IProjectService projects,
        CancellationToken ct)
    {
        if (Global && Project is { Length: > 0 })
        {
            return OperationResult<DumpPlace>.Fail(
                "--global and --project name different lists. Give one.", ExitCode.InvalidArguments);
        }

        if (Global || Project is { Length: > 0 })
        {
            var place = new DumpPlace(Global ? null : Project, Id.Trim());
            var read = await dumps.ReadAsync(place, ct).ConfigureAwait(false);

            return read.Succeeded
                ? OperationResult<DumpPlace>.Ok(place)
                : OperationResult<DumpPlace>.Fail(read.Error!, read.ExitCode);
        }

        var here = await ProjectHandle.ResolveAsync(projects, null, Repo, ct).ConfigureAwait(false);

        return await dumps.LocateAsync(Id, here.Succeeded ? here.Value!.Entry.Slug : null, ct).ConfigureAwait(false);
    }
}

/// <summary>Drops in notes kept elsewhere, and has an agent split them.</summary>
[Description("Drop in notes kept elsewhere, from a file, standard input or --text, and have an agent split them into ideas and tasks.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "dump notes import brain dump braindump paste file split ideas tasks list",
    Mutates = true,
    Example = "notes.md")]
public sealed class IdeaDumpAddCommand : AsyncCommand<IdeaDumpAddCommand.Settings>
{
    private readonly IIdeaDumps _dumps;
    private readonly IDumpSplitter _splitter;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDumpAddCommand(IIdeaDumps dumps, IDumpSplitter splitter, IProjectService projects, IAnsiConsole console)
    {
        _dumps = dumps;
        _splitter = splitter;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[file]")]
        [Description("A file of notes, or - for standard input. Leave out when giving --text.")]
        public string? File { get; init; }

        [CommandOption("--text <TEXT>")]
        [Description("The notes themselves, instead of a file. Notes that start with a dash go as --text=\"- ...\".")]
        public string? Text { get; init; }

        [CommandOption("--project <SLUG>")]
        [Description("Keep it on this project's list. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--global")]
        [Description("Keep it on the workspace-wide list.")]
        public bool Global { get; init; }

        [CommandOption("--no-split")]
        [Description("Keep the notes and ask no agent yet. Split them later with 'idea dump split'.")]
        public bool NoSplit { get; init; }

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

        if (settings.Global && settings.Project is { Length: > 0 })
        {
            return output.Fail("--global and --project name different lists. Give one.", ExitCode.InvalidArguments);
        }

        var (text, source, error) = await ReadAsync(settings, cancellationToken).ConfigureAwait(false);

        if (error is not null)
        {
            return output.Fail(error, ExitCode.InvalidArguments);
        }

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
                $"Would keep {text!.Length} characters from {Markup.Escape(source!)} on "
                + $"{Markup.Escape(TaskPaths.Describe(project))}"
                + (settings.NoSplit ? "." : " and ask an agent to split them.")
                + " Nothing was written.");

            return CommandOutput.Success();
        }

        var kept = await _dumps.KeepAsync(project, text!, source!, cancellationToken).ConfigureAwait(false);

        if (kept.Failed)
        {
            return output.Fail(kept);
        }

        var place = new DumpPlace(project, kept.Value!.Id);

        if (settings.NoSplit)
        {
            if (output.IsJson)
            {
                output.WriteJson(DumpView.Of(place, kept.Value));

                return CommandOutput.Success();
            }

            output.WriteLine($"[green]+[/] Kept as {Markup.Escape(place.Id)} on {Markup.Escape(place.Where)}.");
            output.WriteLine($"[dim]  Split it with: loadout idea dump split {Markup.Escape(place.Id)}[/]");

            return CommandOutput.Success();
        }

        output.Meanwhile($"[dim]Kept as {Markup.Escape(place.Id)}. Asking an agent to split it; this takes a minute or so.[/]");

        var split = await _splitter
            .SplitAsync(new SplitRequest(place, settings.Agent, settings.Model), cancellationToken)
            .ConfigureAwait(false);

        if (split.Failed)
        {
            // The notes are kept whatever happened to the split, and said so:
            // losing somebody's notes because an agent stumbled would be the
            // one outcome worse than not splitting them.
            output.WriteLine($"[yellow]![/] The notes are kept as {Markup.Escape(place.Id)}, but the split did not work.");

            return output.Fail(split);
        }

        if (output.IsJson)
        {
            output.WriteJson(DumpView.Of(place, split.Value!.Dump));

            return CommandOutput.Success();
        }

        DumpView.Write(output, place, split.Value!.Dump);

        return CommandOutput.Success();
    }

    private static async Task<(string? Text, string? Source, string? Error)> ReadAsync(
        Settings settings,
        CancellationToken ct)
    {
        if (settings.Text is { Length: > 0 } given)
        {
            return settings.File is { Length: > 0 }
                ? (null, null, "Give a file or --text, not both.")
                : (given, "pasted", null);
        }

        if (settings.File is "-" || (settings.File is null && Console.IsInputRedirected))
        {
            var piped = await Console.In.ReadToEndAsync(ct).ConfigureAwait(false);

            return (piped, "standard input", null);
        }

        if (settings.File is not { Length: > 0 } path)
        {
            return (null, null, "Give a file of notes, - for standard input, or --text \"...\".");
        }

        if (!System.IO.File.Exists(path))
        {
            return (null, null, $"'{path}' does not exist.");
        }

        try
        {
            return (await System.IO.File.ReadAllTextAsync(path, ct).ConfigureAwait(false), Path.GetFileName(path), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null, $"'{path}' could not be read: {ex.Message}");
        }
    }
}

/// <summary>Every dump, and how much of each has been recorded.</summary>
[Description("List the notes dropped in, and how much of each has been recorded.")]
[CommandMeta(CommandCategory.Workspace, Intent = "dumps notes list imported")]
public sealed class IdeaDumpListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IIdeaDumps _dumps;
    private readonly IAnsiConsole _console;

    public IdeaDumpListCommand(IIdeaDumps dumps, IAnsiConsole console)
    {
        _dumps = dumps;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var listed = await _dumps.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { dumps = listed.Value!.Select(d => DumpView.Of(d.Place, d.Dump)) });

            return CommandOutput.Success();
        }

        if (listed.Value!.Count == 0)
        {
            output.WriteLine("[dim]Nothing dropped in yet. Drop notes in with: loadout idea dump add <file>[/]");

            return CommandOutput.Success();
        }

        foreach (var (place, dump) in listed.Value!)
        {
            output.WriteLine(
                $"{Markup.Escape(place.Id),-22} {Markup.Escape(place.Where),-24} {DumpView.Progress(dump)}  "
                + $"[dim]from {Markup.Escape(dump.Source)}[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Shows one dump and its split.</summary>
[Description("Show a dump: the pieces it was split into, and which have been recorded.")]
[CommandMeta(CommandCategory.Workspace, Intent = "dump show split pieces notes")]
public sealed class IdeaDumpShowCommand : AsyncCommand<IdeaDumpShowCommand.Settings>
{
    private readonly IIdeaDumps _dumps;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDumpShowCommand(IIdeaDumps dumps, IProjectService projects, IAnsiConsole console)
    {
        _dumps = dumps;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaDumpSettings
    {
        [CommandOption("--text")]
        [Description("Show the notes as they were dropped in, too.")]
        public bool ShowText { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var place = await settings.PlaceAsync(_dumps, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        var read = await _dumps.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (output.IsJson)
        {
            output.WriteJson(DumpView.Of(place.Value!, read.Value!, withText: true));

            return CommandOutput.Success();
        }

        if (settings.ShowText)
        {
            output.WriteLine($"[dim]{Markup.Escape(read.Value!.Text)}[/]");
            output.WriteBlankLine();
        }

        DumpView.Write(output, place.Value!, read.Value!);

        return CommandOutput.Success();
    }
}

/// <summary>Asks an agent to split a dump again.</summary>
[Description("Have an agent split a dump, replacing the last split, when nothing of it has been recorded yet.")]
[CommandMeta(CommandCategory.Workspace, Intent = "dump split again resplit agent notes")]
public sealed class IdeaDumpSplitCommand : AsyncCommand<IdeaDumpSplitCommand.Settings>
{
    private readonly IIdeaDumps _dumps;
    private readonly IDumpSplitter _splitter;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDumpSplitCommand(IIdeaDumps dumps, IDumpSplitter splitter, IProjectService projects, IAnsiConsole console)
    {
        _dumps = dumps;
        _splitter = splitter;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaDumpSettings
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
        var place = await settings.PlaceAsync(_dumps, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        if (!settings.DryRun)
        {
            output.Meanwhile("[dim]Asking an agent to split it; this takes a minute or so.[/]");
        }

        var split = await _splitter
            .SplitAsync(new SplitRequest(place.Value!, settings.Agent, settings.Model, settings.DryRun), cancellationToken)
            .ConfigureAwait(false);

        if (split.Failed)
        {
            return output.Fail(split);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would start {Markup.Escape(split.Value!.Plan.Executable)}. Nothing was started.");

            return CommandOutput.Success();
        }

        if (output.IsJson)
        {
            output.WriteJson(DumpView.Of(place.Value!, split.Value!.Dump));

            return CommandOutput.Success();
        }

        DumpView.Write(output, place.Value!, split.Value!.Dump);

        return CommandOutput.Success();
    }
}

/// <summary>Records the pieces of a dump as ideas and tasks.</summary>
[Description("Record the pieces of a dump as ideas and tasks: all of them, or the ones named with --only.")]
[CommandMeta(CommandCategory.Workspace, Intent = "dump apply record accept pieces ideas tasks", Mutates = true)]
public sealed class IdeaDumpApplyCommand : AsyncCommand<IdeaDumpApplyCommand.Settings>
{
    private readonly IIdeaDumps _dumps;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public IdeaDumpApplyCommand(IIdeaDumps dumps, IProjectService projects, IAnsiConsole console)
    {
        _dumps = dumps;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : IdeaDumpSettings
    {
        [CommandOption("--only <NUMBERS>")]
        [Description("Which pieces, by number: 1,3,4. Every piece not yet recorded when left out.")]
        public string? Only { get; init; }

        [CommandOption("--to <SLUG>")]
        [Description("Put every piece on this project, whatever the agent thought.")]
        public string? To { get; init; }

        [CommandOption("--to-global")]
        [Description("Put every piece on the workspace-wide list.")]
        public bool ToGlobal { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is recording them. Defaults to this machine's user.")]
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

        if (settings.ToGlobal && settings.To is { Length: > 0 })
        {
            return output.Fail("--to and --to-global name different lists. Give one.", ExitCode.InvalidArguments);
        }

        var numbers = new List<int>();

        foreach (var part in (settings.Only ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return output.Fail($"'{part}' is not a piece number. Give them as 1,3,4.", ExitCode.InvalidArguments);
            }

            numbers.Add(number);
        }

        var place = await settings.PlaceAsync(_dumps, _projects, cancellationToken).ConfigureAwait(false);

        if (place.Failed)
        {
            return output.Fail(place);
        }

        var to = settings.ToGlobal ? string.Empty : settings.To;

        if (settings.DryRun)
        {
            var read = await _dumps.ReadAsync(place.Value!, cancellationToken).ConfigureAwait(false);

            if (read.Failed)
            {
                return output.Fail(read);
            }

            foreach (var item in read.Value!.Items.Where(i => i.Recorded.Length == 0 && (numbers.Count == 0 || numbers.Contains(i.Number))))
            {
                output.WriteLine(
                    $"Would record {item.Number} as {(item.Kind == DumpItemKind.Idea ? "an idea" : "a task")}: "
                    + Markup.Escape(item.Title));
            }

            output.WriteLine("Nothing was recorded.");

            return CommandOutput.Success();
        }

        var applied = await _dumps.ApplyAsync(
            place.Value!,
            numbers,
            to,
            settings.By is { Length: > 0 } who ? who : Environment.UserName,
            cancellationToken).ConfigureAwait(false);

        if (applied.Failed)
        {
            return output.Fail(applied);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                recorded = applied.Value!.Select(a => new
                {
                    number = a.Item.Number,
                    kind = a.Item.Kind.ToString().ToLowerInvariant(),
                    list = a.List,
                    id = a.Id,
                    title = a.Item.Title,
                }),
            });

            return CommandOutput.Success();
        }

        foreach (var done in applied.Value!)
        {
            output.WriteLine(
                $"[green]+[/] {done.Item.Number}  {(done.Item.Kind == DumpItemKind.Idea ? "idea" : "task")} "
                + $"[bold]{Markup.Escape(done.Id)}[/] on {Markup.Escape(TaskPaths.Describe(done.List))}");
        }

        if (applied.Value!.Any(a => a.Item.Kind == DumpItemKind.Idea))
        {
            output.WriteLine("[dim]  Flesh an idea out with: loadout idea refine <id>[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>How a dump is shown.</summary>
internal static class DumpView
{
    public static string Progress(IdeaDump dump) =>
        dump.Items.Count == 0
            ? dump.LastError.Length > 0 ? "[yellow]split failed[/]" : "[dim]not split[/]"
            : $"{dump.Items.Count(i => i.Recorded.Length > 0)} of {dump.Items.Count} recorded";

    public static object Of(DumpPlace place, IdeaDump dump, bool withText = false) => new
    {
        id = place.Id,
        project = place.Project,
        source = dump.Source,
        captured = dump.CapturedUtc,
        split = dump.SplitUtc,
        error = dump.LastError.Length > 0 ? dump.LastError : null,
        text = withText ? dump.Text : null,
        items = dump.Items.Select(item => new
        {
            number = item.Number,
            title = item.Title,
            excerpt = item.Excerpt,
            kind = item.Kind.ToString().ToLowerInvariant(),
            project = item.Project.Length > 0 ? item.Project : null,
            reason = item.Reason,
            recorded = item.Recorded.Length > 0 ? item.Recorded : null,
        }),
    };

    public static void Write(CommandOutput output, DumpPlace place, IdeaDump dump)
    {
        output.WriteLine(
            $"[bold]{Markup.Escape(place.Id)}[/] on {Markup.Escape(place.Where)}, from {Markup.Escape(dump.Source)}  {Progress(dump)}");

        if (dump.LastError.Length > 0)
        {
            output.WriteLine($"[yellow]The last split went wrong:[/] {Markup.Escape(dump.LastError)}");
        }

        foreach (var item in dump.Items)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"  [bold]{item.Number}[/] {(item.Kind == DumpItemKind.Idea ? "[blue]idea[/]" : "[green]task[/]")} "
                + $"{Markup.Escape(item.Title)}"
                + (item.Project.Length > 0 ? $"  [dim]for {Markup.Escape(item.Project)}[/]" : string.Empty)
                + (item.Recorded.Length > 0 ? $"  [dim]recorded as {Markup.Escape(item.Recorded)}[/]" : string.Empty));
            output.WriteLine($"     [dim]\"{Markup.Escape(item.Excerpt)}\"[/]");
        }

        if (dump.Items.Any(i => i.Recorded.Length == 0))
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Next[/]");
            output.WriteLine($"  [dim]loadout idea dump apply {Markup.Escape(place.Id)} [[--only 1,3]] [[--to <project>]][/]");
        }
    }
}
