using System.ComponentModel;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Models;
using AgentWorkspace.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>
/// Runs first-run configuration (spec sections 61 to 63).
/// <para>
/// Also reachable by running the launcher with no arguments on a machine that
/// has never been configured, which is where most people will meet it.
/// </para>
/// </summary>
[Description("Configure the launcher on this machine.")]
public sealed class SetupCommand : AsyncCommand<SetupCommand.Settings>
{
    private readonly ISetupWizard _wizard;
    private readonly IAnsiConsole _console;

    public SetupCommand(ISetupWizard wizard, IAnsiConsole console)
    {
        _wizard = wizard;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--force")]
        [Description("Run again even though the launcher is already configured.")]
        public bool Force { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        if (!settings.AllowsPrompting)
        {
            // The wizard is a conversation. Running it against a pipe would
            // either hang or invent answers, and spec section 37 rules both out.
            return output.Fail(
                "Setup is interactive and cannot run without a terminal. Write config.yaml "
                + "directly, or use: agentctl config set",
                ExitCode.InvalidArguments);
        }

        if (_wizard.IsConfigured() && !settings.Force)
        {
            output.WriteLine("[dim]The launcher is already configured on this machine.[/]");
            output.WriteLine("[dim]Run[/] agentctl setup --force [dim]to go through setup again, "
                + "or[/] agentctl doctor [dim]to check it.[/]");

            return CommandOutput.Success();
        }

        return await _wizard.RunAsync().ConfigureAwait(false);
    }
}
