using System.ComponentModel;
using System.Globalization;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Teams.Daemon;
using Loadout.Core.Triggers;
using Loadout.Models;
using Loadout.Models.Teams;
using Loadout.Models.Triggers;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>
/// What every trigger command shares: whether triggers are on, where their
/// files are, and how a change reaches the rules.
/// </summary>
/// <remarks>
/// <para>
/// A change goes through the running daemon when there is one, and never
/// round it. The daemon holds actions waiting on each rule and decisions made
/// against it, and a file written underneath it would be raced, or never
/// checked against what was waiting. With no daemon running there is nothing
/// to race, so the file is written here and the daemon reads it as it starts.
/// </para>
/// <para>
/// A daemon that does not answer is asked once more, with the same request,
/// then the change fails. It never falls back to writing the file, for the
/// reason above; and the second asking carries the same id, so a change that
/// landed after the first wait gave up is reported, not made twice.
/// </para>
/// </remarks>
internal sealed class TriggerDesk(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    TimeProvider time)
{
    /// <summary>How long one asking of the daemon waits for its answer.</summary>
    internal TimeSpan Wait { get; init; } = TimeSpan.FromSeconds(10);

    private string State => paths.Paths.State;

    public TriggerRules Rules => new(State, yaml);

    public TriggerJournal Journal => new(State, permissions);

    public TimeProvider Time => time;

    /// <summary>The daemon running now, or null.</summary>
    public DaemonState? Daemon => DaemonNote.Live(paths, processes);

    /// <summary>Whether triggers are on, and what to say when they are not.</summary>
    public (bool On, string? Refusal) Switch() => TriggerSwitch.Ask(Daemon?.Triggers, environment);

    /// <summary>Makes a change to the rules, through the daemon when one is running.</summary>
    /// <param name="asking">The change.</param>
    /// <param name="output">Where to say what happened.</param>
    /// <param name="offline">The same change made straight to the file, for when no daemon is running.</param>
    /// <param name="ct">Stops the wait for the daemon's answer.</param>
    public async Task<int> ChangeAsync(
        TriggerAsk asking,
        CommandOutput output,
        Func<Task<TriggerAnswer>> offline,
        CancellationToken ct)
    {
        if (Daemon is null)
        {
            var made = await offline().ConfigureAwait(false);

            if (!made.Applied)
            {
                return output.Fail(made.Message, ExitCode.InvalidArguments);
            }

            return Said(output, made, "saved, and it takes effect when the daemon next starts");
        }

        var inbox = new TriggerInbox(State);
        var answer = await inbox.AskAsync(asking, Wait, time, ct).ConfigureAwait(false)
            ?? await inbox.AskAsync(asking, Wait, time, ct).ConfigureAwait(false);

        if (answer is null)
        {
            return output.Fail(
                $"The daemon did not answer, asked twice over {2 * Wait.TotalSeconds:0} seconds, so nothing is known "
                + "to have changed. 'loadout doctor' says whether it is still running; if it is stuck, "
                + "'loadout daemon restart' restarts it, and this can then be asked again.");
        }

        // A refusal is an answer, not a failure to answer: shown, never sent again.
        return answer.Applied
            ? Said(output, answer, "applied by the daemon")
            : output.Fail($"The daemon refused it: {answer.Message}", ExitCode.InvalidArguments);
    }

    private static int Said(CommandOutput output, TriggerAnswer answer, string path)
    {
        if (output.IsJson)
        {
            output.WriteJson(new { applied = true, path, message = answer.Message, version = answer.Version });
        }
        else
        {
            output.WriteLine(Markup.Escape(answer.Message));
        }

        return CommandOutput.Success();
    }

    /// <summary>A change made straight to the file, written in the journal the way the daemon would.</summary>
    public TriggerAnswer Journalled(string request, string kind, string rule, int? version, string reason, string message)
    {
        Journal.Append(new TriggerEntry(
            time.GetUtcNow(), kind, Rule: rule, Version: version, Reason: reason, Request: request));

        return new TriggerAnswer(request, true, message, version);
    }

    /// <summary>The ledger as the journal has it, for reading where rules stand without a daemon.</summary>
    public SpendLedger Ledger() => SpendLedger.Rebuild(Journal.Read(time.GetUtcNow() - TriggerSpine.Remembered));
}

/// <summary>Settings every trigger command takes.</summary>
public class TriggerSettings : GlobalSettings
{
}

