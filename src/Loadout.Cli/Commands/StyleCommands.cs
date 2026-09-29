using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Instructions;
using Loadout.Models.Projects;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for listing styles.</summary>
public sealed class StyleListSettings : GlobalSettings
{
    [CommandArgument(0, "[PROJECT]")]
    [Description("Project whose styles to read. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>
/// Lists the coding styles in the library and which of them a project uses.
/// </summary>
/// <remarks>
/// A style is a set of specialists sharing a name, so <c>instructions list</c>
/// already shows every part of every style. What it cannot show is the thing
/// somebody asks about a style: which are in force here, in which layer, and
/// what each costs on every launch.
/// </remarks>
[Description("List coding styles, and which of them are in force for a project.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "coding style conventions house style personal named codebase which style")]
public sealed class StyleListCommand : InstructionsCommandBase<StyleListSettings>
{
    public StyleListCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        StyleListSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        var manifest = project is not null && WorkspacePath is not null
            ? (await Workspace.ReadProjectAsync(project.Entry.Slug, cancellationToken).ConfigureAwait(false)).Value
            : null;

        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, project?.Entry.Slug, cancellationToken)
            .ConfigureAwait(false);

        var chosen = StyleChoice.Of(manifest, settings.Profile);
        var styles = CodingStyles.All(catalogue);

        // A chosen style with nothing in the library is the one fault here
        // that nothing else reports: the launch carries on without it.
        var missing = chosen is { Length: > 0 }
            && CodingStyles.LayerOf(chosen, chosen) == StyleLayer.Named
            && styles.All(s => !string.Equals(s.Name, chosen, StringComparison.OrdinalIgnoreCase));

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                project = project?.Entry.Slug,
                chosen,
                chosenMissing = missing,
                styles = styles.Select(style => new
                {
                    name = style.Name,
                    layer = CodingStyles.LayerOf(style.Name, chosen)?.ToString().ToLowerInvariant(),
                    core = style.Core?.Id,
                    coreTokens = style.Core?.EstimatedTokens ?? 0,
                    languages = style.Languages.Select(s => s.Id),
                    patterns = style.Patterns.Select(s => s.Id),
                    origins = style.Parts.Select(s => Origin(s.Origin)).Distinct(),
                }),
            });

            return CommandOutput.Success();
        }

        if (styles.Count == 0)
        {
            output.WriteLine("[dim]No styles yet.[/]");
            output.WriteLine(
                "[dim]Start your personal style with: loadout instructions new style.personal[/]");

            return missing ? Missing(output, chosen!) : CommandOutput.Success();
        }

        foreach (var style in styles)
        {
            var layer = CodingStyles.LayerOf(style.Name, chosen);

            var state = layer switch
            {
                StyleLayer.Personal => "[green]in force[/] [dim]personal, everywhere[/]",
                StyleLayer.Named when chosen is { Length: > 0 } => "[green]in force[/] [dim]chosen for this work[/]",
                StyleLayer.Codebase when style.Parts.Any(s => s.Origin == SpecialistOrigin.Project) =>
                    "[green]in force[/] [dim]this codebase[/]",
                _ => "[dim]not in force[/]",
            };

            output.WriteLine($"[cyan]{Markup.Escape(style.Name)}[/]  {state}");

            var core = style.Core is { } found
                ? $"core about {found.EstimatedTokens:N0} tokens"
                : "no core";

            output.WriteLine(
                $"  [dim]{core}, {style.Languages.Count} language file(s), "
                + $"{style.Patterns.Count} pattern(s)[/]");
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Inspect one with loadout style show <name>.[/]");

        return missing ? Missing(output, chosen!) : CommandOutput.Success();
    }

    private static int Missing(CommandOutput output, string chosen)
    {
        output.WriteBlankLine();
        output.WriteLine(
            $"[yellow]This project chose the '{Markup.Escape(chosen)}' style, and the library has no "
            + "part of it. Launches carry on without it.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Options for showing a style.</summary>
public sealed class StyleShowSettings : GlobalSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Style name, for example personal or work.")]
    public string Name { get; init; } = string.Empty;

    [CommandOption("--project <PROJECT>")]
    [Description("Project whose library to read. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>Shows one style: its core in full, and when each other part loads.</summary>
[Description("Show one coding style: its core, and when its language files and patterns load.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "coding style detail rules patterns")]
public sealed class StyleShowCommand : InstructionsCommandBase<StyleShowSettings>
{
    public StyleShowCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        StyleShowSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var project = await ProjectAsync(settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        var catalogue = await Instructions
            .LibraryAsync(WorkspacePath, project?.Entry.Slug, cancellationToken)
            .ConfigureAwait(false);

        if (CodingStyles.Find(catalogue, settings.Name) is not { } style)
        {
            return output.Fail(
                $"No style named '{settings.Name}'. Run 'loadout style list' to see what there is.",
                ExitCode.ProjectNotFound);
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                name = style.Name,
                parts = style.Parts.Select(part => new
                {
                    id = part.Id,
                    part = CodingStyles.PartOf(part.Id)?.ToString().ToLowerInvariant(),
                    title = part.Title,
                    origin = Origin(part.Origin),
                    path = part.Path,
                    estimatedTokens = part.EstimatedTokens,
                    accompanies = part.Activation.AccompaniesList,
                    taskPhrases = part.Activation.TaskPhraseList,
                    body = part.Body,
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(style.Name)}[/]");

        if (style.Core is { } core)
        {
            output.WriteLine(
                $"[dim]core, {Origin(core.Origin)}, about {core.EstimatedTokens:N0} tokens on every launch[/]");
            output.WriteBlankLine();
            output.WriteLine(Markup.Escape(core.Body));
        }
        else
        {
            output.WriteLine("[dim]No core, so nothing of this style loads on every launch.[/]");
        }

        if (style.Languages.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Language files[/]");

            foreach (var part in style.Languages)
            {
                output.WriteLine(
                    $"  [cyan]{Markup.Escape(part.Id)}[/]  [dim]with "
                    + $"{Markup.Escape(string.Join(" or ", part.Activation.AccompaniesList))}, "
                    + $"about {part.EstimatedTokens:N0} tokens[/]");
            }
        }

        if (style.Patterns.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[bold]Patterns[/]");

            foreach (var part in style.Patterns)
            {
                var phrases = part.Activation.TaskPhraseList.Count > 0
                    ? "when the task mentions " + string.Join(", ", part.Activation.TaskPhraseList.Select(p => $"\"{p}\""))
                    : "only when named";

                output.WriteLine(
                    $"  [cyan]{Markup.Escape(part.Id)}[/]  [dim]{Markup.Escape(phrases)}, "
                    + $"about {part.EstimatedTokens:N0} tokens[/]");
            }
        }

        if (style.Languages.Count + style.Patterns.Count > 0)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Read a part in full with loadout instructions show <id>.[/]");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Options for choosing a project's style.</summary>
public sealed class StyleUseSettings : GlobalSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("Style to use, or 'none' to go back to only the personal and codebase styles.")]
    public string Name { get; init; } = string.Empty;

    [CommandOption("--project <SLUG>")]
    [Description("Project to change. Defaults to the repository you are in.")]
    public string? Project { get; init; }
}

/// <summary>
/// Chooses the named style for a project, or for one of its profiles.
/// </summary>
/// <remarks>
/// With <c>--profile</c>, the choice goes on that profile and applies only when
/// it is picked; without, on the project. The personal and codebase styles are
/// in force whatever is chosen here, so this only ever says which third layer
/// sits between them.
/// </remarks>
[Description("Choose the named coding style for a project or one of its profiles.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "use coding style choose style work strict loose per project profile",
    Mutates = true,
    Example = "style use work")]
public sealed class StyleUseCommand : InstructionsCommandBase<StyleUseSettings>
{
    public StyleUseCommand(
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        StyleUseSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolution = await ProjectHandle
            .ResolveAsync(Projects, settings.Project, settings.Repo, cancellationToken)
            .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        var read = await Workspace.ReadProjectAsync(slug, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var manifest = read.Value!;
        var clearing = string.Equals(settings.Name.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        var wanted = clearing ? string.Empty : settings.Name.Trim().ToLowerInvariant();

        if (!clearing)
        {
            if (CodingStyles.LayerOf(wanted, wanted) != StyleLayer.Named)
            {
                return output.Fail(
                    $"'{wanted}' is always in force where it exists, so it cannot be chosen. "
                    + "Choose a named style such as 'work'.",
                    ExitCode.InvalidArguments);
            }

            var catalogue = await Instructions
                .LibraryAsync(WorkspacePath, slug, cancellationToken)
                .ConfigureAwait(false);

            if (CodingStyles.Find(catalogue, wanted) is null)
            {
                return output.Fail(
                    $"No style named '{wanted}'. Run 'loadout style list' to see what there is, or "
                    + $"start it with: loadout instructions new style.{wanted}",
                    ExitCode.InvalidArguments);
            }
        }

        SpecialistPreferences target;
        string where;

        if (settings.Profile is { Length: > 0 } profileName)
        {
            if (!manifest.Profiles.TryGetValue(profileName, out var profile))
            {
                return output.Fail(
                    $"{slug} has no profile named '{profileName}'.", ExitCode.InvalidArguments);
            }

            target = profile.Specialists;
            where = $"the {profileName} profile of {slug}";
        }
        else
        {
            target = manifest.Specialists;
            where = slug;
        }

        if (string.Equals(target.Style, wanted, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"{Markup.Escape(where)} already uses {Describe(wanted)}. Nothing to change.");

            return CommandOutput.Success();
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[bold]Would set[/] {Markup.Escape(where)} to use {Describe(wanted)}. Nothing was written.");

            return CommandOutput.Success();
        }

        target.Style = wanted;

        var written = await Workspace.WriteProjectAsync(manifest, cancellationToken).ConfigureAwait(false);

        if (written.Failed)
        {
            return output.Fail(written);
        }

        output.WriteLine($"[green]+[/] {Markup.Escape(where)} uses {Describe(wanted)}.");
        output.WriteLine("[dim]Takes effect at the next launch.[/]");

        return CommandOutput.Success();
    }

    private static string Describe(string style) =>
        style.Length == 0
            ? "no named style"
            : $"the [cyan]{Markup.Escape(style)}[/] style";
}

/// <summary>The named style a project or profile has chosen, read the way a launch reads it.</summary>
internal static class StyleChoice
{
    public static string? Of(ProjectManifest? manifest, string? profileName)
    {
        if (manifest is null)
        {
            return null;
        }

        if (profileName is { Length: > 0 }
            && manifest.Profiles.TryGetValue(profileName, out var profile)
            && profile.Specialists.Style is { Length: > 0 } fromProfile)
        {
            return fromProfile.Trim();
        }

        return manifest.Specialists.Style is { Length: > 0 } fromProject
            ? fromProject.Trim()
            : null;
    }
}
