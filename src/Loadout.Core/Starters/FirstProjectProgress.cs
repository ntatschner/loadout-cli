namespace Loadout.Core.Starters;

/// <summary>The steps of <c>loadout start</c> that are recorded once done.</summary>
/// <remarks>
/// Checking what is installed is not among them. It is cheap, and its answer
/// can change between one run and the next, which is often the reason for the
/// next run.
/// </remarks>
public enum GuideStep
{
    /// <summary>The folder, the starter's files, Git and registration.</summary>
    Create,

    /// <summary>The pre-commit hook that keeps agent files out of the project.</summary>
    Protect,

    /// <summary>The advise session that ends with PLAN.md.</summary>
    Plan,

    /// <summary>The implement session that builds what PLAN.md describes.</summary>
    Build,

    /// <summary>Looking at what changed, and keeping it.</summary>
    Review,
}

/// <summary>
/// How far one person has got through <c>loadout start</c>.
/// </summary>
/// <remarks>
/// <para>
/// Kept because people stop halfway. The first time somebody runs the guide is
/// often the moment they find Git is missing, and installing it means a new
/// terminal. Running the command again should carry on, not begin a second
/// project beside the first.
/// </para>
/// <para>
/// One per machine, in the state directory. A guide for a first project is not
/// something to run three of at once, and a record per project would need the
/// project to exist before the first question is asked.
/// </para>
/// </remarks>
public sealed class FirstProjectProgress
{
    public int SchemaVersion { get; set; } = 1;

    /// <summary>The starter's id.</summary>
    public string Starter { get; set; } = string.Empty;

    /// <summary>The name the person gave it.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The registered project's slug.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>The project's folder on this machine.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The commit holding the starter's files, or null when Git could not commit.</summary>
    public string? StarterCommit { get; set; }

    /// <summary>The commit holding PLAN.md, which is what the build is measured from.</summary>
    public string? PlanCommit { get; set; }

    /// <summary>The steps finished, by name.</summary>
    public List<string> Done { get; set; } = [];

    public DateTimeOffset StartedUtc { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>Whether a step has been finished.</summary>
    public bool Has(GuideStep step) =>
        Done.Contains(step.ToString(), StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a step as finished.</summary>
    public void Finish(GuideStep step)
    {
        if (!Has(step))
        {
            Done.Add(step.ToString());
        }
    }

    /// <summary>The first step not yet finished, or null when the guide is complete.</summary>
    public GuideStep? Next =>
        Enum.GetValues<GuideStep>().Cast<GuideStep?>().FirstOrDefault(step => !Has(step!.Value));
}
