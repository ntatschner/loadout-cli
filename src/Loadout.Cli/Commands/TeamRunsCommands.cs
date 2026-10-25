using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Forgetting runs.
/// </summary>
/// <remarks>
/// <para>
/// A run's directory is small — a journal, the briefs, the reports and one
/// stream per node, under a megabyte for a four-minute run — and nothing had
/// ever deleted one, so a machine that has been running teams for a month has
/// a directory nobody can read and nobody can clear. Disk is not the reason;
/// the listing is. <c>team runs</c> is the first place anybody looks and it
/// showed every experiment anyone had ever started.
/// </para>
/// <para>
/// Two commands rather than one because they are asked in different words.
/// "Get rid of that one" names a run. "Clear out the old ones" names no run at
/// all, and the thing that makes it safe is being told what it picked before
/// it takes anything.
/// </para>
/// <para>
/// Both move what they take to the bin rather than deleting it. They are typed
/// while clearing up, which is when the wrong identifier gets pasted, and the
/// journal is the only record of what a run did. See <see cref="TeamBin"/>.
/// </para>
/// </remarks>
[Description("Forget one or more team runs: moved to the bin, where they can be restored until it is emptied.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team runs remove delete forget clear old run history tidy", Mutates = true)]
public sealed class TeamRunsRemoveCommand : AsyncCommand<TeamRunsRemoveCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly TeamBin _bin;
    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamRunsRemoveCommand(
        IRunJournal journal,
        TeamBin bin,
        Loadout.Core.Configuration.IConfigurationService configuration,
        TimeProvider time,
        IAnsiConsole console)
    {
        _journal = journal;
        _bin = bin;
        _configuration = configuration;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<run>")]
        [Description("The runs to forget, as 'team runs' names them.")]
        public string[] Runs { get; init; } = [];

        [CommandOption("--force")]
        [Description("Take a run that has not finished. Its nodes lose the directory they read answers from.")]
        public bool Force { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Runs.Length == 0)
        {
            return output.Fail(
                "Name at least one run. See what there is with: loadout team runs",
                ExitCode.InvalidArguments);
        }

        var days = await Binning.DaysAsync(_configuration, cancellationToken).ConfigureAwait(false);

        var forgotten = new List<RunForgotten>();
        var refused = new List<Loadout.Models.Results.OperationResult>();

        foreach (var runId in settings.Runs)
        {
            var summary = _journal.Summarise(runId);

            if (summary.Failed)
            {
                refused.Add(Loadout.Models.Results.OperationResult.Fail(
                    summary.Error!, summary.ExitCode));

                continue;
            }

            var run = summary.Value!;

            if (run.Running && !settings.Force)
            {
                refused.Add(Loadout.Models.Results.OperationResult.Fail(
                    $"'{runId}' has not finished. Stop it first with: loadout team halt {runId}",
                    ExitCode.PolicyViolation));

                continue;
            }

            if (settings.DryRun)
            {
                // Everything the real run would report, from the same summary
                // it would report it from, and nothing touched.
                forgotten.Add(new RunForgotten(
                    run.RunId, run.Team, 0, 0, RunRetention.Unmerged(run)));

                continue;
            }

            var gone = _journal.Forget(runId, settings.Force);

            if (gone.Failed)
            {
                refused.Add(Loadout.Models.Results.OperationResult.Fail(gone.Error!, gone.ExitCode));

                continue;
            }

            forgotten.Add(gone.Value!);
        }

        if (!settings.DryRun && forgotten.Count > 0)
        {
            Binning.Sweep(_bin, _time, days);
        }

        return Report(output, forgotten, refused, settings.DryRun, days);
    }

    /// <summary>
    /// What went, what did not, and what is left over — shared with the prune
    /// so both say the same words about the same thing.
    /// </summary>
    internal static int Report(
        CommandOutput output,
        IReadOnlyList<RunForgotten> forgotten,
        IReadOnlyList<Loadout.Models.Results.OperationResult> refused,
        bool dryRun,
        int days)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(forgotten);
        ArgumentNullException.ThrowIfNull(refused);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun,
                forgotten = forgotten.Select(one => new
                {
                    run = one.RunId,
                    one.Team,
                    one.Bytes,
                    one.Files,
                    one.Unmerged,
                    bin = one.Bin,
                }),
                keptDays = days,
                refused = refused.Select(one => one.Error),
            });

            return Ended(refused);
        }

        foreach (var one in forgotten)
        {
            output.WriteLine(dryRun
                ? $"[dim]Would move[/] [bold]{Markup.Escape(one.RunId)}[/]"
                  + (one.Team is { Length: > 0 } ? $" [dim]({Markup.Escape(one.Team)})[/]" : string.Empty)
                  + " [dim]to the bin[/]"
                : $"[green]+[/] Moved [bold]{Markup.Escape(one.RunId)}[/]"
                  + (one.Team is { Length: > 0 } ? $" [dim]({Markup.Escape(one.Team)})[/]" : string.Empty)
                  + " to the bin"
                  + (one.Files > 0 ? $"[dim], {one.Files} file(s), {Size(one.Bytes)}[/]" : string.Empty));

            // Named rather than counted, because the name is the recoverable
            // part: the branch is still in Git and this line is the last thing
            // on the machine that says which run put it there.
            foreach (var branch in one.Unmerged)
            {
                output.WriteLine(
                    $"  [yellow]![/] left behind [bold]{Markup.Escape(branch)}[/], "
                    + "[dim]which nothing merged. Git still has it.[/]");
            }
        }

        foreach (var why in refused)
        {
            // Shown.Safely rather than Markup.Escape: escaping stops a bracket
            // being read as markup and says nothing about what the text
            // contains, and a refusal carries whatever the failure said.
            output.WriteLine($"[yellow]-[/] {Shown.Safely(why.Error ?? string.Empty)}");
        }

        if (forgotten.Count == 0 && refused.Count == 0)
        {
            output.WriteLine("[dim]Nothing to forget.[/]");
        }

        if (dryRun && forgotten.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was removed.[/]");
        }

        if (!dryRun && forgotten.Count > 0)
        {
            // How to get it back, said once rather than under every run: a
            // prune of forty is forty lines already. One run is named, so the
            // line is the command to type rather than its shape.
            output.WriteBlankLine();
            output.WriteLine(
                $"[dim]The bin keeps {(forgotten.Count == 1 ? "it" : "them")} {Binning.Kept(days)}. "
                + $"Get {(forgotten.Count == 1 ? "it" : "one")} back with:[/] loadout team runs restore "
                + (forgotten.Count == 1 ? Markup.Escape(forgotten[0].RunId) : "[[<run>]]"));
        }

        return Ended(refused);
    }

    /// <summary>
    /// The exit code for a run of this.
    /// </summary>
    /// <remarks>
    /// One refusal keeps its own code, because a caller that named one run
    /// wants to know which way it failed — the dashboard turns exactly this
    /// into the sentence it shows, and "no such run" and "it is still going"
    /// are different things to be told. Several refusals collapse to a general
    /// failure: there is no one answer, and picking one of them would be
    /// reporting the others as though they had not happened.
    /// </remarks>
    private static int Ended(IReadOnlyList<Loadout.Models.Results.OperationResult> refused) =>
        refused.Count switch
        {
            0 => CommandOutput.Success(),
            1 => (int)refused[0].ExitCode,
            _ => (int)ExitCode.GeneralFailure,
        };

    /// <summary>A byte count as somebody would say it.</summary>
    internal static string Size(long bytes) =>
        bytes >= 1024L * 1024
            ? (bytes / (1024.0 * 1024)).ToString("0.#", CultureInfo.InvariantCulture) + " MB"
            : bytes >= 1024
                ? (bytes / 1024.0).ToString("0.#", CultureInfo.InvariantCulture) + " KB"
                : bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
}

