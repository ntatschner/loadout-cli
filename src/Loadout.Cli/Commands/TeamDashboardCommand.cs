using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Serves a page showing what the teams on this machine are doing.
/// </summary>
/// <remarks>
/// <para>
/// A run already has three views: the command that started it prints as it
/// goes, <c>team status</c> reads the journal back, and <c>team log --follow</c>
/// watches one. This is the fourth, and the one that answers "what is
/// happening" without somebody asking again every thirty seconds - and the
/// only one that shows every run at once.
/// </para>
/// <para>
/// It reads and nothing else. Approving a gate or stopping a run from a page
/// has to go through the same parser somebody would type at, which is the rule
/// the launcher already follows; and there is nothing yet on the other end to
/// receive it, because a run is a process its own command is holding. That
/// arrives with the daemon, which will serve this same page.
/// </para>
/// </remarks>
[Description("Serve a page on this machine showing what the team runs are doing. Reads only; nothing can be changed from it.")]
[CommandMeta(CommandCategory.Start, Intent = "team dashboard web page browser watch runs live")]
public sealed class TeamDashboardCommand : AsyncCommand<TeamDashboardCommand.Settings>
{
    private readonly IRunJournal _journal;
    private readonly Loadout.Core.Git.IGitManager _git;
    private readonly IAnsiConsole _console;
    private readonly IApplicationLauncher _opener;
    private readonly ReadingProfile _reading;
    private readonly IConfigurationService _configuration;
    private readonly IPlatformPaths _paths;
    private readonly IScheduleService _schedules;
    private readonly Loadout.Core.Tasks.ITaskService _tasks;
    private readonly Loadout.Core.Projects.IProjectService _projects;
    private readonly AccessibleMode _accessible;
    private readonly Loadout.Platform.Abstractions.ISpeech _speech;
    private readonly ICommandCatalogue _commands;
    private readonly ISecretProvider _secrets;
    private readonly TimeProvider _time;
    private readonly Loadout.Core.Teams.ITeamCatalogue _teams;
    private readonly Loadout.Core.Instructions.ISpecialistLibrary _library;
    private readonly Loadout.Core.Workspace.IWorkspaceManager _workspace;

    public TeamDashboardCommand(
        IRunJournal journal,
        Loadout.Core.Git.IGitManager git,
        IAnsiConsole console,
        IApplicationLauncher opener,
        ReadingProfile reading,
        IConfigurationService configuration,
        IPlatformPaths paths,
        IScheduleService schedules,
        Loadout.Core.Tasks.ITaskService tasks,
        Loadout.Core.Projects.IProjectService projects,
        AccessibleMode accessible,
        Loadout.Platform.Abstractions.ISpeech speech,
        ICommandCatalogue commands,
        ISecretProvider secrets,
        TimeProvider time,
        Loadout.Core.Teams.ITeamCatalogue teams,
        Loadout.Core.Instructions.ISpecialistLibrary library,
        Loadout.Core.Workspace.IWorkspaceManager workspace)
    {
        _teams = teams;
        _library = library;
        _workspace = workspace;
        _accessible = accessible;
        _speech = speech;
        _commands = commands;
        _secrets = secrets;
        _time = time;
        _journal = journal;
        _git = git;
        _console = console;
        _opener = opener;
        _reading = reading;
        _configuration = configuration;
        _paths = paths;
        _schedules = schedules;
        _tasks = tasks;
        _projects = projects;
    }

