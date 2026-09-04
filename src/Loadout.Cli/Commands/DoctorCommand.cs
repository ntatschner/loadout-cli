using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Diagnostics;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Models.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Options for the doctor report.</summary>
public sealed class DoctorSettings : GlobalSettings
{
    [CommandOption("--fix")]
    [Description("Offer to put right the findings the launcher can fix itself.")]
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

    [CommandOption("--bundle [PATH]")]
    [Description("Write the findings to one file to send somebody. Defaults to this directory.")]
    public Spectre.Console.Cli.FlagValue<string> Bundle { get; init; } = new();
}

/// <summary>
/// Reports on the launcher, the platform, Git, the workspace and the agents
/// (spec section 60).
/// <para>
/// This is also where the cross-platform contract becomes visible: every
/// optional capability is listed with its status and, when unavailable, the
/// reason. A gap that cannot be seen here is a gap that spec section 5 says
/// should not exist.
/// </para>
/// </summary>
[Description("Check the launcher, platform, Git, workspace, secrets and agents.")]
[CommandMeta(CommandCategory.Health, Intent = "check broken problem diagnose wrong health fix", Mutates = true, Example = "--fix")]
public sealed class DoctorCommand : AsyncCommand<DoctorSettings>
{
    private readonly IDoctorService _doctor;
    private readonly IRemediationService _remediation;
    private readonly IAnsiConsole _console;

    private readonly IPlatformPaths _paths;

    public DoctorCommand(
        IDoctorService doctor,
        IRemediationService remediation,
        IPlatformPaths paths,
        IAnsiConsole console)
    {
        _doctor = doctor;
        _remediation = remediation;
        _paths = paths;
        _console = console;
    }

    /// <summary>
    /// Writes the findings to one file, or refuses to.
    /// </summary>
    /// <remarks>
    /// The bundle exists to leave this machine, so it is screened before it is
    /// written rather than after: a file that has been created and then reported
    /// as unsafe is a file somebody may already have attached to something.
    /// </remarks>
    private async Task<int> WriteBundleAsync(
        CommandOutput output,
        DiagnosticReport report,
        DoctorSettings settings,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var built = DiagnosticBundle.Build(
            report,
            _paths.Host,
            typeof(DoctorCommand).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
            now);

        if (built.Failed)
        {
            return output.Fail(built);
        }

        var path = settings.Bundle.Value is { Length: > 0 } given
            ? Directory.Exists(given)
                ? Path.Combine(given, DiagnosticBundle.FileName(now))
                : given
            : Path.Combine(Directory.GetCurrentDirectory(), DiagnosticBundle.FileName(now));

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Would write[/] {Markup.Escape(path)}");

            return CommandOutput.Success();
        }

