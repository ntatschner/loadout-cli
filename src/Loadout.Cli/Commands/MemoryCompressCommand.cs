using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;

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

    [CommandOption("--apply")]
    [Description("Write the topics and shorten the instruction file.")]
    public bool Apply { get; init; }
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
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        MemoryCompressSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var slugResult = await ResolveAsync(settings).ConfigureAwait(false);

        if (slugResult.Failed)
        {
            return output.Fail(slugResult);
        }

        var slug = slugResult.Value!;
        var agent = settings.Agent is { Length: > 0 } named ? named : "claude";

        var source = Path.Combine(
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
