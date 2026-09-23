using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams;
using Loadout.Models.Configuration;
using Loadout.Models;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Saying that a remediator may run a particular script.
/// </summary>
/// <remarks>
/// <para>
/// The key an agent can never turn. A node can register a remedy, improve it,
/// and write that it is wonderful; only a person at this machine says it may
/// run unattended, and only about the script as it is now.
/// </para>
/// <para>
/// Trust is spent by a change. The declaration that tells a team to keep
/// improving what it registers is exactly the thing that would otherwise carry
/// one script's trust onto another, so what is recorded is a fingerprint of the
/// script and a remedy that no longer matches it asks again.
/// </para>
/// </remarks>
[Description("Trust a remedy so a remediator may run it, or take that back.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team remedy trust approve automation allow script", Mutates = true,
    Example = "loadout team remedy trust clear-build-cache --team system-watch")]
public sealed class TeamRemedyTrustCommand : AsyncCommand<TeamRemedyTrustCommand.Settings>
{
    private readonly IRemedyBook _book;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamRemedyTrustCommand(
        IRemedyBook book,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _book = book;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : RemedySettings
    {
        [CommandArgument(0, "<REMEDY>")]
        [Description("The remedy, as 'team remedies' names it.")]
        public string Remedy { get; init; } = string.Empty;

        [CommandOption("--revoke")]
        [Description("Take the agreement back. A remediator asks about it again from now on.")]
        public bool Revoke { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is saying so, for the record. Your user name when omitted.")]
        public string By { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Team is not { Length: > 0 })
        {
            return output.Fail("Which team? Name one with --team.", ExitCode.InvalidArguments);
        }

        var found = _book.Find(settings.Team, settings.Remedy);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var remedy = found.Value!;

        // This machine's own configuration, which is where a decision this
        // machine makes belongs. The remedy's record is in the team's
        // directory, and the team's nodes are told to write there.
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);

        if (machine.Failed)
        {
            return output.Fail(machine);
        }

        var config = machine.Value!;
        var already = config.Teams.TrustedRemedies.FirstOrDefault(one =>
            string.Equals(one.Team, settings.Team, StringComparison.OrdinalIgnoreCase)
            && string.Equals(one.Remedy, remedy.Name, StringComparison.OrdinalIgnoreCase));

        if (settings.Revoke)
        {
            if (already is null)
            {
                output.WriteLine($"[dim]{Markup.Escape(remedy.Name)} was not agreed to here.[/]");

                return CommandOutput.Success();
            }

            if (settings.DryRun)
            {
                output.WriteLine(
                    $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(remedy.Name)} would stop being agreed to.");

                return CommandOutput.Success();
            }

            config.Teams.TrustedRemedies.Remove(already);

            var undone = await _configuration.SaveMachineAsync(config, cancellationToken).ConfigureAwait(false);

            if (undone.Failed)
            {
                return output.Fail(undone);
            }

            output.WriteLine(
                $"[yellow]{Markup.Escape(remedy.Name)} is no longer agreed to.[/] "
                + "It is asked about from now on.");

            return CommandOutput.Success();
        }

        // Agreeing to something nobody can read is agreeing to a name. The
        // script has to be there and readable before anybody says it may run.
        var script = _book.ScriptOf(settings.Team, remedy);

        if (script.Failed)
        {
            return output.Fail(script);
        }

        var fingerprint = RemedyCeiling.Fingerprint(script.Value!);

        if (already is not null
            && string.Equals(already.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine(
                $"[dim]{Markup.Escape(remedy.Name)} is already agreed to, and has not changed since.[/]");

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(remedy.Name)} would be agreed to"
                + (already is not null ? " again, at its current script." : "."));

            return CommandOutput.Success();
        }

        if (already is not null)
        {
            config.Teams.TrustedRemedies.Remove(already);
        }

        config.Teams.TrustedRemedies.Add(new TrustedRemedy
        {
            Team = settings.Team,
            Remedy = remedy.Name,
            Fingerprint = fingerprint,
            By = settings.By is { Length: > 0 } who ? who : Environment.UserName,
            At = DateTimeOffset.UtcNow,
        });

        var saved = await _configuration.SaveMachineAsync(config, cancellationToken).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        output.WriteLine($"[green]{Markup.Escape(remedy.Name)} is agreed to on this machine.[/]");

        // What that actually comes to is the other key, and saying "agreed"
        // without it reads as a grant. It is not one on its own.
        output.WriteLine(
            "[dim]A remediator may run it where this machine allows a trusted "
            + $"'{Markup.Escape(TeamRemediesCommand.Kind(remedy))}' remedy to run. "
            + "Check with: loadout config get team-remediation[/]");

        output.WriteLine("[dim]Changing the script takes this back, and it is asked about again.[/]");

        if (remedy.ClaimsTrust)
        {
            // Worth saying. A record that already claimed trust was claiming
            // something nothing acted on, and somebody agreeing to it now
            // should know the claim was there.
            output.WriteLine(
                "[dim]Its own record already claimed to be trusted. That decides nothing, and "
                + "is worth knowing about: the record lives where the team's nodes write.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>What a remediator is waiting to be told, and telling it.</summary>
[Description("List remediations waiting on you, and answer one.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team remedy requests pending approve refuse remediation waiting", Mutates = true,
    Example = "loadout team remedy requests --team system-watch")]
public sealed class TeamRemedyRequestsCommand : AsyncCommand<TeamRemedyRequestsCommand.Settings>
{
    private readonly IRemedyBook _book;
    private readonly IAnsiConsole _console;

    public TeamRemedyRequestsCommand(IRemedyBook book, IAnsiConsole console)
    {
        _book = book;
        _console = console;
    }

    public sealed class Settings : RemedySettings
    {
        [CommandOption("--approve <ID>")]
        [Description("Let that one run. The remediator reads the answer and goes ahead.")]
        public string Approve { get; init; } = string.Empty;

        [CommandOption("--refuse <ID>")]
        [Description("Refuse that one. The remediator reports what it needed and why.")]
        public string Refuse { get; init; } = string.Empty;

        [CommandOption("--reason <WHY>")]
        [Description("Why, for the record and for the node reading it.")]
        public string Reason { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Team is not { Length: > 0 })
        {
            return Task.FromResult(output.Fail(
                "Which team? Name one with --team.", ExitCode.InvalidArguments));
        }

        if (settings.Approve is { Length: > 0 } && settings.Refuse is { Length: > 0 })
        {
            return Task.FromResult(output.Fail(
                "Approve one or refuse one, not both.", ExitCode.InvalidArguments));
        }

        var waiting = _book.Waiting(settings.Team);

        if (waiting.Failed)
        {
            return Task.FromResult(output.Fail(waiting));
        }

        if (settings.Approve is { Length: > 0 } yes)
        {
            return Task.FromResult(Answer(
                output, settings.Team, waiting.Value!, yes, allowed: true, settings.Reason, settings.DryRun));
        }

        if (settings.Refuse is { Length: > 0 } no)
        {
            return Task.FromResult(Answer(
                output, settings.Team, waiting.Value!, no, allowed: false, settings.Reason, settings.DryRun));
        }

        if (output.IsJson)
        {
            output.WriteJson(new { team = settings.Team, waiting = waiting.Value! });

            return Task.FromResult(CommandOutput.Success());
        }

        if (waiting.Value!.Count == 0)
        {
            output.WriteLine($"[dim]{Markup.Escape(settings.Team)} is not waiting on anything.[/]");

            return Task.FromResult(CommandOutput.Success());
        }

        foreach (var one in waiting.Value!)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Markup.Escape(one.Id)}[/]  "
                + $"[dim]{Markup.Escape(one.Node)} ({Markup.Escape(one.Role)}) in {Markup.Escape(one.Run)}[/]");

            // The question carries what the remedy does, what it assumes and
            // how somebody would know it worked. "May it use Bash for this
            // command line" is not a question anybody can answer without going
            // and reading the script themselves.
            output.WriteLine($"  {Loadout.Tui.Shown.Safely(one.Asked)}");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]Answer one with: loadout team remedy requests --team {Markup.Escape(settings.Team)} "
            + "--approve <id>[/]");

        return Task.FromResult(CommandOutput.Success());
    }

