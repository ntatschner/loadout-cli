using System.ComponentModel;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Triggers;
using Loadout.Models;
using Loadout.Platform.Abstractions;
using Loadout.Tui;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Loadout.Cli.Commands;

/// <summary>Names one approval.</summary>
public class TriggerApprovalSettings : TriggerSettings
{
    [CommandArgument(0, "<APPROVAL>")]
    [Description("The approval, as 'trigger approvals' names it.")]
    public string Approval { get; init; } = string.Empty;
}

/// <summary>Lists the actions that write, waiting for a person to say yes or no.</summary>
[Description("List the actions trigger rules want to start that write, each waiting for your approval: what it would act on, whose words, what it would spend, and whether it would start at once or wait for headroom. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger approvals pending writes approve reject inbox")]
public sealed class TriggerApprovalsCommand(
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

        var now = time.GetUtcNow();
        var pending = TriggerSpine.Approvals(desk.Journal.Read(now - TriggerSpine.Remembered));
        var ledger = desk.Ledger();
        var rules = (await desk.Rules.ListAsync(cancellationToken).ConfigureAwait(false)).Value ?? [];

        // What approving it now would do, as the ledger stands: start, or wait
        // for runs in flight to give money back. Said before you approve, so
        // a held start is no surprise.
        string Then(PendingApproval one)
        {
            if (one.Approved)
            {
                return "approved; waiting for headroom";
            }

            if (rules.FirstOrDefault(each => string.Equals(each.Rule.Id, one.Rule, StringComparison.OrdinalIgnoreCase))
                is not { } found)
            {
                return "its rule is gone";
            }

            return ledger.Ask(found.Rule, one.BudgetUsd ?? 0m, now).Verdict switch
            {
                LedgerVerdict.Go => "would start at once",
                LedgerVerdict.Hold => "would wait for headroom",
                _ => "would pause its rule: it is at a cap",
            };
        }

        if (output.IsJson)
        {
            output.WriteJson(pending.Select(one => new
            {
                id = one.Id,
                rule = one.Rule,
                version = one.Version,
                @event = one.Event.Id,
                kind = one.Event.Kind,
                subject = one.Event.Subject,
                project = one.Event.Project,
                title = one.Event.Field("title"),
                url = one.Event.Field("url"),
                trust = one.Event.Field("trust"),
                because = one.Event.Field("because"),
                budgetUsd = one.BudgetUsd,
                asked = one.Asked,
                expires = one.Expires,
                approved = one.Approved,
                then = Then(one),
            }));

            return CommandOutput.Success();
        }

        if (pending.Count == 0)
        {
            output.WriteLine("Nothing is waiting for your approval.");

            return CommandOutput.Success();
        }

        foreach (var one in pending)
        {
            var trust = one.Event.Field("trust") is { } word ? $", {word} ({one.Event.Field("because")})" : string.Empty;

            output.WriteLine(Markup.Escape(
                $"{one.Id}: {one.Rule} on {one.Event.Kind} about {one.Event.Subject}{trust}; "
                + $"${one.BudgetUsd ?? 0m:0.00} a run; {Then(one)}; withdrawn {one.Expires:yyyy-MM-dd HH:mm} UTC unless started"));
        }

        output.WriteBlankLine();
        output.WriteLine("[dim]Answer with:[/] loadout trigger approve <approval>  [dim]or[/]  loadout trigger reject <approval>");

        return CommandOutput.Success();
    }
}

/// <summary>Approves an action that writes, which sets its money aside and starts it.</summary>
[Description("Approve an action a trigger rule wants to start that writes. Its budget is set aside now, and it starts at once or when its rule has headroom. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger approve write action yes", Mutates = true)]
public sealed class TriggerApproveCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerApprovalSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerApprovalSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        var id = settings.Approval.Trim();
        var pending = TriggerSpine.Approvals(desk.Journal.Read(time.GetUtcNow() - TriggerSpine.Remembered))
            .FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase));

        if (pending is null)
        {
            return output.Fail($"Nothing is waiting for approval as '{id}'. 'loadout trigger approvals' lists what is.", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was approved.[/] '{Markup.Escape(pending.Id)}' would set aside "
                + $"${pending.BudgetUsd ?? 0m:0.00} and start {Markup.Escape(pending.Rule)} on {Markup.Escape(pending.Event.Subject)}.");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Approve, Approval: pending.Id),
            output,

            // Refused rather than recorded: only the daemon starts anything,
            // and an approval written here would start nothing until it did,
            // on a state you may no longer be looking at.
            () => Task.FromResult(new TriggerAnswer(
                request, false, "Only a running daemon can start what you approve. Start it with 'loadout daemon', then approve it.")),
            cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Rejects an action that writes.</summary>
[Description("Reject an action a trigger rule wants to start that writes; it will not start. Unfinished: needs LOADOUT_TRIGGERS.")]
[CommandMeta(CommandCategory.Integration, Intent = "trigger reject write action no", Mutates = true)]
public sealed class TriggerRejectCommand(
    IPlatformPaths paths,
    IProcessInspector processes,
    IEnvironmentProvider environment,
    YamlStore yaml,
    IFilePermissions permissions,
    IAnsiConsole console,
    TimeProvider time) : AsyncCommand<TriggerApprovalSettings>
{
    /// <inheritdoc />
    public override async Task<int> ExecuteAsync(CommandContext context, TriggerApprovalSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var output = new CommandOutput(console, settings);
        var desk = new TriggerDesk(paths, processes, environment, yaml, permissions, time);

        if (desk.Switch() is (false, var refusal))
        {
            return output.Fail(refusal, ExitCode.InvalidArguments);
        }

        var id = settings.Approval.Trim();
        var pending = TriggerSpine.Approvals(desk.Journal.Read(time.GetUtcNow() - TriggerSpine.Remembered))
            .FirstOrDefault(one => string.Equals(one.Id, id, StringComparison.OrdinalIgnoreCase));

        if (pending is null)
        {
            return output.Fail($"Nothing is waiting for approval as '{id}'. 'loadout trigger approvals' lists what is.", ExitCode.InvalidArguments);
        }

        if (settings.DryRun)
        {
            output.WriteLine($"[dim]Dry run: nothing was rejected.[/] '{Markup.Escape(pending.Id)}' would not start.");

            return CommandOutput.Success();
        }

        var request = Guid.NewGuid().ToString("N");

        return await desk.ChangeAsync(
            new TriggerAsk(request, TriggerVerbs.Reject, Approval: pending.Id),
            output,

            // With no daemon, nothing holds it in memory: saying no is a line
            // in the journal, the same line the daemon would write.
            () => Task.FromResult(desk.Journalled(
                request, Loadout.Core.Triggers.TriggerEntryKinds.Rejected, pending, $"'{pending.Id}' is rejected and will not start.")),
            cancellationToken).ConfigureAwait(false);
    }
}
