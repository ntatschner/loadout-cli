using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Diagnostics;
using Loadout.Models;
using Loadout.Models.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

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
public sealed class DoctorCommand : AsyncCommand<GlobalSettings>
{
    private readonly IDoctorService _doctor;
    private readonly IAnsiConsole _console;

    public DoctorCommand(IDoctorService doctor, IAnsiConsole console)
    {
        _doctor = doctor;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _doctor.RunAsync().ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var report = result.Value!;

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
                }),
            });
        }
        else
        {
            Render(output, report);
        }

        // The exit code follows the worst finding so a scripted health check
        // can act on it without parsing anything.
        return report.Overall switch
        {
            DiagnosticSeverity.Error => (int)ExitCode.GeneralFailure,
            _ => (int)ExitCode.Success,
        };
    }

    private static void Render(CommandOutput output, DiagnosticReport report)
    {
        output.WriteLine("[bold]AI Workspace Launcher Diagnostics[/]");

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
                    + $"[dim]{Markup.Escape(check.Detail)}[/]");
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
    }
}
