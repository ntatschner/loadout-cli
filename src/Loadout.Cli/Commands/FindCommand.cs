using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Lists;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// One search across tasks, ideas, sessions, team runs and projects, grouped
/// by kind, each with the command that shows it.
/// </summary>
/// <remarks>
/// The same search Studio's palette runs, with the same query as every list:
/// <c>kind:task state:open login</c> narrows it as it would a task list, and
/// is refused in the same words.
/// </remarks>
[Description("Search tasks, ideas, sessions, team runs and projects at once.")]
[CommandMeta(CommandCategory.Workspace,
    Intent = "find search look for where is anything everything global query")]
public sealed class FindCommand : AsyncCommand<FindCommand.Settings>
{
    private readonly SearchService _search;
    private readonly IAnsiConsole _console;

    public FindCommand(SearchService search, IAnsiConsole console)
    {
        _search = search;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[QUERY]")]
        [Description("What to look for, such as \"login\" or \"kind:task state:open login\".")]
        public string? Given { get; init; }

        [CommandOption("--query <QUERY>")]
        [Description("The query as an option, for one starting with a minus: --query=-kind:session.")]
        public string? Option { get; init; }

        [CommandOption("--limit <N>")]
        [Description("At most this many of each kind. Defaults to 10.")]
        public int Limit { get; init; } = 10;

        /// <summary>The query, whichever way it was given.</summary>
        public string Query => Option ?? Given ?? string.Empty;

        /// <inheritdoc />
        public override ValidationResult Validate() =>
            Given is not null && Option is not null
                ? ValidationResult.Error("Give the query once: after the command, or as --query, not both.")
                : Limit < 1
                    ? ValidationResult.Error("--limit must be at least 1.")
                    : base.Validate();
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var found = await _search.SearchAsync(settings.Query, settings.Limit, cancellationToken).ConfigureAwait(false);

        if (found.Failed)
        {
            return output.Fail(found);
        }

        var groups = found.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                query = settings.Query,
                groups = groups.Select(group => new
                {
                    group.Kind,
                    hits = group.Hits.Select(hit => new { hit.Id, hit.Title, hit.Project, hit.State, updated = hit.Updated, open = hit.Open }),
                    group.More,
                }),
            });

            return CommandOutput.Success();
        }

        if (groups.Count == 0)
        {
            output.WriteLine(settings.Query.Length > 0
                ? $"[dim]Nothing matches {Markup.Escape(settings.Query)}.[/]"
                : "[dim]There is nothing to search yet.[/]");

            return CommandOutput.Success();
        }

        foreach (var group in groups)
        {
            output.WriteLine($"[bold]{Heading(group.Kind)}[/]");

            foreach (var hit in group.Hits)
            {
                output.WriteLine(
                    $"  {Markup.Escape(hit.Title)}"
                    + $"  [dim]{Markup.Escape(string.Join(" · ", new[] { hit.State, hit.Project }.Where(part => part is { Length: > 0 })))}[/]");
                output.WriteLine($"    [dim]{Markup.Escape(hit.Open)}[/]");
            }

            if (group.More > 0)
            {
                output.WriteLine($"  [dim]{group.More} more. Narrow it with kind:{group.Kind}, or raise --limit.[/]");
            }

            output.WriteBlankLine();
        }

        return CommandOutput.Success();
    }

    private static string Heading(string kind) => kind switch
    {
        "task" => "Tasks",
        "idea" => "Ideas",
        "session" => "Sessions",
        "run" => "Team runs",
        "project" => "Projects",
        _ => kind,
    };
}
