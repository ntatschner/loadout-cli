using System.ComponentModel;
using Loadout.Agents.Teams;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What every one of these needs: which run.</summary>
public class RunSettings : GlobalSettings
{
    [CommandArgument(0, "[RUN]")]
    [Description("The run, as 'team runs' shows it. The most recent one when omitted.")]
    public string? Run { get; init; }
}

/// <summary>What the control commands share.</summary>
internal static class RunControlling
{
    /// <summary>The run asked for, or the newest, or a sentence saying there is none.</summary>
    internal static (string? Run, string? Error) Resolve(IRunJournal journal, string? asked)
    {
        var run = asked ?? journal.List(1).FirstOrDefault();

        if (run is not { Length: > 0 })
        {
            return (null, "No team has run on this machine yet.");
        }

        // Said here as well as enforced in the journal, because a sentence
        // about what a run identifier looks like is more use to whoever typed
        // one than "no such run" would be.
        return RunJournal.Names(run)
            ? (run, null)
            : (null, $"'{run}' is not a run identifier. They look like 20260918-1436-ed59.");
    }
}

/// <summary>Answers something a run stopped to ask.</summary>
/// <remarks>
/// <para>
/// The same answer the terminal would give, written the same way. A run asks by
/// leaving a file in its own directory and reads the answer from another; that
/// is how a browser can answer a run it is not inside, and it is why this
/// command exists rather than the dashboard writing files itself.
/// </para>
/// <para>
/// A gate that has already been answered is not answered again. Two answers to
/// one question is one answer the run never sees and one somebody thinks they
/// gave.
/// </para>
/// </remarks>
[Description("Answer something a run stopped to ask: a permission, a gate, or a lead's question.")]
[CommandMeta(CommandCategory.Start, Intent = "gate answer permission question allow refuse run", Mutates = true)]
public sealed class TeamGateCommand : AsyncCommand<TeamGateCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;
    private readonly IEnvironmentProvider _environment;

    public TeamGateCommand(IRunJournal journal, IAnsiConsole console, TimeProvider time, IEnvironmentProvider environment)
    {
        _journal = journal;
        _console = console;
        _time = time;
        _environment = environment;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--gate <ID>")]
        [Description("Which question, as 'team status' lists it. The only one waiting, when omitted.")]
        public string? Gate { get; init; }

        [CommandOption("--answer <ANSWER>")]
        [Description("yes, no, always (yes, and don't ask again, where a permission offers it), or one of the options the question offered.")]
        public string? Answer { get; init; }

        [CommandOption("--instead <TEXT>")]
        [Description("For a brief: the one to send instead of the one the lead wrote.")]
        public string? Instead { get; init; }

        [CommandOption("--reason <WHY>")]
        [Description("Why, in your words, or what to do instead. The node is told.")]
        public string? Reason { get; init; }

        [CommandOption("--by <WHERE>")]
        [Description("Where the answer came from: terminal or dashboard. For the daemon.")]
        public string? By { get; init; }

        [CommandOption("--think-again")]
        [Description("For a lead's question: choose none of its options and send it back to think again.")]
        public bool ThinkAgain { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        // Before anything is looked up. A node answering its own run's
        // questions, its own permission asks among them, is the run agreeing
        // with itself.
        if (NodeMarker.Refusal(_environment, "Answering what a run asked") is { } refused)
        {
            return output.Fail(refused, ExitCode.PolicyViolation);
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var directory = _journal.DirectoryOf(run);
        var waiting = NodePermissions.Pending(directory);

        if (waiting.Count == 0)
        {
            return output.Fail($"Run {run} is not waiting to be told anything.", ExitCode.InvalidArguments);
        }

        /*
          A question outlives the run that asked it. The ask file stays in the
          directory whether or not anybody answered, so "there is a question
          with no answer beside it" was read as "a run is waiting on this" -
          and an answer to a run that had already finished was written, and
          reported as having gone through.

          It has happened: a lead asked at 14:43:55, gave up at 14:48:55, and
          the run ended. The answer arrived at 14:51:33, landed in the
          directory of a finished run, and the dashboard said it worked.
        */
        if (_journal.Summarise(run) is { Succeeded: true, Value: { Finished: { } finished } over })
        {
            return output.Fail(
                $"Run {run} finished at {finished.ToLocalTime():HH:mm}"
                + (over.Ended is { Length: > 0 } how ? $" - {how}" : string.Empty)
                + ". Nothing is waiting for this answer any more.",
                ExitCode.InvalidArguments);
        }

        var gate = settings.Gate is { Length: > 0 } named
            ? waiting.FirstOrDefault(one => string.Equals(one.Id, named, StringComparison.Ordinal))
            : waiting.Count == 1 ? waiting[0] : null;

        if (gate is null)
        {
            // Named nothing with several waiting, or named one that is not.
            // Listing them is more use than refusing on its own.
            output.WriteLine($"Run {Markup.Escape(run)} is waiting on:");

            foreach (var one in waiting)
            {
                output.WriteLine($"  [bold]{Markup.Escape(one.Id)}[/]  {Markup.Escape(one.Asking)}");
            }

            return output.Fail("Say which with --gate.", ExitCode.InvalidArguments);
        }

        // Only where there are options to reject. A permission or a
        // confirmation is yes or no, and "think again" there would read as a
        // yes to a question nobody answered.
        if (settings.ThinkAgain)
        {
            if (gate.Kind != "question")
            {
                return output.Fail(
                    "--think-again sends a lead's question back to it. This is not a question with options: answer it with --answer.",
                    ExitCode.InvalidArguments);
            }

            if (settings.Answer is { Length: > 0 })
            {
                return output.Fail("Say --think-again or --answer, not both.", ExitCode.InvalidArguments);
            }
        }

        if ((settings.ThinkAgain ? TeamRunner.ThinkAgain : settings.Answer) is not { Length: > 0 } answer)
        {
            answer = string.Empty;
        }

        // "always" for the long option a permission offers, so nobody has to
        // type "yes, and don't ask again for Bash(git status:*)" at a prompt.
        // And whatever matches an option is recorded as the option, since the
        // node's side compares the two to decide whether to remember it.
        answer = string.Equals(answer, "always", StringComparison.OrdinalIgnoreCase)
            && gate.Choices.FirstOrDefault(one => one.StartsWith("yes, and don't ask again", StringComparison.Ordinal))
                is { } always
                ? always
                : gate.Choices.FirstOrDefault(one => string.Equals(one, answer, StringComparison.OrdinalIgnoreCase))
                    ?? answer;

        if (answer.Length == 0)
        {
            return output.Fail(
                $"{gate.Asking} Answer with --answer "
                + string.Join(" or ", gate.Choices),
                ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would answer [bold]{Markup.Escape(gate.Id)}[/] with "
                + $"{Markup.Escape(answer)}. Nothing was written.");

            return CommandOutput.Success();
        }

        // A question is answered, not permitted. Whatever was said is the
        // answer - including words that match none of the options, which is
        // what the page's own box sends - so recording it as a refusal would
        // write down the opposite of what happened. The option that ends a run
        // is one of the lead's own, and whoever asked reads it as that.
        var yes = gate.Kind == "question"
            || answer is "yes" or "y" or "allow" or "true"
            || gate.Choices.Any(one => string.Equals(one, answer, StringComparison.OrdinalIgnoreCase))
                && answer is not ("no" or "n" or "refuse" or "false");

        await NodePermissions.AnswerAsync(
            directory,
            gate.Id,
            new AskAnswer(
                yes,
                gate.Kind is "permission" or "remedy" || settings.Reason is not { Length: > 0 }
                    ? NodePermissions.Told(yes, settings.Reason)
                    : settings.Reason,
                Chosen: answer,

                // Recorded because "somebody allowed this" and "somebody
                // allowed this from a browser on the other side of the house"
                // are different sentences to whoever reads the run back.
                By: settings.By is { Length: > 0 } where ? where : "terminal",

                // Only meaningful for a brief, and harmless on anything else:
                // whatever asked decides whether it has anywhere to put this.
                Instead: settings.Instead),
            cancellationToken).ConfigureAwait(false);

        output.WriteLine($"[green]+[/] Told run {Markup.Escape(run)}: {Markup.Escape(answer)}.");

        return CommandOutput.Success();
    }
}

/// <summary>Says something to a run's lead.</summary>
/// <remarks>
/// Delivered at the start of its next round rather than now, because the lead
/// is mid-turn and cannot hear anything until it comes back. Said once: a
/// message handed over twice reads as somebody repeating themselves crossly.
/// </remarks>
[Description("Say something to a run's lead, delivered at the start of its next round.")]
[CommandMeta(CommandCategory.Start, Intent = "message tell say lead run steer", Mutates = true)]
public sealed class TeamMessageCommand : AsyncCommand<TeamMessageCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamMessageCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--message <TEXT>")]
        [Description("What to say, in your words.")]
        public string? Message { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Message is not { Length: > 0 } message)
        {
            return output.Fail("Say what with --message.", ExitCode.InvalidArguments);
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would tell run {Markup.Escape(run)}. Nothing was written.");

            return CommandOutput.Success();
        }

        var directory = _journal.DirectoryOf(run);

        await RunControl.SayAsync(directory, message, cancellationToken).ConfigureAwait(false);

        output.WriteLine(
            $"[green]+[/] The lead of {Markup.Escape(run)} reads that at the start of its next round.");

        /*
          Which may never come. A message is delivered at the start of the next
          round, and a lead stopped on a question has no next round until that
          question is answered - so a message sent instead of an answer sits in
          the directory, unread, while the run's own clock runs out.

          It is still written, because the person asked for it to be and it
          will be read if the run carries on. What changes is that they are
          told, rather than finding out from a run that ended without ever
          mentioning it.
        */
        foreach (var pending in NodePermissions.Pending(directory))
        {
            output.WriteLine(
                $"[yellow]It is waiting on a question first:[/] {Markup.Escape(pending.Asking)}");
            output.WriteLine(
                $"[dim]Nothing is read until that is answered. Answer it with: "
                + $"loadout team gate {run} --gate {pending.Id} --answer <one of "
                + $"{string.Join(", ", pending.Choices)}>[/]");

            break;
        }

        return CommandOutput.Success();
    }
}

