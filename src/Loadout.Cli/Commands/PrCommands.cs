using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Models;
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
                mergeRequested = row.Landing.MergeRequestedUtc,
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

/// <summary>What the commands acting on one waiting branch are given: the branch, and where it waits.</summary>
public class PrBranchSettings : GlobalSettings
{
    [CommandArgument(0, "<BRANCH>")]
    [Description("The branch, as 'pr list' shows it.")]
    public string Branch { get; init; } = string.Empty;

    [CommandOption("--project <SLUG>")]
    [Description("The project it waits in. Only needed when more than one project has a branch of that name.")]
    public string? Project { get; init; }
}

/// <summary>Pushes a branch waiting to land and opens its pull request.</summary>
/// <remarks>
/// <para>
/// For a branch left pending: a node's work the gate did not merge, or a
/// run's own branch whose pull request mode was none. The pull request is
/// opened against the repository's default branch, with the task's title and
/// done-when when the branch was made for one.
/// </para>
/// <para>
/// It pushes before it opens, because a pull request needs the branch on the
/// remote. <c>--dry-run</c> names both before either happens.
/// </para>
/// </remarks>
[Description("Push a branch waiting to land and open its pull request.")]
[CommandMeta(CommandCategory.Start, Intent = "pr open pull request push pending branch draft", Mutates = true)]
public sealed class PrOpenCommand : AsyncCommand<PrOpenCommand.Settings>
{
    private readonly IDeliveryService _delivery;
    private readonly IAnsiConsole _console;

    public PrOpenCommand(IDeliveryService delivery, IAnsiConsole console)
    {
        _delivery = delivery;
        _console = console;
    }

    public sealed class Settings : PrBranchSettings
    {
        [CommandOption("--draft")]
        [Description("Open it as a draft rather than ready for review.")]
        public bool Draft { get; init; }

        [CommandOption("--title <TITLE>")]
        [Description("The pull request's title. The task's title when left out, or the branch's name.")]
        public string? Title { get; init; }

        [CommandOption("--body <TEXT>")]
        [Description("The pull request's description. Worked out from the task and the run when left out.")]
        public string? Body { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await _delivery.FindAsync(settings.Branch, settings.Project, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        if (DeliveryService.CannotOpen(found.Value!.Landing) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        var opening = await _delivery.OpeningAsync(
            found.Value, settings.Draft, settings.Title, settings.Body, cancellationToken).ConfigureAwait(false);

        if (settings.DryRun)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dryRun = true, branch = opening.Head, draft = opening.Draft, title = opening.Title, body = opening.Body });

                return CommandOutput.Success();
            }

            output.WriteLine(
                "[dim]Dry run: nothing was pushed and nothing was opened.[/] It would push "
                + $"[bold]{Markup.Escape(opening.Head)}[/] to origin, then open a "
                + (opening.Draft ? "draft " : string.Empty)
                + "pull request titled:");
            output.WriteLine($"  {Markup.Escape(opening.Title)}");

            return CommandOutput.Success();
        }

        var opened = await _delivery.OpenAsync(found.Value, opening, cancellationToken).ConfigureAwait(false);

        if (opened.Failed)
        {
            return output.Fail(opened);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { branch = opened.Value!.Branch, state = LandingMoves.Word(opened.Value.State), pullRequest = opened.Value.PullRequest });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] Opened {(opening.Draft ? "a draft" : "a pull request")} for {Markup.Escape(opening.Head)}: "
            + Shown.Safely(opened.Value!.PullRequest));

        return CommandOutput.Success();
    }
}

/// <summary>Marks a waiting branch's draft pull request ready for review.</summary>
[Description("Mark a waiting branch's draft pull request ready for review.")]
[CommandMeta(CommandCategory.Start, Intent = "pr ready draft review pull request", Mutates = true)]
public sealed class PrReadyCommand : AsyncCommand<PrReadyCommand.Settings>
{
    private readonly IDeliveryService _delivery;
    private readonly IAnsiConsole _console;

