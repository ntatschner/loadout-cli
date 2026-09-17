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

        [CommandOption("--task <TASK>")]
        [Description("What you are about to do. Chooses the specialists the agent is given.")]
        public string? Task { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Posture: advise, investigate, implement or review.")]
        public string? Mode { get; init; }

        [CommandOption("--specialist <ID>")]
        [Description("Load this specialist whatever the evidence says. Repeatable.")]
        public string[] Specialist { get; init; } = [];

        [CommandOption("--without <ID>")]
        [Description("Never load this specialist for this launch. Repeatable.")]
        public string[] Without { get; init; } = [];
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
            _passthrough.Arguments,
            Task: settings.Task,
            Specialists: settings.Specialist,
            ExcludedSpecialists: settings.Without,
            Mode: settings.Mode,
            DryRun: settings.DryRun);

        var result = await _launcher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var outcome = result.Value!;

        LaunchReport.Write(output, outcome, settings.DryRun);

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

        [CommandOption("--task <TASK>")]
        [Description("What you are about to do. Chooses the specialists the agent is given.")]
        public string? Task { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Posture: advise, investigate, implement or review.")]
        public string? Mode { get; init; }

        [CommandOption("--specialist <ID>")]
        [Description("Load this specialist whatever the evidence says. Repeatable.")]
        public string[] Specialist { get; init; } = [];

        [CommandOption("--without <ID>")]
        [Description("Never load this specialist for this launch. Repeatable.")]
        public string[] Without { get; init; } = [];
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
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
            _passthrough.Arguments,
            Task: settings.Task,
            Specialists: settings.Specialist,
            ExcludedSpecialists: settings.Without,
            Mode: settings.Mode,
            DryRun: settings.DryRun);

        var result = await _launcher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        // The same report as 'launch'. It used to print the warnings and
        // nothing else, so 'here -v' silently showed less than 'launch -v'.
        LaunchReport.Write(output, result.Value!, settings.DryRun);

        await _savePrompt.HandleAsync(result.Value!, settings).ConfigureAwait(false);

        return result.Value!.AgentExitCode;
    }
}

/// <summary>
/// What a launch prints before the agent's own output begins, or instead of
/// it on a dry run. One implementation, so the two commands that launch
/// cannot report differently.
/// </summary>
internal static class LaunchReport
{
    internal static void Write(CommandOutput output, LaunchOutcome outcome, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(outcome);

        if (dryRun && outcome.Plan is { } plan && output.IsJson)
        {
            // Nothing else: a document with prose in front of it is not one.
            output.WriteJson(Describe(outcome, plan));

            return;
        }

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

        if (dryRun && outcome.Plan is { } resolved)
        {
            WritePlan(output, outcome, resolved);
        }
    }

    /// <summary>
    /// The launch as it would have happened: the command, what it would be
    /// given, and the specialists chosen with the reason for each.
    /// </summary>
    /// <remarks>
    /// This is what somebody asking for a dry run wants to read, and until it
    /// was written the whole report was a count of arguments. Environment
    /// variables are listed by name only, because their values are where
    /// resolved secrets end up.
    /// </remarks>
    private static void WritePlan(CommandOutput output, LaunchOutcome outcome, LaunchPlan plan)
    {
        output.WriteBlankLine();
        output.WriteLine("[bold]What would run[/]");
        output.WriteLine($"  {"Agent",-12} {Markup.Escape(outcome.AgentName ?? string.Empty)}  "
            + $"[dim]{Markup.Escape(plan.Executable)}[/]");
        output.WriteLine($"  {"Directory",-12} {Markup.Escape(plan.WorkingDirectory)}");

        if (plan.Task is { Length: > 0 } task)
        {
            output.WriteLine($"  {"Task",-12} {Markup.Escape(task)}");
        }

        output.WriteLine($"  {"Mode",-12} {Markup.Escape(plan.Mode ?? plan.Instructions?.Mode ?? "default")}"
            + (plan.Mode is null ? "  [dim]chosen by the task[/]" : string.Empty));

        if (plan.ContextPath is { Length: > 0 } context)
        {
            output.WriteLine(
                $"  {"Context",-12} {plan.ContextBytes / 1024.0:0.#} KB from {plan.ContextSources} "
                + $"source(s), profile '{Markup.Escape(plan.Profile ?? "default")}'  "
                + $"[dim]{Markup.Escape(context)}[/]");
        }
        else
        {
            output.WriteLine($"  {"Context",-12} [dim]none compiled[/]");
        }

        foreach (var file in plan.McpConfigFiles)
        {
            output.WriteLine($"  {"MCP",-12} {Markup.Escape(file)}");
        }

        if (plan.EnvironmentVariables.Count > 0)
        {
            output.WriteLine(
                $"  {"Environment",-12} {Markup.Escape(string.Join(", ", plan.EnvironmentVariables))}  "
                + "[dim]names only[/]");
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Command[/]");
        output.WriteLine($"  {Markup.Escape(plan.Executable)}");

        foreach (var argument in plan.Arguments)
        {
            output.WriteLine($"    {Markup.Escape(argument)}");
        }

        if (plan.Instructions is not { } instructions)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Specialists[/]");

        foreach (var selection in instructions.Selected)
        {
            output.WriteLine(
                $"  [green]+[/] {Markup.Escape(selection.Specialist.Id),-32} "
                + $"[dim]{Markup.Escape(selection.Reason)}[/]");
        }

        foreach (var omitted in instructions.Omitted)
        {
            output.WriteLine(
                $"  [dim]o {Markup.Escape(omitted.Specialist.Id),-32} {Markup.Escape(omitted.Reason)}[/]");
        }

        var budget = instructions.Budget;

        output.WriteLine(budget.TokenBudget > 0
            ? $"  [dim]about {budget.EstimatedTokens:N0} tokens, {budget.UsedFraction * 100:N0}% "
                + $"of {budget.TokenBudget:N0}[/]"
            : $"  [dim]about {budget.EstimatedTokens:N0} tokens[/]");
    }

    private static object Describe(LaunchOutcome outcome, LaunchPlan plan) => new
    {
        dryRun = true,
        agent = outcome.AgentName,
        agentSource = outcome.AgentSource.ToString(),
        task = plan.Task,
        mode = plan.Mode ?? plan.Instructions?.Mode,
        executable = plan.Executable,
        arguments = plan.Arguments,
        workingDirectory = plan.WorkingDirectory,
        environmentVariables = plan.EnvironmentVariables,
        mcpConfigFiles = plan.McpConfigFiles,
        context = plan.ContextPath is null
            ? null
            : new
            {
                path = plan.ContextPath,
                bytes = plan.ContextBytes,
                sources = plan.ContextSources,
                profile = plan.Profile,
            },
        specialists = plan.Instructions?.Selected.Select(s => new
        {
            id = s.Specialist.Id,
            kind = s.Specialist.Kind.ToString().ToLowerInvariant(),
            reason = s.Reason,
            estimatedTokens = s.Specialist.EstimatedTokens,
        }),
        omitted = plan.Instructions?.Omitted.Select(s => new
        {
            id = s.Specialist.Id,
            reason = s.Reason,
        }),
        budget = plan.Instructions is { } i
            ? new { estimatedTokens = i.Budget.EstimatedTokens, tokenBudget = i.Budget.TokenBudget }
            : null,
        warnings = outcome.Warnings,
    };
}
