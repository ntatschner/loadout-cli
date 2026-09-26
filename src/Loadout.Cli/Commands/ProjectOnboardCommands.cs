using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Models.Tasks;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Onboards a project now, in a session you watch, or records that you are
/// skipping it.
/// </summary>
/// <remarks>
/// Registering queues onboarding for the first session started without a task
/// of its own. This is for doing it on purpose: straight away, again after the
/// project has changed shape, or not at all.
/// </remarks>
[Description("Onboard a project now in an agent session, or record that it is skipped.")]
[CommandMeta(CommandCategory.Projects, Intent = "onboard learn set up new project first session skip",
    Mutates = true)]
public sealed class ProjectOnboardCommand : AsyncCommand<ProjectOnboardCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly Core.Tasks.ITaskService _tasks;
    private readonly IAgentLauncher _launcher;
    private readonly PassthroughArguments _passthrough;
    private readonly WorkspaceSavePrompt _savePrompt;
    private readonly IAnsiConsole _console;

    public ProjectOnboardCommand(
        IProjectService projects,
        Core.Tasks.ITaskService tasks,
        IAgentLauncher launcher,
        PassthroughArguments passthrough,
        WorkspaceSavePrompt savePrompt,
        IAnsiConsole console)
    {
        _projects = projects;
        _tasks = tasks;
        _launcher = launcher;
        _passthrough = passthrough;
        _savePrompt = savePrompt;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--skip")]
        [Description("Record that this project is not being onboarded, instead of starting a session.")]
        public bool Skip { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Posture for the session. Defaults to investigate.")]
        public string? Mode { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.Skip)
        {
            if (settings.DryRun)
            {
                output.WriteLine(
                    $"[bold]Would record[/] onboarding as skipped for '{Markup.Escape(slug)}'. "
                    + "Nothing was changed.");

                return CommandOutput.Success();
            }

            var skipped = await _tasks.DeclareAsync(
                slug,
                ProjectOnboardingTask.Id,
                TaskState.Dropped,
                Environment.UserName,
                ProjectOnboardingTask.Title,
                "Skipped with 'loadout project onboard --skip'.",
                cancellationToken).ConfigureAwait(false);

            if (skipped.Failed)
            {
                return output.Fail(skipped);
            }

            output.WriteLine(
                $"[green]Skipped[/] onboarding for {Markup.Escape(slug)}. Recorded as the task "
                + $"'{ProjectOnboardingTask.Id}', so later sessions and other machines can see it was chosen.");

            return CommandOutput.Success();
        }

        // Opened before the launch rather than after, so the session is the one
        // the launcher treats as the onboarding, and a project onboarded before
        // is simply onboarded again. Not on a dry run, which changes nothing.
        if (!settings.DryRun)
        {
            var opened = await _tasks.DeclareAsync(
                slug,
                ProjectOnboardingTask.Id,
                TaskState.Doing,
                Environment.UserName,
                ProjectOnboardingTask.Title,
                "Started with 'loadout project onboard'.",
                cancellationToken).ConfigureAwait(false);

            if (opened.Failed)
            {
                return output.Fail(opened);
            }
        }

        // Named in full as well, so a dry run shows the onboarding session even
        // when nothing is pending for the launcher to find.
        var request = new LaunchRequest(
            slug,
            settings.Agent,
            settings.Offline,
            settings.NoSync,
            Profile: settings.Profile,
            Environment: settings.Environment,
            PassthroughArguments: _passthrough.Arguments,
            Task: ProjectOnboardingTask.Title,
            Specialists: [ProjectOnboardingTask.Skill],
            Mode: settings.Mode ?? ProjectOnboardingTask.Mode,
            DryRun: settings.DryRun);

        var result = await _launcher.LaunchAsync(request, cancellationToken).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        LaunchReport.Write(output, result.Value!, settings.DryRun);

        await _savePrompt.HandleAsync(result.Value!, settings).ConfigureAwait(false);

        return result.Value!.AgentExitCode;
    }
}

