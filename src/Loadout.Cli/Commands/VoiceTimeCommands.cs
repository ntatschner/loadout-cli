using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Voice;
using Loadout.Core.Workspace;
using Loadout.Models;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Models.Voice;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options shared by the time commands.</summary>
public class VoiceTimeSettings : VoiceSettings
{
}

/// <summary>Options for changing a time setting.</summary>
public class VoiceTimeChangeSettings : VoiceTimeSettings
{
    [CommandArgument(0, "<SETTING>")]
    [Description("estimates, or format.<surface>: format.doc, format.commit, format.pr, format.chat or format.ui.")]
    public string Setting { get; init; } = string.Empty;

    [CommandOption("--layer <LAYER>")]
    [Description("Whose setting it is: personal (the default) or codebase, which needs a registered project.")]
    [DefaultValue("personal")]
    public string Layer { get; init; } = "personal";
}

/// <summary>Options for setting one.</summary>
public sealed class VoiceTimeSetSettings : VoiceTimeChangeSettings
{
    [CommandArgument(1, "<VALUE>")]
    [Description("For estimates: range-with-owner or never. For a format: a preset such as iso or long-british, or pattern:<format>.")]
    public string Value { get; init; } = string.Empty;
}

/// <summary>Options for removing one.</summary>
public sealed class VoiceTimeUnsetSettings : VoiceTimeChangeSettings
{
}

/// <summary>Shared by the time commands: the time guide, and where a setting goes.</summary>
public abstract class VoiceTimeCommandBase<TSettings> : VoiceCommandBase<TSettings>
    where TSettings : VoiceTimeSettings
{
    protected VoiceTimeCommandBase(
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
    protected override string GuideOf(TSettings settings) => VoiceGuides.Time().Id;

    /// <summary>
    /// The rule id a setting is kept under, or why it is not one.
    /// </summary>
    protected static OperationResult<(string Id, string? Surface)> ParseSetting(string setting, VoiceGuide guide)
    {
        var text = setting.Trim().ToLowerInvariant();

        if (text == VoiceTime.EstimatesRule)
        {
            return OperationResult<(string, string?)>.Ok((VoiceTime.EstimatesRule, null));
        }

        if (text.StartsWith(VoiceTime.FormatPrefix, StringComparison.Ordinal)
            && text[VoiceTime.FormatPrefix.Length..] is { Length: > 0 } surface
            && guide.Surfaces.Contains(surface, StringComparer.Ordinal))
        {
            return OperationResult<(string, string?)>.Ok((VoiceTime.FormatRule(surface), surface));
        }

        return OperationResult<(string, string?)>.Fail(
            $"'{setting}' is not a time setting. There is estimates, and "
            + $"{string.Join(", ", guide.Surfaces.Select(VoiceTime.FormatRule))}.",
            ExitCode.InvalidArguments);
    }

    /// <summary>The layer a change goes to: personal, or the project's codebase.</summary>
    protected static OperationResult<(StyleLayer Layer, string Name)> LayerOf(string layer, ProjectResolution? project) =>
        layer.Trim().ToLowerInvariant() switch
        {
            "personal" => OperationResult<(StyleLayer, string)>.Ok((StyleLayer.Personal, CodingStyles.Personal)),
            "codebase" when project is not null =>
                OperationResult<(StyleLayer, string)>.Ok((StyleLayer.Codebase, CodingStyles.Codebase)),
            "codebase" => OperationResult<(StyleLayer, string)>.Fail(
                "A codebase setting belongs to a registered project, and this is not one. Pass --project.",
                ExitCode.InvalidArguments),
            _ => OperationResult<(StyleLayer, string)>.Fail(
                $"'{layer}' is not a layer a time setting can go in. Choose personal or codebase.",
                ExitCode.InvalidArguments),
        };

    /// <summary>
    /// Writes the changed profile, unless this is a dry run, and renders the
    /// layer so sessions see the change.
    /// </summary>
    protected async Task<int> SaveAsync(
        CommandOutput output,
        bool dryRun,
        VoiceGuide guide,
        ProjectResolution? project,
        StyleLayer layer,
        string name,
        VoiceProfile profile,
        string done,
        CancellationToken ct)
    {
        if (Store.PathOf(layer, name, guide.Id, project?.Entry.Slug) is not { } path)
        {
            return output.Fail(
                "There is no workspace on this machine, so there is nowhere to keep a codebase setting. Run 'loadout setup' first.",
                ExitCode.ConfigurationInvalid);
        }

        if (!dryRun)
        {
            var written = await VoiceStore.WriteProfileAsync(path, profile, ct).ConfigureAwait(false);

            if (written.Failed)
            {
                return output.Fail(written);
            }
        }

        // A dry run renders against the profile on disk, which this run has
        // not changed, so it could only ever say "unchanged". Say what would
        // be written and stop there.
        VoiceRendered? rendered = null;

        // A personal setting is kept in user configuration, which every
        // machine has, but rendered into the workspace, which a machine may
        // not have yet. The setting still counts for 'voice check'.
        var renderable = Store.SpecialistPath(layer, name, project?.Entry.Slug) is not null;

        if (!dryRun && renderable)
        {
            var render = await Store
                .RenderAsync(layer, name, project?.Entry.Slug, ct: ct)
                .ConfigureAwait(false);

            if (render.Failed)
            {
                return output.Fail(render);
            }

            rendered = render.Value!;
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                dryRun,
                layer = Layer(layer),
                path,
                rules = profile.Rules.Select(r => new { id = r.Id, value = r.Check?.Value }),
                rendered = rendered is null ? null : new
                {
                    id = rendered.Id,
                    path = rendered.Path,
                    state = VoiceRenderReport.State(rendered.State),
                },
            });

            return CommandOutput.Success();
        }

        output.WriteLine(dryRun
            ? $"[dim]Would have {Markup.Escape(done)} in your {Layer(layer)} voice. Nothing was written.[/]"
            : $"[green]+[/] {char.ToUpperInvariant(done[0])}{Markup.Escape(done[1..])} in your {Layer(layer)} voice.");

        if (rendered is not null)
        {
            output.WriteLine(VoiceRenderReport.Line(rendered, dryRun: false, layer, name));
        }
        else if (!renderable)
        {
            output.WriteLine(
                "[yellow]There is no workspace on this machine, so sessions will not be told this until "
                + "'loadout setup' has made one and 'loadout voice render' has run.[/] 'loadout voice check' uses it already.");
        }

        return CommandOutput.Success();
    }
}

