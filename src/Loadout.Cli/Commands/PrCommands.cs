using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Models.Delivery;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Lists the branches of work waiting to land: each with its pull request
/// where it has one, how long it has waited, and whether it is still there.
/// </summary>
/// <remarks>
/// <para>
/// Read from the landings a queued run leaves behind. A run's own branch is
/// listed with its pull request, and a node branch its gate did not merge is
/// listed as pending, so work that is nobody's pull request is not forgotten.
/// </para>
/// <para>
/// Gone means the branch no longer exists in the project's repository. Whether
/// a branch has been squash-merged by hand is not something git can say from
/// the branch alone, so it is not claimed here.
/// </para>
/// </remarks>
[Description("List the branches waiting to land, with their pull requests and how long they have waited.")]
[CommandMeta(CommandCategory.Start, Intent = "pr pull requests pending branches landing waiting deliver")]
public sealed class PrListCommand : AsyncCommand<PrListCommand.Settings>
{
    private readonly ILandingService _landings;
    private readonly IProjectService _projects;
    private readonly IGitManager _git;
    private readonly IAnsiConsole _console;

    public PrListCommand(ILandingService landings, IProjectService projects, IGitManager git, IAnsiConsole console)
    {
        _landings = landings;
        _projects = projects;
        _git = git;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Only this project. Every project when left out.")]
        public string? Project { get; init; }

        [CommandOption("--all")]
        [Description("Include branches already merged or abandoned.")]
        public bool All { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        IReadOnlyList<string> slugs;

        if (settings.Project is { Length: > 0 } handle)
        {
            var named = await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false);

            if (named.Failed)
            {
                return output.Fail(named);
            }

            slugs = [named.Value!.Entry.Slug];
        }
        else
        {
            // Every project rather than the one this was run from, so Studio,
            // run from wherever its daemon started, lists the same as a person.
            slugs = _landings.Projects();
        }

        var rows = new List<(string Project, BranchLanding Landing, bool Gone)>();

        foreach (var slug in slugs)
        {
            var loaded = await _landings.LoadAsync(slug, cancellationToken).ConfigureAwait(false);

            if (loaded.Failed)
            {
                return output.Fail(loaded);
            }

            var shown = loaded.Value!.Landings
                .Where(one => settings.All || one.State is LandingState.Pending or LandingState.Draft or LandingState.Ready)
                .OrderBy(one => one.CreatedUtc)
                .ToList();

            if (shown.Count == 0)
            {
                continue;
            }

            var repository = await RepositoryAsync(slug, cancellationToken).ConfigureAwait(false);

            foreach (var landing in shown)
            {
                rows.Add((slug, landing, await GoneAsync(repository, landing, cancellationToken).ConfigureAwait(false)));
            }
        }

        if (output.IsJson)
        {
            output.WriteJson(rows.Select(row => new
            {
                project = row.Project,
                branch = row.Landing.Branch,
                state = LandingMoves.Word(row.Landing.State),
                team = row.Landing.Team,
                run = row.Landing.RunId,
                node = row.Landing.Node,
                task = row.Landing.Task,
                pullRequest = row.Landing.PullRequest,
                why = row.Landing.Why,
                created = row.Landing.CreatedUtc,
                updated = row.Landing.UpdatedUtc,
                gone = row.Gone,
            }));

            return CommandOutput.Success();
        }

        if (rows.Count == 0)
        {
            output.WriteLine(settings.All
                ? "[dim]No branches have been landed or left waiting.[/]"
                : "[dim]Nothing is waiting to land.[/]");

            return CommandOutput.Success();
        }

        foreach (var (project, landing, gone) in rows)
        {
            var from = landing.Node.Length > 0
                ? $"{landing.Node} in run {landing.RunId}"
                : landing.RunId.Length > 0 ? $"run {landing.RunId}" : "no run";

            output.WriteLine(
                $"[bold]{Markup.Escape(LandingMoves.Word(landing.State)),-8}[/] "
                + $"{Markup.Escape(landing.Branch)}"
                + (gone ? " [yellow](gone)[/]" : string.Empty));
            output.WriteLine(
                $"         [dim]{Markup.Escape(project)}, {Markup.Escape(from)}"
                + (landing.Task.Length > 0 ? $", task {Markup.Escape(landing.Task)}" : string.Empty)
                + $", waiting {Markup.Escape(Waited(DateTimeOffset.UtcNow - landing.CreatedUtc))}[/]");

            if (landing.PullRequest.Length > 0)
            {
                output.WriteLine($"         {Markup.Escape(landing.PullRequest)}");
            }
        }

        return CommandOutput.Success();
    }

    /// <summary>How long a landing has waited, in the roughest useful unit, counted in days however long.</summary>
    internal static string Waited(TimeSpan waited) => waited switch
    {
        { TotalHours: < 1 } => "under an hour",
        { TotalDays: < 1 } => Plural((int)waited.TotalHours, "hour"),
        _ => Plural((int)waited.TotalDays, "day"),
    };

    private static string Plural(int count, string unit) => count == 1 ? $"1 {unit}" : $"{count} {unit}s";

    private async Task<string?> RepositoryAsync(string slug, CancellationToken ct)
    {
        var resolved = await _projects.ResolveAsync(slug, ct).ConfigureAwait(false);

        return resolved.Succeeded ? resolved.Value!.LocalPath : null;
    }

    /// <summary>
    /// Whether a waiting branch has gone from the repository. Unknown counts as
    /// there: a checkout that is not on this machine says nothing either way.
    /// </summary>
    private async Task<bool> GoneAsync(string? repository, BranchLanding landing, CancellationToken ct)
    {
        if (repository is null || landing.State is LandingState.Merged or LandingState.Abandoned)
        {
            return false;
        }

        var resolved = await _git.ResolveAsync(repository, "refs/heads/" + landing.Branch, ct).ConfigureAwait(false);

        return resolved.Failed;
    }
}