/// <summary>Settings for a trigger command naming one rule.</summary>
public class TriggerRuleSettings : TriggerSettings
{
    [CommandArgument(0, "<NAME>")]
    [Description("The rule, as 'trigger list' names it.")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>Lists the trigger rules, and where each stands against its caps.</summary>
[Description("List the trigger rules on this machine, and where each stands against its caps. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger rules list events automation")]
public sealed class TriggerListCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var ledger = desk.Ledger();
        var now = time.GetUtcNow();
        var rows = listed.Value!.Select(one => (one.Rule, one.Problem, Standing: ledger.Standing(one.Rule, now))).ToList();

        if (output.IsJson)
        {
            output.WriteJson(rows.Select(one => new
            {
                rule = one.Rule,
                problem = one.Problem,
                runs = one.Standing.Runs,
                landedUsd = one.Standing.Landed,
                reservedUsd = one.Standing.Reserved,
                headroomUsd = one.Standing.Headroom,
                paused = one.Standing.Paused,
            }));

            return CommandOutput.Success();
        }

        if (rows.Count == 0)
        {
            output.WriteLine("There are no trigger rules on this machine. Add one with: loadout trigger add");

            return CommandOutput.Success();
        }

        foreach (var (rule, problem, standing) in rows)
        {
            output.WriteLine(Markup.Escape(TriggerWords.Line(rule, standing)));

            if (problem is not null)
            {
                output.WriteLine($"  [yellow]Not acted on:[/] {Markup.Escape(problem)}");
            }
        }

        if (desk.Daemon is null)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing acts on these until the daemon is running:[/] loadout daemon");
        }

        return CommandOutput.Success();
    }
}

/// <summary>Adds a trigger rule, or replaces one of the same name.</summary>
[Description("Add a trigger rule: an event to wait for and what to start when it happens. Run again with the same name to change one. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger rule add edit when event happens start team", Mutates = true)]
public sealed class TriggerAddCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerAddCommand.Settings>
{
    public sealed class Settings : TriggerRuleSettings
    {
        [CommandArgument(1, "<EVENT>")]
        [Description("What to wait for: run-finished, docs-drift or webhook.")]
        public string On { get; init; } = string.Empty;

        [CommandOption("--team <TEAM>")]
        [Description("Start this team. Give a --goal too.")]
        public string? Team { get; init; }

        [CommandOption("--builtin <COMMAND>")]
        [Description("Start this built-in command instead, such as \"docs audit\". Costs nothing, so needs no cap.")]
        public string? Builtin { get; init; }

        [CommandOption("--goal <GOAL>")]
        [Description("What to ask the team for. {team}, {project}, {goal} and the event's other fields are filled in.")]
        public string? Goal { get; init; }

        [CommandOption("--autonomy <MODE>")]
        [Description("supervised or autonomous. Never manual: nobody is watching when it fires.")]
        public string? Autonomy { get; init; }

        [CommandOption("--project <SLUG>")]
        [Description("Only events from this project count. Give it more than once for several; leave it out for all.")]
        public string[] Projects { get; init; } = [];

        [CommandOption("--where <FIELD=VALUE>")]
        [Description("Only events whose field holds this value count, such as outcome=failed. Give it more than once to need several.")]
        public string[] Where { get; init; } = [];

        [CommandOption("--cap-usd <AMOUNT>")]
        [Description("The most its runs may spend in the window, in US dollars. Required for a team.")]
        public string? CapUsd { get; init; }

        [CommandOption("--budget-usd <AMOUNT>")]
        [Description("What each run may spend, in US dollars. The team's own when left out; required when it has none.")]
        public string? BudgetUsd { get; init; }

        [CommandOption("--cap-runs <COUNT>")]
        [Description("The most runs it may start in the window. 10 when left out.")]
        public int? CapRuns { get; init; }

        [CommandOption("--window <DURATION>")]
        [Description("The rolling period the caps count over, such as 24h or 7d. 24h when left out.")]
        public string? Window { get; init; }

        [CommandOption("--debounce <DURATION>")]
        [Description("How long a subject must go quiet before acting, such as 10m, so a burst becomes one run.")]
        public string? Debounce { get; init; }

