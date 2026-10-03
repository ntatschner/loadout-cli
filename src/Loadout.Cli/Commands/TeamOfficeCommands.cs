using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Whether an office set's tile scene is fit for the dashboard to draw.
/// </summary>
/// <remarks>
/// The dashboard leaves out a scene that fails rather than drawing it wrong for
/// a whole run, and a page is no place to explain why. This is where somebody
/// making a set finds out, a place at a time.
/// </remarks>
[Description("Check an office set's tile scene and say what is wrong with it.")]
[CommandMeta(CommandCategory.Start, Intent = "team office check scene tiles sprites set pack art validate")]
public sealed class TeamOfficeCheckCommand : Command<TeamOfficeCheckCommand.Settings>
{
    private readonly IPlatformPaths _paths;
    private readonly IAnsiConsole _console;

    public TeamOfficeCheckCommand(IPlatformPaths paths, IAnsiConsole console)
    {
        _paths = paths;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "<SET>")]
        [Description("The set: a directory under <state>/teams/office.")]
        public string Set { get; init; } = string.Empty;
    }

    /// <inheritdoc />
    protected override int Execute(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var root = OfficeArt.Root(_paths);

        if (!OfficeArt.Names(settings.Set) || !Directory.Exists(Path.Combine(root, settings.Set)))
        {
            return output.Fail($"There is no set called '{settings.Set}' in {root}.", ExitCode.InvalidArguments);
        }

        // A kit is what the building is generated from, and a set with one is
        // checked as a kit, with its rules; a scene is a single room.
        if (OfficeKits.Has(root, settings.Set))
        {
            return Kit(output, root, settings.Set);
        }

        if (!OfficeScenes.Has(root, settings.Set))
        {
            return output.Fail(
                $"'{settings.Set}' has no {OfficeKit.FileName} or {OfficeScene.FileName}: it is a painted room, which the office no longer draws, and only kits and tile scenes are checked.",
                ExitCode.InvalidArguments);
        }

        var check = OfficeScenes.Check(root, settings.Set);

        if (output.IsJson)
        {
            output.WriteJson(new { set = settings.Set, fit = check.Fit, problems = check.Problems });
        }
        else if (check.Fit)
        {
            var scene = check.Scene!;

            output.WriteLine(
                $"[green]{Markup.Escape(settings.Set)}[/] is fit to draw: "
                + $"{scene.Width}x{scene.Height} tiles, {scene.Desks.Count} desk(s), "
                + (scene.Sheets is { Count: > 0 } sheets ? $"{sheets.Count} sheet(s)." : "people drawn in the kit's shapes."));
        }
        else
        {
            output.WriteLine($"[red]{Markup.Escape(settings.Set)}[/] will not be drawn until these are put right:");

            foreach (var problem in check.Problems)
            {
                output.WriteLine($"  {Markup.Escape(problem)}");
            }
        }

        return check.Fit ? CommandOutput.Success() : (int)ExitCode.ConfigurationInvalid;
    }

    private static int Kit(CommandOutput output, string root, string set)
    {
        var check = OfficeKits.Check(root, set);

        if (output.IsJson)
        {
            output.WriteJson(new { set, kind = "kit", fit = check.Fit, problems = check.Problems, drawnFromTheBuiltInKit = check.Missing });
        }
        else if (check.Fit)
        {
            var kit = check.Kit!;

            output.WriteLine(
                $"[green]{Markup.Escape(set)}[/] is a kit the building can be made from: "
                + $"{kit.Pieces.Count} piece(s), {kit.Tilesets.Count} tileset(s), {kit.Sheets?.Count ?? 0} sheet(s), "
                + $"{kit.Materials?.Count ?? 0} material(s) for the neighbourhood.");
        }
        else
        {
            output.WriteLine($"[red]{Markup.Escape(set)}[/] will not be used until these are put right:");

            foreach (var problem in check.Problems)
            {
                output.WriteLine($"  {Markup.Escape(problem)}");
            }
        }

        // Not a fault: a pack can be built up a piece at a time, and whatever
        // it lacks is drawn in the built-in kit's shapes meanwhile.
        if (!output.IsJson && check.Missing.Count > 0)
        {
            output.WriteLine($"[dim]Drawn from the built-in kit until the set has them: {Markup.Escape(string.Join(", ", check.Missing))}.[/]");
        }

        return check.Fit ? CommandOutput.Success() : (int)ExitCode.ConfigurationInvalid;
    }
}
