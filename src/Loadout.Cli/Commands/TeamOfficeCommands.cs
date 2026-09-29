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

        if (!OfficeScenes.Has(root, settings.Set))
        {
            return output.Fail(
                $"'{settings.Set}' has no {OfficeScene.FileName}: it is a painted room, and only tile scenes are checked.",
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
}
