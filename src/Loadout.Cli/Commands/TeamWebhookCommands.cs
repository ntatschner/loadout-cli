using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams.Daemon;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Lets something outside this machine start a team run.
/// </summary>
/// <remarks>
/// <para>
/// Two separate acts, deliberately. This one makes a token, which says who may
/// ask. <c>config set team-webhook-teams</c> says what they may ask for, and
/// until something is on that list a caller with a perfectly good token can
/// start nothing at all.
/// </para>
/// <para>
/// The token lives in the operating system's credential store and is printed
/// once. Nothing reads it back out afterwards: one that can be re-read is one
/// in every screenshot of the machine it is on. Lost it, run this again — the
/// old one stops working the moment the new one is written.
/// </para>
/// </remarks>
[Description("Make a token so something outside this machine can start a team run.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "webhook trigger token remote start team run", Mutates = true)]
public sealed class TeamWebhookEnableCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamWebhookEnableCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine(
                "Would make a token and keep it in this machine's credential store. "
                + "Nothing was made or written.");

            return CommandOutput.Success();
        }

        var made = await Webhook.EnableAsync(_secrets, cancellationToken).ConfigureAwait(false);

        if (made.Failed)
        {
            return output.Fail(made);
        }

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var teams = machine.Value?.Teams;

        if (output.IsJson)
        {
            // The one place it is shown. A caller scripting this needs it
            // somewhere it can read, and this is the moment it exists.
            output.WriteJson(new
            {
                token = made.Value,
                teams = teams?.WebhookTeams ?? [],
                listen = Webhook.Listen(teams),
            });

            return CommandOutput.Success();
        }

        output.WriteLine("[green]+[/] Triggered runs are on for this machine.");
        output.WriteBlankLine();
        output.WriteLine("[bold]Your token, shown once:[/]");
        output.WriteLine($"  {made.Value}");
        output.WriteBlankLine();

        if (teams?.WebhookTeams is { Count: > 0 } named)
        {
            output.WriteLine($"It may start: {Markup.Escape(string.Join(", ", named))}.");
        }
        else
        {
            // The half somebody forgets, said here rather than discovered as a
            // 403 from a git hook at three in the morning.
            output.WriteLine(
                "[yellow]It may start nothing yet.[/] A token says who may ask; this machine still has "
                + "to say what they may ask for:");
            output.WriteLine("  loadout config set team-webhook-teams \"docs-crew\"");
        }

        output.WriteBlankLine();
        output.WriteLine($"[dim]Listening on {Markup.Escape(Webhook.Listen(teams))} while the daemon runs. "
            + "Change it with: loadout config set team-webhook-listen[/]");
        output.WriteLine("[dim]Then: loadout team daemon[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Stops this machine accepting triggered runs.</summary>
[Description("Forget the webhook token, so nothing outside this machine can start a run.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "webhook disable revoke token remote", Mutates = true)]
public sealed class TeamWebhookDisableCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IAnsiConsole _console;

    public TeamWebhookDisableCommand(ISecretProvider secrets, IAnsiConsole console)
    {
        _secrets = secrets;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.DryRun)
        {
            output.WriteLine("Would forget this machine's webhook token. Nothing was removed.");

            return CommandOutput.Success();
        }

        var gone = await Webhook.DisableAsync(_secrets, cancellationToken).ConfigureAwait(false);

        if (gone.Failed)
        {
            return output.Fail(gone);
        }

        // The whole of the off switch. Without a token every trigger is
        // refused, whatever the teams list still says, which is why forgetting
        // it is enough and the list is left alone.
        output.WriteLine("[green]+[/] Forgotten. Nothing outside this machine can start a run.");

        return CommandOutput.Success();
    }
}

/// <summary>Whether this machine accepts triggered runs, and of what.</summary>
[Description("Say whether this machine accepts triggered runs, and which teams.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "webhook show status trigger teams")]
public sealed class TeamWebhookShowCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public TeamWebhookShowCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        // Whether there is one, never what it is. This command is the one
        // somebody runs while screen-sharing to work out why a hook is
        // failing.
        var on = await Webhook.TokenAsync(_secrets, cancellationToken).ConfigureAwait(false) is not null;

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var teams = machine.Value?.Teams;
        var named = teams?.WebhookTeams ?? [];

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                enabled = on,
                teams = named,
                listen = Webhook.Listen(teams),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(on
            ? "[green]on[/]  a token exists on this machine"
            : "[dim]off[/] no token, so every triggered run is refused");

        output.WriteLine(named.Count > 0
            ? $"     may start: {Markup.Escape(string.Join(", ", named))}"
            : "     may start: nothing, whatever token is presented");

        output.WriteLine($"     listening on {Markup.Escape(Webhook.Listen(teams))} while the daemon runs");

        return CommandOutput.Success();
    }
}
