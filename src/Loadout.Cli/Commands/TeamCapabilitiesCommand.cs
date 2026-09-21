using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Teams;
using Loadout.Core.Workspace;
using Loadout.Models.Instructions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// The machinery a team's declarations can lean on, and where each came from.
/// </summary>
/// <remarks>
/// A declaration is prose in a brief and enforces nothing by itself. Somebody
/// writing one needs to know what is actually there to lean on, which until
/// now meant reading the source.
/// </remarks>
[Description("List the machinery a team's declarations can ask for, and what each one needs.")]
[CommandMeta(CommandCategory.Start,
    Intent = "team capabilities declarations machinery remedies shelf gate list")]
public sealed class TeamCapabilitiesCommand : AsyncCommand<TeamSettings>
{
    private readonly ISpecialistLibrary _library;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public TeamCapabilitiesCommand(
        ISpecialistLibrary library,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console)
    {
        _library = library;
        _workspace = workspace;
        _projects = projects;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        TeamSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var root = _workspace.IsAvailable() ? _workspace.LocalPath : null;

        var resolution = await ProjectHandle
            .ResolveAsync(_projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        var slug = resolution.Succeeded ? resolution.Value!.Entry.Slug : null;

        var catalogue = await new CapabilityCatalogue()
            .LoadAsync(root, slug, cancellationToken)
            .ConfigureAwait(false);

        var found = catalogue.Capabilities.Values
            .OrderBy(one => one.Id, StringComparer.Ordinal)
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                gates = CapabilityCatalogue.Gates.Order(StringComparer.Ordinal),
                capabilities = found.Select(one => new
                {
                    one.Id,
                    one.Summary,
                    one.Shelf,
                    one.Roles,
                    one.Gate,
                    origin = catalogue.Origins.TryGetValue(one.Id, out var where)
                        ? where.ToString().ToLowerInvariant()
                        : "built-in",
                }),
                findings = catalogue.Findings.Select(one => new { one.Kind, one.Severity, one.Detail }),
            });

            return CommandOutput.Success();
        }

        foreach (var one in found)
        {
            output.WriteBlankLine();
            output.WriteLine(
                $"[bold]{Markup.Escape(one.Id)}[/]"
                + (catalogue.Origins.TryGetValue(one.Id, out var where) && where != SpecialistOrigin.BuiltIn
                    ? $"  [dim]{Markup.Escape(where.ToString().ToLowerInvariant())}[/]"
                    : string.Empty));

            if (one.Summary is { Length: > 0 })
            {
                output.WriteLine($"  {Markup.Escape(one.Summary)}");
            }

            if (one.Shelf is { Length: > 0 })
            {
                output.WriteLine($"  [dim]keeps things in[/] {Markup.Escape(one.Shelf)}/");
            }

            if (one.Roles.Count > 0)
            {
                output.WriteLine($"  [dim]needs a node with[/] {Markup.Escape(string.Join(" or ", one.Roles))}");
            }

            // Said plainly, because it is the difference between a rule an
            // agent is asked to follow and one it runs into.
            output.WriteLine(
                one.Gate is { Length: > 0 } gate
                    ? $"  [dim]acting on one is decided by the[/] {Markup.Escape(gate)} [dim]gate[/]"
                    : "  [dim]nothing gates acting on what it keeps[/]");
        }

        foreach (var finding in catalogue.Findings)
        {
            output.WriteBlankLine();
            output.WriteLine($"  [yellow]{Markup.Escape(finding.Detail)}[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            "[dim]A team asks for one with 'capabilities: [[<id>]]' beside its declarations. "
            + "Add your own under <workspace>/global/capabilities or a project's own.[/]");

        return CommandOutput.Success();
    }
}
