using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Common;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Options shared by listing sessions and resuming one.</summary>
public class SessionSettings : GlobalSettings
{
    [CommandOption("--project <SLUG>")]
    [Description("Only sessions belonging to this registered project.")]
    public string? Project { get; init; }

    [CommandOption("--all")]
    [Description("Every project, rather than only the one for the current directory.")]
    public bool All { get; init; }

    [CommandOption("--limit <COUNT>")]
    [Description("How many sessions to consider. Defaults to 20.")]
    public int Limit { get; init; } = 20;
}

/// <summary>
/// Lists recent agent conversations.
/// <para>
/// The agents each keep their own history, and neither offers a way to see
/// across both or to say which project a session belonged to. That is the gap
/// this fills: one list, newest first, attributed to registered projects.
/// </para>
/// </summary>
[Description("List recent agent sessions, newest first.")]
[CommandMeta(CommandCategory.Start, Intent = "history recent previous what was i doing")]
public sealed class SessionListCommand : AsyncCommand<SessionListCommand.Settings>
{
    private readonly ISessionResumption _sessions;
    private readonly SessionScope _scope;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;
    private readonly Core.Configuration.IConfigurationService _configuration;

    public SessionListCommand(
        ISessionResumption sessions,
        SessionScope scope,
        IAnsiConsole console,
        TimeProvider time,
        Core.Configuration.IConfigurationService configuration)
    {
        _sessions = sessions;
        _scope = scope;
        _console = console;
        _time = time;
        _configuration = configuration;
    }

    /// <summary>What listing takes beyond what resuming does: the query, over the sessions --limit considers.</summary>
    public sealed class Settings : SessionSettings
    {
        [CommandOption("--filter <QUERY>")]
        [Description("Keep only what the query says, the query every list takes, such as \"agent:codex login\". One starting with a minus goes as --filter=-agent:claude.")]
        public string? Filter { get; init; }

        [CommandOption("--sort <FIELDS>")]
        [Description("Sort by these fields, commas between: id, title, project, agent, branch, active. Reversed with a minus: --sort=-active.")]
        public string? Sort { get; init; }

