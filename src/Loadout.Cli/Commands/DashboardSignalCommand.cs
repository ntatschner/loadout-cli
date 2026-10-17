using System.ComponentModel;
using System.Text.Json;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions.Hosting;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What the hook says.</summary>
public sealed class DashboardSignalSettings : GlobalSettings
{
    [CommandArgument(0, "<STATE>")]
    [Description("waiting, approval or turn.")]
    public string State { get; init; } = string.Empty;
}

/// <summary>
/// Records, for Studio, that the agent in a hosted session is waiting for its
/// person. Run by the agent's own hook, never by a person.
/// </summary>
/// <remarks>
/// <para>
/// The launch installs the hook only in a session the daemon hosts, which it
/// marks with <see cref="SessionSignals.Variable"/>; anywhere else this does
/// nothing. The agent's hook input is read for its conversation id alone,
/// up to <see cref="SessionSignals.MaximumInput"/>, and nothing else in it is
/// kept: its message can quote what the agent was about to run.
/// </para>
/// <para>
/// It prints nothing and always succeeds. A hook that prints can have its
/// words put in front of the agent, and one that fails is reported to the
/// person in the middle of their session, for a status light they may not be
/// looking at.
/// </para>
/// </remarks>
[Description("Record that the agent in a Studio session is waiting. Run by the agent's own hook, not by a person.")]
[CommandMeta(CommandCategory.Integration, Intent = "studio hook waiting approval notification signal", Mutates = true)]
public sealed class DashboardSignalCommand : AsyncCommand<DashboardSignalSettings>
{
    private readonly IPlatformPaths _paths;
    private readonly IEnvironmentProvider _environment;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public DashboardSignalCommand(IPlatformPaths paths, IEnvironmentProvider environment, IAnsiConsole console, TimeProvider time)
    {
        _paths = paths;
        _environment = environment;
        _console = console;
        _time = time;
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

        var agentSession = await AgentSessionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SessionSignals.Write(SessionSignals.Directory(_paths.Paths.State), id!, settings.State, agentSession, _time.GetUtcNow());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Studio shows the session as working or idle instead, which is
            // what it shows for an agent with no hooks at all.
        }

        return CommandOutput.Success();
    }

    /// <summary>The <c>session_id</c> in the hook's input, or null for none or none readable.</summary>
    private async Task<string?> AgentSessionAsync(CancellationToken ct)
    {
        var reader = Input ?? (Console.IsInputRedirected ? Console.In : null);

        if (reader is null)
        {
            return null;
        }

        var buffer = new char[SessionSignals.MaximumInput];
        var read = 0;

        while (read < buffer.Length)
        {
            var got = await reader.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);

            if (got == 0)
            {
                break;
            }

            read += got;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, read));

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("session_id", out var session)
                && session.ValueKind == JsonValueKind.String
                ? session.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
