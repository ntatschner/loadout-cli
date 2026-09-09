using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

public sealed class InstructionsProbeSettings : GlobalSettings
{
    [CommandArgument(0, "[specialist]")]
    [Description("Specialist to measure. Omit to list the ones that can be measured.")]
    public string? Specialist { get; init; }

    [CommandOption("--days <DAYS>")]
    [Description("How far back to look. Defaults to 60.")]
    public int Days { get; init; } = 60;
}

/// <summary>
/// Says how often sessions actually did what a specialist asks for.
/// </summary>
/// <remarks>
/// <para>
/// <c>instructions stats</c> answers which specialists launches reached, which
/// is delivery. This is the other question, and the one worth more: did any of
/// it change what happened next. A specialist can be in every launch, read by
/// nobody, and cost its tokens every time — and until this there was no way to
/// find that out.
/// </para>
/// <para>
/// What it reports is a rate over time and not a verdict. It cannot say a
/// session did something <em>because</em> of a specialist: the launcher never
/// learns the identifier the agent gives its own session, so a launch and the
/// conversation it started cannot be joined, and no amount of reporting fixes
/// that. What can be shown honestly is whether the rate moved, for specialists
/// that apply to every launch anyway.
/// </para>
/// </remarks>
[Description("Measure how often sessions did what a specialist asks for.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "measure specialist effect compliance probe evidence working")]
public sealed class InstructionsProbeCommand : AsyncCommand<InstructionsProbeSettings>
{
    private readonly IInstructionService _instructions;
    private readonly IProbeService _probes;
    private readonly IEnvironmentProvider _environment;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public InstructionsProbeCommand(
        IInstructionService instructions,
        IProbeService probes,
        IEnvironmentProvider environment,
        IAnsiConsole console,
        TimeProvider time)
    {
        _instructions = instructions;
        _probes = probes;
        _environment = environment;
        _console = console;
        _time = time;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsProbeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var catalogue = await _instructions
            .LibraryAsync(workspacePath: null, ct: cancellationToken)
            .ConfigureAwait(false);

        var library = catalogue.Specialists.Values;

        var measurable = library
            .Where(specialist => specialist.Probe is not null)
            .OrderBy(specialist => specialist.Id, StringComparer.Ordinal)
            .ToList();

        if (settings.Specialist is not { Length: > 0 } asked)
        {
            return List(output, measurable);
        }

        var chosen = measurable.FirstOrDefault(
            specialist => specialist.Id.Equals(asked, StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
        {
            var known = library.Any(s => s.Id.Equals(asked, StringComparison.OrdinalIgnoreCase));

            return output.Fail(
                known
                    ? $"'{asked}' declares no probe, so there is nothing to measure. "
                        + "See which ones do with: loadout instructions probe"
                    : $"There is no specialist called '{asked}'.",
                ExitCode.InvalidArguments);
        }

        var since = DateOnly.FromDateTime(
            _time.GetUtcNow().UtcDateTime.AddDays(-Math.Max(1, settings.Days)));

        var measured = await _probes
            .MeasureAsync(chosen, Roots(), since, cancellationToken)
            .ConfigureAwait(false);

        if (measured.Failed)
        {
            return output.Fail(measured);
        }

        return Report(output, measured.Value!);
    }

    private static int List(CommandOutput output, IReadOnlyList<Models.Instructions.SpecialistDocument> measurable)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                measurable = measurable.Select(s => new { id = s.Id, probe = s.Probe!.Summary }),
            });

            return CommandOutput.Success();
        }

        if (measurable.Count == 0)
        {
            output.WriteLine("[dim]No specialist declares a probe.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine("[bold]Specialists that can be measured[/]");
        output.WriteBlankLine();

        foreach (var specialist in measurable)
        {
            output.WriteLine($"  {Markup.Escape(specialist.Id)}");
            output.WriteLine($"    [dim]{Markup.Escape(specialist.Probe!.Summary)}[/]");
        }

        output.WriteBlankLine();

        // Said here rather than left to be inferred from a short list. Most
        // specialists have no honest signature, and a reader who does not know
        // that will read their absence as an oversight.
        output.WriteLine(
            "[dim]Most specialists have none. A probe has to be something a session "
            + "visibly does, and inventing one would put a number on a question it "
            + "does not answer.[/]");

        return CommandOutput.Success();
    }

    private static int Report(CommandOutput output, ProbeReport report)
    {
        if (output.IsJson)
        {
            output.WriteJson(new
            {
                specialist = report.Specialist.Id,
                probe = report.Specialist.Probe!.Summary,
                weeks = report.Weeks.Select(w => new
                {
                    beginning = w.Beginning.ToString("yyyy-MM-dd"),
                    sessions = w.Sessions,
                    showed = w.Showed,
                    percent = w.Percent,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[bold]{Markup.Escape(report.Specialist.Id)}[/]  "
            + $"[dim]{Markup.Escape(report.Specialist.Probe!.Summary)}[/]");
        output.WriteBlankLine();

        if (report.Weeks.Count == 0)
        {
            output.WriteLine("[dim]No sessions with any tool use in that period.[/]");

            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple).BorderColor(Color.Grey);
        table.AddColumn("Week beginning");
        table.AddColumn(new TableColumn("Sessions").RightAligned());
        table.AddColumn(new TableColumn("Showed it").RightAligned());

        foreach (var week in report.Weeks)
        {
            table.AddRow(
                week.Beginning.ToString("yyyy-MM-dd"),
                week.Sessions.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{week.Showed} ({week.Percent}%)");
        }

        output.Write(table);
        output.WriteBlankLine();

        // The caveat belongs beside the number, not in the documentation. A
        // rate shown without it will be read as an effect.
        output.WriteLine(
            "[dim]A rate, not a verdict. A launch and the session it started cannot be "
            + "joined — the agent names its own session and never says so — and a session "
            + "doing this is not proof it did so because of the specialist. Sessions with "
            + "no tool use at all are not counted either way.[/]");

        return CommandOutput.Success();
    }

    /// <summary>Where the agents keep their transcripts on this machine.</summary>
    private IReadOnlyList<string> Roots() =>
        [Core.Agents.AgentHome.ClaudeProjects(_environment)];
}