        [CommandOption("--disabled")]
        [Description("Record it without letting it act yet.")]
        public bool Disabled { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        var (rule, problem) = TriggerWords.Build(settings);

        if (problem is not null)
        {
            return output.Fail(problem, ExitCode.InvalidArguments);
        }

        if (TriggerRules.Check(rule) is { } wrong)
        {
            return output.Fail(wrong, ExitCode.InvalidArguments);
        }

        var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);
        var was = listed.Succeeded
            ? listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase))
            : null;

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was saved.[/] {(was is null ? "This would add" : $"This would replace version {was.Rule.Version} of")} "
                + $"'{Markup.Escape(rule.Id)}':");
            output.WriteLine(Markup.Escape(TriggerWords.Line(rule, null)));

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Save, Rule: rule),
            output,
            async () =>
            {
                var saved = await desk.Rules.SaveAsync(rule, cancellationToken).ConfigureAwait(false);

                return saved.Failed
                    ? new TriggerAnswer(request, false, saved.Error!)
                    : desk.Journalled(
                        request, TriggerEntryKinds.Edited, saved.Value!.Id, saved.Value!.Version,
                        "saved with no daemon running",
                        $"'{saved.Value!.Id}' is saved as version {saved.Value!.Version}. No daemon is running, so it "
                        + "takes effect when one starts.");
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Removes a trigger rule.</summary>
[Description("Remove a trigger rule. Anything waiting to start under it is withdrawn. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger rule remove delete", Mutates = true)]
public sealed class TriggerRemoveCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerRuleSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerRuleSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was removed.[/] '{Markup.Escape(settings.Name)}' would go, and anything "
                + "waiting to start under it would be withdrawn.");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Remove, RuleId: settings.Name),
            output,
            async () =>
            {
                var removed = await desk.Rules.RemoveAsync(settings.Name, cancellationToken).ConfigureAwait(false);

                return removed.Failed
                    ? new TriggerAnswer(request, false, removed.Error!)
                    : desk.Journalled(
                        request, TriggerEntryKinds.Removed, settings.Name, null, "removed with no daemon running",
                        $"'{settings.Name}' is gone.");
            },
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Lets a paused trigger rule act again.</summary>
[Description("Let a trigger rule that paused at a cap act again. Its caps count afresh from now. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger rule resume paused cap", Mutates = true)]
public sealed class TriggerResumeCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerRuleSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerRuleSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, settings.Name, StringComparison.OrdinalIgnoreCase))
            is not { } found)
        {
            return output.Fail($"There is no trigger rule called '{settings.Name}' on this machine.", ExitCode.ProjectNotFound);
        }

        // Shown before it happens, so somebody resuming a rule that paused
        // for a reason sees what the reason was.
        var standing = desk.Ledger().Standing(found.Rule, time.GetUtcNow());

        if (settings.DryRun)
        {
            output.WriteLine(
                $"[dim]Dry run: nothing was resumed.[/] {Markup.Escape(TriggerWords.Line(found.Rule, standing))}");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Resume, RuleId: found.Rule.Id),
            output,
            () => Task.FromResult(standing.Paused
                ? desk.Journalled(
                    request, TriggerEntryKinds.Resumed, found.Rule.Id, found.Rule.Version, "resumed with no daemon running",
                    $"'{found.Rule.Id}' will act again when the daemon starts, its caps counting from now.")
                : new TriggerAnswer(request, true, $"'{found.Rule.Id}' was not paused, so nothing changed.")),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Shows what the trigger journal recorded.</summary>
[Description("Show what the trigger journal recorded: events, and every decision made about them. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger journal log events decisions why")]
public sealed class TriggerJournalCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerJournalCommand.Settings>
{
    public sealed class Settings : TriggerSettings
    {
        [CommandOption("--since <DURATION>")]
        [Description("How far back to look, such as 2h or 7d. 24h when left out.")]
        public string? Since { get; init; }

        [CommandOption("--rule <NAME>")]
        [Description("Only what this rule decided.")]
        public string? Rule { get; init; }

        [CommandOption("--limit <COUNT>")]
        [Description("The most entries to show, newest kept. 100 when left out.")]
        public int? Limit { get; init; }
    }

    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return Task.FromResult(output.Fail(refusal, ExitCode.InvalidArguments));
        }

        if (TriggerWords.Duration(settings.Since, TimeSpan.FromHours(24)) is not { } since)
        {
            return Task.FromResult(output.Fail(TeamDuration.Refusal(settings.Since!), ExitCode.InvalidArguments));
        }

        var entries = desk.Journal.Read(time.GetUtcNow() - since)
            .Where(one => settings.Rule is null || string.Equals(one.Rule, settings.Rule, StringComparison.OrdinalIgnoreCase))
            .TakeLast(settings.Limit is > 0 and var limit ? limit : 100)
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(entries);

            return Task.FromResult(CommandOutput.Success());
        }

        if (entries.Count == 0)
        {
            output.WriteLine($"Nothing was recorded in the last {TeamDuration.Spell(since)}.");

            return Task.FromResult(CommandOutput.Success());
        }

        foreach (var one in entries)
        {
            output.WriteLine(Markup.Escape(TriggerWords.Entry(one)));
        }

        return Task.FromResult(CommandOutput.Success());
    }
}