/// <summary>
/// Shows, applies or discards the settings change a session proposed for a
/// project.
/// </summary>
[Description("Show, apply or discard a proposed change to a project's settings.")]
[CommandMeta(CommandCategory.Projects, Intent = "proposal proposed settings project.yaml apply review onboarding",
    Mutates = true)]
public sealed class ProjectProposalCommand : AsyncCommand<ProjectProposalCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly ISettingsProposals _proposals;
    private readonly IAnsiConsole _console;

    public ProjectProposalCommand(
        IProjectService projects,
        ISettingsProposals proposals,
        IAnsiConsole console)
    {
        _projects = projects;
        _proposals = proposals;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--apply")]
        [Description("Write the proposal over project.yaml.")]
        public bool Apply { get; init; }

        [CommandOption("--discard")]
        [Description("Remove the proposal without applying it.")]
        public bool Discard { get; init; }

        public override ValidationResult Validate() =>
            Apply && Discard
                ? ValidationResult.Error("--apply and --discard cannot be used together.")
                : base.Validate();
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var read = await _proposals.ReadAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        if (read.Value is not { } view)
        {
            if (settings.Apply || settings.Discard)
            {
                return output.Fail($"There is no proposal for '{slug}'.", Models.ExitCode.InvalidArguments);
            }

            output.WriteLine($"There is no proposal for '{Markup.Escape(slug)}'.");

            return CommandOutput.Success();
        }

        if (output.IsJson && !settings.Apply && !settings.Discard)
        {
            output.WriteJson(new
            {
                project = slug,
                proposedBy = view.Proposal.ProposedBy,
                proposedUtc = view.Proposal.ProposedUtc,
                reasons = view.Proposal.Reasons,
                stale = view.Stale,
                changes = view.Changes,
            });

            return CommandOutput.Success();
        }

        Show(output, slug, view);

        if (settings.DryRun && (settings.Apply || settings.Discard))
        {
            output.WriteLine(
                $"[bold]Would {(settings.Apply ? "apply" : "discard")}[/] it. Nothing was changed.");

            return CommandOutput.Success();
        }

        if (settings.Discard)
        {
            var discarded = await _proposals.DiscardAsync(slug, cancellationToken).ConfigureAwait(false);

            if (discarded.Failed)
            {
                return output.Fail(discarded);
            }

            output.WriteLine("[green]Discarded.[/] project.yaml is unchanged.");

            return CommandOutput.Success();
        }

        if (settings.Apply)
        {
            var applied = await _proposals.ApplyAsync(slug, cancellationToken).ConfigureAwait(false);

            if (applied.Failed)
            {
                return output.Fail(applied);
            }

            output.WriteLine(
                "[green]Applied.[/] 'loadout workspace save' commits it, and 'loadout instructions explain "
                + $"--project {Markup.Escape(slug)}' shows what a session gets now.");

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[dim]loadout project proposal {Markup.Escape(slug)} --apply   or   --discard[/]");

        return CommandOutput.Success();
    }

    private static void Show(CommandOutput output, string slug, SettingsProposalView view)
    {
        output.WriteLine(
            $"[bold]Proposed for {Markup.Escape(slug)}[/] by {Markup.Escape(view.Proposal.ProposedBy)}, "
            + $"{view.Proposal.ProposedUtc:yyyy-MM-dd HH:mm} UTC");

        if (view.Stale)
        {
            output.WriteLine(
                "[yellow]project.yaml has changed since this was proposed.[/] It cannot be applied; "
                + "discard it and propose again.");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("[bold]Why[/]");

        foreach (var line in view.Proposal.Reasons.Split('\n'))
        {
            output.WriteLine($"  {Markup.Escape(line.TrimEnd())}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine("[bold]Changes to project.yaml[/]");

        foreach (var line in view.Changes)
        {
            var colour = line.StartsWith('+') ? "green" : line.StartsWith('-') ? "red" : "dim";

            output.WriteLine($"  [{colour}]{Markup.Escape(line)}[/]");
        }

        output.WriteLine(string.Empty);
    }
}
