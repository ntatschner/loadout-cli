using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Events;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What needed you, and what you did: the event history the daemon keeps.
/// </summary>
/// <remarks>
/// <para>
/// Reads the files itself rather than asking the daemon, so it answers with
/// the daemon stopped, and through the same filter and the same fold as
/// Studio's Events page, so the two never disagree about what happened.
/// </para>
/// <para>
/// The options are the filter's parts, and <c>--filter</c> takes the whole
/// string as Studio's address holds it; an option given beside it wins, so a
/// view copied from the page can be narrowed further here.
/// </para>
/// </remarks>
[Description("Show what needed you over the last month, and what you did about it: questions, quiet runs, waiting sessions, answers, merges.")]
[CommandMeta(CommandCategory.Start,
    Intent = "events history notifications what needed me asked waiting approval unread attention log feed",
    Mutates = true,
    Example = "loadout events --since 1d --level needs")]
public sealed class EventsCommand : AsyncCommand<EventsCommand.Settings>
{
    private readonly IPlatformPaths _paths;
    private readonly IConfigurationService _configuration;
    private readonly TimeProvider _time;
    private readonly IAnsiConsole _console;

    public EventsCommand(IPlatformPaths paths, IConfigurationService configuration, TimeProvider time, IAnsiConsole console)
    {
        _paths = paths;
        _configuration = configuration;
        _time = time;
        _console = console;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandOption("--filter <filter>")]
        [Description("A whole filter as Studio's Events address holds it, such as 'level=needs&since=1d'.")]
        public string? Filter { get; init; }

        [CommandOption("--level <levels>")]
        [Description("needs, problem, done or info, comma separated.")]
        public string? Level { get; init; }

        [CommandOption("--project <slug>")]
        [Description("Only this project's events.")]
        public string? Project { get; init; }

        [CommandOption("--source <sources>")]
        [Description("run, session, daemon or notice, comma separated.")]
        public string? Source { get; init; }

        [CommandOption("--direction <direction>")]
        [Description("to-me (the default): what was said to you. by-me: what you did. any: both.")]
        public string? Direction { get; init; }

        [CommandOption("--since <when>")]
        [Description("The earliest: a span back from now such as 30m, 6h, 1d or 2w, or a date such as 2026-10-01.")]
        public string? Since { get; init; }

        [CommandOption("--until <when>")]
        [Description("The latest, written the same way as --since.")]
        public string? Until { get; init; }

        [CommandOption("--unread")]
        [Description("Only what has not been read.")]
        public bool Unread { get; init; }

        [CommandOption("--text <words>")]
        [Description("Words the title must contain, ignoring case.")]
        public string? Text { get; init; }

        [CommandOption("--limit <count>")]
        [Description("The most to show, newest first. 50 unless given.")]
        public int? Limit { get; init; }

