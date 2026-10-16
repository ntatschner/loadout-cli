using System.ComponentModel;
using FluentAssertions;
using Loadout.Cli.Infrastructure;
using Spectre.Console.Cli;
using Spectre.Console.Testing;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Help for a group that is also a command, such as 'loadout daemon', lists
/// the options the command takes rather than only --help.
/// </summary>
public sealed class BranchHelpTests
{
    [Fact]
    public void A_group_that_runs_a_command_lists_that_commands_options()
    {
        Help(["serve", "--help"]).Should().Contain("--port");
    }

    [Fact]
    public void A_command_inside_the_group_lists_its_own_options_and_not_the_groups_command()
    {
        Help(["serve", "stop", "--help"]).Should().Contain("--now").And.NotContain("--port");
    }

    private static string Help(string[] args)
    {
        var console = new TestConsole();
        var app = new CommandApp();

        app.Configure(config =>
        {
            config.ConfigureConsole(console);
            config.SetHelpProvider(new BranchHelp(config.Settings));
            config.AddBranch<CommandSettings>("serve", serve =>
            {
                serve.SetDefaultCommand<Serve>();
                serve.AddCommand<Stop>("stop");
            });
        });

        app.Run(args);

        return console.Output;
    }

    private sealed class ServeSettings : CommandSettings
    {
        [CommandOption("--port <PORT>")]
        [Description("Where to listen.")]
        public int Port { get; init; }
    }

    private sealed class StopSettings : CommandSettings
    {
        [CommandOption("--now")]
        [Description("Without waiting.")]
        public bool Now { get; init; }
    }

    private sealed class Serve : Command<ServeSettings>
    {
        public override int Execute(CommandContext context, ServeSettings settings, CancellationToken cancellationToken) => 0;
    }

    private sealed class Stop : Command<StopSettings>
    {
        public override int Execute(CommandContext context, StopSettings settings, CancellationToken cancellationToken) => 0;
    }
}
