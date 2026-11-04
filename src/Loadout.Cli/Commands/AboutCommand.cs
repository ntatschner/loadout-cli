using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.About;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// The installed version, what it can do, where each part is explained, and
/// the releases around it: the About page, as a command.
/// </summary>
/// <remarks>
/// Reads only. The release list honours <c>updates-source</c>: switched off,
/// <c>--offline</c> or a source that is not GitHub asks nobody, and the
/// timeline says which.
/// </remarks>
[Description("Show the installed version, the feature matrix, guides and the release timeline.")]
[CommandMeta(CommandCategory.Administration, Intent = "about version info features guides releases changelog")]
public sealed class AboutCommand : AsyncCommand<AboutCommand.Settings>
{
    private readonly IAboutService _about;
    private readonly IAnsiConsole _console;

    public AboutCommand(IAboutService about, IAnsiConsole console)
    {
        _about = about;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--refresh")]
        [Description("Ask GitHub for the releases now, even when the kept list is fresh.")]
        public bool Refresh { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var result = await _about.GetAsync(settings.Refresh, settings.Offline, cancellationToken).ConfigureAwait(false);
        if (result.Failed)
        {
            return output.Fail(result);
        }

        var about = result.Value!;

        if (output.IsJson)
        {
            output.WriteJson(about);
            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]Loadout[/] {Markup.Escape(about.Version)}{(about.DevBuild ? " [dim](development build)[/]" : string.Empty)}");
        output.WriteLine(about.UpdateAvailable
            ? $"[yellow]{Markup.Escape(about.Available!)} is available.[/] Install it with: loadout update"
            : "[dim]No newer version is known.[/]");

        output.WriteBlankLine();

        var features = new Table().AddColumns("Feature", "Since", "Status", "Installed", "Guide");
        foreach (var feature in about.Features)
        {
            features.AddRow(
                Markup.Escape(feature.Name),
                Markup.Escape(feature.Since),
                Markup.Escape(feature.Status),
                feature.Installed ? "yes" : "[yellow]no[/]",
                Markup.Escape(feature.Guide));
        }

        _console.Write(features);

        output.WriteBlankLine();
        output.WriteLine("[bold]Guides[/]");
        foreach (var guide in about.Guides)
        {
            output.WriteLine($"  {Markup.Escape(guide.Key),-24} {Markup.Escape(guide.Url)}");
        }

        output.WriteBlankLine();
        output.WriteLine("[bold]Releases[/]");

        if (about.Timeline.Empty is { } empty)
        {
            output.WriteLine($"[dim]{Markup.Escape(empty.Describe())}[/]");
        }

        if (about.Timeline.Stale)
        {
            output.WriteLine("[dim]GitHub could not be asked just now, so this is the list kept from before.[/]");
        }

        foreach (var release in about.Timeline.Releases)
        {
            output.WriteLine(
                $"  {Markup.Escape(release.Version),-10} {release.Kind,-8}"
                + (release.Published is { } published ? $" {published:yyyy-MM-dd}" : string.Empty)
                + (release.Installed ? string.Empty : " [yellow]not installed[/]"));
        }

        return CommandOutput.Success();
    }
}
