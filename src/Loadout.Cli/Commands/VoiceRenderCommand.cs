using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Voice;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for rendering a voice.</summary>
public sealed class VoiceRenderSettings : VoiceSettings
{
    [CommandOption("--layer <LAYER>")]
    [Description("Which profile to render: personal (the default), named or codebase.")]
    [DefaultValue("personal")]
    public string Layer { get; init; } = "personal";

    [CommandOption("--style <NAME>")]
    [Description("The named style to render, with --layer named.")]
    public string? Style { get; init; }

    [CommandOption("--force")]
    [Description("Replace a rendered file that was changed by hand. The change is lost.")]
    public bool Force { get; init; }
}

/// <summary>
/// Renders a learned profile into the style specialist a launch loads.
/// </summary>
/// <remarks>
/// A learn run renders on its own, so this is for the cases it does not
/// cover: a profile edited by hand, a guard that refused, or a check of what
/// the rendered file would say before it says it.
/// </remarks>
[Description("Render a learned voice into the style specialist your sessions load.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "render voice style specialist apply learned rules sessions load",
    Mutates = true,
    Example = "voice render --dry-run")]
public sealed class VoiceRenderCommand : VoiceCommandBase<VoiceRenderSettings>
{
    public VoiceRenderCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console)
        : base(store, instructions, workspace, projects, git, console)
    {
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceRenderSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, _) = resolved.Value!;

        StyleLayer layer;
        string name;

        switch (settings.Layer.Trim().ToLowerInvariant())
        {
            case "personal":
                (layer, name) = (StyleLayer.Personal, CodingStyles.Personal);
                break;

            case "codebase" when project is not null:
                (layer, name) = (StyleLayer.Codebase, CodingStyles.Codebase);
                break;

            case "codebase":
                return output.Fail(
                    "A codebase voice belongs to a registered project, and this is not one. Pass --project.",
                    ExitCode.InvalidArguments);

            case "named" when settings.Style is { Length: > 0 } style
                && CodingStyles.LayerOf(style.Trim().ToLowerInvariant(), style.Trim().ToLowerInvariant()) == StyleLayer.Named:
                (layer, name) = (StyleLayer.Named, style.Trim().ToLowerInvariant());
                break;

            case "named":
                return output.Fail("--layer named needs --style with the style's name.", ExitCode.InvalidArguments);

            default:
                return output.Fail(
                    $"'{settings.Layer}' is not a layer. Choose personal, named or codebase.", ExitCode.InvalidArguments);
        }

        var rendered = await Store
            .RenderAsync(layer, name, guide, project?.Entry.Slug, settings.Force, settings.DryRun, cancellationToken)
            .ConfigureAwait(false);

        if (rendered.Failed)
        {
            return output.Fail(rendered);
        }

        var result = rendered.Value!;

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun = settings.DryRun,
                id = result.Id,
                path = result.Path,
                state = VoiceRenderReport.State(result.State),
                estimatedTokens = result.EstimatedTokens,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(VoiceRenderReport.Line(result, settings.DryRun, layer, name));

        return CommandOutput.Success();
    }
}

/// <summary>What rendering did, in words, for this command and for a learn run.</summary>
internal static class VoiceRenderReport
{
    public static string State(VoiceRenderState state) => state switch
    {
        VoiceRenderState.Written => "written",
        VoiceRenderState.Unchanged => "unchanged",
        VoiceRenderState.Removed => "removed",
        VoiceRenderState.Nothing => "nothing",
        _ => "hand-edited",
    };

    /// <param name="rendered">What happened.</param>
    /// <param name="dryRun">Whether anything was written.</param>
    /// <param name="layer">The layer, so the command to replace a hand-edited file names it.</param>
    /// <param name="name">The style's name.</param>
    public static string Line(VoiceRendered rendered, bool dryRun, StyleLayer layer, string name)
    {
        var id = Markup.Escape(rendered.Id);

        return rendered.State switch
        {
            VoiceRenderState.Written =>
                $"[green]+[/] {(dryRun ? "Would render" : "Rendered")} [cyan]{id}[/], "
                + $"about {rendered.EstimatedTokens:N0} tokens on every launch its style is in force."
                + (dryRun ? " Nothing was written." : $"\n  [dim]{Markup.Escape(rendered.Path)}[/]"),
            VoiceRenderState.Unchanged => $"[dim]{id} already says this. Nothing to change.[/]",
            VoiceRenderState.Removed =>
                $"[dim]The profile has no rules, so {id} {(dryRun ? "would be" : "was")} taken away.[/]",
            VoiceRenderState.Nothing => "[dim]No rules learned yet, so there is nothing to render.[/]",
            _ =>
                $"[yellow]{id} was changed by hand, so it was left as it is.[/] Rules belong in the profile, "
                + $"where learning and checking can see them. Replace the file with: loadout voice render{Markup.Escape(Flags(layer, name))} --force"
                + $"\n  [dim]{Markup.Escape(rendered.Path)}[/]",
        };
    }

    private static string Flags(StyleLayer layer, string name) => layer switch
    {
        StyleLayer.Named => $" --layer named --style {name}",
        StyleLayer.Codebase => " --layer codebase",
        _ => string.Empty,
    };
}
