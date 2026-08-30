using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Usage;
using Loadout.Models;
using Spectre.Console;
using Spectre.Console.Cli;
using Loadout.Tui;

namespace Loadout.Cli.Commands;

/// <summary>
/// Receives what the agents report about their own usage.
/// </summary>
/// <remarks>
/// <para>
/// Runs in the foreground and stops on Ctrl+C. A resident background service
/// would collect more, and is a bigger decision than this: something that
/// starts with the machine and holds a socket open is not a thing to arrive by
/// surprise in a launcher somebody installed to open projects.
/// </para>
/// <para>
/// Nothing is lost by it not running. The transcripts are still on disk and
/// <c>loadout usage</c> still reads them, which is why that came first and this
/// is an addition rather than a replacement.
/// </para>
/// </remarks>
[Description("Listen for the usage launched agents report. Stops on Ctrl+C.")]
[CommandMeta(CommandCategory.Administration, Intent = "telemetry collect otel metrics receiver")]
public sealed class TelemetryServeCommand : AsyncCommand<GlobalSettings>
{
    private readonly IConfigurationService _configuration;
    private readonly ITelemetryStore _store;
    private readonly IAnsiConsole _console;

    public TelemetryServeCommand(
        IConfigurationService configuration,
        ITelemetryStore store,
        IAnsiConsole console)
    {
        _configuration = configuration;
        _store = store;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings, CancellationToken cancellationToken)
    {
        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);

        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var telemetry = configResult.Value!.Telemetry;

        if (!telemetry.Enabled)
        {
            // Listening while nothing is being told to report would look like
            // it was working and collect nothing at all.
            return output.Fail(
                "Usage reporting is off, so no agent will report anything. "
                + "Turn it on with 'loadout config set telemetry true'.",
                ExitCode.InvalidArguments);
        }

        using var receiver = new TelemetryReceiver(_store);

        var started = receiver.Start(telemetry.Endpoint);

        if (started.Failed)
        {
            return output.Fail(started);
        }

        output.WriteLine($"Listening on [cyan]{telemetry.Endpoint.EscapeMarkup()}[/]");
        output.WriteLine($"[dim]Writing to {_store.Path.EscapeMarkup()}[/]");
        output.WriteLine("[dim]Agents launched from here report while this runs. Ctrl+C to stop.[/]");

        using var stopping = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            // Handled here so the file is left whole rather than the process
            // being torn down part way through a write.
            e.Cancel = true;
            stopping.Cancel();
        };

        await receiver.ListenAsync(stopping.Token).ConfigureAwait(false);

        output.WriteBlankLine();
        output.WriteLine(
            $"Stopped. Took {receiver.Accepted} reports and wrote {receiver.Written} numbers.");

        return CommandOutput.Success();
    }
}

/// <summary>Options for the telemetry summary.</summary>
public sealed class TelemetryStatusSettings : GlobalSettings
{
    [CommandOption("--days <COUNT>")]
    [Description("How many days back to include, counting today. Defaults to 30.")]
    public int Days { get; init; } = 30;
}

/// <summary>
/// Says what the receiver has collected.
/// </summary>
/// <remarks>
/// Kept apart from <c>loadout usage</c> rather than folded into it. The two
/// describe the same work from different sources, and adding them together
/// would count everything the receiver saw a second time — which is precisely
/// the mistake this whole feature was built to avoid making.
/// </remarks>
[Description("Show what the usage receiver has collected, including reported cost.")]
[CommandMeta(CommandCategory.Administration, Intent = "telemetry collected cost otel")]
public sealed class TelemetryStatusCommand : AsyncCommand<TelemetryStatusSettings>
{
    private readonly IConfigurationService _configuration;
    private readonly ITelemetryStore _store;
    private readonly IAnsiConsole _console;

