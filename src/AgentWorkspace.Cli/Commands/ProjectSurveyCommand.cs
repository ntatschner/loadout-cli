using System.ComponentModel;
using AgentWorkspace.Cli.Infrastructure;
using AgentWorkspace.Core.Projects;
using AgentWorkspace.Models.Projects;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AgentWorkspace.Cli.Commands;

/// <summary>
/// Reports agent state on this machine that the workspace does not account for.
/// <para>
/// Agents key their state by the directory they were started in, which is not
/// always a repository. Somebody working across several repositories from their
/// parent accumulates memory against that parent, where it describes all of them
/// and belongs to none of them. Nothing surfaced that before: the state simply
/// sat there while the launcher reported the projects as having none.
/// </para>
/// </summary>
[Description("Find agent state on this machine that no project accounts for.")]
public sealed class ProjectSurveyCommand : AsyncCommand<GlobalSettings>
{
    private readonly IRepositoryAttribution _attribution;
    private readonly IAnsiConsole _console;

    public ProjectSurveyCommand(IRepositoryAttribution attribution, IAnsiConsole console)
    {
        _attribution = attribution;
        _console = console;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var output = new CommandOutput(_console, settings);

        var surveyed = await _attribution.SurveyAsync().ConfigureAwait(false);
        if (surveyed.Failed)
        {
            return output.Fail(surveyed);
        }

        var found = surveyed.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                state = found.Select(item => new
                {
                    path = item.StatePath,
                    recordedAgainst = item.SubjectPath,
                    kind = item.Kind.ToString().ToLowerInvariant(),
                    project = item.Slug,
                    repositories = item.Repositories,
                    topics = item.Topics,
                }),
            });

            return CommandOutput.Success();
        }

        if (found.Count == 0)
        {
            output.WriteLine("[dim]No agent state was found outside the workspace.[/]");
            return CommandOutput.Success();
        }

        foreach (var item in found)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Markup.Escape(item.SubjectPath)}[/]  "
                + $"[dim]{item.Topics} topic(s)[/]");

            switch (item.Kind)
            {
                case AttributionKind.Project:
                    output.WriteLine(
                        $"  [green]{Markup.Escape(item.Slug!)}[/]  "
                        + "[dim]bring it in with:[/] "
                        + $"agentctl memory import {Markup.Escape(item.Slug!)}");
                    break;

                case AttributionKind.Container:
                    // Named, not chosen. The state describes work across all of
                    // these, so picking one would be a guess presented as a
                    // fact, and the wrong guess files a repository's hard-won
                    // notes under its neighbour.
                    output.WriteLine(
                        $"  [yellow]holds {item.Repositories.Count} repositories[/] "
                        + "[dim]so this was recorded across all of them[/]");

                    foreach (var repository in item.Repositories)
                    {
                        output.WriteLine($"    {Markup.Escape(Path.GetFileName(repository))}");
                    }

                    output.WriteLine(
                        "  [dim]decide which project it belongs to, then:[/] "
                        + $"agentctl memory import <project> --from {Markup.Escape(item.StatePath)}");
                    break;

                case AttributionKind.Unregistered:
                    output.WriteLine(
                        "  [yellow]a repository that is not registered[/]  "
                        + "[dim]register it with:[/] "
                        + $"agentctl project add {Markup.Escape(item.SubjectPath)}");
                    break;

                default:
                    output.WriteLine(
                        "  [dim]nothing is there any more, so this state describes a directory "
                        + "that has moved or gone[/]");
                    break;
            }
        }

        output.WriteBlankLine();
        output.WriteLine(
            "[dim]Nothing here was changed. This only reports what is on the machine.[/]");

        return CommandOutput.Success();
    }
}
