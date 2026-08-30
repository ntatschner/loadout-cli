using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Usage;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>Options for the usage report.</summary>
public sealed class UsageSettings : GlobalSettings
{
    [CommandOption("--days <COUNT>")]
    [Description("How many days back to include, counting today. Defaults to 30.")]
    public int Days { get; init; } = 30;

    [CommandOption("--project <SLUG>")]
    [Description("Only this registered project.")]
    public string? Project { get; init; }

    // --agent is a global option already, and it narrows the report here
    // rather than choosing what to launch. Redeclaring it would shadow the
    // inherited one and give the same flag two meanings.

    [CommandOption("--by <GROUPING>")]
    [Description("Break down by project, day, model or agent. Defaults to project.")]
    public string By { get; init; } = "project";
}

/// <summary>
/// Says what the agents have spent, and where it went.
/// </summary>
/// <remarks>
/// <para>
/// Both agents record their own accounting and neither will show it to you
/// across projects, across agents, or for any window other than the session in
/// front of you. That is the gap this fills, using files the launcher already
/// opens to build the resume list.
/// </para>
/// <para>
/// Reported in tokens rather than money on purpose. The agents' own cost
/// figures are computed from public list rates, which is not what a
/// subscription charges — printing "spent this week" against those rates would
/// show a number in the thousands that nobody was ever billed. Tokens are what
/// actually happened.
/// </para>
/// </remarks>
[Description("Show what the agents have spent, by project, day, model or agent.")]
[CommandMeta(CommandCategory.Administration, Intent = "tokens cost spend usage stats how much")]
public sealed class UsageCommand : AsyncCommand<UsageSettings>
{
    private readonly IUsageService _usage;
    private readonly IPlanHeadroomReader _headroom;
    private readonly IAnsiConsole _console;