    public TelemetryStatusCommand(
        IConfigurationService configuration,
        ITelemetryStore store,
        IAnsiConsole console)
    {
        _configuration = configuration;
        _store = store;
        _console = console;
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        TelemetryStatusSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        var configResult = await _configuration.LoadConfigAsync().ConfigureAwait(false);

        if (configResult.Failed)
        {
            return output.Fail(configResult);
        }

        var telemetry = configResult.Value!.Telemetry;

        var since = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, settings.Days));

        var read = await _store.ReadAsync(since).ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var samples = read.Value!;

        var tokens = Sum(samples, "claude_code.token.usage");
        var cost = Sum(samples, "claude_code.cost.usage");
        var active = Sum(samples, "claude_code.active_time.total");

        var sessions = samples
            .Where(s => s.Session is { Length: > 0 })
            .Select(s => s.Session)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (output.IsJson)
        {
            output.WriteJson(new
            {
                enabled = telemetry.Enabled,
                endpoint = telemetry.Endpoint,
                store = _store.Path,
                since = since.ToString("O", CultureInfo.InvariantCulture),
                sessions,
                samples = samples.Count,
                tokensByKind = ByKind(samples, "claude_code.token.usage"),
                listRateCostUsd = cost,
                activeSeconds = active,
                tokens,
            });

            return CommandOutput.Success();
        }

        output.WriteLine(telemetry.Enabled
            ? $"Reporting is [green]on[/], to [cyan]{telemetry.Endpoint.EscapeMarkup()}[/]"
            : "Reporting is [yellow]off[/]. Turn it on with 'loadout config set telemetry true'.");

        output.WriteLine($"[dim]{_store.Path.EscapeMarkup()}[/]");
        output.WriteBlankLine();

        if (samples.Count == 0)
        {
            output.WriteLine(
                "[dim]Nothing collected. Run 'loadout telemetry serve' and launch an agent.[/]");

            return CommandOutput.Success();
        }

        output.WriteLine(
            $"{sessions} {(sessions == 1 ? "session" : "sessions")} reported {tokens:N0} tokens.");

        foreach (var (kind, value) in ByKind(samples, "claude_code.token.usage"))
        {
            output.WriteLine($"  [dim]{kind.EscapeMarkup(),-14}[/] {value:N0}");
        }

        if (active > 0)
        {
            // Rounded to hours and minutes, a short session reads as zero,
            // which looks like the figure is broken rather than small.
            var spent = TimeSpan.FromSeconds(active);

            output.WriteLine("Active time: " + (spent.TotalMinutes < 1
                ? $"{spent.TotalSeconds:N0}s."
                : spent.TotalHours < 1
                    ? $"{spent:m\\m\\ ss\\s}."
                    : $"{spent:h\\h\\ mm\\m}."));
        }

        if (cost > 0)
        {
            // Said as what it is. The agents compute this from published list
            // rates, which is not what a subscription charges — printing it as
            // "spent" would show somebody a bill they never received.
            output.WriteBlankLine();
            output.WriteLine(
                $"[dim]The agents put this at ${cost:N2} at public list rates. "
                + "On a subscription that is not what you were charged.[/]");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// Adds up one metric.
    /// </summary>
    /// <remarks>
    /// Running totals are not added: the last one already includes every
    /// earlier one. The agents send differences today, and this handles both
    /// because the difference between the two is a factor of however many times
    /// something reported.
    /// </remarks>
    private static double Sum(IReadOnlyList<TelemetrySample> samples, string metric)
    {
        var wanted = samples.Where(s => string.Equals(s.Metric, metric, StringComparison.Ordinal));

        return wanted
            .GroupBy(s => (s.Session, s.Kind, s.Model, s.IsCumulative), StringTupleComparer.Instance)
            .Sum(group => group.Key.IsCumulative
                ? group.Max(s => s.Value)
                : group.Sum(s => s.Value));
    }

    private static Dictionary<string, double> ByKind(
        IReadOnlyList<TelemetrySample> samples,
        string metric) =>
        samples
            .Where(s => string.Equals(s.Metric, metric, StringComparison.Ordinal))
            .Select(s => s.Kind is { Length: > 0 } ? s.Kind : "total")
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(
                kind => kind,
                kind => Sum(
                    samples
                        .Where(s => (s.Kind is { Length: > 0 } ? s.Kind : "total") == kind)
                        .ToList(),
                    metric),
                StringComparer.Ordinal);

    /// <summary>Groups the four parts of a series key without allocating a string.</summary>
    private sealed class StringTupleComparer
        : IEqualityComparer<(string Session, string Kind, string Model, bool IsCumulative)>
    {
        internal static readonly StringTupleComparer Instance = new();

        public bool Equals(
            (string Session, string Kind, string Model, bool IsCumulative) x,
            (string Session, string Kind, string Model, bool IsCumulative) y) =>
            string.Equals(x.Session, y.Session, StringComparison.Ordinal)
            && string.Equals(x.Kind, y.Kind, StringComparison.Ordinal)
            && string.Equals(x.Model, y.Model, StringComparison.Ordinal)
            && x.IsCumulative == y.IsCumulative;

        public int GetHashCode((string Session, string Kind, string Model, bool IsCumulative) obj) =>
            HashCode.Combine(obj.Session, obj.Kind, obj.Model, obj.IsCumulative);
    }
}
