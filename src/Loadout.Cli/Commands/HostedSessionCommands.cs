using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Lists the sessions the running daemon hosts for Studio.</summary>
/// <remarks>
/// 'loadout sessions' lists conversations from the agents' own history, and
/// 'loadout running' what this machine started; neither knows the daemon's
/// ids, which are what renaming and ending one take.
/// </remarks>
[Description("List the sessions the daemon hosts for Studio, with the ids 'session rename' and 'session end' take.")]
[CommandMeta(CommandCategory.Start, Intent = "studio hosted sessions terminals tabs daemon list")]
public sealed class HostedSessionListCommand : AsyncCommand<GlobalSettings>
{
    private readonly IDaemonSessions _sessions;
    private readonly IAnsiConsole _console;

    public HostedSessionListCommand(IDaemonSessions sessions, IAnsiConsole console)
    {
        _sessions = sessions;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var listed = await _sessions.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (output.IsJson)
        {
            output.WriteJson(listed.Value!);

            return CommandOutput.Success();
        }

        if (listed.Value!.Count == 0)
        {
            output.WriteLine("The daemon is hosting no sessions.");

            return CommandOutput.Success();
        }

        var table = new Table().Border(TableBorder.Simple)
            .AddColumn("Id").AddColumn("Name").AddColumn("Project").AddColumn("State");

        foreach (var one in listed.Value)
        {
            table.AddRow(
                Markup.Escape(one.Id),
                Markup.Escape(one.Title) + (one.Title == one.LaunchTitle ? string.Empty : $" [dim]({Markup.Escape(one.LaunchTitle)})[/]"),
                Markup.Escape(one.Project ?? (one.Kind == "command" ? "loadout" : "-")),
                one.Ended ? "[dim]ended[/]" : one.Needs is { } needs ? $"[yellow]{Markup.Escape(needs)}[/]" : "running");
        }

        output.Write(table);

        return CommandOutput.Success();
    }
}

/// <summary>Renames a session the daemon hosts, as renaming its tab in Studio does.</summary>
/// <remarks>
/// Through the daemon's own route, so every browser shows the new name at
/// once, and the agent is given it too where its adapter can be.
/// </remarks>
[Description("Rename a session the daemon hosts, as renaming its tab in Studio does.")]
[CommandMeta(CommandCategory.Start, Intent = "studio hosted session rename name tab title", Mutates = true)]
public sealed class HostedSessionRenameCommand : AsyncCommand<HostedSessionRenameCommand.Settings>
{
    private readonly IDaemonSessions _sessions;
    private readonly IAnsiConsole _console;

    public HostedSessionRenameCommand(IDaemonSessions sessions, IAnsiConsole console)
    {
        _sessions = sessions;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<session>")]
        [Description("The session: its id, the start of its id, or its name, as 'loadout session list' shows them.")]
        public string Session { get; init; } = string.Empty;

        [CommandArgument(1, "[name]")]
        [Description("The new name, quoted if it has spaces.")]
        public string? Name { get; init; }

        [CommandOption("--reset")]
        [Description("Give it back the name it was launched as.")]
        public bool Reset { get; init; }
    }

    /// <inheritdoc />
    public override ValidationResult Validate(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Reset == string.IsNullOrWhiteSpace(settings.Name)
            ? ValidationResult.Success()
            : ValidationResult.Error(settings.Reset
                ? "Give a new name or --reset, not both."
                : "Give the new name, or --reset for the one it was launched as.");
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await HostedSessionCommon.FindAsync(_sessions, settings.Session, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var session = found.Value!;
        var name = settings.Reset ? null : settings.Name!.Trim();

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { session.Id, from = session.Title, to = name ?? session.LaunchTitle, applied = false });

                return CommandOutput.Success();
            }

            output.WriteLine($"Would rename {Markup.Escape(session.Id)} from '{Markup.Escape(session.Title)}' to '{Markup.Escape(name ?? session.LaunchTitle)}'. Nothing was changed.");

