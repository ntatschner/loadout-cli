using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Sessions;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;

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
public sealed class SessionListCommand : AsyncCommand<SessionSettings>
{
    private readonly ISessionHistoryService _sessions;
    private readonly SessionScope _scope;
    private readonly IAnsiConsole _console;

    public SessionListCommand(
        ISessionHistoryService sessions,
        SessionScope scope,
        IAnsiConsole console)
    {
        _sessions = sessions;
        _scope = scope;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, SessionSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var query = await _scope.QueryAsync(settings).ConfigureAwait(false);

        var result = await _sessions.ListAsync(query).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var sessions = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(sessions.Select(s => new
            {
                agent = s.Agent,
                id = s.SessionId,
                title = s.Title,
                project = s.ProjectSlug,
                directory = s.Directory,
                branch = s.Branch,
                lastActive = s.LastActive,
                transcript = s.TranscriptPath,
            }));

            return CommandOutput.Success();
        }

        if (sessions.Count == 0)
        {
            output.WriteLine(query.ProjectSlug is { Length: > 0 } slug
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
/// </summary>
[Description("Resume a previous agent session.")]
public sealed class ResumeCommand : AsyncCommand<ResumeSettings>
{
    private readonly ISessionHistoryService _sessions;
    private readonly SessionScope _scope;
    private readonly IAgentLauncher _launcher;
    private readonly IAnsiConsole _console;

    public ResumeCommand(
        ISessionHistoryService sessions,
        SessionScope scope,
        IAgentLauncher launcher,
        IAnsiConsole console)
    {
        _sessions = sessions;
        _scope = scope;
        _launcher = launcher;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, ResumeSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var query = await _scope.QueryAsync(settings).ConfigureAwait(false);

        var result = await _sessions.ListAsync(query).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var sessions = result.Value!;

        if (sessions.Count == 0)
        {
            return output.Fail(
                "No recorded agent sessions were found to resume.",
                ExitCode.ProjectNotFound);
        }

        var chosen = await ChooseAsync(sessions, settings, output).ConfigureAwait(false);

        if (chosen is null)
        {
            // Backing out of the picker is a decision, not a failure.
            return CommandOutput.Success();
        }

        if (chosen.ProjectSlug is not { Length: > 0 } slug)
        {
            // Resuming goes through the project pipeline, so a session from an
            // unregistered directory has nothing to hang off. Say what would
            // fix it rather than just refusing.
            return output.Fail(
                $"That session ran in {chosen.Directory}, which is not a registered project. "
                + $"Register it with 'loadout project add \"{chosen.Directory}\"' and try again.",
                ExitCode.ProjectNotFound);
        }

        output.WriteLine(
            $"Resuming [cyan]{slug.EscapeMarkup()}[/] with {chosen.Agent.EscapeMarkup()}: "
            + $"{chosen.Label.EscapeMarkup()}");

        var launch = await _launcher.LaunchAsync(new LaunchRequest(
            slug,
            chosen.Agent,
            Offline: settings.Offline,
            NoSync: settings.NoSync,
            Profile: settings.Profile,
            Environment: settings.Environment,
            ResumeSessionId: chosen.SessionId)).ConfigureAwait(false);

        if (launch.Failed)
        {
            return output.Fail(launch);
        }

        foreach (var warning in launch.Value!.Warnings)
        {
            output.WriteLine($"[yellow]{warning.EscapeMarkup()}[/]");
        }

        // The agent's own exit status is the command's, per spec section 40.
        return launch.Value.AgentExitCode;
    }

    /// <summary>
    /// Works out which session was meant: named outright, the most recent, or
    /// whichever one is picked from the list.
    /// </summary>
    private async Task<AgentSession?> ChooseAsync(
        IReadOnlyList<AgentSession> sessions,
        ResumeSettings settings,
        CommandOutput output)
    {
        if (settings.Session is { Length: > 0 } wanted)
        {
            // Prefixes are accepted because nobody types a whole UUID.
            var matches = sessions
                .Where(s => s.SessionId.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return matches.Count == 1 ? matches[0] : null;
        }

        if (settings.Last)
        {
            return sessions[0];
        }

        if (settings.NonInteractive || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            // Spec section 37: no menu where nobody can answer it.
            output.WriteLine(
                "[yellow]A session must be named, or --last used, when there is no terminal to ask in.[/]");

            return null;
        }

        var prompt = new SelectionPrompt<SessionChoice>()
            .Title("Which session?")
            .PageSize(15);

        // The picker leaves room for the selection marker it draws itself.
        var width = Math.Max(40, _console.Profile.Width - 4);

        prompt.UseConverter(choice => choice.Render(width));

        prompt.AddChoices(sessions.Select(s => new SessionChoice(s)));
        prompt.AddChoice(SessionChoice.Cancel);

        var chosen = await prompt.ShowAsync(_console, CancellationToken.None).ConfigureAwait(false);

        return chosen.Session;
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

        return SessionDisplay.Line(Session, width);
    }
}
