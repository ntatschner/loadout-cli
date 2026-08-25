using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Launches an agent against a project (spec sections 22, 35 and 45).
/// <para>
/// Also reached as <c>loadout &lt;project&gt;</c>, which is the shortest path
/// the spec asks for and the one people will actually type.
/// </para>
/// </summary>
[Description("Launch an agent against a project.")]
[CommandMeta(CommandCategory.Start, Intent = "run open work begin agent claude codex", Example = "starstats")]
public sealed class LaunchCommand : AsyncCommand<LaunchCommand.Settings>
{
    private readonly IAgentLauncher _launcher;
    private readonly PassthroughArguments _passthrough;
    private readonly WorkspaceSavePrompt _savePrompt;
    private readonly IAnsiConsole _console;

    public LaunchCommand(
        IAgentLauncher launcher,
        PassthroughArguments passthrough,
        WorkspaceSavePrompt savePrompt,
        IAnsiConsole console)
    {
        _launcher = launcher;
        _passthrough = passthrough;
        _savePrompt = savePrompt;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandOption("--worktree <WORKTREE>")]
        [Description("Launch in a named worktree instead of the main working tree.")]
        public string? Worktree { get; init; }

        [CommandOption("--handoff")]
        [Description("Append the most recent handoff to the compiled context.")]
        public bool Handoff { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);

        var request = new LaunchRequest(
            settings.Project,
            settings.Agent,
            settings.Offline,
            settings.NoSync,
            settings.Worktree,
            settings.Profile,
            settings.Handoff,
            settings.Environment,
            _passthrough.Arguments);

        var result = await _launcher.LaunchAsync(request).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var outcome = result.Value!;

        // Verbose shows every preflight check, including the ones that passed,
        // which is what someone debugging a launch actually wants to see.
        if (output.IsVerbose && outcome.Preflight is not null)
        {
            foreach (var check in outcome.Preflight.Checks)
            {
                output.WriteVerbose(
                    $"[dim]{Markup.Escape(check.Category)}[/] {Markup.Escape(check.Name)}  "
                    + $"[dim]{Markup.Escape(check.Detail)}[/]");
            }
        }

        // Warnings are printed before the agent's own output would have
        // started, so a stale workspace is visible rather than buried.
        foreach (var warning in outcome.Warnings)
        {
            output.WriteLine($"[yellow]warning[/] {Markup.Escape(warning)}");
        }

        await _savePrompt.HandleAsync(outcome, settings).ConfigureAwait(false);

        // The agent's exit code is the command's exit code. A script wrapping
        // the launcher should see exactly what it would have seen running the
        // agent directly (spec section 40).
        return outcome.AgentExitCode;
    }
}

/// <summary>
/// Launches the project that owns the current directory (spec section 24).
/// </summary>
[Description("Launch the agent for the repository in the current directory.")]
[CommandMeta(CommandCategory.Start, Intent = "this repo current directory run here")]
public sealed class HereCommand : AsyncCommand<HereCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IAgentLauncher _launcher;
    private readonly PassthroughArguments _passthrough;
    private readonly WorkspaceSavePrompt _savePrompt;
    private readonly IAnsiConsole _console;

    public HereCommand(
        IProjectService projects,
        IAgentLauncher launcher,
        PassthroughArguments passthrough,
        WorkspaceSavePrompt savePrompt,
        IAnsiConsole console)
    {
        _projects = projects;
        _launcher = launcher;
        _passthrough = passthrough;
        _savePrompt = savePrompt;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--worktree <WORKTREE>")]
        [Description("Launch in a named worktree instead of the main working tree.")]
        public string? Worktree { get; init; }

        [CommandOption("--handoff")]
        [Description("Append the most recent handoff to the compiled context.")]
        public bool Handoff { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        var output = new CommandOutput(_console, settings);
        var directory = settings.Repo ?? Directory.GetCurrentDirectory();

        var resolveResult = await _projects.ResolveFromDirectoryAsync(directory).ConfigureAwait(false);
        if (resolveResult.Failed)
        {
            return output.Fail(resolveResult);
        }

        var project = resolveResult.Value!;

        output.WriteLine($"[dim]Detected project:[/] {Markup.Escape(project.Entry.Name)}");

        var request = new LaunchRequest(
            project.Entry.Slug,
            settings.Agent,
            settings.Offline,
            settings.NoSync,
            settings.Worktree,
            settings.Profile,
            settings.Handoff,
            settings.Environment,
            _passthrough.Arguments);

        var result = await _launcher.LaunchAsync(request).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        foreach (var warning in result.Value!.Warnings)
        {
            output.WriteLine($"[yellow]warning[/] {Markup.Escape(warning)}");
        }

        await _savePrompt.HandleAsync(result.Value, settings).ConfigureAwait(false);

        return result.Value.AgentExitCode;
    }
}
