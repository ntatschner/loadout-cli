using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Finds the memory topics that answer a question.
/// </summary>
/// <remarks>
/// <para>
/// The query comes first and the project is an option, which is the other way
/// round from the rest of the memory commands. It is not a slip: the common case
/// is asking about the repository you are standing in, and
/// <c>memory find "restart manager"</c> has to mean the search rather than a
/// project called "restart manager". Where the project is positional it is
/// always the only argument, so there is nothing to be ambiguous about.
/// </para>
/// </remarks>
[Description("Find the memory topics that answer a question.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "search memory find topic recall lookup question")]
public sealed class MemoryFindCommand : AsyncCommand<MemoryFindCommand.Settings>
{
    private readonly IMemoryService _memory;
    private readonly IProjectService _projects;
    private readonly IWorkspaceManager _workspace;
    private readonly IAnsiConsole _console;

    public MemoryFindCommand(
        IMemoryService memory,
        IProjectService projects,
        IWorkspaceManager workspace,
        IAnsiConsole console)
    {
        _memory = memory;
        _projects = projects;
        _workspace = workspace;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<query>")]
        [Description("What you are looking for, in your own words.")]
        public string Query { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Project to search. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("How many topics to return. Defaults to 5.")]
        public int Limit { get; init; } = 5;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Limit <= 0)
        {
            return output.Fail("--limit has to be at least 1.", ExitCode.InvalidArguments);
        }

        var resolution = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken).ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var listed = await _memory.ListAsync(_workspace.LocalPath, slug, cancellationToken)
            .ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var matches = MemorySearch.Rank(listed.Value!, settings.Query, settings.Limit);

        if (output.IsJson)
        {
            output.WriteJson(matches.Select(match => new
            {
                match.Topic.Name,
                match.Topic.Description,
                match.Topic.Path,
                match.Matched,
            }));

            return CommandOutput.Success();
        }

        if (matches.Count == 0)
        {
            output.WriteLine($"[yellow]Nothing in {Markup.Escape(slug)}'s memory matches that.[/]");

            // Said because the alternative is somebody concluding the fact is
            // not recorded when it is recorded in other words. The search
            // matches words, not meanings, and never claimed otherwise.
            output.WriteLine(
                "[dim]It matches words rather than meanings, so a topic that says the same thing "
                + "differently will not come back. 'loadout memory list' shows all of them.[/]");

            return CommandOutput.Success();
        }

        foreach (var match in matches)
        {
            output.WriteLine(
                $"[bold]{Markup.Escape(match.Topic.Name)}[/]  "
                + $"[dim]{Markup.Escape(match.Topic.Description)}[/]");

            foreach (var fact in match.Matched.Take(2))
            {
                output.WriteLine($"  {Markup.Escape(Shorten(fact))}");
            }

            output.WriteBlankLine();
        }

        output.WriteLine(
            $"[dim]Read one in full with: loadout memory list {Markup.Escape(slug)} "
            + $"--show {Markup.Escape(matches[0].Topic.Name)}[/]");

        return CommandOutput.Success();
    }

    /// <summary>
    /// A fact cut to something that fits a results list.
    /// </summary>
    /// <remarks>
    /// Cut at a word rather than mid-syllable, and marked as cut. A truncated
    /// fact that does not say it was truncated is a fact that has been changed.
    /// Three dots rather than an ellipsis for that reason: a Windows console
    /// transliterates the character to a full stop, which turns a cut sentence
    /// into what reads as a complete one — the opposite of saying so. The other
    /// memory commands truncate the same way.
    /// </remarks>
    private static string Shorten(string fact)
    {
        const int Room = 110;

        if (fact.Length <= Room)
        {
            return fact;
        }

        var cut = fact.LastIndexOf(' ', Room);

        return string.Concat(fact.AsSpan(0, cut > 40 ? cut : Room), "...");
    }
}