    public PrReadyCommand(IDeliveryService delivery, IAnsiConsole console)
    {
        _delivery = delivery;
        _console = console;
    }

    public sealed class Settings : PrBranchSettings
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await _delivery.FindAsync(settings.Branch, settings.Project, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var landing = found.Value!.Landing;

        if (DeliveryService.CannotReady(landing) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was changed.[/] It would mark {Shown.Safely(landing.PullRequest)} ready for review.");

            return CommandOutput.Success();
        }

        var marked = await _delivery.ReadyAsync(found.Value, cancellationToken).ConfigureAwait(false);

        if (marked.Failed)
        {
            return output.Fail(marked);
        }

        if (output.IsJson)
        {
            output.WriteJson(new { branch = marked.Value!.Branch, state = LandingMoves.Word(marked.Value.State), pullRequest = marked.Value.PullRequest });

            return CommandOutput.Success();
        }

        output.WriteLine($"[green]+[/] {Shown.Safely(landing.PullRequest)} is ready for review.");

        return CommandOutput.Success();
    }
}

/// <summary>
/// Asks for a waiting branch's pull request to be squash-merged, with a
/// message the person writes, once its required checks pass.
/// </summary>
/// <remarks>
/// <para>
/// The message is never worked out: the subject has to be given, and the body
/// is only what is given, because a squash commit is what <c>git log</c> shows
/// for the work from then on.
/// </para>
/// <para>
/// The host does the merge through auto-merge, so the command returns before
/// anything has merged and the branch stays ready in <c>pr list</c> until the
/// daemon sees it go in. A repository that does not allow auto-merge refuses,
/// and says so.
/// </para>
/// </remarks>
[Description("Squash-merge a waiting branch's pull request, with your message, once its checks pass.")]
[CommandMeta(CommandCategory.Start, Intent = "pr merge squash auto-merge pull request message", Mutates = true)]
public sealed class PrMergeCommand : AsyncCommand<PrMergeCommand.Settings>
{
    private readonly IDeliveryService _delivery;
    private readonly IAnsiConsole _console;

    public PrMergeCommand(IDeliveryService delivery, IAnsiConsole console)
    {
        _delivery = delivery;
        _console = console;
    }

    public sealed class Settings : PrBranchSettings
    {
        [CommandOption("--subject <LINE>")]
        [Description("The squash commit's subject, one line.")]
        public string? Subject { get; init; }

        [CommandOption("--body <TEXT>")]
        [Description("The squash commit's body. Empty when left out, rather than the branch's commit messages.")]
        public string? Body { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await _delivery.FindAsync(settings.Branch, settings.Project, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var landing = found.Value!.Landing;

        if ((DeliveryService.CannotMerge(landing) ?? DeliveryService.CannotUseSubject(settings.Subject)) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was changed.[/] It would ask for {Shown.Safely(landing.PullRequest)} "
                + "to be squash-merged once its required checks pass, as:");
            output.WriteLine($"  {Markup.Escape(settings.Subject!.Trim())}");

            if (settings.Body?.Trim() is { Length: > 0 } body)
            {
                output.WriteLine(string.Empty);
                output.WriteLine(Markup.Escape(body));
            }

            return CommandOutput.Success();
        }

        var asked = await _delivery.MergeAsync(
            found.Value, settings.Subject!, settings.Body ?? string.Empty, cancellationToken).ConfigureAwait(false);

        if (asked.Failed)
        {
            return output.Fail(asked);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                branch = asked.Value.Landing.Branch,
                state = LandingMoves.Word(asked.Value.Landing.State),
                pullRequest = asked.Value.Landing.PullRequest,
                mergeRequested = asked.Value.Landing.MergeRequestedUtc,
                said = asked.Value.Said,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]+[/] {Shown.Safely(landing.PullRequest)} will be squash-merged once its required checks pass.");

        if (asked.Value.Said.Length > 0)
        {
            output.WriteLine($"[dim]{Markup.Escape(asked.Value.Said)}[/]");
        }

        return CommandOutput.Success();
    }
}
