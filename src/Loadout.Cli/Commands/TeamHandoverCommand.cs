using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What a run handed to you because only you could do it, and ticking each off
/// once you have.
/// </summary>
/// <remarks>
/// <para>
/// A run ends this way when every criterion left needs a push, a CI run or
/// your own check, and none is something another round could do. Each one you
/// tick is written into the run's journal as review evidence in your name.
/// </para>
/// <para>
/// Once every one is ticked the run is judged again, through the same path as
/// <c>team resume</c> and without a turn for the lead: it ends done, and the
/// merge gate decides what comes back to the repository as it would have.
/// </para>
/// </remarks>
[Description("What a team run handed to you because only you could do it, and ticking each off once you have.")]
[CommandMeta(CommandCategory.Start, Intent = "handover hand check checked by hand needs person ci push criterion tick run team", Mutates = true)]
public sealed class TeamHandoverCommand : AsyncCommand<TeamHandoverCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly TeamResumeCommand _resume;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamHandoverCommand(IRunJournal journal, TeamResumeCommand resume, TimeProvider time, IAnsiConsole console)
    {
        _journal = journal;
        _resume = resume;
        _time = time;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--check <N>")]
        [Description("Tick one off as done and met, by its number or its words. Repeat it for more than one.")]
        public string[] Check { get; init; } = [];

        [CommandOption("--by <WHO>")]
        [Description("Who checked it, for the journal. Defaults to the name you are signed in as.")]
        public string? By { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var summary = _journal.Summarise(run);

        if (summary.Failed)
        {
            return output.Fail(summary.Error!, summary.ExitCode);
        }

        var by = settings.By is { Length: > 0 } who ? who.Trim() : Environment.UserName;

        RunHandover handover;
        IReadOnlyList<HandoverItem> ticked = [];

        if (settings.Check.Length == 0)
        {
            // Only looking, which is refused the same way a check would be for
            // a run that handed nothing over.
            if (RunHandoverChecks.Refusal(summary.Value!) is { } refused)
            {
                return output.Fail(refused, ExitCode.InvalidArguments);
            }

            handover = summary.Value!.Handover!;
        }
        else if (settings.DryRun)
        {
            var picked = RunHandoverChecks.Pick(summary.Value!, settings.Check);

            if (picked.Failed)
            {
                return output.Fail(picked.Error!, picked.ExitCode);
            }

            ticked = picked.Value!;
            handover = RunHandoverChecks.Ticked(summary.Value!.Handover!, ticked, by, _time.GetUtcNow());
        }
        else
        {
            var recorded = RunHandoverChecks.Record(_journal, run, settings.Check, by, _time.GetUtcNow());

            if (recorded.Failed)
            {
                return output.Fail(recorded.Error!, recorded.ExitCode);
            }

            handover = recorded.Value!;
        }

        var judging = settings.Check.Length > 0 && handover.Settled;

        if (output.IsJson && !(judging && !settings.DryRun))
        {
            output.WriteJson(new
            {
                dryRun = settings.DryRun,
                run,
                items = handover.Items.Select((one, i) => new
                {
                    number = i + 1,
                    criterion = one.Criterion,
                    needs = CriterionTags.Spell(one.Needs),
                    todo = one.ToDo,
                    tagged = one.By,
                    checkedBy = one.CheckedBy,
                    checkedAt = one.CheckedAt,
                }),
                branches = handover.Branches,
                settled = handover.Settled,
                judgedAgain = false,
            });

            return CommandOutput.Success();
        }

        if (!output.IsJson)
        {
            Show(output, run, handover, settings.DryRun ? ticked : []);
        }

        if (!judging)
        {
            if (!output.IsJson && handover.Open.Count > 0)
            {
                output.WriteBlankLine();
                output.WriteLine(
                    settings.DryRun
                        ? "[dim]Nothing was changed.[/]"
                        : $"[dim]Tick one off once it is done:[/] loadout team handover {Markup.Escape(run)} --check "
                          + (handover.Items.ToList().IndexOf(handover.Open[0]) + 1));
            }

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was changed. Ticking these would judge it again, and it would end done.[/]");

            return CommandOutput.Success();
        }

        if (!output.IsJson)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Every criterion it handed over is checked. Judging it again.[/]");
        }

        return await _resume.ExecuteAsync(
            context,
            new TeamResumeCommand.Settings
            {
                Run = run,
                Json = settings.Json,
                Quiet = settings.Quiet,
                Verbose = settings.Verbose,
                Debug = settings.Debug,
                NonInteractive = settings.NonInteractive,
                Offline = settings.Offline,
                NoSync = settings.NoSync,
                Agent = settings.Agent,
            },
            cancellationToken).ConfigureAwait(false);
    }

    private static void Show(CommandOutput output, string run, RunHandover handover, IReadOnlyList<HandoverItem> wouldTick)
    {
        output.WriteLine($"[bold]{Markup.Escape(run)}[/] handed over {handover.Items.Count} criterion(s) only you can meet:");
        output.WriteBlankLine();

        for (var i = 0; i < handover.Items.Count; i++)
        {
            var one = handover.Items[i];
            var mark = one.Checked ? "[green]x[/]" : wouldTick.Contains(one) ? "[yellow]x[/]" : " ";

            output.WriteLine(
                $"  {i + 1}. [[{mark}]] {Markup.Escape(one.Criterion)} [dim]({CriterionTags.Spell(one.Needs)}"
                + (one.By == "keyword" ? ", from its wording" : string.Empty) + ")[/]");
            output.WriteLine($"        {Markup.Escape(one.ToDo)}");

            if (one.Checked)
            {
                output.WriteLine($"        [dim]checked by {Markup.Escape(one.CheckedBy!)}[/]");
            }
            else if (wouldTick.Contains(one))
            {
                output.WriteLine("        [dim]would be checked[/]");
            }
        }

        if (handover.Branches.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine($"[dim]The work is on[/] {Markup.Escape(string.Join(", ", handover.Branches))}");
        }
    }
}