/// <summary>Gives a run's room a name somebody chose.</summary>
/// <remarks>
/// <para>
/// A run is called 20260918-1436-ed59, which is precise, sortable and
/// impossible to hold in your head. Every run gets a room worked out from that
/// identifier so there is always something to say out loud; this is for when
/// the worked-out one is not the one you would use.
/// </para>
/// <para>
/// One file beside the journal rather than an event in it. The journal is a
/// record of what happened; what somebody decided to call it afterwards is
/// not that, and putting it there would mean renaming a run by appending to
/// its history.
/// </para>
/// </remarks>
[Description("Name a run's room, or clear the name to get the worked-out one back.")]
[CommandMeta(CommandCategory.Start, Intent = "name rename room run call label", Mutates = true)]
public sealed class TeamNameCommand : AsyncCommand<TeamNameCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamNameCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--room <NAME>")]
        [Description("What to call it. Leave out with --clear to go back to the worked-out one.")]
        public string? Room { get; init; }

        [CommandOption("--clear")]
        [Description("Forget the chosen name.")]
        public bool Clear { get; init; }
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (!settings.Clear && settings.Room is not { Length: > 0 })
        {
            return Task.FromResult(output.Fail(
                "Say what to call it with --room, or --clear to go back to the worked-out one.",
                ExitCode.InvalidArguments));
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return Task.FromResult(output.Fail(error!, ExitCode.ProjectNotFound));
        }

        var directory = _journal.DirectoryOf(run);

        if (settings.DryRun)
        {
            output.WriteLine(settings.Clear
                ? $"Would forget what {Markup.Escape(run)} is called. Nothing was written."
                : $"Would call {Markup.Escape(run)} "
                    + $"\u201c{Markup.Escape(settings.Room!)}\u201d. Nothing was written.");

            return Task.FromResult(CommandOutput.Success());
        }

        try
        {
            if (settings.Clear)
            {
                RoomNames.Forget(directory);
            }
            else
            {
                RoomNames.Rename(directory, settings.Room!);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(output.Fail(
                $"That name could not be written: {ex.Message}", ExitCode.GeneralFailure));
        }

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(run)} is "
            + $"[bold]{Markup.Escape(RoomNames.For(directory, run))}[/].");

        return Task.FromResult(CommandOutput.Success());
    }
}

