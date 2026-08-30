using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Diagnostics;
using Loadout.Core.Projects;
using Loadout.Models;
using Loadout.Models.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Options for the drift report.</summary>
public sealed class DriftSettings : GlobalSettings
{
    [CommandArgument(0, "[PROJECT]")]
    [Description("Project to inspect. Omit for every registered project.")]
    public string? Project { get; init; }

    [CommandOption("--fix")]
    [Description("Offer to put right the drift the launcher can fix itself.")]
    public bool FixRequested { get; init; }

    /// <summary>
    /// Whether to go ahead, once --dry-run has had its say.
    /// </summary>
    /// <remarks>
    /// --dry-run is accepted on every command and always means the
    /// same thing, so it overrides --fix rather than
    /// competing with it. Asking for both is not a contradiction to
    /// resolve: the more cautious of the two is what was meant.
    /// </remarks>
    public bool Fix => FixRequested && !DryRun;

    [CommandOption("--yes")]
    [Description("With --fix, apply without asking first.")]
    public bool Yes { get; init; }

    [CommandOption("--quiet-clean")]
    [Description("Only show projects that have drifted.")]
    public bool QuietClean { get; init; }
}

/// <summary>
/// Reports where projects have drifted from what the workspace records.
/// <para>
/// The doctor answers whether this machine is set up, for wherever the shell
/// is standing. This answers what has quietly stopped being true across every
/// registered project — which is the drift that goes unnoticed, because nobody
/// runs a check in a repository they have not opened for a month.
/// </para>
/// </summary>
[Description("Show where projects have drifted from their recorded configuration.")]
[CommandMeta(CommandCategory.Health, Intent = "changed differs configuration out of date fix", Mutates = true, Example = "--fix")]
public sealed class DriftCommand : AsyncCommand<DriftSettings>
{
    private readonly IDriftService _drift;
    private readonly IRemediationService _remediation;
    private readonly IAnsiConsole _console;

    public DriftCommand(
        IDriftService drift,
        IRemediationService remediation,
        IAnsiConsole console)
    {
        _drift = drift;
        _remediation = remediation;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, DriftSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _drift.InspectAsync(settings.Project).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var reports = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(reports.Select(r => new
            {
                project = r.Slug,
                drifted = r.HasDrift,
                overall = r.Overall.ToString(),
                findings = r.Findings.Select(f => new
                {
                    name = f.Name,
                    severity = f.Severity.ToString(),
                    detail = f.Detail,
                    fixable = f.Remedy is not null,
                }),
            }));

            return Verdict(reports);
        }

        Render(output, reports, settings);

        var remedies = reports
            .SelectMany(r => r.Remedies)
            .DistinctBy(r => (r.Kind, r.Target))
            .ToList();

        if (settings.Fix && remedies.Count > 0)
        {
            await FixAsync(output, remedies, settings).ConfigureAwait(false);
        }
        else if (remedies.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[dim]{remedies.Count} of these can be put right for you: loadout drift --fix[/]");
        }

        return Verdict(reports);
    }

    private static void Render(
        CommandOutput output,
        IReadOnlyList<ProjectDrift> reports,
        DriftSettings settings)
    {
        var shown = 0;

        foreach (var report in reports)
        {
            if (settings.QuietClean && !report.HasDrift)
            {
                continue;
            }

            shown++;

            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Markup.Escape(report.Slug)}[/] "
                + (report.HasDrift ? string.Empty : "[dim]no drift[/]"));

            foreach (var finding in report.Findings)
            {
                // A clean finding is worth printing only when something else in
                // the same project is not: otherwise it is noise across twenty
                // projects.
                if (finding.Severity == DiagnosticSeverity.Info && !report.HasDrift)
                {
                    continue;
                }

                var (glyph, colour) = finding.Severity switch
                {
                    DiagnosticSeverity.Error => ("x", "red"),
                    DiagnosticSeverity.Warning => ("!", "yellow"),
                    _ => ("+", "green"),
                };

                output.WriteLine(
                    $"  [{colour}]{glyph}[/] {Markup.Escape(finding.Name)}  "
                    + $"[dim]{Markup.Escape(finding.Detail)}[/]"
                    + (finding.Remedy is null ? string.Empty : " [dim](fixable)[/]"));
            }
        }

        if (shown == 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[green]No project has drifted.[/]");
        }
    }

    /// <summary>
    /// Previews then applies, the same way the doctor does, because these are
    /// the same remedies reached from a different question.
    /// </summary>
    private async Task FixAsync(
        CommandOutput output,
        IReadOnlyList<Remedy> remedies,
        DriftSettings settings)
    {
        output.WriteBlankLine();
        output.WriteLine("[bold]Fixable[/]");

        var previews = new List<RemedyOutcome>();

        foreach (var remedy in remedies)
        {
            var preview = await _remediation.PreviewAsync(remedy).ConfigureAwait(false);

            if (preview.Failed)
            {
                output.WriteLine(
                    $"  [yellow]![/] {Markup.Escape(remedy.Description)} "
                    + $"[dim]cannot be previewed: {Markup.Escape(preview.Error!)}[/]");

                continue;
            }

            previews.Add(preview.Value!);

            output.WriteLine($"  [green]+[/] {Markup.Escape(remedy.Description)}");
            output.WriteLine($"    [dim]{Markup.Escape(preview.Value!.Detail)}[/]");
        }

        if (previews.Count == 0)
        {
            return;
        }

        if (!settings.Yes)
        {
            output.WriteBlankLine();

            // Spec section 37: never a prompt where nobody can answer it.
            if (settings.NonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                output.WriteLine("[dim]Nothing was changed. Re-run with --fix --yes to apply these.[/]");

                return;
            }

            if (!_console.Confirm($"Apply {previews.Count} fix(es)?", defaultValue: false))
            {
                output.WriteLine("[dim]Nothing was changed.[/]");

                return;
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Fixing[/]");

        foreach (var preview in previews)
        {
            var applied = await _remediation.ApplyAsync(preview.Remedy).ConfigureAwait(false);

            // One failing must not stop the others: they are independent, and
            // stopping halfway leaves the least explicable state of all.
            output.WriteLine(applied.Failed
                ? $"  [red]x[/] {Markup.Escape(preview.Remedy.Description)} "
                    + $"[dim]{Markup.Escape(applied.Error!)}[/]"
                : $"  [green]+[/] {Markup.Escape(applied.Value!.Detail)}");
        }
    }

    /// <summary>
    /// The exit code follows the worst finding, so a scheduled run can act on
    /// it without parsing anything.
    /// </summary>
    private static int Verdict(IReadOnlyList<ProjectDrift> reports)
    {
        if (reports.Count == 0)
        {
            return (int)ExitCode.Success;
        }

        return reports.Max(r => r.Overall) == DiagnosticSeverity.Error
            ? (int)ExitCode.GeneralFailure
            : (int)ExitCode.Success;
    }
}
