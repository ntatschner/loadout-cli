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
/// Where a run's call for help is sent when nobody is looking at the browser.
/// </summary>
/// <remarks>
/// <para>
/// The address is held in the operating system's credential store and never in
/// a file, because a Slack or Discord webhook address <em>is</em> the
/// credential: anybody holding it can post into that channel as you.
/// </para>
/// <para>
/// Which service it is goes in the machine's own configuration, because that
/// is not a secret and is worth being able to read.
/// </para>
/// </remarks>
[Description("Send a run's call for help to Slack, Discord, Teams, Telegram or your own endpoint.")]
[CommandMeta(CommandCategory.Integration,
    Intent = "notify slack discord teams telegram webhook alert", Mutates = true)]
public sealed class TeamNotifySetCommand : AsyncCommand<TeamNotifySetCommand.Settings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly HttpClient _client;

    public TeamNotifySetCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        HttpClient client,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _client = client;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<WHERE>")]
        [Description("slack, discord, teams, telegram or generic.")]
        public string Where { get; init; } = string.Empty;

        [CommandOption("--url <URL>")]
        [Description("The address to post to. Kept in this machine's credential store.")]
        public string? Url { get; init; }

        [CommandOption("--chat <ID>")]
        [Description("The Telegram chat to send to. Meaningless for the others.")]
        public string? Chat { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (Notices.KindOf(settings.Where) is not { } kind)
        {
            return output.Fail(
                $"There is nowhere called '{settings.Where}'. "
                + "It is slack, discord, teams, telegram or generic.",
                ExitCode.InvalidArguments);
        }

        if (settings.Url is not { Length: > 0 } url)
        {
            return output.Fail("Say where with --url.", ExitCode.InvalidArguments);
        }

        if (kind == NoticeKind.Telegram && settings.Chat is not { Length: > 0 })
        {
            // Said now rather than discovered as a rejected request the first
            // time a run actually needs somebody.
            return output.Fail("Telegram needs --chat as well as --url.", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would send to {Markup.Escape(settings.Where)} and keep the address in this "
                + "machine's credential store. Nothing was written.");

            return CommandOutput.Success();
        }

        var notices = new Notices(_secrets, _client);

        var kept = await notices.RememberAsync(url, cancellationToken).ConfigureAwait(false);

        if (kept.Failed)
        {
            return output.Fail(kept);
        }

        await _configuration.UpdateMachineAsync(
            machine =>
            {
                machine.Teams.NotifyKind = settings.Where.Trim().ToLowerInvariant();
                machine.Teams.NotifyChat = settings.Chat ?? string.Empty;
            },
            cancellationToken).ConfigureAwait(false);

        output.WriteLine($"[green]+[/] A run that needs you will say so on {Markup.Escape(settings.Where)}.");
        output.WriteLine("[dim]Try it with: loadout team notify test[/]");
        output.WriteLine("[dim]It only goes out while the daemon is running.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Sends one, so somebody can see it arrive.</summary>
/// <remarks>
/// Worth its own command: a webhook address is pasted from a browser and is
/// wrong about as often as it is right, and finding that out at three in the
/// morning when a run actually needed somebody is the worst time to find out.
/// </remarks>
[Description("Send a test message, so you can see it arrive.")]
[CommandMeta(CommandCategory.Integration, Intent = "notify test try slack discord telegram")]
public sealed class TeamNotifyTestCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly HttpClient _client;

    public TeamNotifyTestCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        HttpClient client,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _client = client;
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
        var notices = new Notices(_secrets, _client);

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var teams = machine.Value?.Teams;

        if (Notices.KindOf(teams?.NotifyKind) is not { } kind)
        {
            return output.Fail(
                "Nothing is set up. Start with: loadout team notify set slack --url <address>",
                ExitCode.ConfigurationInvalid);
        }

        if (await notices.AddressAsync(cancellationToken).ConfigureAwait(false) is not { } address)
        {
            return output.Fail(
                "There is no address in this machine's credential store. Set it again.",
                ExitCode.ConfigurationInvalid);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"Would send one message to {Markup.Escape(teams!.NotifyKind)}. Nothing was sent.");

            return CommandOutput.Success();
        }

        var sent = await notices.PostAsync(
            kind,
            "Loadout is set up",
            "This is the message a run will send when it needs you.",
            "loadout team status",
            teams!.NotifyChat,
            address,
            cancellationToken).ConfigureAwait(false);

        if (!sent)
        {
            // Never says what the address was. This is the command somebody
            // runs while screen-sharing to work out why nothing arrives.
            return output.Fail(
                $"{teams.NotifyKind} did not accept it. Check the address and, for Telegram, the chat.",
                ExitCode.GeneralFailure);
        }

        output.WriteLine($"[green]+[/] Sent. It should be on {Markup.Escape(teams.NotifyKind)} now.");

        return CommandOutput.Success();
    }
}

/// <summary>Whether anything is set up, and where.</summary>
[Description("Say whether a run's call for help goes anywhere, and where.")]
[CommandMeta(CommandCategory.Integration, Intent = "notify show status slack discord telegram")]
public sealed class TeamNotifyShowCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly HttpClient _client;

    public TeamNotifyShowCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        HttpClient client,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _client = client;
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

        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var teams = machine.Value?.Teams;

        // Whether there is one, never what it is.
        var held = await new Notices(_secrets, _client).IsSetAsync(cancellationToken).ConfigureAwait(false);
        var where = teams?.NotifyKind ?? string.Empty;

        if (output.IsJson)
        {
            output.WriteJson(new { enabled = held && where.Length > 0, where, chat = teams?.NotifyChat ?? string.Empty });

            return CommandOutput.Success();
        }

        output.WriteLine(held && where.Length > 0
            ? $"[green]on[/]  a run that needs you says so on {Markup.Escape(where)}"
            : "[dim]off[/] nothing is sent anywhere");

        if (where.Length > 0 && !held)
        {
            output.WriteLine(
                $"     [yellow]{Markup.Escape(where)} is configured but no address is held.[/] Set it again.");
        }

        output.WriteLine("     [dim]it only goes out while the daemon is running[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Stops anything being sent.</summary>
[Description("Forget where to send, so nothing is sent anywhere.")]
[CommandMeta(CommandCategory.Integration, Intent = "notify off disable forget slack discord", Mutates = true)]
public sealed class TeamNotifyClearCommand : AsyncCommand<GlobalSettings>
{
    private readonly ISecretProvider _secrets;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;
    private readonly HttpClient _client;

    public TeamNotifyClearCommand(
        ISecretProvider secrets,
        IConfigurationService configuration,
        HttpClient client,
        IAnsiConsole console)
    {
        _secrets = secrets;
        _configuration = configuration;
        _client = client;
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
            output.WriteLine("Would forget the address and stop sending. Nothing was removed.");

            return CommandOutput.Success();
        }

        await new Notices(_secrets, _client).ForgetAsync(cancellationToken).ConfigureAwait(false);

        await _configuration.UpdateMachineAsync(
            machine =>
            {
                machine.Teams.NotifyKind = string.Empty;
                machine.Teams.NotifyChat = string.Empty;
            },
            cancellationToken).ConfigureAwait(false);

        output.WriteLine("[green]+[/] Forgotten. Nothing is sent anywhere.");

        return CommandOutput.Success();
    }
}
