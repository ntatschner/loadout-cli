using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What is running right now, and whether it is doing anything.
/// </summary>
/// <remarks>
/// <para>
/// Passive. The registry says what was started and the agent's own transcript
/// says when it last wrote; nothing here attaches to a console, reads another
/// process or drives a terminal. Doing that on this machine once took out every
/// live session on it, and a monitor that can break what it watches is not one
/// worth having.
/// </para>
/// <para>
/// Best effort, like the session listing it sits beside and for the same
/// reason: neither transcript format is a published contract. A session whose
/// transcript cannot be found is reported as unseen rather than as idle, since
/// telling somebody their agent had stopped when it is working perfectly well
/// is the one answer here that would be worse than none.
/// </para>
/// </remarks>
[Description("Show the sessions running now, with how long they have been quiet.")]
[CommandMeta(CommandCategory.Start,
    Intent = "running live sessions now active idle what is going on")]
public sealed class SessionRunningCommand : AsyncCommand<SessionRunningCommand.Settings>
{
    private readonly ISessionRegistry _registry;
    private readonly ISessionHistoryService _sessions;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public SessionRunningCommand(
        ISessionRegistry registry,
        ISessionHistoryService sessions,
        IAnsiConsole console,
        TimeProvider time)
    {
        _registry = registry;
        _sessions = sessions;
        _console = console;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--idle-after <MINUTES>")]
        [Description("How long without a word counts as idle. Defaults to 5.")]
        public int IdleAfter { get; init; } = 5;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.IdleAfter <= 0)
        {
            return output.Fail("--idle-after has to be at least 1.", ExitCode.InvalidArguments);
        }

        var running = await _registry.ListAsync(cancellationToken).ConfigureAwait(false);

        if (running.Count == 0)
        {
            // An empty list rather than no output. A caller parsing this gets
            // valid JSON whatever the answer, where silence is something it has
            // to special-case — and the prose line is suppressed in JSON mode,
            // so without this the command printed nothing at all.
            if (output.IsJson)
            {
                output.WriteJson(Array.Empty<object>());
            }
            else
            {
                output.WriteLine("[dim]Nothing is running.[/]");
            }

            return CommandOutput.Success();
        }

        // One read of the transcripts for all of them, rather than one per
        // session: the readers scan a directory either way, and asking once is
        // the difference between this being instant and it being noticeable.
        var known = await _sessions
            .ListAsync(new SessionQuery(Limit: 100), cancellationToken)
            .ConfigureAwait(false);

        var now = _time.GetUtcNow();
        var idleAfter = TimeSpan.FromMinutes(settings.IdleAfter);

        var activity = running
            .Select(session => SessionMonitor.Describe(
                session, LastWrite(known.Value, session), now, idleAfter))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(activity.Select(a => new
            {
                launchId = a.Session.LaunchId,
                project = a.Session.ProjectSlug,
                a.Session.Agent,
                started = a.Session.StartedAt,
                elapsedSeconds = (long)a.Elapsed.TotalSeconds,
                quietSeconds = a.Quiet is { } quiet ? (long?)quiet.TotalSeconds : null,
                state = a.State.ToString().ToLowerInvariant(),
            }));

            return CommandOutput.Success();
        }

        foreach (var entry in activity)
        {
            output.WriteLine(
                $"{Markup.Escape(entry.Session.ProjectSlug),-18} "
                + $"{Markup.Escape(entry.Session.Agent),-8} "
                + $"{SessionMonitor.Spoken(entry.Elapsed),-9} "
                + $"{State(entry)}");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{activity.Count} running. Quiet times come from each agent's own "
            + "transcript; nothing here attaches to a session.[/]");

        return CommandOutput.Success();
    }

    private static string State(SessionActivity entry) => entry.State switch
    {
        SessionState.Working => "[green]working[/]",
        SessionState.Idle =>
            $"[yellow]idle[/] [dim]{SessionMonitor.Spoken(entry.Quiet ?? TimeSpan.Zero)}[/]",

        // Said as what it is. "Unseen" is a fact about this launcher's view;
        // "idle" would be a claim about somebody's agent that nothing supports.
        _ => "[dim]unseen[/]",
    };

    /// <summary>
    /// When the transcript for a running session last changed.
    /// </summary>
    /// <remarks>
    /// Matched on the directory the session is running in, because that is the
    /// one thing both sides record: the registry knows where it started the
    /// agent and the agent writes where it ran. Session identifiers would be
    /// better and are not available — the launcher does not learn the agent's
    /// own id for a session it started.
    /// </remarks>
    private static DateTimeOffset? LastWrite(
        IReadOnlyList<AgentSession>? sessions,
        RunningSession running)
    {
        if (sessions is null)
        {
            return null;
        }

        DateTimeOffset? newest = null;

        foreach (var session in sessions)
        {
            if (!string.Equals(session.Agent, running.Agent, StringComparison.OrdinalIgnoreCase)
                || !SameDirectory(session.Directory, running.WorkingDirectory))
            {
                continue;
            }

            if (newest is null || session.LastActive > newest)
            {
                newest = session.LastActive;
            }
        }

        return newest;
    }

    private static bool SameDirectory(string left, string right)
    {
        static string Tidy(string path) => path
            .Replace('/', Path.DirectorySeparatorChar)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(
            Tidy(left),
            Tidy(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
