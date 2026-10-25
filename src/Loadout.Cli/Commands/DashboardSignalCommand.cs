using System.ComponentModel;
using Loadout.Core.Configuration;
using Loadout.Core.Sessions;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions.Hosting;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Common;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What the hook says.</summary>
public sealed class DashboardSignalSettings : GlobalSettings
{
    [CommandArgument(0, "<STATE>")]
    [Description("waiting, approval, working or turn.")]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// Records, for Studio, that the agent in a hosted session is waiting for its
/// person, or working again. Run by the agent's own hook, never by a person.
/// </summary>
/// <remarks>
/// <para>
/// The launch installs the hook only in a session the daemon hosts, which it
/// marks with <see cref="SessionSignals.Variable"/>; anywhere else this does
/// nothing. The agent's hook input is read for its conversation id and its
/// transcript's path alone, up to <see cref="SessionSignals.MaximumInput"/>,
/// and nothing else in it is kept: its message can quote what the agent was
/// about to run.
/// </para>
/// <para>
/// <c>waiting</c> is checked against the end of the transcript first. An
/// agent that stopped to wait on work it left running in the background is
/// recorded as <c>background</c>, which Studio shows without asking for
/// anybody, until its wakeup is due or the work has been silent for
/// studio-background-limit. All that is kept from the transcript is that
/// answer and that time. A transcript that cannot be read leaves it
/// <c>waiting</c>, which is the side to be wrong on.
/// </para>
/// <para>
/// It prints nothing and always succeeds. A hook that prints can have its
/// words put in front of the agent, and one that fails is reported to the
/// person in the middle of their session, for a status light they may not be
/// looking at.
/// </para>
/// </remarks>
[Description("Record that the agent in a Studio session is waiting or working. Run by the agent's own hook, not by a person.")]
[CommandMeta(CommandCategory.Integration, Intent = "studio hook waiting approval working background notification signal", Mutates = true)]
public sealed class DashboardSignalCommand : AsyncCommand<DashboardSignalSettings>
{
    private readonly IPlatformPaths _paths;
    private readonly IEnvironmentProvider _environment;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;
    private readonly IConfigurationService _configuration;

    public DashboardSignalCommand(
        IPlatformPaths paths,
        IEnvironmentProvider environment,
        IAnsiConsole console,
        TimeProvider time,
        IConfigurationService configuration)
    {
        _paths = paths;
        _environment = environment;
        _console = console;
        _time = time;
        _configuration = configuration;
    }

    /// <summary>Where the hook's input is read from; the console's own unless a test says otherwise.</summary>
    internal TextReader? Input { get; init; }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        DashboardSignalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        // Taken before anything is read: the hooks that say 'working' run in
        // the background, and a signal is never replaced by one that started
        // earlier than it.
        var at = _time.GetUtcNow();
        var id = _environment.GetVariable(SessionSignals.Variable);

        if (!SessionSignals.IsSessionId(id) || !SessionSignals.States.Contains(settings.State, StringComparer.Ordinal))
        {
            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            new CommandOutput(_console, settings).WriteLine($"Would record that session {id} is '{settings.State}'. Nothing was changed.");

            return CommandOutput.Success();
        }

        var (agentSession, transcript) = await SessionLinks.ReadHookInputAsync(
            Input ?? (Invocation.IsInputRedirected ? Console.In : null), cancellationToken).ConfigureAwait(false);

        var state = settings.State;
        DateTimeOffset? until = null;

        if (state == "waiting" && transcript is not null)
        {
            var limit = await LimitAsync(cancellationToken).ConfigureAwait(false);

            // Work already silent past the limit does not count at all.
            if (BackgroundWork.Read(transcript, at, limit) is { Outstanding: true } reading)
            {
                state = "background";
                until = reading.WakeupDue is { } due ? due + SessionSignals.WakeupGrace : at + limit;
            }
        }

        // The hook names its own agent with the global --agent, which is how
        // the daemon knows whose command passes a new name on.
        try
        {
            SessionSignals.Write(SessionSignals.Directory(_paths.Paths.State), id!, state, agentSession, at, until, settings.Agent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Studio shows the session as working or idle instead, which is
            // what it shows for an agent with no hooks at all.
        }

        return CommandOutput.Success();
    }

    /// <summary>studio-background-limit, or its default where it is unset or the configuration cannot be read.</summary>
    private async Task<TimeSpan> LimitAsync(CancellationToken ct)
    {
        var loaded = await _configuration.LoadConfigAsync(ct).ConfigureAwait(false);

        return loaded.Succeeded && loaded.Value!.Studio.BackgroundLimit is { } minutes
            ? TimeSpan.FromMinutes(minutes)
            : SessionSignals.BackgroundLimit;
    }
}
