using System.ComponentModel;
using Loadout.Agents;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Starters;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Guides somebody new to programming and to AI through their first project.
/// </summary>
/// <remarks>
/// <para>
/// Split by who teaches each part best. The steps that touch the machine -
/// checking what is installed, making the folder, Git, registering and
/// protecting it - are done here, one at a time, and every one of them can be
/// driven by flags and tested. Deciding what to build, and building it, is
/// handed to a real agent, which is the better teacher for that.
/// </para>
/// <para>
/// Each agent session is its own decision. Both are previewed first and asked
/// about, and nothing that starts an agent runs without a person answering:
/// <c>--yes</c> covers the machine steps only. A session spends from somebody's
/// plan or account, and a flag typed to skip a question about a folder name is
/// not consent to that.
/// </para>
/// <para>
/// Running it again carries on from the first step not done, because the first
/// run is often where somebody finds Git missing, and installing it means a new
/// terminal.
/// </para>
/// </remarks>
[Description("Make your first project, step by step, with an agent to help plan and build it.")]
[CommandMeta(CommandCategory.Start, Intent = "first project beginner new to programming guide tutorial learn start begin", Mutates = true, Example = "--starter web-page")]
public sealed class StartCommand : AsyncCommand<StartCommand.Settings>
{
    private readonly IFirstProjectGuide _guide;
    private readonly IAgentRegistry _agents;
    private readonly IAgentLauncher _launcher;
    private readonly IPlatformPaths _paths;
    private readonly IExecutableResolver _resolver;
    private readonly IProcessLauncher _processes;
    private readonly PassthroughArguments _passthrough;
    private readonly WorkspaceSavePrompt _savePrompt;
    private readonly ReadingProfile _reading;
    private readonly IAnsiConsole _console;

    /// <summary>The agents found on this machine at the last check, by name.</summary>
    private IReadOnlyList<string> _installed = [];

    public StartCommand(
        IFirstProjectGuide guide,
        IAgentRegistry agents,
        IAgentLauncher launcher,
        IPlatformPaths paths,
        IExecutableResolver resolver,
        IProcessLauncher processes,
        PassthroughArguments passthrough,
        WorkspaceSavePrompt savePrompt,
        ReadingProfile reading,
        IAnsiConsole console)
    {
        _guide = guide;
        _agents = agents;
        _launcher = launcher;
        _paths = paths;
        _resolver = resolver;
        _processes = processes;
        _passthrough = passthrough;
        _savePrompt = savePrompt;
        _reading = reading;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--starter <STARTER>")]
        [Description("What to start from: web-page, checklist or own-idea.")]
        public string? Starter { get; init; }

        [CommandOption("--name <NAME>")]
        [Description("What to call the project.")]
        public string? Name { get; init; }

        [CommandOption("--path <PATH>")]
        [Description("Where to make it. Defaults to this machine's clone root plus the name.")]
        public string? Path { get; init; }

        [CommandOption("--yes")]
        [Description("Take the suggested answer to every question about this machine. Never starts an agent.")]
        public bool Yes { get; init; }

        [CommandOption("--again")]
        [Description("Start a new first project. The one already made is kept.")]
        public bool Again { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);
        var asking = settings.AllowsPrompting;

        if (settings.Starter is { Length: > 0 } wanted && Starters.Find(wanted) is null)
        {
            return output.Fail(
                $"There is no starter called '{wanted}'. Choose one of: "
                + string.Join(", ", Starters.All.Select(s => s.Id)) + ".",
                ExitCode.InvalidArguments);
        }

        var progress = await CarryOnFromAsync(output, settings, cancellationToken).ConfigureAwait(false);

        if (progress is { Next: null })
        {
            return Finished(output, progress);
        }

        var statuses = await CheckUntilReadyAsync(output, asking, cancellationToken).ConfigureAwait(false);

        if (statuses.Any(s => !s.Met))
        {
            if (output.IsJson)
            {
                output.WriteJson(Describe(statuses, progress, "missing-prerequisites"));
            }

            return (int)ExitCode.ConfigurationInvalid;
        }

        if (progress is null)
        {
            var starter = ChooseStarter(output, settings, asking);
            var name = ChooseName(settings, starter, asking);

            if (settings.DryRun)
            {
                output.WriteLine(
                    $"[bold]Would make[/] {Markup.Escape(name)} from the {Markup.Escape(starter.Id)} "
                    + "starter, start Git in it, register it with Loadout and protect it. Nothing was changed.");

                if (output.IsJson)
                {
                    output.WriteJson(Describe(statuses, null, "dry-run"));
                }

                return CommandOutput.Success();
            }

            var created = await _guide.CreateAsync(starter, name, settings.Path, cancellationToken).ConfigureAwait(false);

            if (created.Failed)
            {
                return output.Fail(created);
            }

            progress = created.Value!;

            output.WriteBlankLine();
            output.WriteLine($"[green]+[/] Made [bold]{Markup.Escape(progress.Name)}[/] in {Markup.Escape(progress.Path)}");
            output.WriteLine("[dim]  Git now keeps every version of its files, so anything can be undone.[/]");
            output.WriteLine($"[dim]  Loadout knows it as '{Markup.Escape(progress.Slug)}'.[/]");
        }
        else if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would carry on[/] with {Markup.Escape(progress.Name)}, next "
                + $"{Markup.Escape(StepName(progress.Next!.Value))}. Nothing was changed.");

