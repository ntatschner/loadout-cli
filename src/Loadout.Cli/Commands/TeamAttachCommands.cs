using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Sets the passphrase that lets the page type at a live node.</summary>
/// <remarks>
/// <para>
/// The dashboard's own token is for watching and deciding: reading a run,
/// answering a gate it asked, holding it, stopping it. Every one of those is
/// something the run offered to have decided.
/// </para>
/// <para>
/// Typing at a live node is not. It puts words into a process running with
/// your file access, at a moment nobody chose, and the page can be reached
/// from the network. So it has its own passphrase, kept where the other
/// credentials are kept, and the page exchanges it for something that stops
/// working on its own.
/// </para>
/// </remarks>
[Description("Set the passphrase that lets the dashboard type at a running node.")]
[CommandMeta(CommandCategory.Safety, Intent = "attach passphrase node type steer live", Mutates = true)]
public sealed class TeamAttachSetCommand : AsyncCommand<TeamAttachSetCommand.Settings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public TeamAttachSetCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--passphrase <TEXT>")]
        [Description("The passphrase. Eight characters at least.")]
        public string? Passphrase { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Passphrase is not { Length: > 0 } passphrase)
        {
            return output.Fail(
                "Give one with --passphrase. It is not read from a prompt, so it does not end up "
                + "in a transcript of one.",
                ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine("Dry run: nothing was kept. It would go in this machine's credential store.");

            return CommandOutput.Success();
        }

        var kept = await new Attaching(_secrets).RememberAsync(passphrase, cancellationToken)
            .ConfigureAwait(false);

        if (kept.Failed)
        {
            return output.Fail(kept);
        }

        output.WriteLine(
            "[green]+[/] Kept. The dashboard asks for it once per session and forgets it after "
            + $"{Attaching.Lasts.TotalMinutes:0} minutes.");

        return CommandOutput.Success();
    }
}

/// <summary>Forgets the attach passphrase.</summary>
[Description("Forget the attach passphrase, so nothing can type at a running node.")]
[CommandMeta(CommandCategory.Safety, Intent = "attach clear forget passphrase node", Mutates = true)]
public sealed class TeamAttachClearCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public TeamAttachClearCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine("Dry run: nothing was forgotten.");

            return CommandOutput.Success();
        }

        await new Attaching(_secrets).ForgetAsync(cancellationToken).ConfigureAwait(false);

        output.WriteLine("[green]+[/] Forgotten. Nothing can type at a running node until one is set again.");

        return CommandOutput.Success();
    }
}

/// <summary>Says whether a passphrase is set, never what it is.</summary>
[Description("Say whether the dashboard can be given permission to type at a running node.")]
[CommandMeta(CommandCategory.Safety, Intent = "attach show passphrase whether node")]
public sealed class TeamAttachShowCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public TeamAttachShowCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var set = await new Attaching(_secrets).IsSetAsync(cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new { set, minutes = (int)Attaching.Lasts.TotalMinutes });

            return CommandOutput.Success();
        }

        // Whether, never what. The value never leaves the credential store.
        output.WriteLine(set
            ? "A passphrase is set. The dashboard asks for it once per session and forgets it after "
                + $"{Attaching.Lasts.TotalMinutes:0} minutes."
            : "No passphrase is set, so nothing can type at a running node. Set one with: "
                + "loadout team attach set --passphrase ...");

        return CommandOutput.Success();
    }
}

/// <summary>Says something to one node while it is still working.</summary>
/// <remarks>
/// The lead reads messages between its rounds, which is the right place for
/// "change the plan". This is the other one: a worker has gone the wrong way
/// and is spending money doing it, and waiting for its turn to come back means
/// waiting for exactly the spend somebody is trying to stop.
/// </remarks>
[Description("Say something to one node of a run while it is still working.")]
[CommandMeta(CommandCategory.Start, Intent = "say node tell steer live attach interrupt", Mutates = true)]
public sealed class TeamSayCommand : AsyncCommand<TeamSayCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public TeamSayCommand(IRunJournal journal, TimeProvider time, IAnsiConsole console)
    {
        _journal = journal;
        _time = time;
        _console = console;
    }

    public sealed class Settings : RunSettings
    {
        [CommandOption("--node <NODE>")]
        [Description("Which node, as the run names it.")]
        public string? Node { get; init; }

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

        if (settings.Node is not { Length: > 0 } node)
        {
            return output.Fail("Say which with --node.", ExitCode.InvalidArguments);
        }

        if (settings.Message is not { Length: > 0 } message)
        {
            return output.Fail("Say what with --message.", ExitCode.InvalidArguments);
        }

        var (run, error) = RunControlling.Resolve(_journal, settings.Run);

        if (run is null)
        {
            return output.Fail(error!, ExitCode.ProjectNotFound);
        }

        var read = _journal.Summarise(run);

        if (read.Succeeded && read.Value is { } summary)
        {
            if (!summary.Running)
            {
                return output.Fail(
                    $"{run} has finished, so there is nobody to tell.", ExitCode.InvalidArguments);
            }

            if (!summary.Nodes.Any(one =>
                string.Equals(one.Node, node, StringComparison.OrdinalIgnoreCase)))
            {
                return output.Fail(
                    $"{run} has no node called {node}. It has: "
                    + string.Join(", ", summary.Nodes.Select(one => one.Node)),
                    ExitCode.InvalidArguments);
            }
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would tell {Shown.Safely(node)}. Nothing was written.");

            return CommandOutput.Success();
        }

        await NodeControl.SayAsync(
            _journal.DirectoryOf(run), node, message, _time.GetUtcNow(), cancellationToken)
            .ConfigureAwait(false);

        // Between the events it is already reading, which is as often as it
        // says anything. A node that has gone quiet is a node nobody can steer.
        output.WriteLine(
            $"[green]+[/] {Shown.Safely(node)} is told that the next time it says anything.");

        return CommandOutput.Success();
    }
}