/// <summary>Stops, holds or releases a run.</summary>
/// <remarks>
/// <para>
/// Cooperative, and the page says so: a run checks between rounds, so a stop
/// lands when the turn it is in comes back. The alternative is killing a
/// headless agent mid-turn, which loses the turn and what was paid for it.
/// </para>
/// <para>
/// Nothing here kills anything. A run whose coordinator has gone is not
/// stopped by this and does not need to be.
/// </para>
/// </remarks>
[Description("Stop a run after its current round, hold it, or let a held one carry on.")]
[CommandMeta(CommandCategory.Start, Intent = "stop pause resume hold cancel run team", Mutates = true)]
public sealed class TeamHaltCommand : AsyncCommand<TeamHaltCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamHaltCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--pause")]
        [Description("Hold it before its next round instead of stopping it.")]
        public bool Pause { get; init; }

        [CommandOption("--resume")]
        [Description("Let a held run carry on.")]
        public bool Resume { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Pause && settings.Resume)
        {
            return output.Fail("Hold it or let it go, not both.", ExitCode.InvalidArguments);
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var directory = _journal.DirectoryOf(run);

        var what = settings.Pause ? "hold" : settings.Resume ? "release" : "stop";

        if (settings.DryRun)
        {
            output.WriteLine($"Would {what} run {Markup.Escape(run)}. Nothing was written.");

            return CommandOutput.Success();
        }

        if (settings.Resume)
        {
            RunControl.Resume(directory);
            output.WriteLine($"[green]+[/] {Markup.Escape(run)} carries on.");

            return CommandOutput.Success();
        }

        if (settings.Pause)
        {
            await RunControl.PauseAsync(directory, cancellationToken).ConfigureAwait(false);
            output.WriteLine($"[green]+[/] {Markup.Escape(run)} holds before its next round.");

            return CommandOutput.Success();
        }

        await RunControl.StopAsync(directory, cancellationToken).ConfigureAwait(false);

        // Said plainly, because somebody stopping a run that is spending money
        // wants to know it is not stopped yet.
        output.WriteLine(
            $"[green]+[/] {Markup.Escape(run)} ends when the round it is in comes back. "
            + "Nothing is killed; what it is doing now finishes first.");

        return CommandOutput.Success();
    }
}