            return CommandOutput.Success();
        }

        if (!progress.Has(GuideStep.Protect))
        {
            var protecting = await _guide.ProtectAsync(progress, cancellationToken).ConfigureAwait(false);

            if (protecting.Failed)
            {
                return output.Fail(protecting);
            }

            output.WriteLine("[green]+[/] Protected it, so the agent's own working files are never saved into it by accident");
        }

        if (progress.Next is GuideStep.Plan or GuideStep.Build && !asking)
        {
            // The handover. Everything up to here is the machine's; from here a
            // session spends from somebody's plan, which only they can agree to.
            output.WriteBlankLine();
            output.WriteLine(
                "The next step starts an agent, which uses your plan or account. That is yours to "
                + "decide, so it waits for you: run [bold]loadout start[/] in a terminal to carry on.");

            if (output.IsJson)
            {
                output.WriteJson(Describe(statuses, progress, "waiting-for-a-person"));
            }

            return CommandOutput.Success();
        }

        if (progress.Next is GuideStep.Plan)
        {
            var planned = await PlanAsync(output, settings, progress, cancellationToken).ConfigureAwait(false);

            if (planned is { } stopped)
            {
                return stopped;
            }
        }

        if (progress.Next is GuideStep.Build)
        {
            var built = await BuildAsync(output, settings, progress, cancellationToken).ConfigureAwait(false);

            if (built is { } stopped)
            {
                return stopped;
            }
        }

        return await ReviewAsync(output, statuses, progress, asking, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The progress to carry on from, or null to begin a new project.
    /// </summary>
    private async Task<FirstProjectProgress?> CarryOnFromAsync(
        CommandOutput output,
        Settings settings,
        CancellationToken ct)
    {
        if (settings.Again)
        {
            if (!settings.DryRun)
            {
                _guide.Forget();
            }

            return null;
        }

        var loaded = await _guide.LoadAsync(ct).ConfigureAwait(false);

        if (loaded.Failed || loaded.Value is not { } progress)
        {
            if (loaded.Failed)
            {
                output.WriteLine($"[yellow]note[/] {Shown.Safely(loaded.Error!)} Starting afresh.");
            }

            return null;
        }

        if (await _guide.StaleReasonAsync(progress, ct).ConfigureAwait(false) is { } stale)
        {
            output.WriteLine(
                $"[yellow]note[/] The guide was part-way through {Markup.Escape(progress.Name)}, but "
                + $"{Markup.Escape(stale)}. Starting a new one.");

            if (!settings.DryRun)
            {
                _guide.Forget();
            }

            return null;
        }

        if (progress.Next is { } next)
        {
            output.WriteLine(
                $"Carrying on with [bold]{Markup.Escape(progress.Name)}[/] from where you stopped: "
                + $"{Markup.Escape(StepName(next))}.");
        }

        return progress;
    }

    /// <summary>
    /// Checks what is needed, and while something is missing, says how to get
    /// it and offers to look again.
    /// </summary>
    private async Task<IReadOnlyList<PrerequisiteStatus>> CheckUntilReadyAsync(
        CommandOutput output,
        bool asking,
        CancellationToken ct)
    {
        var os = _paths.Host.OperatingSystem;

        while (true)
        {
            var agents = await _agents.DetectAllAsync(ct).ConfigureAwait(false);

            _installed = [.. agents.Where(a => a.IsInstalled).Select(a => a.Name)];
            var statuses = await _guide
                .CheckAsync([.. agents.Where(a => a.IsInstalled).Select(a => a.DisplayName)], ct)
                .ConfigureAwait(false);

            output.WriteLine("[bold]What a first project needs[/]");

            foreach (var status in statuses)
            {
                output.WriteLine(status.Met
                    ? $"  [green]+[/] {Markup.Escape(status.Prerequisite.Name)}"
                        + (status.Found is { } found ? $"  [dim]{Markup.Escape(found)}[/]" : string.Empty)
                    : $"  [yellow]![/] {Markup.Escape(status.Prerequisite.Name)}  [dim]missing[/]");
            }

            var missing = statuses.Where(s => !s.Met).ToList();

            if (missing.Count == 0)
            {
                return statuses;
            }

            // The first missing thing only. Somebody new to this does better
            // with one instruction than with four, and an identity cannot be
            // set before Git exists to set it in.
            var first = missing[0].Prerequisite;

            output.WriteBlankLine();
            output.WriteLine($"[bold]{Markup.Escape(first.Name)}[/]");
            output.WriteLine(Markup.Escape(first.Why));
            output.WriteLine("Type this, then press Enter:");

            foreach (var command in first.CommandsFor(os))
            {
                output.WriteLine($"  [bold]{Markup.Escape(command)}[/]");
            }

            if (first.Afterwards is { } afterwards)
            {
                output.WriteLine(Markup.Escape(afterwards));
            }

            output.WriteLine(
                $"[dim]More help: {Markup.Escape(Prerequisites.PageUrl)}, under \"{Markup.Escape(first.Section)}\".[/]");

            if (!asking || !_reading.Confirm(_console, "Done that? Check again?"))
            {
                output.WriteLine("[dim]Run[/] loadout start [dim]again when it is done, and the guide carries on from here.[/]");

                return statuses;
            }

            output.WriteBlankLine();
        }
    }

    private Starter ChooseStarter(CommandOutput output, Settings settings, bool asking)
    {
        if (settings.Starter is { Length: > 0 } id)
        {
            return Starters.Find(id)!;
        }

        if (!asking || settings.Yes)
        {
            return Starters.Find(Starters.Default)!;
        }

        output.WriteBlankLine();

        return _reading.Ask(
            _console,
            "What would you like to make first?",
            Starters.All,
            starter => $"{starter.Name} - {starter.Summary}");
    }

    private string ChooseName(Settings settings, Starter starter, bool asking)
    {
        if (settings.Name is { Length: > 0 } name)
        {
            return name.Trim();
        }

        if (!asking || settings.Yes)
        {
            return starter.DefaultName;
        }

        _reading.Ring(_console);

        return _console.Prompt(
            new TextPrompt<string>("What should it be called?")
                .DefaultValue(starter.DefaultName)).Trim();
    }

    /// <summary>The planning session. Returns an exit code when the guide stops here.</summary>
    private async Task<int?> PlanAsync(
        CommandOutput output,
        Settings settings,
        FirstProjectProgress progress,
        CancellationToken ct)
    {
        var starter = Starters.Find(progress.Starter) ?? Starters.Find(Starters.Default)!;

        output.WriteBlankLine();
        output.WriteLine("[bold]Next: decide what to build[/]");
        output.WriteLine(
            "An agent talks it through with you and writes the plan down in PLAN.md. It is asked "
            + "not to change anything else, so this step cannot break the project.");

        var launched = await PreviewThenLaunchAsync(
            output,
            settings,
            progress,
            FirstProjectBriefs.PlanTask,
            "advise",
            FirstProjectBriefs.Plan(starter, progress.Name),
            "Start the planning session?",
            ct).ConfigureAwait(false);

        if (launched is not null)
        {
            return launched;
        }

        var kept = await _guide.KeepPlanAsync(progress, ct).ConfigureAwait(false);

        if (kept.Failed)
        {
            return output.Fail(kept);
        }

        foreach (var other in kept.Value!.AlsoChanged)
        {
            output.WriteLine(
                $"[yellow]note[/] The planning session also changed {Markup.Escape(other)}, which it was "
                + "asked not to. It has been left as it is, for you to keep or undo.");
        }

        if (!kept.Value.Written)
        {
            output.WriteLine(
                "The session ended without writing PLAN.md, so there is nothing to build from yet. "
                + "Run [bold]loadout start[/] to plan again.");

            return CommandOutput.Success();
        }

        output.WriteLine("[green]+[/] The plan is in PLAN.md, saved as a version of the project.");

        return null;
    }

    /// <summary>The building session. Returns an exit code when the guide stops here.</summary>
    private async Task<int?> BuildAsync(
        CommandOutput output,
        Settings settings,
        FirstProjectProgress progress,
        CancellationToken ct)
    {
        var starter = Starters.Find(progress.Starter) ?? Starters.Find(Starters.Default)!;

        output.WriteBlankLine();
        output.WriteLine("[bold]Next: build it[/]");
        output.WriteLine(
            "An agent builds what PLAN.md describes, explaining each change as it goes. Every "
            + "version is kept, so nothing it does is permanent.");

        var launched = await PreviewThenLaunchAsync(
            output,
            settings,
            progress,
            FirstProjectBriefs.BuildTask,
            "implement",
            FirstProjectBriefs.Build(starter, progress.Name),
            "Start building?",
            ct).ConfigureAwait(false);

        if (launched is not null)
        {
            return launched;
        }

        progress.Finish(GuideStep.Build);

        var saved = await _guide.SaveAsync(progress, ct).ConfigureAwait(false);

        return saved.Failed ? output.Fail(saved) : null;
    }

    /// <summary>
    /// Shows what a session will be given and asks before starting it. Returns
    /// an exit code when the guide stops here, or null once the session ended.
    /// </summary>
    /// <remarks>
    /// The preview is a dry run of the same launch, so what is shown is what
    /// would start, not a description written separately that could drift
    /// from it.
    /// </remarks>
    private async Task<int?> PreviewThenLaunchAsync(
        CommandOutput output,
        Settings settings,
        FirstProjectProgress progress,
        string task,
        string mode,
        string brief,
        string question,
        CancellationToken ct)
    {
        var request = new LaunchRequest(
            progress.Slug,
            settings.Agent,
            settings.Offline,
            settings.NoSync,
            Profile: settings.Profile,
            Environment: settings.Environment,
            PassthroughArguments: _passthrough.Arguments,
            Task: task,
            Mode: mode,
            OpeningPrompt: brief,
            DryRun: true);

        var preview = await _launcher.LaunchAsync(request, ct).ConfigureAwait(false);

        // A new project's definition names Claude Code whatever is installed,
        // so somebody with only Codex passed the check above and was then
        // refused by the launch. Unless an agent was asked for by name, the
        // guide uses one this machine has, and says which.
        if (settings.Agent is null
            && _installed.Count > 0
            && (preview.Failed || !_installed.Contains(preview.Value!.AgentName ?? string.Empty, StringComparer.OrdinalIgnoreCase)))
        {
            var chosen = _installed[0];

            output.WriteLine(
                $"[dim]The project names an agent this computer does not have, so this uses "
                + $"{Markup.Escape(chosen)}, which it does.[/]");

            request = request with { AgentName = chosen };
            preview = await _launcher.LaunchAsync(request, ct).ConfigureAwait(false);
        }

        if (preview.Failed)
        {
            return output.Fail(preview);
        }

        var plan = preview.Value!.Plan;

        output.WriteBlankLine();
        output.WriteLine($"  {"Agent",-10} {Markup.Escape(preview.Value.AgentName ?? "the default agent")}");
        output.WriteLine($"  {"Folder",-10} {Markup.Escape(plan?.WorkingDirectory ?? progress.Path)}");

        if (plan?.Instructions is { } instructions)
        {
            output.WriteLine(
                $"  {"Given",-10} {instructions.Selected.Count} set(s) of instructions, about "
                + $"{instructions.Budget.EstimatedTokens:N0} tokens before you type anything");
        }

        output.WriteLine(
            "[dim]  Every reply after that also uses your plan or account. End the session whenever "
            + "you like; in Claude Code that is typing /exit.[/]");

        if (!_reading.Confirm(_console, question))
        {
            output.WriteLine("Stopped here. Nothing was spent. Run [bold]loadout start[/] when you are ready.");

            return CommandOutput.Success();
        }

        var launched = await _launcher
            .LaunchAsync(request with { DryRun = false }, ct)
            .ConfigureAwait(false);

        if (launched.Failed)
        {
            return output.Fail(launched);
        }

        LaunchReport.Write(output, launched.Value!, dryRun: false);

        await _savePrompt.HandleAsync(launched.Value!, settings).ConfigureAwait(false);

        return null;
    }

    private async Task<int> ReviewAsync(
        CommandOutput output,
        IReadOnlyList<PrerequisiteStatus> statuses,
        FirstProjectProgress progress,
        bool asking,
        CancellationToken ct)
    {
        var review = await _guide.ReviewAsync(progress, ct).ConfigureAwait(false);

        if (review.Failed)
        {
            return output.Fail(review);
        }

        var seen = review.Value!;

        output.WriteBlankLine();
        output.WriteLine("[bold]What changed[/]");

        if (seen.Saved.Length > 0)
        {
            output.WriteLine("Already saved as versions:");
            output.WriteLine($"[dim]{Markup.Escape(seen.Saved)}[/]");
        }

        if (seen.Unsaved.Count > 0)
        {
            output.WriteLine("Changed, and not saved as a version yet:");

            foreach (var file in seen.Unsaved)
            {
                output.WriteLine($"  {Markup.Escape(file)}");
            }

            if (asking && _reading.Confirm(_console, "Save these as a version, so you can always come back to them?"))
            {
                var kept = await _guide.KeepBuildAsync(progress, ct).ConfigureAwait(false);

                output.WriteLine(kept.Succeeded
                    ? "[green]+[/] Saved."
                    : $"[yellow]note[/] Not saved: {Shown.Safely(kept.Error!)}");
            }
        }

        if (seen.Saved.Length == 0 && seen.Unsaved.Count == 0)
        {
            output.WriteLine("[dim]Nothing has changed since the plan.[/]");
        }

        progress.Finish(GuideStep.Review);

        var saved = await _guide.SaveAsync(progress, ct).ConfigureAwait(false);

        if (saved.Failed)
        {
            return output.Fail(saved);
        }

        if (output.IsJson)
        {
            output.WriteJson(Describe(statuses, progress, "finished"));

            return CommandOutput.Success();
        }

        if (seen.Page is { } page)
        {
            output.WriteBlankLine();
            output.WriteLine("Open this in your browser to see it:");
            output.WriteLine($"  [bold]{Markup.Escape(new Uri(page).AbsoluteUri)}[/]");
        }

        await OfferGitHubAsync(output, progress, ct).ConfigureAwait(false);

        return Finished(output, progress);
    }

    /// <summary>
    /// Says how to publish the project, only when the GitHub CLI is already
    /// installed and signed in.
    /// </summary>
    /// <remarks>
    /// Printed rather than run. Putting something on the internet is a
    /// different decision from making it, and somebody who has not set up
    /// GitHub is not told they need to: the project is complete without it.
    /// </remarks>
    private async Task OfferGitHubAsync(CommandOutput output, FirstProjectProgress progress, CancellationToken ct)
    {
        if (_resolver.Resolve("gh") is not { } gh)
        {
            return;
        }

        var signedIn = await _processes.RunAsync(
            new ProcessRequest(gh, ["auth", "status"], progress.Path),
            TimeSpan.FromSeconds(20),
            ct).ConfigureAwait(false);

        if (signedIn.Failed || signedIn.Value?.Succeeded != true)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine("If you would like a copy on GitHub, private to you, this makes one and uploads it:");
        output.WriteLine(
            $"  [bold]gh repo create {Markup.Escape(progress.Slug)} --private --source "
            + $"\"{Markup.Escape(progress.Path)}\" --push[/]");
    }

    private static int Finished(CommandOutput output, FirstProjectProgress progress)
    {
        if (output.IsJson)
        {
            output.WriteJson(Describe([], progress, "finished"));

            return CommandOutput.Success();
        }

        output.WriteBlankLine();
        output.WriteLine($"[green]Your first project, {Markup.Escape(progress.Name)}, is done.[/]");
        output.WriteLine(
            $"To work on it again with the agent: [bold]loadout {Markup.Escape(progress.Slug)}[/]. "
            + "To make another: [bold]loadout start --again[/].");

        return CommandOutput.Success();
    }

    private static string StepName(GuideStep step) => step switch
    {
        GuideStep.Create => "making the project",
        GuideStep.Protect => "protecting it",
        GuideStep.Plan => "planning it with an agent",
        GuideStep.Build => "building it with an agent",
        _ => "looking at what changed",
    };

    private static object Describe(
        IReadOnlyList<PrerequisiteStatus> statuses,
        FirstProjectProgress? progress,
        string stoppedAt) => new
    {
        stoppedAt,
        prerequisites = statuses.Select(s => new
        {
            id = s.Prerequisite.Id,
            met = s.Met,
            found = s.Found,
        }),
        project = progress is null
            ? null
            : new
            {
                id = progress.Slug,
                name = progress.Name,
                path = progress.Path,
                starter = progress.Starter,
            },
        done = progress?.Done ?? [],
        next = progress?.Next?.ToString().ToLowerInvariant(),
    };
}
