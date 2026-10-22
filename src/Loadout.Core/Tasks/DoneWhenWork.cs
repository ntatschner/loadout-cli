using System.Text;
using System.Text.Json;
using Loadout.Models;
using Loadout.Models.Results;
using Loadout.Models.Tasks;

namespace Loadout.Core.Tasks;

/// <summary>What an agent proposed as a task's done-when, and why.</summary>
public sealed record DoneWhenProposal(IReadOnlyList<string> DoneWhen, string Why);

/// <summary>
/// What an agent is asked when it proposes a task's done-when, and how its answer is read.
/// </summary>
/// <remarks>
/// For the tasks that never got any: work recorded by hand, and ideas accepted
/// before an idea's plan proposed them. Nothing proposed here is recorded until
/// a person accepts it, because a done-when nobody confirmed is a finish line
/// nobody agreed to, and an unattended run is held to it.
/// </remarks>
public static class DoneWhenWork
{
    /// <summary>The longest a condition may be. One sentence, not a paragraph.</summary>
    public const int Longest = 300;

    /// <summary>How much of a plan the agent is shown.</summary>
    internal const int PlanLength = 12_000;

    /// <summary>The prompt for one task.</summary>
    /// <param name="task">The task.</param>
    /// <param name="project">The project's slug, or null for the workspace-wide list.</param>
    /// <param name="plan">The accepted plan the task came from, as written, or null when it has none.</param>
    public static string Prompt(TaskItem task, string? project, string? plan)
    {
        ArgumentNullException.ThrowIfNull(task);

        var text = new StringBuilder();

        text.AppendLine(
            $"Propose the done-when for one task on {(project is null ? "the workspace-wide task list" : $"the {project} project's task list")}: "
            + "what must be true for the work to count as finished.");
        text.AppendLine();
        text.AppendLine($"Task id: {task.Id}");
        text.AppendLine($"Title: {task.Title}");

        if (task.Note.Trim().Length > 0)
        {
            text.AppendLine($"Note: {task.Note.Trim()}");
        }

        if (task.Detail.Trim().Length > 0)
        {
            text.AppendLine();
            text.AppendLine("Detail:");
            text.AppendLine(task.Detail.Trim());
        }

        if (task.DoneWhen.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("It already has these, which your proposal would replace:");

            foreach (var condition in task.DoneWhen)
            {
                text.AppendLine($"- {condition}");
            }
        }

        if (plan is { Length: > 0 })
        {
            text.AppendLine();
            text.AppendLine("The plan it was accepted from:");
            text.AppendLine(plan.Length > PlanLength ? plan[..PlanLength] + "\n[the rest of the plan is left out]" : plan);
        }

        text.AppendLine();
        text.AppendLine(
            $"Give {DoneWhenSchema.Fewest} to {DoneWhenSchema.Most} conditions somebody could check without asking you, "
            + "each one sentence. Make them specific to this task rather than true of any work: not just 'the tests "
            + "pass', but what in particular is now true that was not. Where it helps, read the repository you are "
            + "in to name the real command, test or file. A team run for the task is held to these, often with "
            + "nobody watching, and its lead must report a verdict and evidence for each, so prefer conditions with "
            + "a clear yes or no. A person reads them and confirms or rewrites them before anything is recorded.");
        text.AppendLine();
        text.AppendLine(
            $"Answer in the {DoneWhenSchema.Version} shape: contract, doneWhen (the conditions) and why (a sentence "
            + "or two on how you chose them).");

        return text.ToString();
    }

    /// <summary>Reads an answer, refusing one that cannot be used as it stands.</summary>
    public static OperationResult<DoneWhenProposal> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Refused("There was no answer.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("contract", out var contract)
                || contract.ValueKind != JsonValueKind.String
                || contract.GetString() != DoneWhenSchema.Version)
            {
                return Refused($"The answer does not name the {DoneWhenSchema.Version} contract.");
            }

            if (!root.TryGetProperty("doneWhen", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Refused("The answer has no doneWhen list.");
            }

            var conditions = list.EnumerateArray()
                .Where(one => one.ValueKind == JsonValueKind.String)
                .Select(one => one.GetString()!.Trim())
                .Where(one => one.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (conditions.Count < DoneWhenSchema.Fewest || conditions.Count > DoneWhenSchema.Most)
            {
                return Refused(
                    $"The answer has {conditions.Count} distinct condition(s); it should have between "
                    + $"{DoneWhenSchema.Fewest} and {DoneWhenSchema.Most}.");
            }

            if (conditions.FirstOrDefault(one => one.Length > Longest) is { } tooLong)
            {
                return Refused(
                    $"A condition is {tooLong.Length} characters long; each should be one sentence, under {Longest}.");
            }

            var why = root.TryGetProperty("why", out var reason) && reason.ValueKind == JsonValueKind.String
                ? reason.GetString()!.Trim()
                : string.Empty;

            return OperationResult<DoneWhenProposal>.Ok(new DoneWhenProposal(conditions, why));
        }
        catch (JsonException ex)
        {
            return Refused($"The answer is not JSON: {ex.Message}");
        }
    }

    private static OperationResult<DoneWhenProposal> Refused(string why) =>
        OperationResult<DoneWhenProposal>.Fail(why, ExitCode.GeneralFailure);
}