    /// <summary>
    /// Stops the server when whatever started it closes its input.
    /// </summary>
    /// <remarks>
    /// Only where the input is a pipe. A person at a terminal has Ctrl+C and
    /// has not asked for their keyboard to be read; a script has closed the
    /// pipe precisely to say it is finished.
    /// </remarks>
    /// <remarks>
    /// An input that cannot be read at all is not the same as one that closed.
    /// Started with no usable handle - detached, or from something that
    /// redirected input it never opened - reading it throws at once, and
    /// treating that as "my owner has finished" would stop the server before
    /// anybody could open the page. There is nothing to watch, so it watches
    /// nothing and waits to be stopped instead.
    /// </remarks>
    internal static async Task Ends(CancellationTokenSource stopping)
    {
        if (!Console.IsInputRedirected)
        {
            return;
        }

        await Task.Run(async () =>
        {
            try
            {
                await Console.In.ReadToEndAsync(stopping.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException)
            {
                // No readable input at all. Nothing closed, so nothing ends.
                return;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }

            await stopping.CancelAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Says out loud when the page is reachable from more than this machine.
    /// </summary>
    /// <remarks>
    /// The token is the whole of the protection. On loopback the only thing
    /// that can reach the port is already running as the person who started
    /// it; off loopback that stops being true, and whoever holds the address
    /// can answer a gate, stop a run and start a team. Said plainly, every
    /// time, rather than left in the documentation.
    /// </remarks>
    /// <summary>
    /// Sets which page the server will serve, and how much it may move.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page is rich unless this person has said something that means it
    /// should not be. What they said lives in one place, their own profile,
    /// and <see cref="Presenting" /> is where the reading of it is written
    /// down; this only chooses between that and a flag.
    /// </para>
    /// <para>
    /// A typed flag beats the profile because somebody typing one means it for
    /// this run. It is also the only way to see the other page without editing
    /// a preference, which is what an audit of either of them needs.
    /// </para>
    /// </remarks>
    internal static void Presented(DashboardServer server, string? view, AccessibleMode accessible)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(accessible);

        var profile = accessible.IsOn ? accessible.Profile : null;

        server.Look = view?.Trim().ToLowerInvariant() switch
        {
            "plain" => Presentation.Plain,
            "rich" => Presentation.Rich,
            _ => Presenting.For(profile),
        };

        // Not switched by the flag. Somebody who asked to see the rich page
        // has asked about its chrome, not for their own motion setting to be
        // overruled, and a page that moved because a flag was typed would be
        // the one thing that setting exists to stop.
        server.Motion = Presenting.Motion(profile);
        server.Colour = Presenting.Colour(profile);
    }

    /// <summary>
    /// Whether this person has asked to be spoken to.
    /// </summary>
    /// <remarks>
    /// Their own <c>show-speech</c> setting, which the launcher already uses.
    /// Somebody who has said "speak to me" has said it once and should not
    /// have to say it again per surface; somebody who has not said it is not
    /// going to be surprised by a machine that talks. It is off by default and
    /// off even under the screen-reader preset, and that does not change here.
    /// </remarks>
    internal static bool Speaking(AccessibleMode accessible)
    {
        ArgumentNullException.ThrowIfNull(accessible);

        // The profile is already resolved by the time it reaches here, preset
        // applied and anything written by hand on top of it.
        return accessible.IsOn
            && string.Equals(
                accessible.Profile.Display.Speech,
                "screen-reader",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Says that a run started here belongs to this window.
    /// </summary>
    /// <remarks>
    /// The daemon has no such caveat: it is meant to stay up, and a run it
    /// starts outlives the browser that asked for it. A <c>team dashboard</c>
    /// is a window somebody closes, and the run is hosted by this process. It
    /// is said when the server starts rather than discovered when a run stops.
    /// </remarks>
    internal static void Owns(CommandOutput output, DashboardServer server)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(server);

        if (server.Begin is null)
        {
            return;
        }

        output.WriteLine(
            "[dim]A team started from this page runs inside this command, so it stops when "
            + "this does. Use the daemon for one that should outlast the window.[/]");
    }

    internal static void Warn(CommandOutput output, DashboardServer server)
    {
        if (!server.Beyond)
        {
            return;
        }

        output.WriteLine(
            "[yellow]Anyone on this network who has that address can answer gates, stop runs "
            + "and start teams.[/] The token is the only thing in the way, and it is in the "
            + "address - so treat the address as the credential it is.");
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("The port to listen on. One the machine chooses when omitted.")]
        public int Port { get; init; }

        [CommandOption("--listen <ADDRESS>")]
        [Description(
            "The address to listen on. 127.0.0.1 by default, which is this machine only. "
            + "0.0.0.0 reaches the network you are on.")]
        public string Listen { get; init; } = "127.0.0.1";

        [CommandOption("--open")]
        [Description("Open the page in a browser once it is listening.")]
        public bool Open { get; init; }

        [CommandOption("--watch-only")]
        [Description(
            "Serve a page that cannot touch anything: no answering a gate, no holding or "
            + "stopping a run, no messaging the lead. For a screen in a corner.")]
        public bool WatchOnly { get; init; }

        [CommandOption("--view <VIEW>")]
        [Description(
            "rich or plain, for this run only. Without it the page follows your accessibility "
            + "profile: plain under screen-reader and low-vision, rich otherwise.")]
        public string? View { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        using var server = new DashboardServer(_journal, _git);

        // The same art the daemon draws with, for the same reason: this is the
        // page people actually open to look at the office, and a dashboard that
        // drew squares while the daemon drew people would be two dashboards.
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var office = OfficeArt.Chosen(_paths, machine.Value?.Teams.OfficeSet);

        server.OfficeRoot = office.Root;
        server.OfficeSet = office.Set;
        server.WaitingSet = OfficeArt.Chosen(_paths, machine.Value?.Teams.WaitingSet).Set;

        // Which of the two pages this is. The flag wins for one run, then the
        // profile - the same order as everything else here, and the same order
        // the accessible mode itself resolves in.
        Presented(server, settings.View, _accessible);

        // A web page cannot detect a screen reader; this machine can, and
        // already has a channel to one. Handed over whatever the settings
        // say - the server reports what it found either way, because a page
        // that cannot tell "switched off" from "nothing here can speak"
        // cannot explain either.
        server.Voice = _speech;
        server.MaySpeak = Speaking(_accessible);

        // Reading only, like the waiting area has always been. It can show what
        // is queued; it cannot fire any of it.
        server.WaitingFor = token => WaitingRoom.ReadAsync(
            _schedules, _tasks, _projects, DateTimeOffset.UtcNow, token);

        // What there is to start, from the same loader 'team list' reads.
        // Offered even to a watch-only page: knowing which teams exist is a
        // read, and a page that cannot start one is still a page somebody is
        // reading to find out what this machine has.
        server.Choices = token => DashboardActions.OfferedAsync(
            _teams, _library, _workspace, _projects, token);

        if (!settings.WatchOnly)
        {
            // What every button on the page does, which until now only the
            // daemon could honour. The page drew "Hold it", "Stop it" and a box
            // for messaging the lead, and this server answered all of them with
            // "this server only reads" - while the documentation said it acts.
            // A page that offers a control it cannot honour is worse than one
            // that does not offer it.
            //
            // The token is the whole of the protection here, exactly as it is
            // for the daemon, and this one was started by somebody who is sitting
            // in front of it.
            server.Act = (action, token) => DashboardActions.RanAsync(
                _commands, _time, action, output, token);

            // And starting one, which is the same rule with a longer wait: the
            // page asks, this types "team run", and the parser decides whether
            // any of it means anything.
            server.Begin = (asking, token) => DashboardActions.BeganAsync(
                _commands, _time, asking, output, token);

            // And writing one. The start form was read as the way to make a
            // team - it asks for a name, what it is for and a project, which
            // is what making one looks like - so a name nobody had written was
            // typed into it and the refusal went to this terminal rather than
            // to the page. This is the thing that form looked like.
            server.Make = (asking, token) => DashboardActions.MadeAsync(
                _commands, _time, asking, output, token);

            // And arranging for one to happen again. A dashboard hosted by this
            // command does not outlive the window, so a schedule made here only
            // fires while the daemon is up - which the page says, rather than
            // leaving somebody to find out at 23:00.
            server.Plan = (asking, token) => DashboardActions.PlannedAsync(
                _commands, _time, asking, output, token);

            // And the second credential, which the dashboard's own token does
            // not grant: typing at a live node is not something the run offered
            // to have decided.
            server.Attach = new Attaching(_secrets, _time);

            // Hosted by this command, which somebody will close.
            server.Owns = true;
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                "[dim]Dry run: nothing was served.[/] A dashboard would listen on "
                + (settings.Port == 0 ? "a port this machine chose" : $"port {settings.Port}")
                + $", on {settings.Listen}, with a token in its address.");

            return CommandOutput.Success();
        }

        var started = server.Start(settings.Port, settings.Listen);

        if (started.Failed)
        {
            return output.Fail(started);
        }

        if (output.IsJson)
        {
            // For whatever started this: the address is the only thing worth
            // having, and it carries the token.
            output.WriteJson(new { address = server.Address, port = new Uri(server.Address).Port });
        }
        else
        {
            foreach (var address in server.Reachable())
            {
                output.WriteLine($"[bold]{Markup.Escape(address)}[/]");
            }

            output.WriteLine(server.Beyond
                ? "[dim]Anything it changes runs the command you would have typed.[/]"
                : "[dim]On this machine only, and only with that token. Anything it changes runs "
                    + "the command you would have typed.[/]");

            Owns(output, server);
            Warn(output, server);
            output.WriteLine(Console.IsInputRedirected
                ? "[dim]It stops when whatever started it closes its input.[/]"
                : "[dim]Press Ctrl+C to stop.[/]");
        }

        if (settings.Open && output.CanOpenAWindow)
        {
            // Asked for, never assumed. A command that opened a browser
            // because it felt like it is one somebody runs once.
            await _opener.OpenUrlAsync(server.Address, cancellationToken).ConfigureAwait(false);
        }

        _reading.Ring(_console);

        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // A server started by something other than a person ends when that
        // something lets go. The same contract the MCP server has, and the
        // reason it matters is not theoretical: the contract test that runs
        // every registered command ran this one, and a command that serves
        // until Ctrl+C held the whole suite for twenty minutes.
        var ending = Ends(stopping);

        try
        {
            await server.ListenAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C, or the input ending, which are the two ways this ends.
        }

        await ending.ConfigureAwait(false);

        return (int)ExitCode.Success;
    }
}
