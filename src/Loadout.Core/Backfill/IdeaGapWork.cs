using System.Text;
using Loadout.Core.Ideas;
using Loadout.Models.Ideas;
using Loadout.Models.Tasks;

namespace Loadout.Core.Backfill;

/// <summary>What an agent is asked when a pass fills the done-when an idea's plan lacks.</summary>
/// <remarks>
/// <para>
/// The plan is shown whole and read-only, and only the missing field is asked
/// for, under the same <c>donewhen/1</c> contract a task's proposal uses: a
/// small answer is cheap to ask for and easy to check, and the rest of the
/// plan stays exactly as the person last saw it.
/// </para>
/// <para>
/// done-when is the only field a pass can fill today, because it is the only
/// one a plan under an earlier contract can lack. A field the next contract
/// adds gets its own prompt and schema here.
/// </para>
/// </remarks>
public static class IdeaGapWork
{
    /// <summary>How much of the plan the agent is shown.</summary>
    internal const int PlanLength = 12_000;

    /// <summary>The prompt for one idea.</summary>
    /// <param name="record">The idea's working record, with a plan.</param>
    /// <param name="project">The list it is on, or null for the workspace-wide one.</param>
    public static string Prompt(IdeaRecord record, string? project)
    {
        ArgumentNullException.ThrowIfNull(record);

        var plan = IdeaWork.Document(record);
        var text = new StringBuilder();

        text.AppendLine(
            $"An idea on {(project is null ? "the workspace-wide list" : $"the {project} project's list")} has a plan "
            + "that was proposed before plans carried a done-when: what must be true for the work to count as "
            + "finished. Propose one for it. Do not change anything else; the rest of the plan stays as it is.");
        text.AppendLine();
        text.AppendLine($"Idea id: {record.Id}");
        text.AppendLine();
        text.AppendLine("The plan, as it stands:");
        text.AppendLine(plan.Length > PlanLength ? plan[..PlanLength] + "\n[the rest of the plan is left out]" : plan);
        text.AppendLine();
        text.AppendLine(
            $"Give {DoneWhenSchema.Fewest} to {DoneWhenSchema.Most} conditions somebody could check without asking you, "
            + "each one sentence, following the layers the plan recommends and the additions it keeps. Make them "
            + "specific to this plan rather than true of any work: not just 'the tests pass', but what in particular "
            + "is now true that was not. Where it helps, read the repository you are in to name the real command, "
            + "test or file. The person reads them before accepting the plan, and can change them then.");
        text.AppendLine();
        text.AppendLine(
            $"Answer in the {DoneWhenSchema.Version} shape: contract, doneWhen (the conditions) and why (a sentence "
            + "or two on how you chose them).");

        return text.ToString();
    }
}