        [CommandOption("--saved")]
        [Description("Use the query saved for this list in Studio. Without --json it is used anyway; --filter sets it aside.")]
        public bool Saved { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var query = await _scope.QueryAsync(settings).ConfigureAwait(false);

        var result = await _sessions.ListAsync(query, ct: cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var config = await _configuration.LoadConfigAsync(cancellationToken).ConfigureAwait(false);
        var (filter, fromSaved) = ListOptions.Filter(
            settings.Filter, Core.Lists.ListViews.Saved(config.Value?.Studio, "sessions"), settings.Saved, output.IsJson);
        var queried = ListOptions.Apply(
            Core.Lists.SessionFields.All, filter, settings.Sort, [.. result.Value!.Select(r => r.Session)], _time.GetUtcNow());

        if (queried.Failed)
        {
            return output.Fail(queried);
        }

        // Back to what each kept session was launched for, in the query's order.
        var launches = result.Value!.ToDictionary<ResumableSession, AgentSession>(r => r.Session, ReferenceEqualityComparer.Instance);
        var resumable = queried.Value!.Select(session => launches[session]).ToList();
        var sessions = queried.Value!;

        if (output.IsJson)
        {
            // launchedFor and running as Studio's history has them, so a
            // script sees what the page does.
            output.WriteJson(resumable.Select(r => new
            {
                agent = r.Session.Agent,
                id = r.Session.SessionId,
                title = r.Session.Title,
                project = r.Session.ProjectSlug,
                directory = r.Session.Directory,
                branch = r.Session.Branch,
                lastActive = r.Session.LastActive,
                transcript = r.Session.TranscriptPath,
                launchedFor = r.LaunchedFor is { } launch
                    ? new
                    {
                        launch = launch.Id,
                        task = launch.Task,
                        taskId = launch.TaskId,
                        mode = launch.Mode,
                        profile = launch.Profile,
                        worktree = launch.Worktree,
                    }
                    : null,
                running = r.Running.Wire,
            }));

            return CommandOutput.Success();
        }

        // A list someone else's filter has narrowed says so, so it never
        // looks short for no reason.
        if (fromSaved)
        {
            output.WriteLine($"[dim]Filtered by the saved query {Markup.Escape(filter!)}. --filter sets it aside.[/]");
        }

        if (sessions.Count == 0)
        {
            output.WriteLine(result.Value!.Count > 0
                ? "[dim]Nothing matches the filter.[/]"
                : query.ProjectSlug is { Length: > 0 } slug
                ? $"[dim]No recorded sessions for {slug.EscapeMarkup()}.[/]"
                : "[dim]No recorded agent sessions were found.[/]");

            return CommandOutput.Success();
        }

        // Written as fixed-width lines rather than a table. A table reflows
        // its columns to fit, so one long title turns every other row into
        // three wrapped ones, and a list that cannot be scanned by eye is not
        // worth printing.
        var width = _console.Profile.Width;

        foreach (var session in sessions)
        {
            var (when, agent, project, what) = SessionDisplay.Columns(session, width);

            output.WriteLine(
                $"[dim]{when.EscapeMarkup()}[/] [dim]{agent.EscapeMarkup()}[/] "
                + (session.ProjectSlug is { Length: > 0 }
                    ? $"[cyan]{project.EscapeMarkup()}[/]"
                    : $"[dim]{project.EscapeMarkup()}[/]")
                + $" {what.EscapeMarkup()}");
        }
        output.WriteBlankLine();
        output.WriteLine("[dim]Pick one up with loadout resume[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Options for resuming.</summary>
public sealed class ResumeSettings : SessionSettings
{
    [CommandArgument(0, "[SESSION]")]
    [Description("Session id to resume. Omit to choose from a list.")]
    public string? Session { get; init; }

    [CommandOption("--last")]
    [Description("Resume the most recent session without asking.")]
    public bool Last { get; init; }

    [CommandOption("--task <TASK>")]
    [Description("What the session is for now. Otherwise what it was launched for is carried over.")]
    public string? Task { get; init; }

    [CommandOption("--task-id <ID>")]
    [Description("A recorded task the session is for now. Its title is the task unless --task is given.")]
    public string? TaskId { get; init; }

    [CommandOption("--mode <MODE>")]
    [Description("Posture: advise, investigate, implement or review. Otherwise carried over.")]
    public string? Mode { get; init; }

    [CommandOption("--worktree <NAME>")]
    [Description("The worktree to reopen it in. Otherwise carried over.")]
    public string? Worktree { get; init; }

    [CommandOption("--fresh")]
    [Description("Carry nothing over from its launch: no task, mode, profile or worktree but those given here.")]
    public bool Fresh { get; init; }
}

/// <summary>
/// Picks a previous conversation and starts the agent back up in it.
/// <para>
/// Both agents can already resume, and both make you find the session
/// yourself — Claude by its own picker inside a running session, Codex by a
/// subcommand. Neither knows about projects. Resuming from here goes through
/// the launcher instead, so the workspace is synchronised, the context is
/// recompiled and the session reopens with the project it belonged to.
/// </para>
/// <para>
/// Choosing, carry-over and the running check are
/// <see cref="ISessionResumption"/>'s, which Studio asks too, so the two
/// cannot describe one conversation differently. This keeps the picker and
/// what is said.
/// </para>
/// </summary>
[Description("Resume a previous agent session.")]
[CommandMeta(CommandCategory.Start, Intent = "continue carry on last session again")]
public sealed class ResumeCommand : AsyncCommand<ResumeSettings>
{
    /// <summary>How many conversations a named id is looked for among: as many as the history reads.</summary>
    private const int NamedLookup = 500;

    private readonly ISessionResumption _resumption;
    private readonly SessionScope _scope;
    private readonly IAgentLauncher _launcher;
    private readonly IHeadlessTranscripts _transcripts;
    private readonly IAnsiConsole _console;

    private readonly ReadingProfile _reading;

    public ResumeCommand(
        ISessionResumption resumption,
        SessionScope scope,
        IAgentLauncher launcher,
        IHeadlessTranscripts transcripts,
        IAnsiConsole console,
        ReadingProfile reading)
    {
        _resumption = resumption;
        _scope = scope;
        _launcher = launcher;
        _transcripts = transcripts;
        _console = console;
        _reading = reading;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ResumeSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var query = await _scope.QueryAsync(settings).ConfigureAwait(false);

        ResumableSession? chosen;

        if (settings.Session is { Length: > 0 } named)
        {
            // Named outright, so a team node's conversation is one somebody
            // means to take over. It is put back where the agent reads it,
            // which is also where the listing below looks, and the listing
            // is asked not to hide it. Only a picker leaves nodes out. Not on
            // a dry run, which moves nothing: a finished node then reads as
            // not found, which is less than the whole truth and changes
            // nothing.
            if (!settings.DryRun)
            {
                _transcripts.Restore("claude", named);
            }

            // Asked for by name, so a name that matches nothing is an error
            // and not a shrug. This used to fall in with the picker's null,
            // which means "changed my mind" — so 'resume nonsense' printed
            // nothing, exited zero, and looked exactly like success. It is
            // how the launcher's Resume button appeared to do nothing at all.
            //
            // Looked for as far back as the history reads, not only among the
            // newest twenty a list shows: an id somebody has is one they mean.
            var found = await _resumption.FindAsync(
                query with { IncludeHeadless = true, Limit = Math.Max(query.Limit, NamedLookup) },
                named,
                ct: cancellationToken).ConfigureAwait(false);

            if (found.Failed)
            {
                return output.Fail(found);
            }

            chosen = found.Value!;
        }
        else
        {
            var listed = await _resumption.ListAsync(query, ct: cancellationToken).ConfigureAwait(false);

            if (listed.Failed)
            {
                return output.Fail(listed);
            }

            if (listed.Value!.Count == 0)
            {
                return output.Fail(
                    "No recorded agent sessions were found to resume.",
                    ExitCode.ProjectNotFound);
            }

            chosen = Choose(listed.Value!, settings, output);

            if (chosen is null)
            {
                // Backing out of the picker is a decision, not a failure.
                return CommandOutput.Success();
            }
        }

        var overrides = new ResumeOverrides(
            settings.Task, settings.TaskId, settings.Mode, settings.Profile, settings.Worktree, settings.Fresh);

        var planned = await _resumption.PlanAsync(chosen, overrides, cancellationToken).ConfigureAwait(false);

        if (planned.Failed)
        {
            return output.Fail(planned);
        }

        var values = planned.Value!;

        output.WriteLine(
            $"Resuming [cyan]{values.ProjectSlug.EscapeMarkup()}[/] with {values.Agent.EscapeMarkup()}: "
            + $"{chosen.Session.Label.EscapeMarkup()}");

        if (chosen.Running.Kind != RunningKind.None)
        {
            // Said, not refused: the check can be wrong, and the person may
            // mean to have it open twice. Studio asks first; a terminal is
            // where somebody typed the id on purpose.
            output.WriteLine(
                "[yellow]It looks to be running already. A second copy writes to the same conversation.[/]");
        }

        // What the session was launched for, so it reopens with the same
        // specialists. Resuming recompiles the context, and until this was
        // read back a resumed session got the no-task, no-mode set — a
        // different one from the session it claimed to be continuing.
        if (values.Carried.Count > 0 && !overrides.ChangesPurpose)
        {
            output.WriteLine(
                $"[dim]Carrying over from its launch: {string.Join(", ", values.Carried).EscapeMarkup()}[/]");
        }

        foreach (var notice in values.Notices)
        {
            output.WriteLine($"[yellow]{notice.EscapeMarkup()}[/]");
        }

        var launch = await _launcher.LaunchAsync(new LaunchRequest(
            values.ProjectSlug,
            values.Agent,
            Offline: settings.Offline,
            NoSync: settings.NoSync,
            Worktree: values.Worktree,
            Profile: values.Profile,
            Environment: settings.Environment,
            ResumeSessionId: values.SessionId,
            Task: values.Task,
            Mode: values.Mode,
            DryRun: settings.DryRun,
            TaskId: values.TaskId)).ConfigureAwait(false);

        if (launch.Failed)
        {
            return output.Fail(launch);
        }

        // The same report 'launch' writes, for the same reason it exists: three
        // commands start an agent and they cannot be allowed to describe one
        // differently. It carries the dry run's plan as well as the warnings.
        LaunchReport.Write(output, launch.Value!, settings.DryRun);

        // The agent's own exit status is the command's, per spec section 40.
        return launch.Value!.AgentExitCode;
    }

    /// <summary>
    /// Works out which session was meant: the most recent, or whichever one is
    /// picked from the list.
    /// </summary>
    private ResumableSession? Choose(
        IReadOnlyList<ResumableSession> sessions,
        ResumeSettings settings,
        CommandOutput output)
    {
        if (settings.Last)
        {
            return sessions[0];
        }

        if (settings.NonInteractive || Invocation.IsInputRedirected || Invocation.IsOutputRedirected)
        {
            // Spec section 37: no menu where nobody can answer it.
            output.WriteLine(
                "[yellow]A session must be named, or --last used, when there is no terminal to ask in.[/]");

            return null;
        }

        // The picker leaves room for the selection marker it draws itself.
        var width = Math.Max(40, _console.Profile.Width - 4);

        var choices = new List<SessionChoice>(sessions.Select(s => new SessionChoice(s.Session)))
        {
            SessionChoice.Cancel,
        };

        var chosen = _reading.Ask(
            _console, "Which session?", choices, choice => choice.Render(width), pageSize: 15);

        return chosen.Session is null
            ? null
            : sessions.First(s => ReferenceEquals(s.Session, chosen.Session));
    }
}

/// <summary>An entry in the resume picker, including the way out of it.</summary>
internal sealed record SessionChoice(AgentSession? Session)
{
    /// <summary>The last entry, so the picker can always be left without choosing.</summary>
    internal static readonly SessionChoice Cancel = new((AgentSession?)null);

    internal string Render(int width)
    {
        if (Session is null)
        {
            return "Cancel";
        }

        // Escaped because the picker parses what this returns as markup, and a
        // session is named after the first thing said in it — which the person
        // who said it was writing as prose, not as markup. A conversation that
        // opened with '[SYSTEM NOTIFICATION - NOT USER INPUT]' took the whole
        // picker down with "Could not find color or style 'SYSTEM'", and the
        // launcher's Resume looked like it had crashed.
        //
        // After the line is built, never before: the columns are padded to a
        // width, and escaping first would have the padding count the doubled
        // brackets that the renderer then draws as one.
        return SessionDisplay.Line(Session, width).EscapeMarkup();
    }
}
