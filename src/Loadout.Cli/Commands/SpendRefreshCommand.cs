using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Usage;
using Loadout.Models;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// Works out where spending stands and writes the answer down.
/// </summary>
/// <remarks>
/// <para>
/// The background half of the status line's spending segment. That line is
/// redrawn several times a minute and this scan reads the agents' transcripts,
/// which takes seconds — so the line reads a file and something else fills it.
/// This is that something else, started detached and never waited for.
/// </para>
/// <para>
/// It is also worth running by hand, which is why it is a command rather than
/// a hidden flag: somebody who has just changed a threshold should not have to
/// wait a quarter of an hour to see whether they are over it.
/// </para>
/// </remarks>
[Description("Work out where spending stands now and record it for the status line.")]
[CommandMeta(CommandCategory.Integration,
    Intent = "refresh spend thresholds recompute budget warning", Mutates = true)]
public sealed class SpendRefreshCommand : AsyncCommand<SpendRefreshCommand.Settings>
{
    private readonly ISpendWatch _spend;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;

    public SpendRefreshCommand(
        ISpendWatch spend,
        IProjectService projects,
        IAnsiConsole console)
    {
        _spend = spend;
        _projects = projects;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Project to work it out for. Defaults to the repository you are in.")]
        public string? Project { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var resolution = settings.Project is { Length: > 0 } handle
            ? await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false)
            : await _projects.ResolveFromDirectoryAsync(
                settings.Repo ?? Directory.GetCurrentDirectory(), cancellationToken)
                .ConfigureAwait(false);

        if (resolution.Failed)
        {
            return output.Fail(resolution);
        }

        var slug = resolution.Value!.Entry.Slug;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"Would work out where {Markup.Escape(slug)} stands. Nothing was written.");

            return CommandOutput.Success();
        }

        // Writing the answer down is what WarningsAsync already does; this
        // command exists to make that happen at a moment nobody is waiting.
        var said = await _spend.WarningsAsync(slug, cancellationToken).ConfigureAwait(false);

        if (output.IsJson)
        {
            output.WriteJson(new { project = slug, warnings = said });

            return CommandOutput.Success();
        }

        if (said.Count == 0)
        {
            output.WriteLine($"[dim]Nothing crossed for {Markup.Escape(slug)}.[/]");

            return CommandOutput.Success();
        }

        foreach (var line in said)
        {
            output.WriteLine($"[yellow]![/] {Markup.Escape(line)}");
        }

        return CommandOutput.Success();
    }
}