/// <summary>Clearing out the old ones.</summary>
[Description("Forget the old team runs, keeping the newest and anything still going.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team runs prune tidy clear old history housekeeping retention", Mutates = true)]
public sealed class TeamRunsPruneCommand : AsyncCommand<TeamRunsPruneCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly TeamBin _bin;
    private readonly Loadout.Core.Configuration.IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamRunsPruneCommand(
        IRunJournal journal,
        TeamBin bin,
        Loadout.Core.Configuration.IConfigurationService configuration,
        TimeProvider time,
        IAnsiConsole console)
    {
        _journal = journal;
        _bin = bin;
        _configuration = configuration;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--keep <COUNT>")]
        [Description("Keep this many of the newest runs whatever else is true. 0 keeps none.")]
        public int? Keep { get; init; }

        [CommandOption("--older-than <AGE>")]
        [Description("Take nothing younger than this: 30d, 12h, 90m.")]
        public string? OlderThan { get; init; }

        [CommandOption("--include-unmerged")]
        [Description("Take runs that left a branch nothing merged. Off by default.")]
        public bool IncludeUnmerged { get; init; }

        [CommandOption("--outcome <OUTCOME>")]
        [Description(
            "Take only runs that ended this way: done, failed, stopped, limited, blocked, "
            + "needs-decision, abandoned, or unrecorded for one that finished without saying how. "
            + "Repeatable.")]
        public string[] Outcome { get; init; } = [];

        [CommandOption("--failed")]
        [Description("Take only the runs that failed. The same as --outcome failed.")]
        public bool Failed { get; init; }

        [CommandOption("--yes")]
        [Description("Do not ask before forgetting them.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var days = await Binning.DaysAsync(_configuration, cancellationToken).ConfigureAwait(false);

        TimeSpan? age = null;

        if (settings.OlderThan is { Length: > 0 } said)
        {
            age = TeamScheduleAddCommand.Duration(said);

            if (age is null)
            {
                return output.Fail(
                    $"'{said}' is not an age. Write one as 30d, 12h or 90m.",
                    ExitCode.InvalidArguments);
            }
        }

        var outcomes = new List<RunOutcome>();

        if (settings.Failed)
        {
            outcomes.Add(RunOutcome.Failed);
        }

        foreach (var asked in settings.Outcome ?? [])
        {
            if (!RunOutcomes.TryParse(asked, out var outcome))
            {
                return output.Fail(
                    $"'{asked}' is not an ending. Ask for one of: "
                    + string.Join(", ", RunOutcomes.Names) + ".",
                    ExitCode.InvalidArguments);
            }

            outcomes.Add(outcome);
        }

        // A run still going is never taken, whatever is asked for, so asking
        // for them by name is somebody who has misread what this does rather
        // than somebody who wants nothing to happen.
        if (outcomes.Contains(RunOutcome.Running))
        {
            return output.Fail(
                "A run that is still going is never forgotten. Stop it first with "
                + "'loadout team stop', then forget it.",
                ExitCode.InvalidArguments);
        }

        // None of them given is not "take everything": it is somebody who has
        // not said what they meant, and the safe reading of an unclear
        // instruction to delete things is to ask for a clearer one.
        if (settings.Keep is null && age is null && outcomes.Count == 0)
        {
            return output.Fail(
                "Say what to take: --keep <count>, --older-than <age>, --failed, "
                + "--outcome <ending>, or any of them together. Together they narrow each "
                + "other: 'the failed ones older than that, but never below the newest count'.",
                ExitCode.InvalidArguments);
        }

        if (settings.Keep is < 0)
        {
            return output.Fail(
                "--keep cannot be negative. Use 0 to keep none.", ExitCode.InvalidArguments);
        }

        var runs = _journal.List(int.MaxValue)
            .Select(_journal.Summarise)
            .Where(result => result.Succeeded)
            .Select(result => result.Value!)
            .ToList();

        var chosen = RunRetention.Choose(
            runs, settings.Keep, age, _time.GetUtcNow(), settings.IncludeUnmerged, outcomes);

        if (output.IsJson && settings.DryRun)
        {
            output.WriteJson(new
            {
                dryRun = true,
                forgetting = chosen.Forgetting.Select(run => new
                {
                    run = run.RunId,
                    run.Team,
                    outcome = Filed(run),
                }),
                keeping = chosen.Keeping.Select(one => new
                {
                    run = one.Run.RunId,
                    one.Run.Team,
                    because = one.Because,
                }),
            });

            return CommandOutput.Success();
        }

        if (chosen.Forgetting.Count == 0)
        {
            if (!output.IsJson)
            {
                output.WriteLine(
                    $"[dim]Nothing to forget. {chosen.Keeping.Count} run(s) here, all kept.[/]");

                Why(output, chosen);
            }
            else
            {
                output.WriteJson(new { dryRun = settings.DryRun, forgotten = Array.Empty<string>(), refused = Array.Empty<string>() });
            }

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            foreach (var run in chosen.Forgetting)
            {
                output.WriteLine(
                    $"[dim]Would forget[/] [bold]{Markup.Escape(run.RunId)}[/] "
                    + $"[dim]({Markup.Escape(run.Team)}, {When(run)}, {Markup.Escape(Filed(run))})[/]");
            }

            Why(output, chosen);

            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was removed. Repeat without --dry-run to take them.[/]");

            return CommandOutput.Success();
        }

        // Named rather than counted, and before the question rather than
        // after: "forget 14 runs?" is a number somebody agrees to without
        // knowing what is in it.
        if (!settings.Yes)
        {
            foreach (var run in chosen.Forgetting)
            {
                output.WriteLine(
                    $"  {Markup.Escape(run.RunId)} "
                    + $"[dim]({Markup.Escape(run.Team)}, {When(run)}, {Markup.Escape(Filed(run))})[/]");
            }

            Why(output, chosen);

            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That would forget {chosen.Forgetting.Count} run(s), and nobody is here to "
                    + "agree to it. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm(
                $"Move {chosen.Forgetting.Count} run(s) to the bin? It keeps them {Binning.Kept(days)}", false))
            {
                output.WriteLine("[dim]Nothing was removed.[/]");

                return CommandOutput.Success();
            }
        }

        var forgotten = new List<RunForgotten>();
        var refused = new List<Loadout.Models.Results.OperationResult>();

        foreach (var run in chosen.Forgetting)
        {
            var gone = _journal.Forget(run.RunId);

            if (gone.Failed)
            {
                refused.Add(Loadout.Models.Results.OperationResult.Fail(gone.Error!, gone.ExitCode));

                continue;
            }

            forgotten.Add(gone.Value!);
        }

        if (forgotten.Count > 0)
        {
            Binning.Sweep(_bin, _time, days);
        }

        return TeamRunsRemoveCommand.Report(output, forgotten, refused, dryRun: false, days);
    }

    /// <summary>
    /// What the prune left alone, gathered by reason rather than listed one by
    /// one. Somebody clearing out forty runs wants to know that three stayed
    /// because they were still going, not to read three lines saying so.
    /// </summary>
    private static void Why(CommandOutput output, Pruning chosen)
    {
        foreach (var group in chosen.Keeping
            .GroupBy(one => one.Because, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count()))
        {
            output.WriteLine(
                $"[dim]Keeping {group.Count()}: {Markup.Escape(group.Key)}.[/]");
        }
    }

    /// <summary>How a run ended, for the line offering it up.</summary>
    /// <remarks>
    /// An ending nothing recognises is said as "ended", not guessed at.
    /// Somebody about to delete a run is owed the difference between "this
    /// failed" and "nobody here can tell".
    /// </remarks>
    private static string Filed(RunSummary run) =>
        RunOutcomes.Of(run) is { } outcome ? RunOutcomes.Spell(outcome) : "ended";

    /// <summary>When a run stopped, for the line offering it up.</summary>
    private static string When(RunSummary run) =>
        (run.Finished ?? run.Started).ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Ending a run whose driver has gone.</summary>
