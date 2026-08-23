using Loadout.Core.Policies;
using Loadout.Core.Projects;
using Loadout.Models.Policies;
using Loadout.Models.Projects;
using Spectre.Console;

namespace Loadout.Tui;

/// <summary>How much to do without asking.</summary>
/// <param name="Interactive">Whether questions may be put to a person at all.</param>
/// <param name="RegisterEverything">Register every discovered repository without asking.</param>
/// <param name="Migrate">Apply the migration without asking.</param>
/// <param name="IncludeIgnored">Also move agent files Git already ignores.</param>
public sealed record OnboardingOptions(
    bool Interactive = true,
    bool RegisterEverything = false,
    bool Migrate = false,
    bool IncludeIgnored = false);

/// <summary>
/// Takes repositories from "on the disk" to "known to the launcher".
/// <para>
/// Shared by first-run setup and the launcher, because bringing a project in is
/// not something that only happens once. Registering a project a fortnight
/// later needed the command line, so the interactive path existed only for
/// people who had never used the tool before — precisely backwards.
/// </para>
/// </summary>
public interface IProjectOnboarding
{
    /// <summary>
    /// Finds unregistered repositories under the configured roots, registers
    /// the ones chosen, and offers to move any agent files they carry.
    /// </summary>
    Task<IReadOnlyList<ProjectResolution>> AddAsync(
        OnboardingOptions options,
        CancellationToken ct = default);