/// <summary>Tries a rule against the events the journal recorded, without acting on any.</summary>
[Description("Try a trigger rule against the events already recorded, saying which it would have acted on and why. Starts nothing. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger rule test replay would it match dry")]
public sealed class TriggerTestCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerTestCommand.Settings>
{
    public sealed class Settings : TriggerRuleSettings
    {
        [CommandOption("--since <DURATION>")]
        [Description("How far back to replay, such as 2h or 7d. 7d when left out.")]
        public string? Since { get; init; }
    }

    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        if (TriggerWords.Duration(settings.Since, TimeSpan.FromDays(7)) is not { } since)
        {
            return output.Fail(TeamDuration.Refusal(settings.Since!), ExitCode.InvalidArguments);
        }

        var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, settings.Name, StringComparison.OrdinalIgnoreCase))
            is not { } found)
        {
            return output.Fail($"There is no trigger rule called '{settings.Name}' on this machine.", ExitCode.ProjectNotFound);
        }

        // The rule as it is now, against events as they were: what it would
        // have done, which is the question somebody editing it is asking.
        var decisions = desk.Journal.Read(time.GetUtcNow() - since)
            .Where(one => one.Kind == TriggerEntryKinds.Event
                && string.Equals(one.EventKind, found.Rule.On, StringComparison.OrdinalIgnoreCase))
            .Select(one => (Entry: one, Decision: TriggerMatcher.Decide(TriggerWords.Replayed(one), found.Rule)))
            .ToList();

        if (output.IsJson)
        {
            output.WriteJson(decisions.Select(one => new
            {
                @event = one.Entry.Event,
                at = one.Entry.Happened ?? one.Entry.At,
                subject = one.Entry.Subject,
                matched = one.Decision.Matched,
                reason = one.Decision.Reason,
            }));

            return CommandOutput.Success();
        }

        if (found.Problem is { } problem)
        {
            output.WriteLine($"[yellow]The rule is not acted on as it stands:[/] {Markup.Escape(problem)}");
        }

        if (decisions.Count == 0)
        {
            output.WriteLine($"No {Markup.Escape(found.Rule.On)} events were recorded in the last {TeamDuration.Spell(since)}.");

            return CommandOutput.Success();
        }

        foreach (var (entry, decision) in decisions)
        {
            output.WriteLine(Markup.Escape(
                $"{(entry.Happened ?? entry.At).ToLocalTime():yyyy-MM-dd HH:mm}  {entry.Subject}  "
                + (decision.Matched ? "would act" : $"would pass: {decision.Reason}")));
        }

        output.WriteBlankLine();
        output.WriteLine(
            $"[dim]{decisions.Count(one => one.Decision.Matched)} of {decisions.Count} would have matched. Caps, "
            + "budgets and quiet periods are not replayed; nothing was started.[/]");

        return CommandOutput.Success();
    }
}

