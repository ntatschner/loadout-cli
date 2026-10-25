using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Files a person's conversation under the launch that started it. Run by
/// the agent's own hook when a conversation starts or resumes, never by a
/// person.
/// </summary>
/// <remarks>
/// <para>
/// The launch puts its id in the agent's environment (see
/// <see cref="SessionLinks"/>); anywhere else this does nothing. The hook's
/// input is read for the conversation id alone.
/// </para>
/// <para>
/// It prints nothing and always succeeds, for a stronger reason than the
/// other hooks: whatever a session-start hook prints is put in front of the
/// agent as context, and a failure is shown to the person as their session
/// opens.
/// </para>
/// </remarks>
[Description("Record which launch a conversation belongs to. Run by the agent's own hook, not by a person.")]
[CommandMeta(CommandCategory.Integration, Intent = "hook session start link conversation launch", Mutates = true)]
public sealed class HookSessionStartCommand : AsyncCommand<GlobalSettings>
{
    private readonly ILaunchLedger _ledger;
    private readonly IEnvironmentProvider _environment;
    private readonly IAnsiConsole _console;

    public HookSessionStartCommand(ILaunchLedger ledger, IEnvironmentProvider environment, IAnsiConsole console)
    {
        _ledger = ledger;
        _environment = environment;
        _console = console;
    }

    /// <summary>Where the hook's input is read from; the console's own unless a test says otherwise.</summary>
    internal TextReader? Input { get; init; }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var launch = _environment.GetVariable(SessionLinks.Variable);

        if (!SessionLinks.IsLaunchId(launch) || settings.Agent is not { Length: > 0 } agent
            || !SessionLinks.IsAgentSession(agent))
        {
            return CommandOutput.Success();
        }

        var session = await SessionLinks.ReadSessionIdAsync(
            Input ?? (Invocation.IsInputRedirected ? Console.In : null), cancellationToken).ConfigureAwait(false);

        if (!SessionLinks.IsAgentSession(session))
        {
            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            new CommandOutput(_console, settings).WriteLine(
                $"Would file conversation {session} under launch {launch}. Nothing was changed.");

            return CommandOutput.Success();
        }

        // The ledger swallows what it cannot write, so this cannot fail the
        // session opening.
        await _ledger.RecordLinkAsync(session!, agent, launch!, cancellationToken).ConfigureAwait(false);

        return CommandOutput.Success();
    }
}
