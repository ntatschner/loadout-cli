using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Projects;
using Loadout.Core.Sessions;
using Loadout.Models;
using Loadout.Models.Agents;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What this machine has launched, and what each launch was given.
/// </summary>
/// <remarks>
/// <para>
/// Not the same list as <c>loadout sessions</c>, and deliberately a different
/// word. Sessions are reconstructed from the transcripts the agents write, and
/// say what a conversation was about; launches are what this launcher recorded
/// as it started one, and say what it was told to be. Neither can be turned into
/// the other: an agent picks its own session identifier and the launcher never
/// learns it, so nothing here claims a line in one is a line in the other.
/// </para>
/// <para>
/// Nor does it claim what a launch spent. Token counts are aggregated by
/// directory and day rather than per launch, so attributing them to one of three
/// launches that day would be arithmetic dressed as fact. The tokens shown are
/// the instruction tokens the launcher itself estimated and recorded, which are
/// genuinely per launch.
/// </para>
/// </remarks>
[Description("List what this machine launched, and what each launch was given.")]
[CommandMeta(CommandCategory.Start, Intent = "launches history what did I run ledger record")]
public sealed class LaunchesCommand : AsyncCommand<LaunchesCommand.Settings>
{
    private readonly ILaunchLedger _ledger;
    private readonly IProjectService _projects;
    private readonly IAnsiConsole _console;
    private readonly TimeProvider _time;

    public LaunchesCommand(
        ILaunchLedger ledger,
        IProjectService projects,
        IAnsiConsole console,
        TimeProvider time)
    {
        _ledger = ledger;
        _projects = projects;
        _console = console;
        _time = time;
    }

    public sealed class Settings : GlobalSettings
    {
        [CommandArgument(0, "[project]")]
        [Description("Only launches of this project. Defaults to all of them.")]
        public string? Project { get; init; }

        [CommandOption("--days <COUNT>")]
        [Description("How many days back to include, counting today. Defaults to 30.")]
        public int Days { get; init; } = 30;

        [CommandOption("--show <ID>")]
        [Description("Print one launch in full instead of listing them.")]
        public string? Show { get; init; }
    }