/// <summary>Turning options into rules, and rules and entries into lines.</summary>
internal static class TriggerWords
{
    /// <summary>The rule the add command's options describe, or what is wrong with them.</summary>
    public static (TriggerRule Rule, string? Problem) Build(TriggerAddCommand.Settings settings)
    {
        var rule = new TriggerRule
        {
            Id = settings.Name.Trim(),
            On = settings.On.Trim().ToLowerInvariant(),
            Projects = [.. settings.Projects.Select(one => one.Trim()).Where(one => one.Length > 0)],
            Enabled = !settings.Disabled,
            CapRuns = settings.CapRuns ?? TriggerRule.DefaultCapRuns,
        };

        string? problem = null;

        if ((settings.Team is null) == (settings.Builtin is null))
        {
            problem = "Say what the rule starts with exactly one of --team or --builtin.";
        }
        else
        {
            rule.Action = settings.Team is { } team
                ? new TriggerAction
                {
                    Kind = TriggerActionKinds.Team,
                    Name = team.Trim(),
                    Goal = settings.Goal?.Trim() ?? string.Empty,
                    Autonomy = settings.Autonomy?.Trim().ToLowerInvariant() ?? "autonomous",
                }
                : new TriggerAction { Kind = TriggerActionKinds.Builtin, Name = settings.Builtin!.Trim().ToLowerInvariant() };
        }

        foreach (var pair in settings.Where)
        {
            var split = pair.IndexOf('=', StringComparison.Ordinal);

            if (split <= 0)
            {
                problem ??= $"'{pair}' is not a condition. Write it as field=value, such as outcome=failed.";

                continue;
            }

            rule.Where[pair[..split].Trim().ToLowerInvariant()] = pair[(split + 1)..].Trim();
        }

        problem ??= Money(settings.CapUsd, "--cap-usd", value => rule.CapUsd = value)
            ?? Money(settings.BudgetUsd, "--budget-usd", value => rule.BudgetUsd = value);

        if (Duration(settings.Window, TriggerRule.DefaultWindow) is { } window)
        {
            rule.Window = window;
        }
        else
        {
            problem ??= TeamDuration.Refusal(settings.Window!);
        }

        if (Duration(settings.Debounce, TimeSpan.Zero) is { } debounce)
        {
            rule.Debounce = debounce;
        }
        else
        {
            problem ??= TeamDuration.Refusal(settings.Debounce!);
        }

        return (rule, problem);
    }

    /// <summary>A duration given as an option, the fallback when none was, or null when what was given is not one.</summary>
    public static TimeSpan? Duration(string? text, TimeSpan fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : TeamDuration.Parse(text);

    private static string? Money(string? text, string option, Action<decimal> set)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!decimal.TryParse(text.Trim().TrimStart('$'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            return $"{option} takes an amount in US dollars, such as 5 or 2.50, not '{text}'.";
        }

        set(value);

        return null;
    }

    /// <summary>One rule as a line, with where it stands when that is known.</summary>
    public static string Line(TriggerRule rule, RuleStanding? standing)
    {
        var starts = rule.Action.Kind == TriggerActionKinds.Team
            ? $"team {rule.Action.Name}"
            : $"{rule.Action.Name}";
        var where = rule.Where.Count > 0
            ? " where " + string.Join(", ", rule.Where.Select(one => $"{one.Key}={one.Value}"))
            : string.Empty;
        var projects = rule.Projects.Count > 0 ? $" in {string.Join(", ", rule.Projects)}" : string.Empty;
        var caps = rule.CapUsd is { } cap
            ? $"; cap ${cap:0.00} and {rule.CapRuns} runs per {TeamDuration.Spell(rule.Window)}"
            : $"; cap {rule.CapRuns} runs per {TeamDuration.Spell(rule.Window)}";
        var state = !rule.Enabled ? " [disabled]"
            : standing is { Paused: true } ? " [paused]"
            : string.Empty;
        var stood = standing is null
            ? string.Empty
            : $"; {standing.Runs} run(s), ${standing.Landed:0.00} spent, ${standing.Reserved:0.00} set aside";

        return $"{rule.Id} v{rule.Version}{state}: on {rule.On}{projects}{where}, start {starts}{caps}{stood}";
    }

    /// <summary>One journal entry as a line.</summary>
    public static string Entry(TriggerEntry one)
    {
        var at = one.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var parts = new List<string> { at, one.Kind };

        if (one.Kind == TriggerEntryKinds.Event)
        {
            parts.Add($"{one.EventKind} from {one.Source}");
        }

        if (one.Rule is { } rule)
        {
            parts.Add(one.Version is { } version ? $"{rule} v{version}" : rule);
        }

        if (one.Subject is { } subject)
        {
            parts.Add(subject);
        }

        if (one.Usd is { } usd)
        {
            parts.Add($"${usd:0.00}");
        }

        if (one.Run is { } run)
        {
            parts.Add($"run {run}");
        }

        if (one.Reason is { } reason)
        {
            parts.Add(reason);
        }

        return string.Join("  ", parts);
    }

    /// <summary>A recorded event, as the matcher would have seen it.</summary>
    public static TriggerEvent Replayed(TriggerEntry one) =>
        new(
            one.Event ?? string.Empty,
            one.Happened ?? one.At,
            one.Source ?? string.Empty,
            one.EventKind ?? string.Empty,
            one.Subject ?? string.Empty,
            one.Project,
            one.Fields ?? new Dictionary<string, string>(),
            one.Origins ?? []);
}
