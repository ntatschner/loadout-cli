using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Runs first-run configuration (spec sections 61 to 63).
/// <para>
/// Also reachable by running the launcher with no arguments on a machine that
/// has never been configured, which is where most people will meet it.
/// </para>
/// <para>
/// Every question can also be answered up front, so provisioning a machine does
/// not require somebody to sit and press keys. Both routes run the same code:
/// an interactive run is simply one where nothing was answered in advance, so
/// the scripted path cannot drift away from the one people see.
/// </para>
/// </summary>
[Description("Configure the launcher on this machine.")]
[CommandMeta(CommandCategory.Workspace, Intent = "first run start install configure new machine", Mutates = true, RequiresNetwork = true)]
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

        [CommandOption("--use-existing")]
        [Description("Clone an existing central workspace. Needs --remote.")]
        public bool UseExisting { get; init; }

        [CommandOption("--create-new")]
        [Description("Create a new central workspace. Needs --github, --remote or --stay-local.")]
        public bool CreateNew { get; init; }

        [CommandOption("--local-only")]
        [Description("Run without central storage.")]
        public bool LocalOnly { get; init; }

        [CommandOption("--github")]
        [Description("Publish a newly created workspace as a private GitHub repository.")]
        public bool GitHub { get; init; }

        [CommandOption("--stay-local")]
        [Description("Create the workspace but do not publish it anywhere yet.")]
        public bool StayLocal { get; init; }

        [CommandOption("--remote <URL>")]
        [Description("Git URL of the workspace, for --use-existing or a supplied host.")]
        public string? Remote { get; init; }

        [CommandOption("--branch <BRANCH>")]
        [Description("Workspace branch. Defaults to main.")]
        public string? Branch { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("Workspace name, and the default repository name.")]
        public string? Name { get; init; }

        [CommandOption("--register-discovered")]
        [Description("Register every repository found in the discovery roots.")]
        public bool RegisterDiscovered { get; init; }

        [CommandOption("--migrate")]
        [Description("Apply the migration plan rather than only showing it.")]
        public bool Migrate { get; init; }

        [CommandOption("--include-ignored")]
        [Description("Include files Git already ignores when migrating.")]
        public bool IncludeIgnored { get; init; }

        [CommandOption("--global-excludes")]
        [Description("Configure the global Git exclude file without asking.")]
        public bool GlobalExcludes { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var modes = new[] { settings.UseExisting, settings.CreateNew, settings.LocalOnly }
            .Count(chosen => chosen);

        if (modes > 1)
        {
            return output.Fail(
                "Choose one of --use-existing, --create-new or --local-only.",
                ExitCode.InvalidArguments);
        }

        if (_wizard.IsConfigured() && !settings.Force)
        {
            output.WriteLine("[dim]The launcher is already configured on this machine.[/]");
            output.WriteLine("[dim]Run[/] loadout setup --force [dim]to go through setup again, "
                + "or[/] loadout doctor [dim]to check it.[/]");

            return CommandOutput.Success();
        }

        var request = new SetupRequest(
            Mode: settings.UseExisting ? WorkspaceMode.UseExisting
                : settings.CreateNew ? WorkspaceMode.CreateNew
                : settings.LocalOnly ? WorkspaceMode.LocalOnly
                : WorkspaceMode.Ask,

            Host: settings.GitHub ? WorkspaceHost.GitHub
                : settings.StayLocal ? WorkspaceHost.None
                // A URL supplied alongside --create-new means "publish there",
                // which saves stating the obvious with a second flag.
                : settings.CreateNew && settings.Remote is not null ? WorkspaceHost.Url
                : WorkspaceHost.Ask,

            Remote: settings.Remote,
            Branch: settings.Branch,
            Name: settings.Name,
            RegisterDiscovered: settings.RegisterDiscovered,
            Migrate: settings.Migrate,
            IncludeIgnored: settings.IncludeIgnored,
            InstallGlobalExcludes: settings.GlobalExcludes ? true : null,
            Interactive: settings.AllowsPrompting);

        if (!settings.AllowsPrompting && request.MissingAnswer() is { } missing)
        {
            // Named precisely rather than "setup is interactive", so somebody
            // scripting this is told which flag to add.
            return output.Fail(missing, ExitCode.InvalidArguments);
        }

        return await _wizard.RunAsync(request).ConfigureAwait(false);
    }
}