/// <remarks>
/// <para>
/// A run reads as going until its runner writes its finish, so a runner that
/// was killed leaves one that remove, prune and resume all refuse for good.
/// This writes the finish for it, and only once nothing on this machine is
/// driving the run: a run that is still going is stopped with
/// <c>team halt</c>, not closed.
/// </para>
/// <para>
/// A run from a build that kept no owner can only be judged by how long it
/// has been quiet, which is a guess, so closing one is asked about first.
/// </para>
/// </remarks>
[Description("End a team run whose driver has died, so it can be removed, pruned or resumed.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team runs close end abandon hung stuck dead orphaned run not responding", Mutates = true)]
public sealed class TeamRunsCloseCommand : Command<TeamRunsCloseCommand.Settings>
{
    private readonly RunCloser _closer;
    private readonly IAnsiConsole _console;

    public TeamRunsCloseCommand(
        IRunJournal journal,
        IRunOwnership ownership,
        TimeProvider time,
        IAnsiConsole console)
    {
        _closer = new RunCloser(journal, ownership, time);
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<run>")]
        [Description("The run to end, as 'team runs' names it.")]
        public string Run { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Close a run from an older build without asking, though it may still be going.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var judged = _closer.Judge(settings.Run);

        if (judged.Failed)
        {
            return output.Fail(judged.Error!, judged.ExitCode);
        }

        var (_, liveness) = judged.Value;
        var reason = Reason(liveness);
        var confirmed = settings.Yes;

        // Asked only where it would otherwise be refused for want of it, and
        // never under a dry run, which changes nothing and so needs nobody's
        // agreement.
        if (liveness is Liveness.PossiblyNotResponding quiet && !confirmed && !settings.DryRun
            && settings.AllowsPrompting)
        {
            if (!_console.Confirm(
                $"Run {Markup.Escape(settings.Run)} has written nothing for {RunCloser.Quiet(quiet.Quiet)}, "
                + "but it kept no owner, so it may still be going. Close it?", false))
            {
                output.WriteLine("[dim]Nothing was changed.[/]");

                return CommandOutput.Success();
            }

            confirmed = true;
        }

        var closed = settings.DryRun
            ? _closer.Preview(settings.Run, confirmed: true, reason)
            : _closer.Close(settings.Run, confirmed, reason);

        if (closed.Failed)
        {
            return output.Fail(closed.Error!, closed.ExitCode);
        }

        var done = closed.Value!;
        var asks = liveness is Liveness.PossiblyNotResponding && !confirmed;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = settings.DryRun,
                run = done.RunId,
                team = done.Team,
                ended = done.Ended,
                ending = done.Ending,
                needsYes = settings.DryRun && asks,
            });

