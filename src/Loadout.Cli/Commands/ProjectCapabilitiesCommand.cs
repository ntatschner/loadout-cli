using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Capabilities;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Capabilities;
using Loadout.Models.Projects;
using Loadout.Models.Tasks;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>What a project already has, such as CI, release and environments, and what it lacks.</summary>
/// <remarks>
/// <para>
/// Read-only. It shows the profile last kept, and how old it is, without
/// reaching GitHub, so it is instant and works offline. <c>--refresh</c>
/// builds it again, asking GitHub, and keeps it.
/// </para>
/// <para>
/// A project with no profile kept yet is shown from its files alone, with
/// every check only GitHub can answer marked unknown, and nothing is kept.
/// </para>
/// </remarks>
[Description("Show what a project already has, such as CI, release and environments, and what it lacks. --refresh asks GitHub again.")]
[CommandMeta(CommandCategory.Projects,
    Intent = "capabilities ci release environments branch protection missing gaps profile github",
    Mutates = true,
    Example = "loadout-cli --refresh")]
public sealed class ProjectCapabilitiesCommand : AsyncCommand<ProjectCapabilitiesCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly ICapabilityProfiles _profiles;
    private readonly ITaskService _tasks;
    private readonly ICapabilityOffers _offers;
    private readonly IAnsiConsole _console;

    public ProjectCapabilitiesCommand(
        IProjectService projects,
        ICapabilityProfiles profiles,
        ITaskService tasks,
        ICapabilityOffers offers,
        IAnsiConsole console)
    {
        _projects = projects;
        _profiles = profiles;
        _tasks = tasks;
        _offers = offers;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<project>")]
        [Description("Project slug, alias or name.")]
        public string Project { get; init; } = string.Empty;

        [CommandOption("--refresh")]
        [Description("Build it again from the repository and GitHub, and keep it. Without this, GitHub is not asked.")]
        public bool Refresh { get; init; }

        [CommandOption("--not-needed <CHECK>")]
        [Description("Say this project will do without a check, so it stops being offered. Undone with --needed.")]
        public string? NotNeeded { get; init; }

        [CommandOption("--release-links")]
        [Description("With --not-needed: also take off the links holding work on its open prerequisite, and drop that task.")]
        public bool ReleaseLinks { get; init; }

        [CommandOption("--needed <CHECK>")]
        [Description("Undo a 'Not needed' answer, and list the open work the gap now touches.")]
        public string? Needed { get; init; }

        [CommandOption("--tick <TASK>")]
        [Description("Tick the step only a person can do on a prerequisite, such as setting a release secret.")]
        public string? Tick { get; init; }

        [CommandOption("--renew <TASK>")]
        [Description("Rewrite an open prerequisite to the current template and stacks. Refused while a run owns it.")]
        public string? Renew { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var resolved = await _projects.ResolveAsync(settings.Project).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var project = resolved.Value!;

        if (await ChangeAsync(output, settings, project, cancellationToken).ConfigureAwait(false) is { } exit)
        {
            return exit;
        }

        var cached = settings.Refresh ? null : await _profiles.CachedAsync(project.Entry.Slug, cancellationToken).ConfigureAwait(false);
        var profile = cached ?? await _profiles.BuildAsync(project, askGitHub: settings.Refresh, cancellationToken).ConfigureAwait(false);
        var kept = cached is not null;
        IReadOnlyList<PrerequisiteSettled> closed = [];

        if (settings.Refresh && !settings.DryRun)
        {
            var saved = await _profiles.KeepAsync(profile, cancellationToken).ConfigureAwait(false);

            if (saved.Failed)
            {
                return output.Fail(saved);
            }

            kept = true;

            var settled = await _offers.SettleAsync(project, Environment.UserName, cancellationToken).ConfigureAwait(false);

            if (settled.Failed)
            {
                return output.Fail(settled);
            }

            closed = settled.Value!;
        }

        var waiting = await WaitingOnCiAsync(project.Entry.Slug, profile, cancellationToken).ConfigureAwait(false);
        var answers = await _offers.AnswersAsync(project.Entry.Slug, cancellationToken).ConfigureAwait(false);
        var stale = await _offers.StaleAsync(project.Entry.Slug, profile, cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = profile.Project,
                built = profile.BuiltUtc,
                kept,
                github = profile.GitHub,
                stacks = profile.Stacks.Select(stack => new { name = stack.Name, manifest = stack.Manifest }),
                checks = profile.Checks.Select(check => new
                {
                    id = check.Id,
                    state = Name(check.State),
                    evidence = check.Evidence,
                    scope = check.Scope.Length > 0 ? check.Scope : null,
                    types = CapabilityChecks.TypesOf(check).Select(TaskTypes.Name),
                    declared = check.Declared,
                }),
                notes = profile.Notes,
                needsCi = waiting.Select(one => new { id = one.Task, criterion = one.Criterion }),
                notNeeded = answers.NotNeeded.Select(answer => new { check = answer.Check, by = answer.By, utc = answer.Utc }),
                stale = stale.Select(one => new { task = one.Task, check = one.Check, why = one.Why }),
                settled = closed.Select(one => new { task = one.Task, closed = one.Closed, reason = one.Reason, released = one.Released.Select(TaskRequires.Name) }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[bold]{Markup.Escape(profile.Project)}[/]  [dim]"
            + (cached is not null
                ? $"built {Markup.Escape(SessionDisplay.Ago(profile.BuiltUtc))}, GitHub {Markup.Escape(profile.GitHub)}"
                : settings.Refresh
                    ? $"built now, GitHub {Markup.Escape(profile.GitHub)}"
                    : "not built yet, so shown from the repository's files alone")
            + "[/]");
        output.WriteBlankLine();

        var width = profile.Checks.Max(check => check.Id.Length);

        foreach (var check in profile.Checks)
        {
            output.WriteLine(
                $"  {Markup.Escape(check.Id.PadRight(width))}  {Coloured(check.State)}  {Markup.Escape(check.Evidence)}"
                + (check.Scope.Length > 0 ? $" [dim]It needs {Markup.Escape(check.Scope)}.[/]" : string.Empty));
        }

        if (profile.Stacks.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine(
                "Stacks: " + Markup.Escape(string.Join(", ", profile.Stacks.Select(stack => $"{stack.Name} ({stack.Manifest})"))));
        }

        foreach (var note in profile.Notes)
        {
            output.WriteLine($"[dim]{Markup.Escape(note)}[/]");
        }

        if (waiting.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[yellow]Done-when that need CI, on a project with none:[/]");

            foreach (var (task, criterion) in waiting)
            {
                output.WriteLine($"  [bold]{Markup.Escape(task)}[/]  {Markup.Escape(criterion)}");
            }
        }

        if (answers.NotNeeded.Count > 0)
        {
            output.WriteBlankLine();

            foreach (var answer in answers.NotNeeded)
            {
                output.WriteLine(
                    $"Not needed: [bold]{Markup.Escape(answer.Check)}[/], said by {Markup.Escape(answer.By)}. "
                    + $"[dim]Undo with --needed {Markup.Escape(answer.Check)}.[/]");
            }
        }

        foreach (var one in stale)
        {
            output.WriteLine($"[yellow]{Markup.Escape(one.Task)}[/] was {Markup.Escape(one.Why)}. [dim]Rewrite it with --renew {Markup.Escape(one.Task)}.[/]");
        }

        foreach (var one in closed)
        {
            output.WriteLine(
                one.Closed
                    ? $"[green]Closed[/] {Markup.Escape(one.Task)}: {Markup.Escape(one.Reason)}"
                    : $"[green]Released[/] what waited on {Markup.Escape(one.Task)}, which a run still owns: {Markup.Escape(one.Reason)}");
        }

        if (cached is null && !settings.Refresh)
        {
            output.WriteBlankLine();
            output.WriteLine($"[dim]  Ask GitHub too, and keep it, with: loadout project capabilities {Markup.Escape(profile.Project)} --refresh[/]");
        }
        else if (settings.Refresh && settings.DryRun)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing was kept.[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>Carries out whichever change was asked for. Null when none was, so the list is shown.</summary>
    private async Task<int?> ChangeAsync(CommandOutput output, Settings settings, ProjectResolution project, CancellationToken ct)
    {
        var asked = new[] { settings.NotNeeded, settings.Needed, settings.Tick, settings.Renew }.Count(one => one is { Length: > 0 });

        if (asked == 0)
        {
            return settings.ReleaseLinks ? output.Fail("--release-links goes with --not-needed.", ExitCode.InvalidArguments) : null;
        }

        if (asked > 1 || settings.Refresh)
        {
            return output.Fail("Ask for one change at a time, and not with --refresh.", ExitCode.InvalidArguments);
        }

        var slug = project.Entry.Slug;
        var by = Environment.UserName;

        if (settings.DryRun)
        {
            output.WriteLine("Would make that change. Nothing was written.");

            return CommandOutput.Success();
        }

        if (settings.Tick is { Length: > 0 } tick)
        {
            var ticked = await _offers.TickAsync(slug, tick, by, ct).ConfigureAwait(false);

            return ticked.Failed ? output.Fail(ticked) : Said(output, new { task = ticked.Value!.Id, ticked = true }, $"Ticked the person's step on {ticked.Value.Id}.");
        }

        if (settings.Renew is { Length: > 0 } renew)
        {
            var renewed = await _offers.RenewAsync(project, renew, by, ct).ConfigureAwait(false);

            return renewed.Failed ? output.Fail(renewed) : Said(output, new { task = renewed.Value!.Id, renewed = true }, $"Rewrote {renewed.Value.Id} to the current template. A ticked secrets step is cleared if its stack changed.");
        }

        if (settings.Needed is { Length: > 0 } needed)
        {
            var touched = await _offers.NeededAsync(project, needed, ct).ConfigureAwait(false);

            if (touched.Failed)
            {
                return output.Fail(touched);
            }

            return Said(
                output,
                new { check = needed, touches = touched.Value },
                touched.Value!.Count > 0
                    ? $"{needed} is offered again. It touches: {string.Join(", ", touched.Value)}. Each can take it with: loadout task offers <id> --project {slug} --accept {needed}"
                    : $"{needed} is offered again. No open work is touched by it.");
        }

        var outcome = await _offers.NotNeededAsync(project, settings.NotNeeded!, settings.ReleaseLinks, by, ct).ConfigureAwait(false);

        if (outcome.Failed)
        {
            return output.Fail(outcome);
        }

        // The answer is kept either way. A link is only taken off when somebody
        // confirms it, because releasing work quietly is what the link prevents.
        if (outcome.Value is { Prerequisite: { } open, Released: false, Waiting.Count: > 0 } && settings.AllowsPrompting
            && _console.Confirm($"{Markup.Escape(open)} is holding {Markup.Escape(string.Join(", ", outcome.Value.Waiting.Select(TaskRequires.Name)))}. Take those links off and drop it?", defaultValue: false))
        {
            outcome = await _offers.NotNeededAsync(project, settings.NotNeeded!, true, by, ct).ConfigureAwait(false);

            if (outcome.Failed)
            {
                return output.Fail(outcome);
            }
        }

        var held = outcome.Value!;

        return Said(
            output,
            new { check = settings.NotNeeded, prerequisite = held.Prerequisite, waiting = held.Waiting.Select(TaskRequires.Name), released = held.Released },
            held is { Prerequisite: { } still, Released: false, Waiting.Count: > 0 }
                ? $"{settings.NotNeeded} is marked not needed, and {still} still holds {string.Join(", ", held.Waiting.Select(TaskRequires.Name))}. --release-links takes those links off."
                : $"{settings.NotNeeded} is marked not needed.");
    }

    private static int Said(CommandOutput output, object json, string line)
    {
        if (output.IsJson)
        {
            output.WriteJson(json);
        }
        else
        {
            output.WriteLine(Markup.Escape(line));
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Open work with a done-when that only CI can meet, on a project where CI
    /// is a gap the profile has seen: a criterion no run can ever meet here.
    /// </summary>
    private async Task<IReadOnlyList<(string Task, string Criterion)>> WaitingOnCiAsync(
        string slug,
        CapabilityProfile profile,
        CancellationToken ct)
    {
        if (profile.Checks.FirstOrDefault(check => check.Id == CapabilityChecks.Ci) is not { State: CapabilityState.Gap })
        {
            return [];
        }

        var listed = await _tasks.ListAsync(slug, ct).ConfigureAwait(false);

        if (listed.Failed)
        {
            return [];
        }

        return
        [
            .. listed.Value!
                .Where(task => task.State is TaskState.Open or TaskState.Doing or TaskState.Blocked)
                .SelectMany(task => task.DoneWhen
                    .Where(criterion => CriterionTags.Of(criterion, null).Needs == CriterionNeeds.Ci)
                    .Select(criterion => (task.Id, criterion))),
        ];
    }

    /// <summary>A state as the command line and JSON name it.</summary>
    internal static string Name(CapabilityState state) => state switch
    {
        CapabilityState.ManualRelease => "manual-release",
        CapabilityState.NoPermission => "no-permission",
        _ => state.ToString().ToLowerInvariant(),
    };

    private static string Coloured(CapabilityState state)
    {
        var said = Name(state).PadRight(14);

        return state switch
        {
            CapabilityState.Present or CapabilityState.Delegated => $"[green]{said}[/]",
            CapabilityState.Gap => $"[yellow]{said}[/]",
            _ => $"[dim]{said}[/]",
        };
    }
}
