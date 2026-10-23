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
    TimeProvider time,
    Loadout.Core.Teams.ITeamCatalogue teams,
    Loadout.Core.Instructions.ISpecialistLibrary library,
    Loadout.Core.Workspace.IWorkspaceManager workspace,
    Loadout.Core.Configuration.IConfigurationService configuration) : AsyncCommand<TriggerSettings>
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
        var megabytes = (await configuration.LoadMachineAsync(cancellationToken).ConfigureAwait(false)).Value?.Teams.TriggerJournalMegabytes;

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var ledger = desk.Ledger();
        var now = time.GetUtcNow();
        var changed = await ChangedAsync(listed.Value!.Select(one => one.Rule).ToList(), cancellationToken).ConfigureAwait(false);
        var rows = listed.Value!
            .Select(one => (one.Rule, one.Problem, Standing: ledger.Standing(one.Rule, now), Changed: changed.Contains(one.Rule.Id)))
            .ToList();

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
                suggestionChanged = one.Changed,
            }));

            return CommandOutput.Success();
        }

        if (rows.Count == 0)
        {
            output.WriteLine("There are no trigger rules on this machine. Add one with: loadout trigger add");

            return CommandOutput.Success();
        }

        foreach (var (rule, problem, standing, suggestionChanged) in rows)
        {
            output.WriteLine(Markup.Escape(TriggerWords.Line(rule, standing)));

            if (problem is not null)
            {
                output.WriteLine($"  [yellow]Not acted on:[/] {Markup.Escape(problem)}");
            }

            if (suggestionChanged)
            {
                var project = rule.Projects.FirstOrDefault() is { } slug ? $" --project {slug}" : string.Empty;

                output.WriteLine(
                    $"  [yellow]Changed since you accepted it:[/] {Markup.Escape(rule.SuggestedBy)} now suggests something "
                    + $"different. This rule runs as accepted until you look: loadout trigger suggested{Markup.Escape(project)}");
            }
        }

        if (desk.Journal.OverAllowance(now, TriggerJournal.Allowance(megabytes)) is { } over)
        {
            output.WriteBlankLine();
            output.WriteLine($"[yellow]{Markup.Escape(over)}[/] Raise it with: loadout config set trigger-journal-mb <MB>");
        }

        if (desk.Daemon is null)
        {
            output.WriteBlankLine();
            output.WriteLine("[dim]Nothing acts on these until the daemon is running:[/] loadout daemon");
        }

        return CommandOutput.Success();
    }

    /// <summary>
    /// The rules accepted from a team suggestion that the team has changed since.
    /// </summary>
    /// <remarks>
    /// Each team is looked for the way 'trigger accept' found it, in the rule's
    /// first project, so a project's own team is found where it was. Rules
    /// written by hand load no teams at all.
    /// </remarks>
    private async Task<HashSet<string>> ChangedAsync(IReadOnlyList<TriggerRule> rules, CancellationToken cancellationToken)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = workspace.IsAvailable() ? workspace.LocalPath : null;

        foreach (var group in rules.Where(one => one.SuggestedBy.Length > 0).GroupBy(one => one.Projects.FirstOrDefault()))
        {
            var specialists = await library.LoadAsync(root, group.Key, cancellationToken).ConfigureAwait(false);
            var catalogue = await teams.LoadAsync(root, group.Key, specialists, cancellationToken).ConfigureAwait(false);

            changed.UnionWith(group.Where(one => TeamSuggestions.Changed(one, catalogue.Teams.Values)).Select(one => one.Id));
        }

        return changed;
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
    TimeProvider time,
    Loadout.Core.Teams.IRunJournal runs,
    ICommandCatalogue commands,
    Loadout.Core.Teams.ITeamCatalogue teams,
    Loadout.Core.Instructions.ISpecialistLibrary library,
    Loadout.Core.Workspace.IWorkspaceManager workspace) : AsyncCommand<TriggerAddCommand.Settings>
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
        [Description("The most its runs may spend in the window, in US dollars, or proposed for a figure from the team's past runs. Required for a team.")]
        public string? CapUsd { get; init; }

        [CommandOption("--budget-usd <AMOUNT>")]
        [Description("What each run may spend, in US dollars, or proposed for a figure from the team's past runs. The team's own when left out; required when it has none.")]
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

        [CommandOption("--every <DURATION>")]
        [Description("For a clock rule: how often it comes round, such as 30m, 2h or 1d. At least five minutes.")]
        public string? Every { get; init; }

        [CommandOption("--at <TIME>")]
        [Description("For a clock rule: the time of day it comes round, as 09:00, in this machine's own time.")]
        public string? At { get; init; }
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

        if (rule.Action.Kind == TriggerActionKinds.Team
            && await TeamMoneyAsync(
                rule, settings.BudgetUsd, settings.CapUsd, output,
                new DaemonTriggerActions(commands, runs, teams, library, workspace), runs, cancellationToken)
                .ConfigureAwait(false) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
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
            TriggerWords.Changes(output, was?.Rule, rule);

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

    /// <summary>
    /// Settles what a team rule may spend, filling in proposed figures, or
    /// says what is missing and what is proposed for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A cap is required, and so is a budget for a team with none of its own:
    /// without one there is nothing to set aside. Rather than make somebody
    /// invent a figure, the refusal offers one worked out from the team's own
    /// runs, with its working, and <c>proposed</c> accepts it.
    /// </para>
    /// <para>
    /// The team is looked up for the first project the rule watches, because
    /// a project's own team of that name replaces the built-in one there. A
    /// team that writes is not refused here, only said: the rule can be
    /// written now and act once approvals exist.
    /// </para>
    /// </remarks>
    internal static async Task<string?> TeamMoneyAsync(
        TriggerRule rule,
        string? budgetText,
        string? capText,
        CommandOutput output,
        DaemonTriggerActions actions,
        Loadout.Core.Teams.IRunJournal runs,
        CancellationToken ct)
    {
        var traits = await actions.DescribeAsync(rule.Action, rule.Projects.FirstOrDefault(), ct).ConfigureAwait(false);

        if (traits.Problem is { } problem)
        {
            return $"The rule cannot start that: {problem}. See what there is with: loadout team list";
        }

        var proposal = BudgetProposal.For(
            rule.Action.Name,
            rule.Projects,
            runs.List(200).Select(runs.Summarise).Where(one => one.Succeeded).Select(one => one.Value!));

        var accepted = false;

        if (TriggerWords.Proposed(budgetText))
        {
            rule.BudgetUsd = proposal.BudgetUsd;
            accepted = true;
        }

        if (TriggerWords.Proposed(capText))
        {
            // Never below one run, or the budget just accepted could not start.
            rule.CapUsd = Math.Max(proposal.CapUsd, rule.BudgetUsd ?? traits.BudgetUsd ?? 0m);
            accepted = true;
        }

        var missing = new List<string>();

        if (rule.BudgetUsd is null && traits.BudgetUsd is null)
        {
            missing.Add($"--budget-usd, since {rule.Action.Name} has no budget of its own");
        }

        if (rule.CapUsd is null)
        {
            missing.Add("--cap-usd, the most its runs may spend in the window");
        }

        if (missing.Count > 0)
        {
            var accept = string.Join(' ', missing.Select(one => one.Split(',', 2)[0] + " proposed"));

            return $"A rule that starts {rule.Action.Name} needs {string.Join(" and ", missing)}. Proposed: "
                + $"{proposal.Working()} Accept it with {accept}, or give your own figures.";
        }

        if (accepted && !output.IsJson)
        {
            output.WriteLine($"[dim]Proposed and taken:[/] {Markup.Escape(proposal.Working())}");
        }

        if (traits.Writes && !output.IsJson)
        {
            output.WriteLine(
                $"[yellow]{Markup.Escape(rule.Action.Name)} writes[/] (a node works in a worktree), so the rule is kept "
                + "but refused each time it matches until triggers can ask for your approval.");
        }

        return null;
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
            () => Task.FromResult(
                !standing.Paused
                    ? new TriggerAnswer(request, true, $"'{found.Rule.Id}' was not paused, so nothing changed.")

                    // Refused rather than done: the daemon is what decides the
                    // events a paused rule held, and resumed here they would
                    // stay held for good with nothing to say so.
                    : TriggerSpine.Held(desk.Journal.Read(time.GetUtcNow() - TriggerSpine.Remembered), found.Rule.Id)
                        is { Count: > 0 } held
                        ? new TriggerAnswer(
                            request,
                            false,
                            $"'{found.Rule.Id}' held {held.Count} event(s) while it was paused, and only a running daemon "
                            + "can decide them. Start it with 'loadout daemon', then resume the rule.")
                        : desk.Journalled(
                            request, TriggerEntryKinds.Resumed, found.Rule.Id, found.Rule.Version, "resumed with no daemon running",
                            $"'{found.Rule.Id}' will act again when the daemon starts, its caps counting from now.")),
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
            .Select(one => (Entry: one, Decision: TriggerMatcher.Decide(one.ToEvent(), found.Rule)))
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

/// <summary>Deletes old days of the trigger journal.</summary>
[Description("Delete days of the trigger journal older than a given age, recording what went. Never the last 31 days, which hold money still set aside. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger journal prune delete old days disk", Mutates = true)]
public sealed class TriggerPruneCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerPruneCommand.Settings>
{
    public sealed class Settings : TriggerSettings
    {
        [CommandOption("--older-than <DURATION>")]
        [Description("Delete days older than this, such as 60d. 90d when left out, which is what the daemon keeps; never less than 31d.")]
        public string? OlderThan { get; init; }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Straight to the files, even with a daemon running: only whole days
    /// older than the ledger reads are deleted, and the daemon appends only to
    /// today's.
    /// </remarks>
    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return Task.FromResult(output.Fail(refusal, ExitCode.InvalidArguments));
        }

        if (TriggerWords.Duration(settings.OlderThan, TriggerJournal.Kept) is not { } age)
        {
            return Task.FromResult(output.Fail(TeamDuration.Refusal(settings.OlderThan!), ExitCode.InvalidArguments));
        }

        if (age < TriggerSpine.Remembered)
        {
            output.WriteLine(
                $"[dim]The last {TriggerSpine.Remembered.TotalDays:0} days are kept whatever is asked: the spend ledger is "
                + "read from them.[/]");
        }

        var now = time.GetUtcNow();
        var (days, bytes, _, _) = desk.Journal.Prunable(now, age);

        if (settings.DryRun || days.Count == 0)
        {
            var said = days.Count == 0
                ? "Nothing in the trigger journal is that old."
                : $"Dry run: nothing was deleted. {days.Count} day(s) would go, {days[0]} to {days[^1]}, about {bytes / 1024} KB.";

            if (output.IsJson)
            {
                output.WriteJson(new { pruned = false, days, bytes });
            }
            else
            {
                output.WriteLine(Markup.Escape(said));
            }

            return Task.FromResult(CommandOutput.Success());
        }

        var gone = desk.Journal.Prune(now, age, $"pruned by hand, older than {TeamDuration.Spell(age)}");

        if (output.IsJson)
        {
            output.WriteJson(new { pruned = true, days = gone, bytes });
        }
        else
        {
            output.WriteLine(Markup.Escape($"Deleted {gone.Count} day(s) of the trigger journal, and recorded that it did."));
        }

        return Task.FromResult(CommandOutput.Success());
    }
}

/// <summary>Lists the events held too long to act on unasked, waiting for somebody to release them.</summary>
[Description("List the events trigger rules held for longer than the 24-hour catch-up window, and when each is dropped if nobody releases it. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger stale held events catch up window")]
public sealed class TriggerStaleCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerSettings>
{
    /// <inheritdoc />
    public override Task<int> ExecuteAsync(CommandContext context, TriggerSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return Task.FromResult(output.Fail(refusal, ExitCode.InvalidArguments));
        }

        var now = time.GetUtcNow();
        var stale = TriggerSpine.Stale(desk.Journal.Read(now - TriggerSpine.Remembered), now);

        if (output.IsJson)
        {
            output.WriteJson(stale.Select(one => new
            {
                rule = one.Rule,
                version = one.Version,
                @event = one.Event.Id,
                subject = one.Event.Subject,
                project = one.Event.Project,
                happened = one.Event.At,
                since = one.Since,
                agesOut = one.AgesOut,
            }));

            return Task.FromResult(CommandOutput.Success());
        }

        if (stale.Count == 0)
        {
            output.WriteLine("Nothing is on the stale list.");

            return Task.FromResult(CommandOutput.Success());
        }

        foreach (var one in stale)
        {
            output.WriteLine(Markup.Escape(
                $"{one.Rule}: {one.Event.Kind} about {one.Event.Subject}, at {one.Event.At:yyyy-MM-dd HH:mm} UTC; "
                + $"dropped {one.AgesOut:yyyy-MM-dd HH:mm} UTC unless released"));
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Release a rule's, under its caps as usual, with:[/] loadout trigger release <rule> [[--subject <subject>]]");

        return Task.FromResult(CommandOutput.Success());
    }
}

/// <summary>Releases stale events, to be decided against their rule as it is now.</summary>
[Description("Release the stale events a trigger rule holds, to be decided against the rule as it is now, under its caps. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger release stale held events", Mutates = true)]
public sealed class TriggerReleaseCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerReleaseCommand.Settings>
{
    public sealed class Settings : TriggerRuleSettings
    {
        [CommandOption("--subject <SUBJECT>")]
        [Description("Only the stale event about this subject, as 'trigger stale' names it. Every one of the rule's when left out.")]
        public string? Subject { get; init; }
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

        var now = time.GetUtcNow();
        var subject = settings.Subject is { Length: > 0 } one ? one.Trim() : null;
        var stale = TriggerSpine.Stale(desk.Journal.Read(now - TriggerSpine.Remembered), now)
            .Where(each => string.Equals(each.Rule, settings.Name, StringComparison.OrdinalIgnoreCase)
                && (subject is null || string.Equals(each.Event.Subject, subject, StringComparison.Ordinal)))
            .ToList();

        if (settings.DryRun)
        {
            output.WriteLine(stale.Count == 0
                ? "[dim]Dry run: nothing was released.[/] There is nothing of that on the stale list."
                : $"[dim]Dry run: nothing was released.[/] {stale.Count} stale event(s) of '{Markup.Escape(settings.Name)}' "
                  + "would be decided against the rule as it is now, under its caps.");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Release, RuleId: settings.Name, Subject: subject),
            output,

            // Refused rather than done: the daemon is what decides an event,
            // and written here it would only record a release nothing acted on.
            () => Task.FromResult(stale.Count == 0
                ? new TriggerAnswer(request, true, "There is nothing of that on the stale list, so nothing changed.")
                : new TriggerAnswer(
                    request,
                    false,
                    $"Only a running daemon can decide the {stale.Count} stale event(s). Start it with 'loadout daemon', "
                    + "then release them.")),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Moves a schedule onto a clock rule, and pauses the schedule so nothing fires twice.</summary>
[Description("Move a team schedule that runs on a clock onto a trigger rule with a spend cap, and pause the schedule. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger adopt schedule clock move fold", Mutates = true)]
public sealed class TriggerAdoptCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    Loadout.Core.Teams.IScheduleService schedules,
    Loadout.Core.Teams.IRunJournal runs,
    ICommandCatalogue commands,
    Loadout.Core.Teams.ITeamCatalogue teams,
    Loadout.Core.Instructions.ISpecialistLibrary library,
    Loadout.Core.Workspace.IWorkspaceManager workspace) : AsyncCommand<TriggerAdoptCommand.Settings>
{
    public sealed class Settings : TriggerSettings
    {
        [CommandArgument(0, "<SCHEDULE>")]
        [Description("The schedule, as 'team schedule list' names it. The rule takes its name.")]
        public string Schedule { get; init; } = string.Empty;

        [CommandOption("--cap-usd <AMOUNT>")]
        [Description("The most its runs may spend in the window, in US dollars, or proposed.")]
        public string? CapUsd { get; init; }

        [CommandOption("--budget-usd <AMOUNT>")]
        [Description("What each run may spend, in US dollars, or proposed. The schedule's own, then the team's, when left out.")]
        public string? BudgetUsd { get; init; }
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

        var listed = await schedules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        if (listed.Value!.FirstOrDefault(one => string.Equals(one.Id, settings.Schedule, StringComparison.OrdinalIgnoreCase))
            is not { } schedule)
        {
            return output.Fail(
                $"There is no schedule called '{settings.Schedule}'. See them with: loadout team schedule list",
                ExitCode.ProjectNotFound);
        }

        if (TriggerAdopt.Rule(schedule) is (null, var why))
        {
            return output.Fail(why!, ExitCode.InvalidArguments);
        }

        var rule = TriggerAdopt.Rule(schedule).Rule!;

        var unreadable = TriggerWords.Money(settings.CapUsd, "--cap-usd", value => rule.CapUsd = value)
            ?? TriggerWords.Money(settings.BudgetUsd, "--budget-usd", value => rule.BudgetUsd = value);

        if (unreadable is not null)
        {
            return output.Fail(unreadable, ExitCode.InvalidArguments);
        }

        if (await TriggerAddCommand.TeamMoneyAsync(
                rule, settings.BudgetUsd, settings.CapUsd, output,
                new DaemonTriggerActions(commands, runs, teams, library, workspace), runs, cancellationToken)
                .ConfigureAwait(false) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        if (TriggerRules.Check(rule) is { } problem)
        {
            return output.Fail($"'{schedule.Id}' cannot become a rule as it stands: {problem}", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was saved and the schedule still runs.[/] This would save '{Markup.Escape(rule.Id)}':");
            output.WriteLine(Markup.Escape(TriggerWords.Line(rule, null)));
            output.WriteLine($"and pause the schedule '{Markup.Escape(schedule.Id)}', keeping its history.");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");
        var saved = await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Save, Rule: rule),
            output,
            async () =>
            {
                var written = await desk.Rules.SaveAsync(rule, cancellationToken).ConfigureAwait(false);

                return written.Failed
                    ? new TriggerAnswer(request, false, written.Error!)
                    : desk.Journalled(
                        request, TriggerEntryKinds.Edited, written.Value!.Id, written.Value!.Version,
                        $"adopted from the schedule '{schedule.Id}' with no daemon running",
                        $"'{written.Value!.Id}' is saved as version {written.Value!.Version}. No daemon is running, so it "
                        + "takes effect when one starts.");
            },
            cancellationToken).ConfigureAwait(false);

        if (saved != 0)
        {
            return saved;
        }

        // Paused only once the rule is saved, so a refused save leaves the
        // schedule running rather than nothing running at all.
        schedule.Enabled = false;
        var paused = await schedules.SaveAsync(schedule, cancellationToken).ConfigureAwait(false);

        if (paused.Failed)
        {
            return output.Fail(
                $"The rule is saved, but the schedule '{schedule.Id}' could not be paused, so both would run: {paused.Error} "
                + $"Remove it yourself with: loadout team schedule remove {schedule.Id}");
        }

        output.WriteLine($"The schedule '{Markup.Escape(schedule.Id)}' is paused, and keeps its history.");

        return CommandOutput.Success();
    }
}

/// <summary>Lists the trigger rules teams suggest for themselves, and where each stands.</summary>
[Description("List the trigger rules teams suggest for themselves: which are accepted here, and which have changed since. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger suggested team suggests rules offered")]
public sealed class TriggerSuggestedCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    Loadout.Core.Teams.ITeamCatalogue teams,
    Loadout.Core.Instructions.ISpecialistLibrary library,
    Loadout.Core.Workspace.IWorkspaceManager workspace) : AsyncCommand<TriggerSuggestedCommand.Settings>
{
    public sealed class Settings : TriggerSettings
    {
        [CommandOption("--project <SLUG>")]
        [Description("Include the teams of this project as well as the built-in and workspace ones.")]
        public string? Project { get; init; }
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

        var root = workspace.IsAvailable() ? workspace.LocalPath : null;
        var specialists = await library.LoadAsync(root, settings.Project, cancellationToken).ConfigureAwait(false);
        var catalogue = await teams.LoadAsync(root, settings.Project, specialists, cancellationToken).ConfigureAwait(false);
        var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);

        if (listed.Failed)
        {
            return output.Fail(listed);
        }

        var suggestions = TeamSuggestions.Of(catalogue.Teams.Values, listed.Value!.Select(one => one.Rule));

        if (output.IsJson)
        {
            output.WriteJson(suggestions.Select(one => new
            {
                team = one.Team,
                name = one.Suggested.Name,
                rule = one.RuleId,
                suggested = one.Suggested,
                accepted = one.Accepted is not null,
                changed = one.Changed,
            }));

            return CommandOutput.Success();
        }

        if (suggestions.Count == 0)
        {
            output.WriteLine("No team here suggests any trigger rules.");

            return CommandOutput.Success();
        }

        foreach (var one in suggestions)
        {
            var where = one.Suggested.Where.Count > 0
                ? " where " + string.Join(", ", one.Suggested.Where.Select(pair => $"{pair.Key}={pair.Value}"))
                : string.Empty;
            var state = one.Accepted is null ? "not accepted"
                : one.Changed ? "accepted, and the team has changed it since: accept it again to take the change"
                : "accepted";

            output.WriteLine(Markup.Escape($"{one.Team} {one.Suggested.Name}: on {one.Suggested.On}{where}, \"{one.Suggested.Goal}\" ({state})"));
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Take one as a rule of your own with:[/] loadout trigger accept <team> <name>");

        return CommandOutput.Success();
    }
}

/// <summary>Takes a team's suggested trigger rule as a rule on this machine.</summary>
[Description("Accept a trigger rule a team suggests, as a rule of your own with a spend cap. Accepting it again takes a changed suggestion. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger accept team suggestion opt in rule", Mutates = true)]
public sealed class TriggerAcceptCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time,
    Loadout.Core.Teams.IRunJournal runs,
    ICommandCatalogue commands,
    Loadout.Core.Teams.ITeamCatalogue teams,
    Loadout.Core.Instructions.ISpecialistLibrary library,
    Loadout.Core.Workspace.IWorkspaceManager workspace) : AsyncCommand<TriggerAcceptCommand.Settings>
{
    public sealed class Settings : TriggerSettings
    {
        [CommandArgument(0, "<TEAM>")]
        [Description("The team that suggests it.")]
        public string Team { get; init; } = string.Empty;

        [CommandArgument(1, "<NAME>")]
        [Description("Which of its suggestions, as 'trigger suggested' names it.")]
        public string Name { get; init; } = string.Empty;

        [CommandOption("--project <SLUG>")]
        [Description("Only events from this project count. Give it more than once for several; leave it out for all.")]
        public string[] Projects { get; init; } = [];

        [CommandOption("--cap-usd <AMOUNT>")]
        [Description("The most its runs may spend in the window, in US dollars, or proposed. The team's suggestion when left out.")]
        public string? CapUsd { get; init; }

        [CommandOption("--budget-usd <AMOUNT>")]
        [Description("What each run may spend, in US dollars, or proposed. The team's suggestion, then the team's own, when left out.")]
        public string? BudgetUsd { get; init; }
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

        var projects = settings.Projects.Select(one => one.Trim()).Where(one => one.Length > 0).ToList();
        var root = workspace.IsAvailable() ? workspace.LocalPath : null;
        var slug = projects.FirstOrDefault();
        var specialists = await library.LoadAsync(root, slug, cancellationToken).ConfigureAwait(false);
        var catalogue = await teams.LoadAsync(root, slug, specialists, cancellationToken).ConfigureAwait(false);

        if (catalogue.Find(settings.Team) is not { } team)
        {
            return output.Fail($"No team named '{settings.Team}'. See what there is with: loadout team list", ExitCode.ProjectNotFound);
        }

        if (team.Triggers.FirstOrDefault(one => string.Equals(one.Name, settings.Name, StringComparison.OrdinalIgnoreCase))
            is not { } suggested)
        {
            return output.Fail(
                $"{team.Name} suggests no trigger called '{settings.Name}'. See what it suggests with: loadout trigger suggested",
                ExitCode.ProjectNotFound);
        }

        var rule = TeamSuggestions.Rule(team.Name, suggested, projects);

        // What the person says wins over what the team suggests, figure by figure.
        var unreadable = TriggerWords.Money(settings.CapUsd, "--cap-usd", value => rule.CapUsd = value)
            ?? TriggerWords.Money(settings.BudgetUsd, "--budget-usd", value => rule.BudgetUsd = value);

        if (unreadable is not null)
        {
            return output.Fail(unreadable, ExitCode.InvalidArguments);
        }

        if (await TriggerAddCommand.TeamMoneyAsync(
                rule, settings.BudgetUsd, settings.CapUsd, output,
                new DaemonTriggerActions(commands, runs, teams, library, workspace), runs, cancellationToken)
                .ConfigureAwait(false) is { } refused)
        {
            return output.Fail(refused, ExitCode.InvalidArguments);
        }

        if (TriggerRules.Check(rule) is { } problem)
        {
            return output.Fail($"{team.Name}'s suggestion cannot be accepted as it stands: {problem}", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            var listed = await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false);
            var was = listed.Succeeded
                ? listed.Value!.FirstOrDefault(one => string.Equals(one.Rule.Id, rule.Id, StringComparison.OrdinalIgnoreCase))?.Rule
                : null;

            output.WriteLine($"[dim]Dry run: nothing was saved.[/] This would save '{Markup.Escape(rule.Id)}':");
            output.WriteLine(Markup.Escape(TriggerWords.Line(rule, null)));
            TriggerWords.Changes(output, was, rule);

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
                        $"accepted from {team.Name}'s suggestion with no daemon running",
                        $"'{saved.Value!.Id}' is saved as version {saved.Value!.Version}. No daemon is running, so it "
                        + "takes effect when one starts.");
            },
            cancellationToken).ConfigureAwait(false);
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

        if (settings.Every is { Length: > 0 } every)
        {
            if (TeamDuration.Parse(every) is { } parsed)
            {
                rule.Every = parsed;
            }
            else
            {
                problem ??= TeamDuration.Refusal(every);
            }
        }

        if (settings.At is { Length: > 0 } at)
        {
            if (TimeOnly.TryParse(at, CultureInfo.InvariantCulture, out var parsed))
            {
                rule.At = parsed;
            }
            else
            {
                problem ??= $"'{at}' is not a time of day. Write it as 09:00.";
            }
        }

        return (rule, problem);
    }

    /// <summary>When a rule waits for, in words: its event, or for a clock rule its time.</summary>
    public static string When(TriggerRule rule) =>
        rule.Every is { } every ? $"every {TeamDuration.Spell(every)}"
        : rule.At is { } at ? $"daily at {at.ToString("HH:mm", CultureInfo.InvariantCulture)}"
        : $"on {rule.On}";

    /// <summary>A duration given as an option, the fallback when none was, or null when what was given is not one.</summary>
    public static TimeSpan? Duration(string? text, TimeSpan fallback) =>
        string.IsNullOrWhiteSpace(text) ? fallback : TeamDuration.Parse(text);

    /// <summary>Whether a money option asked for the proposed figure rather than giving one.</summary>
    public static bool Proposed(string? text) =>
        string.Equals(text?.Trim(), "proposed", StringComparison.OrdinalIgnoreCase);

    internal static string? Money(string? text, string option, Action<decimal> set)
    {
        // Filled in by the command once it has read the team's runs.
        if (string.IsNullOrWhiteSpace(text) || Proposed(text))
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

        return $"{rule.Id} v{rule.Version}{state}: {When(rule)}{projects}{where}, start {starts}{caps}{stood}";
    }

    /// <summary>
    /// Under a dry run's line, what saving would change in a rule already
    /// there, a field to a line; nothing for a new one.
    /// </summary>
    public static void Changes(CommandOutput output, TriggerRule? was, TriggerRule rule)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (was is null)
        {
            return;
        }

        var changes = TriggerRuleChanges.Of(was, rule);

        if (changes.Count == 0)
        {
            output.WriteLine("[dim]Nothing in it would change.[/]");

            return;
        }

        foreach (var one in changes)
        {
            output.WriteLine($"  {Markup.Escape(one)}");
        }
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
}
