using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Editors;
using Loadout.Core.Projects;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Opens a project in the editor, under the profile that suits its agent.
/// <para>
/// VS Code keeps settings, extensions and keybindings in named profiles, and
/// working with an agent usually wants a different set from working without
/// one. Switching by hand every time is the sort of small friction that stops
/// being done, so the launcher does it: a project opened for Claude and the
/// same project opened for Codex can put the editor in two different states.
/// </para>
/// <para>
/// Profiles are opened, never written. Their contents live in a layout the
/// editor does not publish, and rewriting it would be a promise that could not
/// be kept across editor versions.
/// </para>
/// </summary>
[Description("Open a project in the editor, under the profile for its agent.")]
[CommandMeta(CommandCategory.Integration, Intent = "editor vscode open ide profile")]
public sealed class CodeCommand : AsyncCommand<CodeCommand.Settings>
{
    private readonly IEditorService _editors;
    private readonly IProjectService _projects;
    private readonly IConfigurationService _configuration;
    private readonly IAnsiConsole _console;

    public CodeCommand(
        IEditorService editors,
        IProjectService projects,
        IConfigurationService configuration,
        IAnsiConsole console)
    {
        _editors = editors;
        _projects = projects;
        _configuration = configuration;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Project slug, alias or name. Defaults to the repository you are in.")]
        public string? Project { get; init; }

        // --agent comes from GlobalSettings and means the same thing here.
        //
        // --profile does not: everywhere else it names a context profile, which
        // decides what instructions an agent loads, and has nothing to do with
        // the editor. Two meanings behind one option would be a trap, so the
        // editor's is spelled out.
        [CommandOption("--editor-profile <NAME>")]
        [Description("Open this editor profile, whatever is configured for the project or agent.")]
        public string? EditorProfile { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);

        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var config = configResult.Value!;

        var resolution = settings.Project is not null
            ? await _projects.ResolveAsync(settings.Project).ConfigureAwait(false)
            : await _projects
                .ResolveFromDirectoryAsync(settings.Repo ?? Directory.GetCurrentDirectory())
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var project = resolution.Value!;

        if (project.LocalPath is null)
        {
            return output.Fail(
                $"'{project.Entry.Name}' is not on this machine, so there is nothing to open. "
                + $"Clone it first with: loadout project clone {project.Entry.Slug}",
                ExitCode.RepositoryUnavailable);
        }

        // An explicit profile is honoured without consulting anything, which is
        // what makes this usable for a profile that was never configured.
        var entry = settings.EditorProfile is { Length: > 0 }
            ? new Models.Projects.ProjectRegistryEntry
            {
                Slug = project.Entry.Slug,
                Name = project.Entry.Name,
                DefaultAgent = project.Entry.DefaultAgent,
                EditorProfile = settings.EditorProfile,
            }
            : project.Entry;

        var editor = _editors.Describe(config);
        var profile = _editors.ProfileFor(config, entry, settings.Agent);

        var opened = await _editors
            .OpenAsync(config, entry, project.LocalPath, settings.Agent)
            .ConfigureAwait(false);

        if (opened.Failed)
        {
            return output.Fail(opened);
        }

        output.WriteLine(
            $"Opened [bold]{Markup.Escape(project.Entry.Name)}[/] in {editor.Command}.");

        // Told rather than left to be discovered. Asked for a folder and a
        // profile in the same breath, the editor opens a window containing
        // neither and reports nothing, so the profile is left off — and this is
        // the only place that admits the setting was not honoured.
        if (profile is { Length: > 0 })
        {
            output.WriteLine(
                $"[yellow]note[/] the [bold]{Markup.Escape(profile)}[/] profile was not used: "
                + $"{editor.Command} will not open a folder and a profile together.");
        }

        return CommandOutput.Success();
    }
}
