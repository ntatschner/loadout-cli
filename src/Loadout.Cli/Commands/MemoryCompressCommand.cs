using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Options for compressing instructions into memory.</summary>
public sealed class MemoryCompressSettings : GlobalSettings
{
    // --agent is inherited from GlobalSettings rather than redeclared: it means
    // the same thing here as everywhere else, and declaring it twice is how a
    // CLI ends up with one flag that behaves differently per command.
    [CommandArgument(0, "[PROJECT]")]
    [Description("Project whose instructions to compress. Defaults to the current repository.")]
    public string? Project { get; init; }

    [CommandOption("--min-facts <COUNT>")]
    [Description("Fewest facts a heading must yield to become a topic. Defaults to 2.")]
    public int MinimumFacts { get; init; } = MemoryCompressor.MinimumFactsPerTopic;

    [CommandOption("--file <FILE>")]
    [Description(
        "Instruction file to compress. Defaults to the project's agent instructions.")]
    public string? File { get; init; }

    [CommandOption("--apply")]
    [Description("Write the topics and shorten the instruction file.")]
    public bool ApplyRequested { get; init; }

    /// <summary>
    /// Whether to go ahead, once --dry-run has had its say.
    /// </summary>
    /// <remarks>
    /// --dry-run is accepted on every command and always means the
    /// same thing, so it overrides --apply rather than
    /// competing with it. Asking for both is not a contradiction to
    /// resolve: the more cautious of the two is what was meant.
    /// </remarks>
    public bool Apply => ApplyRequested && !DryRun;
}

