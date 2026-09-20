using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
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
    private readonly IAnsiConsole _console;

    public TeamRemedyTrustCommand(IRemedyBook book, IAnsiConsole console)
    {
        _book = book;
        _console = console;
    }

    public sealed class Settings : RemedySettings
    {
        [CommandArgument(0, "<REMEDY>")]
        [Description("The remedy, as 'team remedies' names it.")]
        public string Remedy { get; init; } = string.Empty;

        [CommandOption("--revoke")]
        [Description("Take the trust back. A remediator asks about it again from now on.")]
        public bool Revoke { get; init; }

        [CommandOption("--by <WHO>")]
        [Description("Who is saying so, for the record. Your user name when omitted.")]
        public string By { get; init; } = string.Empty;

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

        var found = _book.Find(settings.Team, settings.Remedy);

        if (found.Failed)
        {
            return Task.FromResult(output.Fail(found));
        }

        var remedy = found.Value!;

        if (settings.Revoke)
        {
            return Task.FromResult(Revoke(output, settings, remedy));
        }

        // Trusting something nobody can read is trusting a name. The script has
        // to be there and readable before anybody says it may run unattended.
        var script = _book.ScriptOf(settings.Team, remedy);

        if (script.Failed)
        {
            return Task.FromResult(output.Fail(script));
        }

        var fingerprint = RemedyCeiling.Fingerprint(script.Value!);

        if (remedy.IsTrusted && string.Equals(remedy.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"[dim]{Markup.Escape(remedy.Name)} is already trusted, and has not changed since.[/]");

            return Task.FromResult(CommandOutput.Success());
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(remedy.Name)} would be trusted"
                + (remedy.IsTrusted ? " again, at its current script." : "."));

            return Task.FromResult(CommandOutput.Success());
        }

        remedy.Trust = Remedy.Trusted;
        remedy.TrustedBy = settings.By is { Length: > 0 } who ? who : Environment.UserName;
        remedy.TrustedAt = DateTimeOffset.UtcNow;
        remedy.Fingerprint = fingerprint;

        var saved = _book.Save(settings.Team, remedy);

        if (saved.Failed)
        {
            return Task.FromResult(output.Fail(saved));
        }

        output.WriteLine($"[green]{Markup.Escape(remedy.Name)} is trusted.[/]");

        // What that actually comes to is the other key, and saying "trusted"
        // without it reads as a grant. It is not one on its own.
        output.WriteLine(
            $"[dim]A remediator may run it where this machine allows a trusted "
            + $"'{Markup.Escape(TeamRemediesCommand.Kind(remedy))}' remedy to run. "
            + $"Check with: loadout config get team-remediation[/]");

        output.WriteLine("[dim]Changing the script takes this back, and it is asked about again.[/]");

        return Task.FromResult(CommandOutput.Success());
    }

    private int Revoke(CommandOutput output, Settings settings, Remedy remedy)
    {
        if (!remedy.IsTrusted)
        {
            output.WriteLine($"[dim]{Markup.Escape(remedy.Name)} was not trusted.[/]");

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was changed.[/] {Markup.Escape(remedy.Name)} would stop being trusted.");

            return CommandOutput.Success();
        }

        remedy.Trust = Remedy.Untrusted;
        remedy.TrustedBy = string.Empty;
        remedy.TrustedAt = null;
        remedy.Fingerprint = string.Empty;

        var saved = _book.Save(settings.Team, remedy);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        output.WriteLine($"[yellow]{Markup.Escape(remedy.Name)} is no longer trusted.[/] It is asked about from now on.");

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
                $"[bold]{Markup.Escape(one.Id)}[/]  {Markup.Escape(one.Remedy)}  "
                + $"[dim]{Markup.Escape(one.Node)} in {Markup.Escape(one.Run)}[/]");

            if (one.Why is { Length: > 0 })
            {
                output.WriteLine($"  {Markup.Escape(one.Why)}");
            }

            output.WriteLine($"  [yellow]{Markup.Escape(one.Because)}[/]");
            output.WriteLine(
                $"  [dim]see it with: loadout team remedy show {Markup.Escape(one.Remedy)} "
                + $"--team {Markup.Escape(settings.Team)} --script[/]");
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
        IReadOnlyList<RemedyRequest> waiting,
        string id,
        bool allowed,
        string reason,
        bool dryRun)
    {
        var found = waiting.FirstOrDefault(one =>
            string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            return output.Fail(
                $"'{team}' is not waiting on anything called '{id}'.", ExitCode.ProjectNotFound);
        }

        if (dryRun)
        {
            // Change nothing, which is what --dry-run means. Said before the
            // answer rather than after it, because a run that reported the same
            // words either way is the failure the rule exists to stop.
            output.WriteLine(
                $"[dim]Dry run: nothing was answered.[/] {Markup.Escape(found.Remedy)} would be "
                + (allowed ? "allowed to run." : "refused."));

            return CommandOutput.Success();
        }

        // The answer goes where the node that asked is looking, which is the
        // run's own directory, and off this list. Two writes, and the one that
        // matters to the node happens first.
        var answered = _book.Answered(team, found.Id);

        if (answered.Failed)
        {
            return output.Fail(answered);
        }

        output.WriteLine(
            allowed
                ? $"[green]{Markup.Escape(found.Remedy)} may run.[/]"
                : $"[yellow]{Markup.Escape(found.Remedy)} was refused.[/]"
                  + " The node reports what it needed rather than finding another way.");

        if (reason is { Length: > 0 })
        {
            output.WriteLine($"  [dim]{Markup.Escape(reason)}[/]");
        }

        return CommandOutput.Success();
    }
}
