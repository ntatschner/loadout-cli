using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using Loadout.Tui;
using Spectre.Console;

namespace Loadout.Cli.Commands;

/// <summary>
/// What a button on the dashboard does.
/// </summary>
/// <remarks>
/// <para>
/// Every one of them types the command a person would have typed and lets the
/// parser decide whether it means anything. Nothing here implements the
/// behaviour of answering a gate, holding a run or messaging a lead — there is
/// one implementation of each and it is the command, or there are two and one
/// of them drifts.
/// </para>
/// <para>
/// In its own class because two commands serve the page. The daemon has always
/// been able to act; <c>team dashboard</c> could not, so the page it serves
/// drew "Hold it", "Stop it" and a box for messaging the lead and the server
/// answered every one of them with "this server only reads". A page that offers
/// a control it cannot honour is worse than one that does not offer it.
/// </para>
/// </remarks>
internal static class DashboardActions
{
    /// <summary>
    /// Every verb the page can send.
    /// </summary>
    /// <remarks>
    /// Named here rather than inferred from the switch, because what a test
    /// needs to walk is the set the page actually uses — a verb in the switch
    /// that no button sends is dead, and a button sending one the switch does
    /// not have is a 400 nobody predicted. Both are worth failing on.
    /// </remarks>
    internal static readonly string[] Verbs =
    [
        "gate", "gates", "message", "stop", "forget", "pause", "resume", "name", "pr", "say",
    ];

    /// <summary>
    /// The command line one verb stands for, or an empty path for a verb that
    /// stands for nothing.
    /// </summary>
    /// <remarks>
    /// Its own function so a test can run every one of these against the real
    /// parser. It is not enough that the command exists: the options have to be
    /// ones it declares, and "forget" shipped for exactly as long as it took to
    /// press the button passing <c>--yes</c> to a command that never asks and
    /// therefore does not take it. Spectre rejected the line, the command never
    /// ran, and the page reported a general failure.
    /// </remarks>
    internal static (string Command, IReadOnlyList<string> Arguments) Maps(RunAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var (command, arguments) = action.Verb switch
        {
            "gates" or "gate" => ("team gate", Gate(action)),
            "message" => ("team message", new List<string> { action.Run, "--message", action.Message ?? string.Empty }),
            "stop" => ("team halt", [action.Run]),

            // No --yes: naming a run is agreeing to it, so the command does not
            // ask and does not take the option. The page asks, by name, before
            // it gets here.
            //
            // No --force either, which is the part that matters. A run still
            // going keeps its directory, because that is where its nodes read
            // the answers they are waiting on — and the command refuses it
            // whatever the page thinks it is looking at.
            "forget" => ("team runs remove", [action.Run]),

            "pause" => ("team halt", [action.Run, "--pause"]),
            "resume" => ("team halt", [action.Run, "--resume"]),

            // An empty name clears it, which is how the page offers "put it
            // back": there is one box, and emptying a box is what people do.
            "name" => ("team name", action.Room is { Length: > 0 } room
                ? [action.Run, "--room", room]
                : [action.Run, "--clear"]),

            "pr" => ("team pr", action.Node is { Length: > 0 } whose
                ? [action.Run, "--node", whose]
                : [action.Run]),

            "say" => ("team say", [
                action.Run,
                "--node", action.Node ?? string.Empty,
                "--message", action.Message ?? string.Empty,
            ]),
            _ => (string.Empty, []),
        };

        return (command, arguments);
    }

    /// <summary>Does what a button asked, by running the command it stands for.</summary>
    internal static async Task<OperationResult> RanAsync(
        ICommandCatalogue commands,
        TimeProvider time,
        RunAction action,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(output);

        var (command, arguments) = Maps(action);

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{action.Verb}' to do to a run.", ExitCode.InvalidArguments);
        }

        if (action.Verb == "message" && action.Message is not { Length: > 0 })
        {
            return OperationResult.Fail("Say something to say.", ExitCode.InvalidArguments);
        }

        // Said where whoever started the server can see it. A page that can
        // stop a run should not be able to stop one silently.
        output.WriteLine(
            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(action.Run)}");

        var code = await commands
            .RunAsync(command, [.. arguments, "--non-interactive"], ct)
            .ConfigureAwait(false);

        if (code == (int)ExitCode.Success)
        {
            return OperationResult.Ok();
        }