            return CommandOutput.Success();
        }

        var verb = settings.DryRun ? "Would close" : "Closed";

        output.WriteLine(
            $"{(settings.DryRun ? "[dim]" : "[green]+[/] ")}{verb} [bold]{Markup.Escape(done.RunId)}[/]"
            + (done.Team is { Length: > 0 } ? $" [dim]({Markup.Escape(done.Team)})[/]" : string.Empty)
            + $"{(settings.DryRun ? "[/]" : string.Empty)}: {Markup.Escape(done.Ending)}");

        foreach (var node in done.Ended)
        {
            output.WriteLine($"  [dim]{(settings.DryRun ? "would end" : "ended")}[/] {Markup.Escape(node)}");
        }

        if (settings.DryRun)
        {
            output.WriteBlankLine();
            output.WriteLine(asks
                ? "[dim]Nothing was changed. It would ask first, or take --yes.[/]"
                : "[dim]Nothing was changed.[/]");
        }
        else
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[dim]Remove it with[/] loadout team runs remove {Markup.Escape(done.RunId)}"
                + $" [dim]or pick it up with[/] loadout team resume {Markup.Escape(done.RunId)}");
        }

        return CommandOutput.Success();
    }

    /// <summary>Why, for the ending the journal keeps.</summary>
    private static string Reason(Liveness liveness) => liveness switch
    {
        Liveness.NotResponding { LastOwner: { } owner } =>
            $"process {owner.ProcessId.ToString(CultureInfo.InvariantCulture)} driving it had gone",
        Liveness.NotResponding => "the process driving it had gone",
        Liveness.PossiblyNotResponding quiet => $"nothing written for {RunCloser.Quiet(quiet.Quiet)}",
        _ => "closed by hand",
    };
}