/// <summary>
/// Shows the time settings in force on each surface and the layer each came from.
/// </summary>
[Description("Show how dates are written and estimates given on each surface, and which layer set each.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "time date format estimates how long voice surface setting which layer")]
public sealed class VoiceTimeShowCommand : VoiceTimeCommandBase<VoiceTimeSettings>
{
    public VoiceTimeShowCommand(
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
        VoiceTimeSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, layers) = resolved.Value!;

        var surfaces = VoiceTime.Effective(guide, layers);

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                guide = guide.Reference,
                project = project?.Entry.Slug,
                personalSurfaces = guide.PersonalSurfaces,
                surfaces = surfaces.Select(s => new
                {
                    surface = s.Surface,
                    estimates = s.Estimates,
                    estimatesFrom = s.EstimatesFrom is { } e ? Layer(e) : "default",
                    format = s.Format,
                    formatFrom = s.FormatFrom is { } f ? Layer(f) : null,
                    checkable = !guide.PersonalSurfaces.Contains(s.Surface, StringComparer.Ordinal),
                }),
            });

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(guide.Reference)}[/]  [dim]{Markup.Escape(guide.Summary)}[/]");
        output.WriteLine(
            $"[dim]Your setting wins on {string.Join(" and ", guide.PersonalSurfaces)}; the codebase's wins elsewhere. "
            + "Each falls back to the other.[/]");
        output.WriteBlankLine();

        foreach (var surface in surfaces)
        {
            var estimates = surface.EstimatesFrom is { } from
                ? $"{surface.Estimates} [dim]({Layer(from)})[/]"
                : $"{surface.Estimates} [dim](default)[/]";

            var format = surface.Format is { } value
                ? $"{Markup.Escape(value)} [dim]({Layer(surface.FormatFrom!.Value)})[/]"
                : "[dim]not set[/]";

            var guidance = guide.PersonalSurfaces.Contains(surface.Surface, StringComparer.Ordinal)
                ? "  [dim]guidance only[/]"
                : string.Empty;

            output.WriteLine($"[cyan]{surface.Surface,-6}[/] estimates {estimates}  format {format}{guidance}");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]Formats: {Markup.Escape(string.Join(", ", VoiceTime.Presets.Select(p => p.Name)))}, or pattern:<format>. "
            + "Change one with: loadout voice time set format.commit iso[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Sets how estimates are given, or how dates are written on one surface.</summary>
