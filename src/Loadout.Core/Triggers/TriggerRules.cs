using Loadout.Core.Configuration;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Triggers;

namespace Loadout.Core.Triggers;

/// <summary>The trigger rules on this machine.</summary>
public interface ITriggerRules
{
    /// <summary>Every rule, in the order they were made, each checked as it is read.</summary>
    /// <remarks>
    /// A rule that fails its check is still listed, beside why, so somebody
    /// who edited the file by hand can see what is wrong with it; the spine
    /// acts only on the ones that pass.
    /// </remarks>
    Task<OperationResult<IReadOnlyList<CheckedRule>>> ListAsync(CancellationToken ct = default);

    /// <summary>Records one, replacing any of the same name and counting its version on.</summary>
    Task<OperationResult<TriggerRule>> SaveAsync(TriggerRule rule, CancellationToken ct = default);

    /// <summary>Forgets one.</summary>
    Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default);
}

/// <summary>A rule as read, and what is wrong with it, or null when nothing is.</summary>
public sealed record CheckedRule(TriggerRule Rule, string? Problem)
{
    /// <summary>Whether the spine may act on it.</summary>
    public bool Usable => Problem is null;
}

/// <inheritdoc />
/// <remarks>
/// One file, <c>&lt;State&gt;/triggers/rules.yaml</c>, beside the schedules
/// and for the same reason: there are a handful of these, and one list is what
/// makes "what could this event start" one read.
/// </remarks>
public sealed class TriggerRules : ITriggerRules
{
    private readonly string _path;
    private readonly YamlStore _yaml;

    /// <param name="state">The state directory.</param>
    /// <param name="yaml">Reads and writes the file.</param>
    public TriggerRules(string state, YamlStore yaml)
    {
        _path = Path.Combine(state, "triggers", "rules.yaml");
        _yaml = yaml;
    }

    /// <summary>Where the rules are kept.</summary>
    public string FilePath => _path;

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<CheckedRule>>> ListAsync(CancellationToken ct = default)
    {
        var loaded = await _yaml.LoadAsync(_path, () => new TriggerRuleList(), ct).ConfigureAwait(false);

        return loaded.Failed
            ? OperationResult<IReadOnlyList<CheckedRule>>.Fail(loaded.Error!, loaded.ExitCode)
            : OperationResult<IReadOnlyList<CheckedRule>>.Ok(
                [.. loaded.Value!.Items.Select(rule => new CheckedRule(rule, Check(rule)))]);
    }

    /// <inheritdoc />
    public async Task<OperationResult<TriggerRule>> SaveAsync(TriggerRule rule, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (Check(rule) is { } wrong)
        {
            return OperationResult<TriggerRule>.Fail(wrong, ExitCode.InvalidArguments);
        }

        TriggerRule? saved = null;

        var written = await _yaml.UpdateAsync<TriggerRuleList>(
            _path,
            () => new TriggerRuleList(),
            list =>
            {
                var was = list.Items.Find(item =>
                    string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));

                // Counted on from whatever was there, never taken from the
                // caller: the journal ties each decision to a version, and two
                // different rules sharing one number would make that a lie.
                rule.Version = was is null ? 1 : was.Version + 1;

                list.Items.RemoveAll(item =>
                    string.Equals(item.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
                list.Items.Add(rule);

                saved = rule;
            },
            true,
            ct).ConfigureAwait(false);

        return written.Succeeded && saved is not null
            ? OperationResult<TriggerRule>.Ok(saved)
            : OperationResult<TriggerRule>.Fail(written.Error ?? "The rule could not be recorded.", written.ExitCode);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RemoveAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var gone = false;

        var written = await _yaml.UpdateAsync<TriggerRuleList>(
            _path,
            () => new TriggerRuleList(),
            list => gone = list.Items.RemoveAll(item =>
                string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase)) > 0,
            true,
            ct).ConfigureAwait(false);

        if (written.Failed)
        {
            return OperationResult.Fail(written.Error!, written.ExitCode);
        }

        return gone
            ? OperationResult.Ok()
            : OperationResult.Fail($"There is no trigger rule called '{id}' on this machine.", ExitCode.ProjectNotFound);
    }

