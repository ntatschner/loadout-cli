using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Delivery;
using Loadout.Core.Git;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Teams;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Says how a project's work lands and is released, and where each part of
/// that was set: the pull request mode at each level, the host, and the
/// release recipe.
/// </summary>
/// <remarks>
/// The pull request mode can be set on the project, on each team's queue and
/// on each queued task, and the narrowest wins. A draft nobody expected is
/// hard to trace without seeing every level at once, which is what this is for.
/// </remarks>
[Description("Say how a project's work lands and is released, and which setting decided each part.")]
[CommandMeta(CommandCategory.Start, Intent = "pr explain why draft ready provider release recipe settings delivery")]
public sealed class PrExplainCommand : AsyncCommand<PrExplainCommand.Settings>
{
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IQueueService _queues;
    private readonly IRepositoryHosts _hosts;
    private readonly IAnsiConsole _console;

    public PrExplainCommand(
        IProjectService projects,
        IWorkspaceManager workspace,
        IQueueService queues,
        IRepositoryHosts hosts,
        IAnsiConsole console)
    {
        _projects = projects;
        _workspace = workspace;
        _queues = queues;
        _hosts = hosts;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<PROJECT>")]
        [Description("The project, by slug or name.")]
        public string Project { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var resolved = await _projects.ResolveAsync(settings.Project, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var slug = resolved.Value!.Entry.Slug;
        var manifest = await _workspace.ReadProjectAsync(slug, cancellationToken).ConfigureAwait(false);
        var delivery = manifest.Succeeded ? manifest.Value!.Delivery : new ProjectDelivery();

        var projectMode = Mode(delivery.PrMode);
        var queues = new List<(string Team, string Mode)>();

        foreach (var team in _queues.Teams())
        {
            var queue = await _queues.LoadAsync(team, cancellationToken).ConfigureAwait(false);

            if (queue.Succeeded && PullRequestModes.OfQueue(queue.Value!.Settings) is { } mode)
            {
                queues.Add((team, PullRequestModes.Word(mode)));
            }
        }

        string host;
        string hostFrom;

        if (resolved.Value.LocalPath is { } repository
            && await _hosts.ForAsync(slug, repository, cancellationToken).ConfigureAwait(false) is { } chosen)
        {
            host = chosen.Succeeded ? chosen.Value!.Host.Provider : "unknown";
            hostFrom = chosen.Succeeded ? chosen.Value!.From : chosen.Error!;
        }
        else
        {
            host = "unknown";
            hostFrom = "the project has no checkout on this machine";
        }

        var recipe = ReleaseService.RecipeOf(delivery.Release);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = slug,
                prMode = new { project = projectMode, queues = queues.Select(one => new { team = one.Team, mode = one.Mode }) },
                provider = new { name = host, from = hostFrom },
                release = new
                {
                    recipe = recipe.Succeeded ? recipe.Value : null,
                    problem = recipe.Failed ? recipe.Error : null,
                    versionFile = delivery.Release.VersionFile,
                    tagPrefix = delivery.Release.TagPrefix.Length > 0 ? delivery.Release.TagPrefix : "v",
                    workflow = delivery.Release.Workflow,
                },
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(slug)}[/]");
        output.WriteLine("Pull requests for queued runs, narrowest first: the queued task, then its team's queue, then the project.");
        output.WriteLine(queues.Count == 0
            ? "  No team's queue sets a mode, so each queued task's own, then the project's, decides."
            : "  Queues that set one: " + string.Join(", ", queues.Select(one => $"{Markup.Escape(one.Team)} {one.Mode}")) + ".");
        output.WriteLine($"  The project: {Markup.Escape(projectMode)}.");
        output.WriteLine($"Lands through [bold]{Markup.Escape(host)}[/], from {Shown.Safely(hostFrom)}.");
        output.WriteLine(recipe.Failed
            ? $"Release: [yellow]{Shown.Safely(recipe.Error!)}[/]"
            : $"Release: [bold]{Markup.Escape(recipe.Value!)}[/]"
                + (recipe.Value == ReleaseService.None ? ", set by nothing, so it is not released from here." : " from the project's delivery.release.recipe."));

        return CommandOutput.Success();
    }

    private static string Mode(string word) =>
        word.Trim().Length == 0
            ? "not set, so none"
            : PullRequestModes.TryParse(word, out var mode) && mode is { } known
                ? PullRequestModes.Word(known)
                : $"'{word}', which is not a mode and is passed over";
}
