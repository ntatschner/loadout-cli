using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Creates a project that does not exist yet, optionally modelled on one that
/// does.
/// </summary>
/// <remarks>
/// <para>
/// 'project add' registers a repository somebody already has and 'project
/// clone' fetches one that already exists somewhere else. Starting something
/// new meant doing it by hand — git init, add, then copying instructions out of
/// a neighbouring project and remembering which parts were safe to take.
/// </para>
/// <para>
/// The template is the point. A second service of the same shape wants the
/// first one's conventions: which agent it launches, which shared instruction
/// files apply, its rules and its per-agent settings. It emphatically does not
/// want the first one's memory, which is facts about a codebase that has not
/// been written yet, and the command says so rather than quietly leaving it out.
/// </para>
/// </remarks>
[Description("Create a new project, optionally from an existing one as a template.")]
[CommandMeta(CommandCategory.Projects, Intent = "start a new repository scaffold template")]
public sealed class ProjectNewCommand : AsyncCommand<ProjectNewCommand.Settings>
{
    private readonly IProjectTemplateService _templates;
    private readonly IAnsiConsole _console;

    public ProjectNewCommand(IProjectTemplateService templates, IAnsiConsole console)
    {
        _templates = templates;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<name>")]
        [Description("Display name for the new project. Becomes its slug.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--from <PROJECT>")]
        [Description("Existing project to copy conventions, instructions and rules from.")]
        public string? From { get; init; }

        [CommandOption("--path <PATH>")]
        [Description("Where to create it. Defaults to this machine's clone root plus the slug.")]
        public string? Path { get; init; }

        [CommandOption("--remote <URL>")]
        [Description("Remote to record and set as origin. Nothing is pushed.")]
        public string? Remote { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _templates
            .CreateAsync(settings.Name, settings.From, settings.Path, settings.Remote)
            .ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var plan = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                id = plan.Slug,
                name = plan.Name,
                path = plan.TargetPath,
                template = plan.TemplateSlug,
                copied = plan.Copied,
                skipped = plan.Skipped,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"[green]Created[/] {Markup.Escape(plan.Name)} "
            + $"[dim]({Markup.Escape(plan.Slug)})[/] at {Markup.Escape(plan.TargetPath)}");

        if (plan.TemplateSlug is { Length: > 0 } template)
        {
            output.WriteLine(
                $"Modelled on [bold]{Markup.Escape(template)}[/], "
                + $"{plan.Copied.Count} file(s) brought across.");

            // Said rather than left to be noticed later, when an agent behaves
            // unlike the project this one was modelled on.
            foreach (var (what, why) in plan.Skipped)
            {
                output.WriteLine($"[dim]  {Markup.Escape(what)} was not copied: {Markup.Escape(why)}[/]");
            }
        }

        if (!plan.Committed)
        {
            output.WriteLine(
                "[yellow]note[/] the repository has no first commit, which usually means Git has "
                + "no name and email configured here. Set those and commit when you are ready.");
        }

        output.WriteLine(
            $"[dim]Nothing has been pushed. Launch it with: loadout launch {Markup.Escape(plan.Slug)}[/]");

        return CommandOutput.Success();
    }
}