        try
        {
            await File.WriteAllTextAsync(path, built.Value!, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return output.Fail($"Could not write '{path}': {ex.Message}");
        }

        output.WriteLine($"[green]+[/] {Markup.Escape(path)}");
        output.WriteLine(
            "[dim]Read it before sending it. It names paths and secret references, never "
            + "credential values, and the machine name is left out.[/]");

        return CommandOutput.Success();
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, DoctorSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _doctor.RunAsync(cancellationToken).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var report = result.Value!;

        if (settings.Bundle.IsSet)
        {
            return await WriteBundleAsync(output, report, settings, cancellationToken)
                .ConfigureAwait(false);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                verdict = report.Verdict,
                overall = report.Overall.ToString(),
                checks = report.Checks.Select(c => new
                {
                    category = c.Category,
                    name = c.Name,
                    severity = c.Severity.ToString(),
                    detail = c.Detail,
                    fixable = c.Remedy is not null,
                }),
                remedies = report.Remedies.Select(r => new
                {
                    kind = r.Kind.ToString(),
                    description = r.Description,
                    target = r.Target,
                }),
            });
        }
        else
        {
            Render(output, report);
        }

        if (settings.Fix)
        {
            var changed = await RemediateAsync(output, report, settings, cancellationToken).ConfigureAwait(false);

            if (changed)
            {
                // Re-checked rather than assumed. A remedy that reported
                // success without actually clearing the finding is exactly the
                // failure a command like this exists to catch.
                var recheck = await _doctor.RunAsync(cancellationToken).ConfigureAwait(false);

                if (recheck.Succeeded)
                {
                    report = recheck.Value!;

                    output.WriteBlankLine();
                    output.WriteLine("[bold]After fixing[/]");
                    output.WriteLine(
                        $"Overall: {Colourise(report.Overall)} "
                        + $"[dim]({report.Remedies.Count} still fixable)[/]");
                }
            }
        }

        // The exit code follows the worst finding so a scripted health check
        // can act on it without parsing anything.
        return report.Overall switch
        {
            DiagnosticSeverity.Error => (int)ExitCode.GeneralFailure,
            _ => (int)ExitCode.Success,
        };
    }

    /// <summary>
    /// Shows what each remedy would do, then applies the ones agreed to.
    /// <para>
    /// Preview first, always. These write hooks and copy memory into a shared
    /// repository, and nothing else in this tool mutates without showing the
    /// change first. A fix that surprises somebody is worse than a warning
    /// they ignored.
    /// </para>
    /// </summary>
    private async Task<bool> RemediateAsync(
        CommandOutput output,
        DiagnosticReport report,
        DoctorSettings settings,
        CancellationToken cancellationToken)
    {
        var remedies = report.Remedies;

        output.WriteBlankLine();

        if (remedies.Count == 0)
        {
            output.WriteLine("[dim]Nothing here can be fixed automatically.[/]");

            return false;
        }

        output.WriteLine("[bold]Fixable[/]");

        var previews = new List<RemedyOutcome>();

        foreach (var remedy in remedies)
        {
            var preview = await _remediation.PreviewAsync(remedy, cancellationToken).ConfigureAwait(false);

            if (preview.Failed)
            {
                output.WriteLine(
                    $"  [yellow]![/] {Markup.Escape(remedy.Description)} "
                    + $"[dim]cannot be previewed: {Loadout.Tui.Shown.Safely(preview.Error!)}[/]");

                continue;
            }

            previews.Add(preview.Value!);

            output.WriteLine($"  [green]+[/] {Markup.Escape(remedy.Description)}");
            output.WriteLine($"    [dim]{Markup.Escape(preview.Value!.Detail)}[/]");
        }

        if (previews.Count == 0)
        {
            return false;
        }

        if (!settings.Yes)
        {
            output.WriteBlankLine();

            // Spec section 37: never a prompt where nobody can answer it.
            if (settings.NonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                output.WriteLine(
                    "[dim]Nothing was changed. Re-run with --fix --yes to apply these.[/]");

                return false;
            }

            if (!_console.Confirm($"Apply {previews.Count} fix(es)?", defaultValue: false))
            {
                output.WriteLine("[dim]Nothing was changed.[/]");

                return false;
            }
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Fixing[/]");

        var applied = 0;

        foreach (var preview in previews)
        {
            var result = await _remediation.ApplyAsync(preview.Remedy, cancellationToken).ConfigureAwait(false);

            if (result.Failed)
            {
                // One remedy failing must not stop the others. They are
                // independent, and stopping halfway leaves the least
                // explicable state of all.
                output.WriteLine(
                    $"  [red]x[/] {Markup.Escape(preview.Remedy.Description)} "
                    + $"[dim]{Loadout.Tui.Shown.Safely(result.Error!)}[/]");

                continue;
            }

            applied++;
            output.WriteLine($"  [green]+[/] {Markup.Escape(result.Value!.Detail)}");
        }

        return applied > 0;
    }

    private static string Colourise(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "[red]UNHEALTHY[/]",
        DiagnosticSeverity.Warning => "[yellow]DEGRADED[/]",
        _ => "[green]HEALTHY[/]",
    };

    private static void Render(CommandOutput output, DiagnosticReport report)
    {
        output.WriteLine("[bold]Loadout Diagnostics[/]");

        foreach (var group in report.Checks.GroupBy(c => c.Category))
        {
            output.WriteBlankLine();
            output.WriteLine($"[bold]{Markup.Escape(group.Key)}[/]");

            foreach (var check in group)
            {
                var (glyph, colour) = check.Severity switch
                {
                    DiagnosticSeverity.Error => ("x", "red"),
                    DiagnosticSeverity.Warning => ("!", "yellow"),
                    _ => ("+", "green"),
                };

                output.WriteLine(
                    $"[{colour}]{glyph}[/] {Markup.Escape(check.Name)}  "
                    + $"[dim]{Markup.Escape(check.Detail)}[/]"
                    + (check.Remedy is null ? string.Empty : " [dim](fixable)[/]"));
            }
        }

        var verdictColour = report.Overall switch
        {
            DiagnosticSeverity.Error => "red",
            DiagnosticSeverity.Warning => "yellow",
            _ => "green",
        };

        output.WriteBlankLine();
        output.WriteLine($"Overall: [{verdictColour}]{report.Verdict}[/]");

        if (report.Remedies.Count > 0)
        {
            output.WriteLine(
                $"[dim]{report.Remedies.Count} of these can be put right for you: "
                + "loadout doctor --fix[/]");
        }
    }
}
