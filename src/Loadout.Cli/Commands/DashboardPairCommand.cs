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

/// <summary>Signs a phone in to the dashboard at the link address.</summary>
/// <remarks>
/// <para>
/// Made here rather than asked for at the link address, because this machine
/// is the one place that proves the person asking is the person whose
/// dashboard it is. What comes out is the same one-time code a local sign-in
/// link carries: it works once, for two minutes, and is exchanged for a
/// cookie that lasts ninety days at the link address and nowhere else.
/// </para>
/// <para>
/// That cookie reaches team runs on the classic page, to read them and answer
/// what they are waiting on, and nothing else the dashboard serves.
/// 'loadout dashboard sign-out' ends it with every other sign-in.
/// </para>
/// </remarks>
[Description("Sign a phone in to the dashboard at the link address, to read and answer team runs there. Prints a link that works once, for two minutes.")]
[CommandMeta(CommandCategory.Start, Intent = "dashboard pair phone mobile sign in remote link address answer away")]
public sealed class DashboardPairCommand : AsyncCommand<GlobalSettings>
{
    private readonly IPlatformPaths _paths;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public DashboardPairCommand(IPlatformPaths paths, IConfigurationService configuration, IAnsiConsole console)
    {
        _paths = paths;
        _configuration = configuration;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        GlobalSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var machine = await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false);
        var linkAddress = machine.Value?.Teams.LinkAddress;

        if (string.IsNullOrWhiteSpace(linkAddress) || NoticeLink.Problem(linkAddress) is not null)
        {
            return output.Fail(
                "There is no link address to sign in at. Set the address a phone reaches this machine by: "
                + "loadout config set team-link-address https://<name>:<port>",
                ExitCode.ConfigurationInvalid);
        }

        var link = NoticeLink.Pairing(linkAddress, SignIn.ForMachine(_paths).MintCode())!;

        if (output.IsJson)
        {
            output.WriteJson(new { link, expires = DateTimeOffset.UtcNow + SignIn.CodeLife });

            return CommandOutput.Success();
        }

        output.WriteLine("Open this on the phone within two minutes. It works once:");
        output.WriteLine($"[bold]{Markup.Escape(link)}[/]");
        output.WriteLine(
            "[dim]Type it, or send it to yourself somewhere private: until it is used it signs in whoever opens it. "
            + "The phone can then read and answer team runs there. "
            + "A daemon started before the link address was set does not know it yet: loadout daemon restart[/]");

        return CommandOutput.Success();
    }
}