            return CommandOutput.Success();
        }

        var renamed = await _sessions.RenameAsync(session.Id, name, cancellationToken).ConfigureAwait(false);

        if (renamed.Failed)
        {
            return output.Fail(renamed);
        }

        if (output.IsJson)
        {
            output.WriteJson(renamed.Value!);

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]Renamed[/] {Markup.Escape(session.Id)} to '{Markup.Escape(renamed.Value!.Title)}'.");

        if (renamed.Value.Kind == "launch")
        {
            output.WriteLine(renamed.Value.NamesAgent
                ? "[dim]Its agent is given the name too, the next time it is waiting.[/]"
                : "[dim]Only Loadout shows this name: its agent has not said it can take one.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Ends a session the daemon hosts, as Studio's End session does.</summary>
/// <remarks>
/// Ended, not forgotten: it stays on Studio's Sessions page and in every
/// browser, with how it ended, as it does when ended from a tab.
/// </remarks>
[Description("End a session the daemon hosts, as Studio's End session does. It stays listed, ended.")]
[CommandMeta(CommandCategory.Start, Intent = "studio hosted session end stop close kill terminal tab", Mutates = true)]
public sealed class HostedSessionEndCommand : AsyncCommand<HostedSessionEndCommand.Settings>
{
    private readonly IDaemonSessions _sessions;
    private readonly IAnsiConsole _console;

    public HostedSessionEndCommand(IDaemonSessions sessions, IAnsiConsole console)
    {
        _sessions = sessions;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<session>")]
        [Description("The session: its id, the start of its id, or its name, as 'loadout session list' shows them.")]
        public string Session { get; init; } = string.Empty;

        [CommandOption("--yes")]
        [Description("Do not ask before ending it.")]
        public bool Yes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await HostedSessionCommon.FindAsync(_sessions, settings.Session, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var session = found.Value!;
        var named = $"'{session.Title}'" + (session.Project is { } project ? $" in {project}" : string.Empty);

        if (session.Ended)
        {
            if (output.IsJson)
            {
                output.WriteJson(session);
            }
            else
            {
                output.WriteLine($"{Markup.Escape(named)} has already ended.");
            }

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { session.Id, session.Title, applied = false });

                return CommandOutput.Success();
            }

            output.WriteLine($"Would end {Markup.Escape(named)} ({Markup.Escape(session.Id)}). Nothing was changed.");

            return CommandOutput.Success();
        }

        if (!settings.Yes)
        {
            if (!settings.AllowsPrompting)
            {
                return output.Fail(
                    $"That would end {named}, and nobody is here to agree to it. Pass --yes.",
                    ExitCode.InvalidArguments);
            }

            if (!_console.Confirm($"End {Markup.Escape(named)}? Its agent stops, and this cannot be undone", false))
            {
                output.WriteLine("[dim]Nothing was ended.[/]");

                return CommandOutput.Success();
            }
        }

        var ended = await _sessions.EndAsync(session.Id, cancellationToken).ConfigureAwait(false);

        if (ended.Failed)
        {
            return output.Fail(ended);
        }

        if (output.IsJson)
        {
            output.WriteJson(ended.Value!);

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]Ended[/] {Markup.Escape(named)}. It stays on Studio's Sessions page until somebody removes it.");

        return CommandOutput.Success();
    }
}

internal static class HostedSessionCommon
{
    /// <summary>The one hosted session somebody named, or why there is not exactly one.</summary>
    public static async Task<Loadout.Models.Results.OperationResult<HostedSessionRow>> FindAsync(
        IDaemonSessions sessions, string asked, CancellationToken ct)
    {
        var listed = await sessions.ListAsync(ct).ConfigureAwait(false);

        return listed.Failed
            ? Loadout.Models.Results.OperationResult<HostedSessionRow>.Fail(listed.Error!, listed.ExitCode)
            : HostedSessionMatch.Find(listed.Value!, asked);
    }
}