        // The words went to the terminal serving this page, and an exit code is
        // all that crosses back. Saying only "exit code 1" sends somebody to
        // that terminal to find out what happened, which is where they were
        // before the page could do any of this — so the ones worth naming are
        // named, and the rest says plainly where the reason is.
        return OperationResult.Fail(
            (ExitCode)code switch
            {
                ExitCode.ProjectNotFound =>
                    $"There is no run called '{action.Run}' on this machine any more.",
                ExitCode.PolicyViolation when action.Verb == "forget" =>
                    "That run has not finished. Stop it first: its nodes are still reading "
                    + "its directory for the answers they are waiting on.",
                ExitCode.PolicyViolation =>
                    "That was refused by a rule. The terminal serving this page has which one.",
                ExitCode.InvalidArguments =>
                    "That is not something this can be asked of that run.",
                _ => $"'{command}' ended with exit code {code}. The terminal serving this page "
                     + "has the reason.",
            },
            (ExitCode)code);
    }

    /// <summary>
    /// Starts a team, by typing the command somebody would have typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not awaited to the end, on purpose. A run takes minutes and an HTTP
    /// request that held open for one would time out long before it finished,
    /// so the answer is "started" and whatever happens next is written where
    /// the schedules write theirs.
    /// </para>
    /// <para>
    /// Not "started" immediately, though, which is the part that was wrong.
    /// Everything that decides whether a run can begin at all — the team
    /// existing, the project resolving, the tree being a repository — is
    /// settled in the first second, and reporting success before any of it had
    /// happened meant the page said "Team Test is starting. It appears in the
    /// list in a moment" while the terminal behind it said no such team. So it
    /// waits <see cref="Settles"/> for an early exit and reports that as the
    /// failure it is. A run still going by then has passed all of it.
    /// </para>
    /// </remarks>
    internal static async Task<OperationResult> BeganAsync(
        ICommandCatalogue commands,
        TimeProvider time,
        StartRequest asking,
        CommandOutput output,
        CancellationToken ct)
    {
        var arguments = new List<string> { asking.Team, asking.Goal };

        if (asking.Project is { Length: > 0 } project)
        {
            arguments.Add("--project");
            arguments.Add(project);
        }

        if (asking.Rounds is { } rounds and > 0)
        {
            arguments.Add("--rounds");
            arguments.Add(rounds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (asking.Autonomy is { Length: > 0 } autonomy)
        {
            arguments.Add("--autonomy");
            arguments.Add(autonomy);
        }

        arguments.Add("--non-interactive");

        output.WriteLine(
            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"team run {Markup.Escape(asking.Team)}"
            + (asking.Project is { Length: > 0 } on ? $" on {Markup.Escape(on)}" : string.Empty));

        // Started here and finished wherever it finishes: whatever it ends up
        // doing is written where the schedules write theirs.
        var running = Task.Run(
            async () =>
            {
                try
                {
                    var code = await commands.RunAsync("team run", arguments, ct).ConfigureAwait(false);

                    if (code != (int)ExitCode.Success)
                    {
                        output.WriteLine(
                            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                            + $"that run ended with exit code {code}.");
                    }

                    return code;
                }
                catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException)
                {
                    output.WriteLine(
                        $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] "
                        + $"that run could not be started: {Markup.Escape(ex.Message)}");

                    return (int)ExitCode.GeneralFailure;
                }
            },
            CancellationToken.None);

        var settled = await Task.WhenAny(running, Task.Delay(Settles, ct)).ConfigureAwait(false);

        if (settled != (Task)running)
        {
            return OperationResult.Ok();
        }

        var exit = await running.ConfigureAwait(false);

        if (exit == (int)ExitCode.Success)
        {
            // A whole run inside four seconds. Unusual and not wrong: a team
            // whose goal was already met ends this fast.
            return OperationResult.Ok();
        }

        // Written here rather than relayed, because what crosses back from the
        // command is an exit code and the words went to the console. Each one
        // names what to do next: a page that says only "that failed" sends
        // somebody to a terminal, which is where they were before.
        return OperationResult.Fail(
            (ExitCode)exit switch
            {
                ExitCode.ProjectNotFound =>
                    $"There is no team called '{asking.Team}', or no project called "
                    + $"'{asking.Project}'. Pick from the lists rather than typing.",
                ExitCode.RepositoryUnavailable =>
                    "That project's directory is not a Git repository. Choose a project: without "
                    + "one, a run works wherever this dashboard was started, which is rarely a repository.",
                ExitCode.PolicyViolation =>
                    "That run was refused before it spent anything. The terminal serving this page "
                    + "has the rule it broke.",
                ExitCode.InvalidArguments =>
                    "That is not a run this can start — a template cannot be run, only copied.",
                _ => $"That run ended immediately, with exit code {exit}. The terminal serving this "
                     + "page has the reason.",
            },
            (ExitCode)exit);
    }

    /// <summary>
    /// How long a start is watched before it is called started.
    /// </summary>
    /// <remarks>
    /// Long enough for a refusal, short enough that nobody thinks the page has
    /// hung. Everything that refuses a run — an unknown team, an unregistered
    /// project, a directory that is not a repository, a template — is decided
    /// before the first agent is launched and well inside this.
    /// </remarks>
    private static readonly TimeSpan Settles = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Writes a team, by typing the command somebody would have typed.
    /// </summary>
    /// <remarks>
    /// Awaited, unlike a run: writing a file takes milliseconds, and the whole
    /// point of this is that somebody finds out on the page whether it worked.
    /// The failure a run had — the refusal going to a terminal behind the
    /// browser — is the one thing this must not repeat.
    /// </remarks>
    internal static async Task<OperationResult> MadeAsync(
        ICommandCatalogue commands,
        TimeProvider time,
        MakeRequest asking,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentNullException.ThrowIfNull(output);

        var arguments = new List<string> { asking.Name };

        if (asking.From is { Length: > 0 } from)
        {
            arguments.Add("--from");
            arguments.Add(from);
        }

        if (asking.Project is { Length: > 0 } project)
        {
            arguments.Add("--for-this-project");
            arguments.Add("--project");
            arguments.Add(project);
        }

        arguments.Add("--non-interactive");

        output.WriteLine(
            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"team new {Markup.Escape(asking.Name)}"
            + (asking.From is { Length: > 0 } copied ? $" from {Markup.Escape(copied)}" : string.Empty));

        var code = await commands.RunAsync("team new", arguments, ct).ConfigureAwait(false);

        // The exit code is what crosses the process boundary, so the sentences
        // are written here rather than relayed. Each names the next thing to
        // do, because a page that says only "that failed" sends somebody to a
        // terminal to find out why — which is where they were before this
        // existed.
        return code == (int)ExitCode.Success
            ? OperationResult.Ok()
            : OperationResult.Fail(
                (ExitCode)code switch
                {
                    ExitCode.InvalidArguments =>
                        $"'{asking.Name}' is either taken or not a name a team can have. "
                        + "Lowercase letters, digits and hyphens, and not one already in the list.",
                    ExitCode.ProjectNotFound =>
                        "That project, or the team to copy, is not one this machine knows about.",
                    ExitCode.WorkspaceSyncFailed =>
                        "There is no workspace to write a team into. Run 'loadout setup' first.",
                    _ => $"'team new' ended with exit code {code}. The terminal serving this page "
                         + "has the reason.",
                },
                (ExitCode)code);
    }

    /// <summary>
    /// Arranges for a run to happen again, by typing the command somebody would
    /// have typed.
    /// </summary>
    /// <remarks>
    /// Awaited, like writing a team. Both verbs write a small file and come
    /// back, and the whole point of doing this from the page is finding out on
    /// the page whether it took.
    /// </remarks>
    internal static async Task<OperationResult> PlannedAsync(
        ICommandCatalogue commands,
        TimeProvider time,
        ScheduleAction asking,
        CommandOutput output,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(asking);
        ArgumentNullException.ThrowIfNull(output);

        var (command, arguments) = Plans(asking);

        if (command.Length == 0)
        {
            return OperationResult.Fail(
                $"There is nothing called '{asking.Verb}' to do to a schedule.",
                ExitCode.InvalidArguments);
        }

        output.WriteLine(
            $"[dim]{time.GetUtcNow().ToLocalTime():HH:mm}[/] from the dashboard: "
            + $"{Markup.Escape(command)} {Markup.Escape(asking.Name)}");

        var code = await commands.RunAsync(command, arguments, ct).ConfigureAwait(false);

        if (code == (int)ExitCode.Success)
        {
            return OperationResult.Ok();
        }

        return OperationResult.Fail(
            (ExitCode)code switch
            {
                ExitCode.InvalidArguments =>
                    "A schedule needs how often it runs, a time of day, or something to watch "
                    + "for - and at least five minutes between runs. It also cannot be manual: "
                    + "nobody is watching when it fires.",
                ExitCode.ProjectNotFound =>
                    "That team, project or schedule is not one this machine knows about.",
                _ => $"'{command}' ended with exit code {code}. The terminal serving this page "
                     + "has the reason.",
            },
            (ExitCode)code);
    }

    /// <summary>
    /// The command line a schedule verb stands for.
    /// </summary>
    /// <remarks>
    /// Its own function for the same reason <see cref="Maps"/> is: a test runs
    /// every one of these against the real parser, because it is not enough
    /// that the command exists - every option in the line has to be one it
    /// declares.
    /// </remarks>
    internal static (string Command, IReadOnlyList<string> Arguments) Plans(ScheduleAction asking)
    {
        ArgumentNullException.ThrowIfNull(asking);

        if (string.Equals(asking.Verb, "remove", StringComparison.Ordinal))
        {
            return ("team schedule remove", [asking.Name, "--non-interactive"]);
        }

        if (!string.Equals(asking.Verb, "add", StringComparison.Ordinal))
        {
            return (string.Empty, []);
        }

        // The three arguments are positional and required, so they go first and
        // in order. An empty one is still passed: the command says which is
        // missing far better than a line that silently shifts the next
        // argument into its place.
        var arguments = new List<string>
        {
            asking.Name,
            asking.Team ?? string.Empty,
            asking.Goal ?? string.Empty,
        };

        foreach (var (option, value) in new[]
        {
            ("--project", asking.Project),
            ("--every", asking.Every),
            ("--at", asking.At),
            ("--on", asking.On),
            ("--autonomy", asking.Autonomy),
        })
        {
            if (value is { Length: > 0 })
            {
                arguments.Add(option);
                arguments.Add(value);
            }
        }

        arguments.Add("--non-interactive");

        return ("team schedule add", arguments);
    }

    /// <summary>
    /// What there is to start, and to copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same loader <c>team list</c> uses, with the same layering, so the
    /// page and the command line cannot disagree about which teams exist. What
    /// the page does with it is offer them; deciding is still the parser's,
    /// exactly as before.
    /// </para>
    /// <para>
    /// Loaded without a project, because a dashboard is not standing in any one
    /// repository. That means a team written under a single project does not
    /// appear until the page names that project — which is honest: it is not a
    /// team this machine can run against anything else.
    /// </para>
    /// </remarks>
    internal static async Task<Choosable> OfferedAsync(
        ITeamCatalogue teams,
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(teams);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(projects);

        var root = workspace.IsAvailable() ? workspace.LocalPath : null;

        var registered = await projects.ListAsync(ct).ConfigureAwait(false);

        var slugs = registered.Succeeded
            ? registered.Value!.Select(one => one.Entry.Slug).OrderBy(one => one, StringComparer.Ordinal).ToList()
            : [];

        var specialists = await library.LoadAsync(root, null, ct).ConfigureAwait(false);
        var catalogue = await teams.LoadAsync(root, null, specialists, ct).ConfigureAwait(false);

        var offered = catalogue.Teams.Values
            .OrderBy(team => team.Name, StringComparer.Ordinal)
            .Select(team => new ChoosableTeam(
                team.Name,
                team.Description,
                Whose(catalogue.Origin(team.Name)),
                team.Template,

                // Whatever the catalogue said was wrong with this one, in one
                // string. A team with a finding against it can still be picked
                // — 'team run' refuses it and says why — but somebody choosing
                // between eight of them should not have to find that out by
                // spending a round on it.
                catalogue.Findings
                    .Where(finding => string.Equals(finding.Rule, team.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(finding => finding.Detail)
                    .FirstOrDefault(),
                team.Nodes.Count,
                team.Rules.Autonomy))
            .ToList();

        var here = (await projects
            .ResolveFromDirectoryAsync(Directory.GetCurrentDirectory(), ct)
            .ConfigureAwait(false)) is { Succeeded: true } found
            ? found.Value!.Entry.Slug
            : null;

        return new Choosable(offered, slugs, here);
    }

    /// <summary>Where a team came from, for the page, in the words the listing uses.</summary>
    private static string Whose(SpecialistOrigin origin) => origin switch
    {
        SpecialistOrigin.Pack => "from a pack",
        SpecialistOrigin.Workspace => "yours",
        SpecialistOrigin.Project => "this project's",
        _ => "ships with Loadout",
    };

    /// <summary>The command line for answering one gate.</summary>
    private static List<string> Gate(RunAction action)
    {
        var arguments = new List<string> { action.Run };

        if (action.Gate is { Length: > 0 } gate)
        {
            arguments.Add("--gate");
            arguments.Add(gate);
        }

        arguments.Add("--answer");
        arguments.Add(action.Answer ?? "no");
        arguments.Add("--by");
        arguments.Add("dashboard");

        if (action.Instead is { Length: > 0 } instead)
        {
            arguments.Add("--instead");
            arguments.Add(instead);
        }

        if (action.Reason is { Length: > 0 } reason)
        {
            arguments.Add("--reason");
            arguments.Add(reason);
        }

        return arguments;
    }
}