    /// <summary>Registers one repository by path, and offers the same migration.</summary>
    Task<ProjectResolution?> AddPathAsync(
        string path,
        OnboardingOptions options,
        CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ProjectOnboarding : IProjectOnboarding
{
    private readonly IAnsiConsole _console;
    private readonly IProjectService _projects;
    private readonly IMigrationService _migrations;

    public ProjectOnboarding(
        IAnsiConsole console,
        IProjectService projects,
        IMigrationService migrations)
    {
        _console = console;
        _projects = projects;
        _migrations = migrations;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProjectResolution>> AddAsync(
        OnboardingOptions options,
        CancellationToken ct = default)
    {
        var discovered = await _projects.DiscoverAsync(ct).ConfigureAwait(false);

        if (discovered.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(discovered.Error!)}[/]");

            return [];
        }

        var unregistered = discovered.Value!.Where(r => !r.IsRegistered).ToList();

        if (unregistered.Count == 0)
        {
            _console.MarkupLine(
                "[dim]No unregistered repositories were found under the folders being scanned.[/]");

            _console.MarkupLine(
                "[dim]Add a folder to scan with:[/] loadout config set discovery-roots <paths>");

            return [];
        }

        _console.WriteLine();
        _console.MarkupLine($"[bold]{unregistered.Count} repositories found[/]");

        IReadOnlyList<string> chosen;

        if (options.RegisterEverything)
        {
            chosen = unregistered.Select(r => r.Path).ToList();
        }
        else if (!options.Interactive)
        {
            // Registering somebody's whole disk because nobody could be asked
            // would be a poor default, so it stays opt-in.
            _console.MarkupLine(
                "[dim]Register them with:[/] loadout project add <path>  "
                + "[dim]or rerun with --register-discovered[/]");

            return [];
        }
        else
        {
            chosen = _console.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Register any of these now? [dim](space to select, enter to confirm)[/]")
                    .NotRequired()
                    .PageSize(15)
                    .MoreChoicesText("[dim](move up and down for more)[/]")
                    .InstructionsText("[dim]Nothing is registered unless you pick it.[/]")
                    .AddChoices(unregistered.Select(r => r.Path)));
        }

        var registered = new List<ProjectResolution>();

        foreach (var path in chosen)
        {
            var result = await _projects.AddAsync(path, null, ct).ConfigureAwait(false);

            if (result.Succeeded)
            {
                registered.Add(result.Value!);
                _console.MarkupLine($"[green]+[/] {Markup.Escape(result.Value!.Entry.Name)}");
            }
            else
            {
                _console.MarkupLine(
                    $"[yellow]![/] {Markup.Escape(path)}  [dim]{Markup.Escape(result.Error!)}[/]");
            }
        }

        await OfferMigrationAsync(registered, options, ct).ConfigureAwait(false);

        return registered;
    }

    /// <inheritdoc />
    public async Task<ProjectResolution?> AddPathAsync(
        string path,
        OnboardingOptions options,
        CancellationToken ct = default)
    {
        var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        if (!Directory.Exists(expanded))
        {
            _console.MarkupLine($"[red]'{Markup.Escape(expanded)}' does not exist.[/]");

            return null;
        }

        var result = await _projects.AddAsync(expanded, null, ct).ConfigureAwait(false);

        if (result.Failed)
        {
            _console.MarkupLine($"[red]{Markup.Escape(result.Error!)}[/]");

            return null;
        }

        _console.MarkupLine($"[green]+[/] {Markup.Escape(result.Value!.Entry.Name)}");

        await OfferMigrationAsync([result.Value], options, ct).ConfigureAwait(false);

        return result.Value;
    }

    /// <summary>
    /// Offers to move existing agent configuration into the workspace
    /// (spec section 96).
    /// <para>
    /// Registering a project does nothing to the agent files already sitting in
    /// it, so without this step onboarding finishes with the repositories in
    /// exactly the state they started. The plan is always shown before anything
    /// moves, and files Git already ignores are left alone unless asked for:
    /// those are not in the repository's content and never will be, so taking
    /// them would remove a working setup to solve a problem that does not exist.
    /// </para>
    /// </summary>
    private async Task OfferMigrationAsync(
        IReadOnlyList<ProjectResolution> projects,
        OnboardingOptions options,
        CancellationToken ct)
    {
        if (projects.Count == 0)
        {
            return;
        }

        var plans = new List<MigrationPlan>();
        var ignoredOnly = new List<string>();

        foreach (var project in projects)
        {
            if (project.LocalPath is null)
            {
                continue;
            }

            var plan = await _migrations
                .PlanAsync(project.LocalPath, project.Entry.Slug, options.IncludeIgnored, ct)
                .ConfigureAwait(false);

            if (plan.Succeeded && plan.Value!.Steps.Count > 0)
            {
                plans.Add(plan.Value);
                continue;
            }

            // Nothing to move, but there may still be agent files here that are
            // simply already excluded. Worth mentioning so the absence of a
            // migration does not look like the launcher missing them.
            var withIgnored = await _migrations
                .PlanAsync(project.LocalPath, project.Entry.Slug, includeIgnored: true, ct)
                .ConfigureAwait(false);

            if (withIgnored.Succeeded && withIgnored.Value!.Steps.Count > 0)
            {
                ignoredOnly.Add(project.Entry.Name);
            }
        }

        if (ignoredOnly.Count > 0)
        {
            _console.WriteLine();
            _console.MarkupLine(
                $"[dim]{string.Join(", ", ignoredOnly.Select(Markup.Escape))}: agent files are "
                + "already excluded from Git and were left where they are. Move them with "
                + "loadout migrate --include-ignored if you want them shared across machines.[/]");
        }

        if (plans.Count == 0)
        {
            return;
        }

        _console.WriteLine();
        _console.MarkupLine(
            $"[bold]{plans.Count} project(s) have agent files in the repository[/]");

        foreach (var plan in plans)
        {
            _console.WriteLine();
            _console.MarkupLine($"[bold]{Markup.Escape(plan.Slug)}[/]");

            foreach (var step in plan.Steps)
            {
                var note = step.Kind == PolicyFindingKind.Tracked
                    ? "[yellow]tracked, will be copied not removed[/]"
                    : "[dim]will be moved[/]";

                _console.MarkupLine($"  {Markup.Escape(step.RepositoryRelativePath)}  {note}");
            }
        }

        _console.WriteLine();

        var migrate = options.Migrate
            || (options.Interactive
                && _console.Confirm("Migrate these into the workspace now?", defaultValue: false));

        if (!migrate)
        {
            _console.MarkupLine("[dim]Left alone. Run later with:[/] loadout migrate <project>");
            return;
        }

        foreach (var plan in plans)
        {
            var applied = await _migrations.ApplyAsync(plan, ct).ConfigureAwait(false);

            if (applied.Failed)
            {
                _console.MarkupLine(
                    $"[yellow]![/] {Markup.Escape(plan.Slug)}  {Markup.Escape(applied.Error!)}");

                continue;
            }

            _console.MarkupLine($"[green]+[/] {Markup.Escape(plan.Slug)}");

            foreach (var path in applied.Value!.TrackedLeftInPlace)
            {
                // The one thing the user must act on themselves, so it is said
                // per project rather than buried in a summary.
                _console.MarkupLine(
                    $"    [yellow]{Markup.Escape(path)}[/] [dim]is still tracked; remove it with "
                    + $"git rm --cached and commit[/]");
            }
        }
    }
}