    /// <inheritdoc />
    protected override async Task<int> ExecuteAsync(
        CommandContext context,
        Settings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(_console, settings);

        if (settings.Days <= 0)
        {
            return output.Fail("--days has to be at least 1.", ExitCode.InvalidArguments);
        }

        var read = await _ledger
            .ReadAsync(_time.GetUtcNow().AddDays(-settings.Days), cancellationToken)
            .ConfigureAwait(false);

        if (read.Failed)
        {
            return output.Fail(read);
        }

        var launches = read.Value!;

        if (settings.Project is { Length: > 0 } handle)
        {
            var resolved = await _projects.ResolveAsync(handle, cancellationToken).ConfigureAwait(false);

            if (resolved.Failed)
            {
                return output.Fail(resolved);
            }

            var slug = resolved.Value!.Entry.Slug;

            launches = launches
                .Where(launch => string.Equals(launch.ProjectSlug, slug, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (settings.Show is { Length: > 0 } id)
        {
            return Show(output, launches, id);
        }

        // Newest first, which is the order somebody asking "what have I been
        // doing" wants. The ledger is written oldest first because it is only
        // ever appended to.
        launches = launches.OrderByDescending(launch => launch.StartedAt).ToList();

        if (output.IsJson)
        {
            output.WriteJson(launches);

            return CommandOutput.Success();
        }

        if (launches.Count == 0)
        {
            output.WriteLine($"[yellow]No launches recorded in the last {settings.Days} day(s).[/]");
            output.WriteLine(
                "[dim]The ledger only holds launches made since it was added, so a window "
                + "reaching further back than that is empty rather than quiet.[/]");

            return CommandOutput.Success();
        }

        foreach (var launch in launches)
        {
            output.WriteLine(
                $"{When(launch.StartedAt),-14} "
                + $"{Markup.Escape(launch.ProjectSlug),-18} "
                + $"{Markup.Escape(launch.Agent),-8} "
                + $"{Markup.Escape(launch.Mode ?? "-"),-11} "
                + $"{Outcome(launch),-11} "
                + $"[dim]{Markup.Escape(Label(launch))}[/]");
        }

        var modes = Loadout.Core.Sessions.LaunchStatistics
            .From(launches, new Dictionary<string, int>()).Modes ?? [];

        if (modes.Count > 1)
        {
            // Only worth the lines when there is a comparison to make. One
            // mode is not a breakdown, it is the same number twice.
            output.WriteBlankLine();
            output.WriteLine("[bold]By posture[/]  [dim]context put in front of the agent, not spend[/]");

            foreach (var mode in modes)
            {
                output.WriteLine(
                    $"  {Markup.Escape(mode.Mode),-11} "
                    + $"{mode.Launches,4} launch(es)  "
                    + $"[dim]{mode.EstimatedTokens:N0} tokens composed[/]");
            }
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{launches.Count} launch(es). One in full: "
            + $"loadout launches --show {Markup.Escape(launches[0].Id)}[/]");

        return CommandOutput.Success();
    }

    private static int Show(CommandOutput output, IReadOnlyList<LaunchRecord> launches, string id)
    {
        // A prefix, because the identifier is a bare hex string and nobody is
        // going to type thirty-two characters of it.
        var found = launches
            .Where(launch => launch.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (found.Count == 0)
        {
            return output.Fail(
                $"No launch in this window starts with '{id}'.", ExitCode.InvalidArguments);
        }

        if (found.Count > 1)
        {
            return output.Fail(
                $"'{id}' matches {found.Count} launches. Give more of the identifier.",
                ExitCode.InvalidArguments);
        }

        var launch = found[0];

        if (output.IsJson)
        {
            output.WriteJson(launch);

            return CommandOutput.Success();
        }

        output.WriteLine($"[bold]{Markup.Escape(launch.ProjectName)}[/]  "
            + $"[dim]{Markup.Escape(launch.Id)}[/]");
        output.WriteBlankLine();

        Field(output, "Started", launch.StartedAt.ToString("u", CultureInfo.InvariantCulture));
        Field(output, "Agent", launch.Agent);
        Field(output, "Mode", launch.Mode ?? "none");
        Field(output, "Outcome", Outcome(launch));

        if (launch.Duration is { } duration)
        {
            Field(output, "Ran for", Duration(duration));
        }

        if (launch.Profile is { Length: > 0 } profile)
        {
            Field(output, "Profile", profile);
        }

        if (launch.Worktree is { Length: > 0 } worktree)
        {
            Field(output, "Worktree", worktree);
        }

        Field(output, "Task", Label(launch));

        Field(
            output,
            "Instructions",
            launch.TokenBudget > 0
                ? $"{launch.EstimatedTokens:N0} estimated token(s) against a budget of {launch.TokenBudget:N0}"
                : $"{launch.EstimatedTokens:N0} estimated token(s)");

        if (launch.Specialists.Count == 0)
        {
            return CommandOutput.Success();
        }

        output.WriteBlankLine();
        output.WriteLine($"[bold]Composed[/]  [dim]{launch.Specialists.Count}[/]");

        foreach (var specialist in launch.Specialists)
        {
            output.WriteLine($"  {Markup.Escape(specialist)}");
        }

        return CommandOutput.Success();
    }

    private static void Field(CommandOutput output, string name, string value) =>
        output.WriteLine($"  [dim]{name,-13}[/] {Markup.Escape(value)}");

    /// <summary>
    /// What became of a launch.
    /// </summary>
    /// <remarks>
    /// Three outcomes and not two. A launch with no ending is not the same as
    /// one that ended without running the agent, and calling either of them
    /// "failed" would be inventing a result neither has.
    /// </remarks>
    internal static string Outcome(LaunchRecord launch) => launch switch
    {
        { IsComplete: false } => "unclosed",
        { ExitCode: null } => "never ran",
        { ExitCode: 0 } => "ok",
        { ExitCode: var code } => $"exit {code}",
    };

    /// <summary>What the launch was for, or why that cannot be shown.</summary>
    internal static string Label(LaunchRecord launch) =>
        launch.Task is { Length: > 0 } task
            ? task
            : launch.TaskWithheld is { Length: > 0 } pattern
                ? $"(withheld: looked like a {pattern})"
                : "(no task given)";

    private static string When(DateTimeOffset when) =>
        when.ToLocalTime().ToString("dd MMM HH:mm", CultureInfo.InvariantCulture);

    private static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{duration.TotalHours:N1} hours"
            : duration.TotalMinutes >= 1
                ? $"{duration.TotalMinutes:N0} minutes"
                : $"{duration.TotalSeconds:N0} seconds";
}