    /// <summary>Whether what a rule starts has a model behind it, and so costs money.</summary>
    /// <remarks>
    /// Built-in commands read and report; teams and remedies start agents. A
    /// built-in that cost nothing would otherwise use up headroom it never
    /// spends.
    /// </remarks>
    public static bool CostsMoney(TriggerAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return !string.Equals(action.Kind, TriggerActionKinds.Builtin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>What is wrong with a rule's clock, or with a clock on a rule that has none to keep.</summary>
    /// <remarks>
    /// A clock rule names one project for a team, because a clock happens
    /// nowhere in particular and a team run has to happen somewhere: the
    /// project is the schedule's, not something an event could supply.
    /// </remarks>
    private static string? ClockProblem(TriggerRule rule)
    {
        var clock = string.Equals(rule.On, TriggerEventKinds.Clock, StringComparison.OrdinalIgnoreCase);

        if (!clock)
        {
            return rule.Every is not null || rule.At is not null
                ? $"Only a clock rule comes round every so often or at a time; a {rule.On} rule waits for its event."
                : null;
        }

        if ((rule.Every is null) == (rule.At is null))
        {
            return "A clock rule comes round either every so often (--every) or once a day at a time (--at), "
                + "and needs exactly one of them.";
        }

        if (rule.Every is { } every && every < TimeSpan.FromMinutes(5))
        {
            return "A clock rule comes round every five minutes at the most.";
        }

        if (string.Equals(rule.Action.Kind, TriggerActionKinds.Team, StringComparison.OrdinalIgnoreCase)
            && rule.Projects.Count != 1)
        {
            return "A clock rule that starts a team names the one project it runs on, with --project.";
        }

        return null;
    }

    /// <summary>What is wrong with a rule, or null when nothing is.</summary>
    /// <remarks>
    /// One check for a rule typed at the command line and a rule edited into
    /// the file by hand, because a requirement that holds only for one of them
    /// does not hold.
    /// </remarks>
    public static string? Check(TriggerRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (Tasks.TaskIds.Rejection(rule.Id) is { } name)
        {
            return name.Replace("task id", "rule name", StringComparison.Ordinal)
                .Replace("A task needs an id.", "A rule needs a name.", StringComparison.Ordinal);
        }

        if (!TriggerEventKinds.Known(rule.On))
        {
            return $"'{rule.On}' is not an event a rule can wait for. It knows: "
                + $"{string.Join(", ", TriggerEventKinds.All)}.";
        }

        var carried = TriggerEventKinds.Fields[rule.On];

        foreach (var field in rule.Where.Keys)
        {
            if (!carried.Contains(field, StringComparer.OrdinalIgnoreCase))
            {
                return $"A {rule.On} event carries no '{field}', so a condition on it could never be met. "
                    + $"It carries: {string.Join(", ", carried)}.";
            }
        }

        var action = rule.Action;

        if (!TriggerActionKinds.All.Contains(action.Kind, StringComparer.OrdinalIgnoreCase))
        {
            return $"'{action.Kind}' is not something a rule can start. It can start: "
                + $"{string.Join(", ", TriggerActionKinds.All)}.";
        }

        if (string.IsNullOrWhiteSpace(action.Name))
        {
            return $"A rule needs the {action.Kind} it starts.";
        }

        if (string.Equals(action.Kind, TriggerActionKinds.Team, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(action.Goal))
            {
                return "A rule that starts a team needs a goal, in your own words, for the lead to work from.";
            }

            if (string.Equals(action.Autonomy, "manual", StringComparison.OrdinalIgnoreCase))
            {
                return "A rule cannot start a manual run: it fires when nobody is watching. Use supervised "
                    + "or autonomous.";
            }
        }

        if (rule.Window < TimeSpan.FromHours(1) || rule.Window > TimeSpan.FromDays(31))
        {
            return "A rule's caps count over a window of between an hour and 31 days.";
        }

        if (rule.CapRuns < 1)
        {
            return "A rule needs to allow at least one run in its window.";
        }

        if (rule.Debounce < TimeSpan.Zero || rule.Debounce > TimeSpan.FromHours(24))
        {
            return "A rule waits between nothing and a day for its subject to go quiet.";
        }

        if (ClockProblem(rule) is { } clock)
        {
            return clock;
        }

        if (!CostsMoney(action))
        {
            return null;
        }

        if (rule.CapUsd is not { } cap)
        {
            return "A rule that starts a model needs a spend cap: the most its runs may spend in its window. "
                + "Without one, a loop has nothing to stop it.";
        }

        if (cap <= 0)
        {
            return "A rule's spend cap has to be more than nothing.";
        }

        if (rule.BudgetUsd is { } budget && (budget <= 0 || budget > cap))
        {
            return $"A rule's budget for each run has to be more than nothing and no more than its spend cap "
                + $"of ${cap:0.##}, or not even one run could start.";
        }

        return null;
    }
}
