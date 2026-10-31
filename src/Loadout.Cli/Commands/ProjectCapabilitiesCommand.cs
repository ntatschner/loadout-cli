using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Capabilities;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Tasks;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Capabilities;
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
    private readonly IAnsiConsole _console;

    public ProjectCapabilitiesCommand(
        IProjectService projects,
        ICapabilityProfiles profiles,
        ITaskService tasks,
        IAnsiConsole console)
    {
        _projects = projects;
        _profiles = profiles;
        _tasks = tasks;
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
        var cached = settings.Refresh ? null : await _profiles.CachedAsync(project.Entry.Slug, cancellationToken).ConfigureAwait(false);
        var profile = cached ?? await _profiles.BuildAsync(project, askGitHub: settings.Refresh, cancellationToken).ConfigureAwait(false);
        var kept = cached is not null;

        if (settings.Refresh && !settings.DryRun)
        {
            var saved = await _profiles.KeepAsync(profile, cancellationToken).ConfigureAwait(false);

            if (saved.Failed)
            {
                return output.Fail(saved);
            }

            kept = true;
        }

        var waiting = await WaitingOnCiAsync(project.Entry.Slug, profile, cancellationToken).ConfigureAwait(false);

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
                    types = CapabilityChecks.Find(check.Id)?.Types.Select(TaskTypes.Name) ?? [],
                }),
                notes = profile.Notes,
                needsCi = waiting.Select(one => new { id = one.Task, criterion = one.Criterion }),
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