    public UsageCommand(
        IUsageService usage,
        IPlanHeadroomReader headroom,
        IAnsiConsole console)
    {
        _usage = usage;
        _headroom = headroom;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, UsageSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var grouping = Grouping(settings.By);

        if (grouping is null)
        {
            return output.Fail(
                $"'{settings.By}' is not something to group by. "
                + "Use project, day, model or agent.",
                ExitCode.InvalidArguments);
        }

        var result = await _usage.ReportAsync(new UsageQuery(
            settings.Days,
            settings.Project,
            settings.Agent)).ConfigureAwait(false);

        if (result.Failed)
        {
            return output.Fail(result);
        }

        var report = result.Value!;

        // Best effort and never blocking: this is an extra sentence at the
        // bottom, and failing to find it must not cost somebody the report.
        var headroomResult = await _headroom.LatestAsync().ConfigureAwait(false);
        var headroom = headroomResult.Succeeded ? headroomResult.Value : null;

        // A directory an agent worked in but spent nothing on is a row of
        // zeroes: it pushes the rows that matter off the screen and answers
        // nothing. Dropped rather than shown, and only ever zeroes — anything
        // that cost something is listed however small.
        var rows = Rows(report, grouping)
            .Where(row => row.Totals.Total > 0)
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                since = report.Since.ToString("yyyy-MM-dd"),
                complete = report.Integrity.IsComplete,
                caveat = report.Integrity.Caveat,
                totals = Describe(report.Totals),
                groupedBy = grouping,
                groups = rows.Select(row => new
                {
                    name = row.Name,
                    registered = row.IsRegistered,
                    totals = Describe(row.Totals),
                }),
                read = new
                {
                    filesRead = report.Integrity.FilesRead,
                    filesSkipped = report.Integrity.FilesSkipped,
                    recordsCounted = report.Integrity.RecordsCounted,
                    recordsRepeated = report.Integrity.RecordsRepeated,
                    recordsUnrecognised = report.Integrity.RecordsUnrecognised,
                },
            });

            return CommandOutput.Success();
        }

        if (rows.Count == 0)
        {
            output.WriteLine(
                $"[dim]No agent usage recorded since {report.Since:yyyy-MM-dd}.[/]");

            // Nothing recorded and nothing readable are different answers, and
            // the second one needs saying even when the table is empty.
            WriteCaveat(output, report);

            return CommandOutput.Success();
        }

        var table = new Table()
            .Border(TableBorder.None)
            .AddColumn(new TableColumn($"[dim]{grouping}[/]"))
            .AddColumn(new TableColumn("[dim]total[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]output[/]").RightAligned())
            .AddColumn(new TableColumn("[dim]cached[/]").RightAligned());

        foreach (var row in rows)
        {
            table.AddRow(
                row.IsRegistered
                    ? $"[cyan]{row.Name.EscapeMarkup()}[/]"
                    : $"[dim]{row.Name.EscapeMarkup()}[/]",
                Short(row.Totals.Total),
                Short(row.Totals.Output),
                Percentage(row.Totals.CacheHitFraction));
        }

        table.AddEmptyRow();
        table.AddRow(
            "[bold]total[/]",
            $"[bold]{Short(report.Totals.Total)}[/]",
            $"[bold]{Short(report.Totals.Output)}[/]",
            $"[bold]{Percentage(report.Totals.CacheHitFraction)}[/]");

        output.WriteLine($"[dim]Since {report.Since:yyyy-MM-dd}[/]");
        output.WriteBlankLine();
        output.Write(table);
        output.WriteBlankLine();

        if (report.Totals.SavedFraction is { } saved)
        {
            // Said as what it is: an efficiency the agents earned by caching,
            // measured against sending everything afresh every turn.
            output.WriteLine(
                $"Caching avoided [green]{saved * 100:N1}%[/] of input: "
                + $"{Short((long)report.Totals.UncachedInputEquivalent)} uncached "
                + $"would have been {Short((long)report.Totals.BilledInputEquivalent)}.");
        }

        if (report.Totals.Thinking > 0)
        {
            output.WriteLine(
                $"[dim]{Short(report.Totals.Thinking)} of the output was thinking.[/]");
        }

        WriteHeadroom(output, headroom);
        WriteCaveat(output, report);

        return CommandOutput.Success();
    }

    /// <summary>
    /// Says where the plan allowance stood when an agent last mentioned it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a subscription this is the figure that actually constrains the work,
    /// and it is the one neither agent will show you outside its own session.
    /// </para>
    /// <para>
    /// Always stamped with its age, never drawn as a gauge. Codex records it in
    /// well under half its sessions, so the honest form of this sentence is
    /// "this is what it was then" rather than "this is what it is". A figure
    /// from two days ago presented as current is worse than no figure, because
    /// somebody would act on it.
    /// </para>
    /// </remarks>
    private static void WriteHeadroom(CommandOutput output, PlanHeadroom? headroom)
    {
        if (headroom is null)
        {
            return;
        }

        var age = headroom.Age(DateTimeOffset.UtcNow);

        var when = age.TotalMinutes < 90
            ? $"{age.TotalMinutes:N0} minutes ago"
            : age.TotalHours < 36
                ? $"{age.TotalHours:N0} hours ago"
                : $"{age.TotalDays:N0} days ago";

        var spent = headroom.UsedFraction;

        var colour = spent >= 0.9 ? "red" : spent >= 0.7 ? "yellow" : "green";

        var plan = headroom.Plan is { Length: > 0 } named ? $" {named}" : string.Empty;

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{headroom.Agent.EscapeMarkup()}{plan.EscapeMarkup()} plan, as of {when}:[/] "
            + $"[{colour}]{spent * 100:N0}%[/] [dim]of the {headroom.WindowName} used.[/]");
    }

    /// <summary>
    /// Says so when the totals could not be trusted.
    /// </summary>
    /// <remarks>
    /// The whole reason this is separate from the listing path. Neither
    /// transcript format is a contract, so a renamed field is a question of
    /// when rather than whether — and a reader that shrugged one off would
    /// print a smaller number that looked just as convincing as the right one.
    /// </remarks>
    private static void WriteCaveat(CommandOutput output, UsageReport report)
    {
        if (report.Integrity.Caveat is { Length: > 0 } caveat)
        {
            output.WriteBlankLine();
            output.WriteLine($"[yellow]{caveat.EscapeMarkup()}[/]");
        }
    }

    /// <summary>The grouping asked for, or null when it was not one of them.</summary>
    private static string? Grouping(string? asked) =>
        asked?.Trim().ToLowerInvariant() switch
        {
            null or "" or "project" => "project",
            "day" or "days" or "date" => "day",
            "model" or "models" => "model",
            "agent" or "agents" => "agent",
            _ => null,
        };

    private static IReadOnlyList<UsageGroup> Rows(UsageReport report, string grouping) =>
        grouping switch
        {
            "day" => report.Days,
            "model" => report.Models,
            "agent" => report.Agents,
            _ => report.Projects,
        };

    /// <summary>The full set of counts, for callers that want to do their own arithmetic.</summary>
    private static object Describe(UsageTotals totals) => new
    {
        input = totals.Input,
        cacheWrite5m = totals.CacheWrite5m,
        cacheWrite1h = totals.CacheWrite1h,
        cacheRead = totals.CacheRead,
        output = totals.Output,
        thinking = totals.Thinking,
        total = totals.Total,
        billedInputEquivalent = Math.Round(totals.BilledInputEquivalent),
        uncachedInputEquivalent = Math.Round(totals.UncachedInputEquivalent),
        savedFraction = totals.SavedFraction,
        cacheHitFraction = totals.CacheHitFraction,
    };

    /// <summary>
    /// A token count somebody can read at a glance. Billions are ordinary here,
    /// and a column of full digits is a column nobody compares.
    /// </summary>
    private static string Short(long count)
    {
        var (value, suffix) = count switch
        {
            >= 1_000_000_000 => (count / 1_000_000_000d, "B"),
            >= 1_000_000 => (count / 1_000_000d, "M"),
            >= 1_000 => (count / 1_000d, "K"),
            _ => (count, string.Empty),
        };

        return suffix.Length == 0
            ? count.ToString(CultureInfo.InvariantCulture)
            : value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.InvariantCulture) + suffix;
    }

    /// <summary>
    /// A share, written the way a person writes one. The invariant culture's
    /// own percent format puts a space before the sign, which reads as a typo
    /// in a right-aligned column.
    /// </summary>
    private static string Percentage(double? fraction) =>
        fraction is { } value
            ? (value * 100).ToString("N1", CultureInfo.InvariantCulture) + "%"
            : "[dim]-[/]";
}