    private int Answer(
        CommandOutput output,
        string team,
        IReadOnlyList<RemedyWaiting> waiting,
        string id,
        bool allowed,
        string reason,
        bool dryRun)
    {
        var found = waiting.FirstOrDefault(one =>
            string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            return output.Fail($"Nothing is waiting under '{id}'.", ExitCode.ProjectNotFound);
        }

        if (dryRun)
        {
            // Change nothing, which is what --dry-run means. Said before the
            // answer rather than after it, because a run that reported the same
            // words either way is the failure the rule exists to stop.
            output.WriteLine(
                $"[dim]Dry run: nothing was answered.[/] {Markup.Escape(found.Id)} would be "
                + (allowed ? "allowed to run." : "refused."));

            return CommandOutput.Success();
        }

        // Where the node that asked is already looking, which is the same place
        // a gate is answered and the same place the terminal and the dashboard
        // write to. One queue, one answering path.
        var answered = _book.Answer(found.Id, allowed, reason is { Length: > 0 } ? reason : null);

        if (answered.Failed)
        {
            return output.Fail(answered);
        }

        output.WriteLine(
            allowed
                ? "[green]It may run.[/]"
                : "[yellow]It was refused.[/] The node reports what it needed rather than "
                  + "finding another way.");

        if (reason is { Length: > 0 })
        {
            output.WriteLine($"  [dim]{Markup.Escape(reason)}[/]");
        }

        return CommandOutput.Success();
    }
}