/// <summary>Changes what a run that is still going may spend.</summary>
/// <remarks>
/// <para>
/// Read by the run at the same check its team's budget is, between rounds, so
/// it lands before the lead's next turn. A run that has already stopped on its
/// budget is finished and is not reached by this: the command says so rather
/// than writing a file nothing will read.
/// </para>
/// <para>
/// Lower is allowed as well as higher. A budget below what has been spent ends
/// the run at its next check, which is a gentler stop than halting it.
/// </para>
/// </remarks>
[Description("Change what a running team may spend, from its next round on.")]
[CommandMeta(CommandCategory.Start, Intent = "budget raise extend increase spend money limit run team", Mutates = true)]
public sealed class TeamBudgetCommand : AsyncCommand<TeamBudgetCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly IAnsiConsole _console;

    public TeamBudgetCommand(IRunJournal journal, IAnsiConsole console)
    {
        _journal = journal;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--usd <AMOUNT>")]
        [Description("What it may spend in total, in USD. Not an addition: 40 means 40 altogether.")]
        public decimal? Usd { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Usd is not { } usd || usd <= 0)
        {
            return output.Fail("Say what it may spend with --usd, as a figure above zero.", ExitCode.InvalidArguments);
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var summary = _journal.Summarise(run);

        if (summary is { Succeeded: true, Value: { Finished: { } finished } over })
        {
            return output.Fail(
                $"Run {run} finished at {finished.ToLocalTime():HH:mm}"
                + (over.Ended is { Length: > 0 } how ? $" - {how}" : string.Empty)
                + ". A budget only reaches a run that is still going.",
                ExitCode.InvalidArguments);
        }

        var spent = summary.Value?.CostUsd ?? 0m;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would let run {Markup.Escape(run)} spend ${usd:0.00} in all "
                + $"(${spent:0.00} so far). Nothing was written.");

            return CommandOutput.Success();
        }

        await RunControl.SetBudgetAsync(_journal.DirectoryOf(run), usd, cancellationToken).ConfigureAwait(false);

        output.WriteLine(
            $"[green]+[/] {Markup.Escape(run)} may spend ${usd:0.00} in all, from its next round "
            + $"(${spent:0.00} so far)."
            + (usd <= spent ? " [yellow]That is below what it has spent, so it stops at its next check.[/]" : string.Empty));

        return CommandOutput.Success();
    }
}