        [CommandOption("--mark-read")]
        [Description("Mark what is shown as read, as opening it in Studio would.")]
        public bool MarkRead { get; init; }
    }

    /// <summary>The filter the options describe, --filter first and each option over it.</summary>
    internal static Loadout.Models.Results.OperationResult<EventFilter> FilterOf(Settings settings)
    {
        var parts = new List<string>();

        void Add(string key, string? value)
        {
            if (value is { Length: > 0 })
            {
                parts.Add(key + "=" + Uri.EscapeDataString(value));
            }
        }

        Add("level", settings.Level);
        Add("project", settings.Project);
        Add("source", settings.Source);
        Add("direction", settings.Direction);
        Add("since", settings.Since);
        Add("until", settings.Until);
        Add("unread", settings.Unread ? "true" : null);
        Add("text", settings.Text);

        // Read after the whole filter, so an option given beside it wins.
        return EventFilter.Parse(string.Join('&', new[] { settings.Filter ?? string.Empty }.Concat(parts).Where(one => one.Length > 0)));
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);
        var filter = FilterOf(settings);

        if (filter.Failed)
        {
            return output.Fail(filter.Error, Loadout.Models.ExitCode.InvalidArguments);
        }

        if (settings.Limit is < 1 or > EventFeed.MaximumLimit)
        {
            return output.Fail($"--limit is a whole number from 1 to {EventFeed.MaximumLimit}.", Loadout.Models.ExitCode.InvalidArguments);
        }

        var store = new EventStore(_paths.Paths.State);
        var days = EventStore.Days((await _configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false)).Value?.Teams.EventsDays);
        var now = _time.GetUtcNow();
        var page = EventFeed.Query(store, filter.Value!, now, settings.Limit ?? 50);
        var marking = settings.MarkRead && !settings.DryRun;

        if (marking && page.Items.Any(item => !item.Read))
        {
            store.MarkRead(page.Items.Where(item => !item.Read).Select(item => item.Event.Id));
            page = page with { Unread = EventFeed.Query(store, EventFilter.Everything, now, 1).Unread };
        }

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                items = page.Items,
                matched = page.Matched,
                unread = page.Unread,
                filter = filter.Value!.Format(),
                days,
                marked = marking,
            });

            return CommandOutput.Success();
        }

        if (!File.Exists(store.FeedPath))
        {
            output.WriteLine(
                "[dim]Nothing recorded yet. The daemon records what needs you while it runs: loadout daemon[/]");

            return CommandOutput.Success();
        }

        if (page.Items.Count == 0)
        {
            output.WriteLine($"[dim]Nothing matches. The history keeps {Kept(days)}.[/]");

            return CommandOutput.Success();
        }

        string? day = null;

        foreach (var item in page.Items)
        {
            var local = item.Event.At.ToLocalTime();
            var heading = local.ToString("dddd d MMMM", CultureInfo.CurrentCulture);

            if (heading != day)
            {
                if (day is not null)
                {
                    output.WriteBlankLine();
                }

                output.WriteLine($"[bold]{Markup.Escape(heading)}[/]");
                day = heading;
            }

            output.WriteLine(Line(item));
        }

        output.WriteBlankLine();

        if (page.Matched > page.Items.Count)
        {
            output.WriteLine($"[dim]{page.Items.Count} of {page.Matched} shown; --limit shows more.[/]");
        }

        output.WriteLine(settings.MarkRead && settings.DryRun
            ? $"[dim]Dry run: nothing was marked read. {page.Unread} unread need you or went wrong.[/]"
            : marking
                ? $"[dim]Marked read. {page.Unread} unread need you or went wrong.[/]"
                : $"[dim]{page.Unread} unread need you or went wrong. Mark what is shown read with --mark-read.[/]");

        return CommandOutput.Success();
    }

    /// <summary>One event as a line: when, how much it wants you, what, where, and whether it cleared.</summary>
    internal static string Line(FeedItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var one = item.Event;
        // Plain characters, which every console font and every pipe has: a
        // tick written to a redirected Windows console comes out as a V.
        var (mark, colour) = one.Level switch
        {
            EventLevels.Needs => ("?", "yellow"),
            EventLevels.Problem => ("!", "red"),
            EventLevels.Done => ("+", "green"),
            _ => ("-", "grey"),
        };

        var where = one.Run is { Length: > 0 } run
            ? $"loadout team status {run}"
            : one.Session is { Length: > 0 } session
                ? $"Studio, session {session}"
                : null;

        var resolved = item.Cleared is { } cleared
            ? $"  [dim]resolved {After(cleared - one.At)} later[/]"
            : string.Empty;

        var title = item.Cleared is null ? Markup.Escape(one.Title) : $"[dim]{Markup.Escape(one.Title)}[/]";

        return $"  {(item.Read ? " " : "[bold]*[/]")} [dim]{one.At.ToLocalTime():HH:mm}[/] [{colour}]{mark}[/] {title}"
            + (one.Project is { Length: > 0 } project ? $" [dim]({Markup.Escape(project)})[/]" : string.Empty)
            + resolved
            + (where is null ? string.Empty : $"{Environment.NewLine}           [dim]{Markup.Escape(where)}[/]");
    }

    /// <summary>A duration as somebody would say it, rounded to the unit that matters.</summary>
    internal static string After(TimeSpan span) =>
        span < TimeSpan.FromMinutes(1) ? "under a minute"
        : span < TimeSpan.FromHours(1) ? $"{(int)span.TotalMinutes}m"
        : span < TimeSpan.FromDays(1) ? $"{(int)span.TotalHours}h {span.Minutes}m"
        : $"{(int)span.TotalDays}d";

    private static string Kept(int days) => days switch
    {
        0 => "everything up to its size limit",
        1 => "1 day",
        _ => $"{days.ToString(CultureInfo.InvariantCulture)} days",
    };
}
