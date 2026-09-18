using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
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

        return run is { Length: > 0 }
            ? (run, null)
            : (null, "No team has run on this machine yet.");
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

    public TeamGateCommand(IRunJournal journal, IAnsiConsole console, TimeProvider time)
    {
        _journal = journal;
        _console = console;
        _time = time;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--gate <ID>")]
        [Description("Which question, as 'team status' lists it. The only one waiting, when omitted.")]
        public string? Gate { get; init; }

        [CommandOption("--answer <ANSWER>")]
        [Description("yes, no, or one of the options the question offered.")]
        public string? Answer { get; init; }

        [CommandOption("--instead <TEXT>")]
        [Description("For a brief: the one to send instead of the one the lead wrote.")]
        public string? Instead { get; init; }

        [CommandOption("--reason <WHY>")]
        [Description("Why, in your words. The node is told.")]
        public string? Reason { get; init; }

        [CommandOption("--by <WHERE>")]
        [Description("Where the answer came from: terminal or dashboard. For the daemon.")]
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
                output.WriteLine($"  [bold]{Markup.Escape(one.Id)}[/]  {Markup.Escape(one.Question)}?");
            }

            return output.Fail("Say which with --gate.", ExitCode.InvalidArguments);
        }

        if (settings.Answer is not { Length: > 0 } answer)
        {
            return output.Fail(
                $"{gate.Question}? Answer with --answer "
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

        var yes = answer is "yes" or "y" or "allow" or "true"
            || gate.Choices.Any(one => string.Equals(one, answer, StringComparison.OrdinalIgnoreCase))
                && answer is not ("no" or "n" or "refuse" or "false");

        await NodePermissions.AnswerAsync(
            directory,
            gate.Id,
            new AskAnswer(
                yes,
                settings.Reason is { Length: > 0 } why
                    ? why
                    : yes
                        ? "The person running this team allowed it, for this call only."
                        : "The person running this team refused it. Report what you needed and why "
                            + "rather than finding another way to do it.",
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

        await RunControl.SayAsync(_journal.DirectoryOf(run), message, cancellationToken)
            .ConfigureAwait(false);

        output.WriteLine(
            $"[green]+[/] The lead of {Markup.Escape(run)} reads that at the start of its next round.");

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