/// <summary>
/// Moves durable facts out of always-loaded instructions into the memory store.
/// <para>
/// The context compiler inlines instructions in full but memory only by its
/// index. A standing fact therefore costs a session its whole length on every
/// launch while it sits in instructions, and one index line once it sits in
/// memory. This is how an instruction layer that has grown for a year gets
/// smaller without anything being thrown away.
/// </para>
/// </summary>
[Description("Move durable facts out of always-loaded instructions into the memory store.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "shrink instructions budget too big", Mutates = true)]
public sealed class MemoryCompressCommand : AsyncCommand<MemoryCompressSettings>
{
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly MemoryCompressor _compressor;
    private readonly IAnsiConsole _console;

    public MemoryCompressCommand(
        IProjectService projects,
        IWorkspaceManager workspace,
        MemoryCompressor compressor,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _compressor = compressor;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        MemoryCompressSettings settings,
        CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var slugResult = await ResolveAsync(settings).ConfigureAwait(false);

        if (slugResult.Failed)
        {
            return output.Fail(slugResult);
        }

        var slug = slugResult.Value!;
        var agent = settings.Agent is { Length: > 0 } named ? named : "claude";

        // Any instruction file, not only the one this command was written for.
        // The compressor never cared which file it read — it takes a path and
        // always has — so the restriction lived here alone, and it left the
        // largest always-loaded files in a workspace with no way to shrink
        // them. Where the facts go is still decided by the project, because
        // memory is per-project regardless of which file the facts came from.
        var source = settings.File is { Length: > 0 } chosen
            ? Path.GetFullPath(chosen)
            : Path.Combine(
                _workspace.LocalPath, "projects", slug, "agents", agent, "instructions.md");

        var planResult = settings.Apply
            ? await _compressor
                .ApplyAsync(source, _workspace.LocalPath, slug, settings.MinimumFacts)
                .ConfigureAwait(false)
            : await _compressor.PlanAsync(source, settings.MinimumFacts).ConfigureAwait(false);

        if (planResult.Failed)
        {
            return output.Fail(planResult);
        }

        var plan = planResult.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug,
                source = plan.SourcePath,
                applied = plan.Applied,
                facts = plan.Facts,
                bytesBefore = plan.BytesBefore,
                bytesAfter = plan.BytesAfter,
                bytesSaved = plan.BytesSaved,
                considered = plan.Considered,
                topics = plan.Topics.Select(t => new
                {
                    name = t.Name,
                    kind = t.Kind.ToString(),
                    facts = t.Facts.Count,
                }),
                leftAlone = plan.Rejected.ToDictionary(r => r.Key.ToString(), r => r.Value),
                withheld = plan.Withheld,
            });

            return CommandOutput.Success();
        }

        if (plan.Topics.Count == 0)
        {
            output.WriteLine(
                $"[dim]Nothing in {agent.EscapeMarkup()}'s instructions for {slug.EscapeMarkup()} "
                + "is worth moving to memory.[/]");

            Explain(output, plan);
            SuggestSplitting(output, plan);

            return CommandOutput.Success();
        }

        output.WriteLine(plan.Applied
            ? $"[green]Compressed[/] {slug.EscapeMarkup()}"
            : $"[bold]Would compress[/] {slug.EscapeMarkup()}");

        output.WriteBlankLine();

        foreach (var topic in plan.Topics)
        {
            output.WriteLine(
                $"  [cyan]{topic.Name.EscapeMarkup()}[/] "
                + $"[dim]{topic.Kind.ToString().ToLowerInvariant()}, {topic.Facts.Count} fact(s)[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"Always loaded: [yellow]{plan.BytesBefore / 1024} KB[/] -> "
            + $"[green]{plan.BytesAfter / 1024} KB[/] "
            + $"[dim]({plan.BytesSaved / 1024} KB off every session)[/]");

        Explain(output, plan);

        if (!plan.Applied)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Re-run with --apply to move them.[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Points at the other tool when this one cannot help.
    /// </summary>
    /// <remarks>
    /// <para>
    /// There are two ways to shrink an always-loaded file and they suit
    /// different files. This one moves standing facts into memory, which needs
    /// facts to move; splitting scopes sections to the paths they concern,
    /// which needs sections. A file of prose under headings has the second and
    /// not the first, and this command's answer for it was "nothing is worth
    /// moving" — true, unhelpful, and silent about the tool that would have
    /// worked.
    /// </para>
    /// <para>
    /// Suggested only on the evidence in the file rather than always: headings
    /// to split on, and a file large enough for the split to be worth the
    /// trouble. A tool that recommends another tool every time it finds
    /// nothing is a tool nobody reads the output of.
    /// </para>
    /// </remarks>
    private static void SuggestSplitting(CommandOutput output, CompressionPlan plan)
    {
        if (SplittingSuggestion(plan.SourcePath, plan.BytesBefore) is not { } suggestion)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine($"[dim]{Markup.Escape(suggestion)}[/]");
    }

    /// <summary>
    /// What to say about splitting this file, or null when there is nothing
    /// worth saying.
    /// </summary>
    /// <remarks>
    /// Separate from the printing so the judgement can be tested against real
    /// files rather than only seen once by eye.
    /// </remarks>
    internal static string? SplittingSuggestion(string path, long bytes)
    {
        // Small files are not the problem, whatever shape they are in.
        if (bytes < 8 * 1024)
        {
            return null;
        }

        int headings;

        try
        {
            headings = File.ReadLines(path)
                .Count(line => line.StartsWith("## ", StringComparison.Ordinal));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saying nothing is a fine answer. The file was read once already
            // to get here, so this is only reachable if it changed underneath.
            return null;
        }

        // Two headings is a document with a preamble; several is a document
        // whose parts concern different things, which is what can be scoped.
        return headings < 3
            ? null
            : $"It is {bytes / 1024}KB of prose under {headings} headings, which is the shape "
                + "splitting suits: sections load only when the paths they concern are touched. "
                + $"Try: loadout rules split --from {path}";
    }

    /// <summary>
    /// Says what was left behind and why.
    /// <para>
    /// Worth printing: somebody looking at a file that barely shrank needs to
    /// know whether the tool found nothing or decided against what it found.
    /// </para>
    /// </summary>
    private static void Explain(CommandOutput output, Loadout.Core.Instructions.CompressionPlan plan)
    {
        // Said first and in colour, because it is the one finding here that is
        // about disclosure rather than tidiness. Pattern names only: reprinting
        // the line to report it would defeat the point of not moving it.
        if (plan.Withheld.Count > 0)
        {
            output.WriteBlankLine();

            foreach (var (pattern, count) in plan.Withheld.OrderByDescending(w => w.Value))
            {
                output.WriteLine(
                    $"[yellow]Withheld[/] {count} line(s) matching "
                    + $"{pattern.EscapeMarkup()}, left in the instructions rather than copied "
                    + "into the workspace repository.");
            }
        }

        if (plan.Rejected.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine($"[dim]Examined {plan.Considered} list item(s). Left alone:[/]");

        foreach (var (verdict, count) in plan.Rejected.OrderByDescending(r => r.Value))
        {
            output.WriteLine(
                $"  [dim]{count,4}  {MemoryFactClassifier.Explain(verdict).EscapeMarkup()}[/]");
        }
    }

    private async Task<Models.Results.OperationResult<string>> ResolveAsync(
        MemoryCompressSettings settings)
    {
        if (settings.Project is { Length: > 0 } named)
        {
            var resolved = await _projects.ResolveAsync(named).ConfigureAwait(false);

            return resolved.Failed
                ? Models.Results.OperationResult<string>.Fail(resolved.Error!, resolved.ExitCode)
                : Models.Results.OperationResult<string>.Ok(resolved.Value!.Entry.Slug);
        }

        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var here = await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);

        return here.Succeeded && here.Value is { } project
            ? Models.Results.OperationResult<string>.Ok(project.Entry.Slug)
            : Models.Results.OperationResult<string>.Fail(
                $"{directory} is not a registered project. Name one instead.",
                ExitCode.ProjectNotFound);
    }
}
