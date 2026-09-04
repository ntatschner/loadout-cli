using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Git;
using Loadout.Core.Instructions;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Core.Workspace;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Options for the specialist statistics report.</summary>
public sealed class InstructionsStatsSettings : GlobalSettings
{
    [CommandOption("--days <COUNT>")]
    [Description("How many days back to include, counting today. Defaults to 30.")]
    public int Days { get; init; } = 30;

    [CommandOption("--project <SLUG>")]
    [Description("Only launches of this project.")]
    public string? Project { get; init; }
}

/// <summary>
/// Which specialists the work actually reaches.
/// </summary>
/// <remarks>
/// <para>
/// <c>instructions explain</c> answers this for one task before it runs.
/// Afterwards there was no answer at all: the transcripts an agent leaves say
/// what a session cost and never what it was told, so a specialist that has
/// never once been composed looked exactly like one composed every day.
/// </para>
/// <para>
/// Read from the launch ledger, which only started being written when it was
/// added. A report that covers a week the ledger did not exist for shows that
/// week as having no launches, and says so rather than implying the library went
/// unused.
/// </para>
/// </remarks>
[Description("Say which specialists launches actually reached, and which none did.")]
[CommandMeta(CommandCategory.AgentConfiguration, Intent = "specialists used unused never loaded cost history")]
public sealed class InstructionsStatsCommand : AsyncCommand<InstructionsStatsSettings>
{
    private readonly IInstructionService _instructions;
    private readonly ILaunchLedger _ledger;
    private readonly IWorkspaceManager _workspace;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public InstructionsStatsCommand(
        IInstructionService instructions,
        ILaunchLedger ledger,
        IWorkspaceManager workspace,
        IProjectService projects,
        IAnsiConsole console,
        TimeProvider time)
    {
        _instructions = instructions;
        _ledger = ledger;
        _workspace = workspace;
        _projects = projects;
        _console = console;
        _time = time;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        InstructionsStatsSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Days <= 0)
        {
            return output.Fail("--days has to be at least 1.", Models.ExitCode.InvalidArguments);
        }

        var since = _time.GetUtcNow().AddDays(-settings.Days);

        var read = await _ledger.ReadAsync(since, cancellationToken).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var records = read.Value!;

        if (settings.Project is { Length: > 0 } handle)
        {
            var resolved = await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return output.Fail(resolved);
            }

            var slug = resolved.Value!.Entry.Slug;

            records = records
                .Where(record => string.Equals(record.ProjectSlug, slug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var workspacePath = _workspace.IsAvailable() ? _workspace.LocalPath : null;

        var catalogue = await _instructions
            .LibraryAsync(workspacePath, ct: cancellationToken)
            .ConfigureAwait(false);

        var statistics = LaunchStatistics.From(
            records,
            catalogue.All.ToDictionary(
                specialist => specialist.Id,
                specialist => specialist.EstimatedTokens,
                StringComparer.OrdinalIgnoreCase));

        if (output.IsJson)
        {
            output.WriteJson(statistics);

            return CommandOutput.Success();
        }

        Render(output, statistics, settings.Days);

        return CommandOutput.Success();
    }

    private static void Render(CommandOutput output, LaunchStatistics statistics, int days)
    {
        if (statistics.Launches == 0)
        {
            output.WriteLine(
                $"[yellow]No launches recorded in the last {days} day(s).[/]");

            // The one reading that would otherwise be misread as a finding about
            // the library rather than about the record.
            output.WriteLine(
                "[dim]The ledger only holds launches made since it was added, so a window "
                + "reaching further back than that is empty rather than quiet.[/]");

            return;
        }

        output.WriteLine(
            $"[bold]{statistics.Launches}[/] launch(es) over {days} day(s), "
            + $"{Number(statistics.EstimatedTokens)} estimated instruction token(s)");

        if (statistics.NeverClosed > 0)
        {
            output.WriteLine(
                $"[dim]{statistics.NeverClosed} never recorded an ending — killed, or still going. "
                + "'loadout doctor' and the launcher say which.[/]");
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[bold]Reached[/]  [dim]{statistics.Loaded.Count} of {statistics.LibrarySize}[/]");

        foreach (var usage in statistics.Loaded)
        {
            var share = usage.Launches * 100.0 / statistics.Launches;

            output.WriteLine(
                $"  {Markup.Escape(usage.Id).PadRight(34)} "
                + $"{usage.Launches,4}  {share,3:F0}%  "
                + $"[dim]{Number(usage.TokensNow)} token(s) now[/]");
        }

        if (statistics.NeverLoaded.Count == 0)
        {
            return;
        }

        output.WriteBlankLine();
        output.WriteLine($"[bold]Never reached[/]  [dim]{statistics.NeverLoaded.Count}[/]");

        foreach (var id in statistics.NeverLoaded)
        {
            output.WriteLine($"  [dim]{Markup.Escape(id)}[/]");
        }
    }

    private static string Number(long value) =>
        value.ToString("N0", CultureInfo.InvariantCulture);
}