[Description("Set how estimates are given, or how dates are written on one surface.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "time date format estimates how long set voice surface preset iso",
    Mutates = true,
    Example = "voice time set format.commit iso")]
public sealed class VoiceTimeSetCommand : VoiceTimeCommandBase<VoiceTimeSetSettings>
{
    private readonly TimeProvider _clock;

    public VoiceTimeSetCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider clock)
        : base(store, instructions, workspace, projects, git, console)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceTimeSetSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, layers) = resolved.Value!;

        var parsed = ParseSetting(settings.Setting, guide);

        if (parsed.Failed)
        {
            return output.Fail(parsed);
        }

        var (id, surface) = parsed.Value;
        var value = settings.Value.Trim();
        var now = _clock.GetUtcNow();
        VoiceRule rule;

        if (surface is null)
        {
            value = value.ToLowerInvariant();

            if (!VoiceTime.Policies.Contains(value, StringComparer.Ordinal))
            {
                return output.Fail(
                    $"'{settings.Value}' is not an estimates policy. Choose {string.Join(" or ", VoiceTime.Policies)}.",
                    ExitCode.InvalidArguments);
            }

            rule = VoiceTime.EstimatesSetting(value, now);
        }
        else
        {
            if (VoiceTime.FormatProblem(value) is { } problem)
            {
                return output.Fail(problem, ExitCode.InvalidArguments);
            }

            if (VoiceTime.Preset(value) is { } preset)
            {
                value = preset.Name;
            }

            rule = VoiceTime.FormatSetting(surface, value, now);
        }

        var target = LayerOf(settings.Layer, project);

        if (target.Failed)
        {
            return output.Fail(target);
        }

        var (layer, name) = target.Value;

        var profile = layers.FirstOrDefault(l => l.Layer == layer)?.Profile
            ?? new VoiceProfile { Guide = guide.Reference };

        profile.Guide = guide.Reference;
        profile.Updated = now;
        profile.Rules.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        profile.Rules.Add(rule);

        return await SaveAsync(
            output, settings.DryRun, guide, project, layer, name, profile, $"set {id} to {value}", cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>Removes a time setting from one layer, so the other layer's, or the default, applies.</summary>
[Description("Remove a time setting from one layer, so the other layer's or the default applies.")]
[CommandMeta(CommandCategory.AgentConfiguration,
    Intent = "time date format estimates remove unset clear voice setting",
    Mutates = true,
    Example = "voice time unset format.commit")]
public sealed class VoiceTimeUnsetCommand : VoiceTimeCommandBase<VoiceTimeUnsetSettings>
{
    private readonly TimeProvider _clock;

    public VoiceTimeUnsetCommand(
        VoiceStore store,
        IInstructionService instructions,
        IWorkspaceManager workspace,
        IProjectService projects,
        IGitManager git,
        IAnsiConsole console,
        TimeProvider clock)
        : base(store, instructions, workspace, projects, git, console)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        VoiceTimeUnsetSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(Console, settings);

        var resolved = await ResolveAsync(settings, cancellationToken).ConfigureAwait(false);

        if (resolved.Failed)
        {
            return output.Fail(resolved);
        }

        var (guide, project, layers) = resolved.Value!;

        var parsed = ParseSetting(settings.Setting, guide);

        if (parsed.Failed)
        {
            return output.Fail(parsed);
        }

        var target = LayerOf(settings.Layer, project);

        if (target.Failed)
        {
            return output.Fail(target);
        }

        var (layer, name) = target.Value;
        var id = parsed.Value.Id;

        if (layers.FirstOrDefault(l => l.Layer == layer)?.Profile is not { } profile
            || profile.Rules.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            if (output.IsJson)
            {
                output.WriteJson(new { dryRun = settings.DryRun, layer = Layer(layer), removed = false });

                return CommandOutput.Success();
            }

            output.WriteLine($"[dim]Your {Layer(layer)} voice does not set {Markup.Escape(id)}. Nothing to remove.[/]");

            return CommandOutput.Success();
        }

        profile.Updated = _clock.GetUtcNow();

        return await SaveAsync(
            output, settings.DryRun, guide, project, layer, name, profile, $"removed {id}", cancellationToken)
            .ConfigureAwait(false);
    }
}
