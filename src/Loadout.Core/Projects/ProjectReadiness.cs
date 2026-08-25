namespace Loadout.Core.Projects;

/// <summary>
/// Whether a project can be launched, and how confidently.
/// <para>
/// The launcher could already say what was wrong with a project. What it could
/// not say, at a glance across a list, was whether any of it actually stopped
/// you working — which is the question somebody scanning a list of projects is
/// asking.
/// </para>
/// </summary>
public enum Readiness
{
    /// <summary>
    /// It has not been read yet, so nothing is claimed about it either way.
    /// </summary>
    /// <remarks>
    /// The launcher reads a project's details only when it is selected, so at
    /// any moment almost every project in the list is in this state. It was
    /// previously reported as NeedsAttention — the launcher asked for the
    /// readiness of a project it had never looked at, was handed a null
    /// overview, and a null overview means "read it and found nothing good to
    /// say". Fifteen of sixteen projects on the machine this was written for
    /// carried a warning that meant only that the cursor had not been on them.
    /// </remarks>
    Unknown,

    /// <summary>Nothing is in the way.</summary>
    Ready,

    /// <summary>It will launch, and something is worth knowing first.</summary>
    NeedsAttention,

    /// <summary>It will not launch until something is done.</summary>
    Blocked,

    /// <summary>Nothing here can run it, and no amount of fixing will change that.</summary>
    Unsupported,
}

/// <summary>
/// Works out a project's readiness from what is already known about it.
/// </summary>
public static class ProjectReadinessRules
{
    /// <summary>
    /// What can be said about a project before anything has been read.
    /// </summary>
    /// <remarks>
    /// Both things that block a launch — the repository not being here, and
    /// the agent not being installed — are known from the registry alone, so a
    /// list can be honest about what it cannot start without reading a single
    /// repository. Everything else waits until there is something to base it
    /// on.
    /// </remarks>
    /// <param name="isAvailableLocally">Whether the repository is on this machine.</param>
    /// <param name="agentInstalled">Whether the agent it would launch is installed here.</param>
    public static Readiness Provisional(bool isAvailableLocally, bool agentInstalled) =>
        !isAvailableLocally || !agentInstalled
            ? Readiness.Blocked
            : Readiness.Unknown;

    /// <summary>
    /// The state a project is in.
    /// </summary>
    /// <param name="overview">What is known about the project, or null when it could not be read.</param>
    /// <param name="isAvailableLocally">Whether the repository is on this machine.</param>
    /// <param name="agentInstalled">Whether the agent it would launch is installed here.</param>
    /// <remarks>
    /// <para>
    /// Blocked is reserved for things that genuinely stop a launch: the
    /// repository is not on this machine, or the agent it wants is not
    /// installed. Everything else the launcher reports — committed agent files,
    /// an oversized instruction layer, memory recorded in the wrong place, no
    /// pre-commit hook — is worth fixing and does not prevent working, so it is
    /// NeedsAttention.
    /// </para>
    /// <para>
    /// That line matters more than the names. Promoting every warning to
    /// Blocked would make the state useless: a list where everything is blocked
    /// says no more than a list with no states at all, and it trains people to
    /// ignore the one project that really is.
    /// </para>
    /// </remarks>
    public static Readiness Of(
        ProjectOverview? overview,
        bool isAvailableLocally,
        bool agentInstalled)
    {
        if (!isAvailableLocally)
        {
            return Readiness.Blocked;
        }

        if (!agentInstalled)
        {
            return Readiness.Blocked;
        }

        if (overview is null)
        {
            // Present and launchable, but nothing could be read about it. Not a
            // clean bill of health, and not a reason to refuse either.
            return Readiness.NeedsAttention;
        }

        return overview.HasWarnings ? Readiness.NeedsAttention : Readiness.Ready;
    }

    /// <summary>
    /// A short label, in words rather than colour alone.
    /// </summary>
    /// <remarks>
    /// Every state reads without colour. A terminal that is monochrome, or a
    /// person who cannot distinguish red from green, must get the same
    /// information as everybody else.
    /// </remarks>
    public static string Label(Readiness readiness) => readiness switch
    {
        Readiness.Unknown => string.Empty,
        Readiness.Ready => "Ready",
        Readiness.NeedsAttention => "Attention",
        Readiness.Blocked => "Blocked",
        _ => "Unsupported",
    };

    /// <summary>A mark that survives a terminal with no colour at all.</summary>
    public static string Mark(Readiness readiness) => readiness switch
    {
        Readiness.Unknown => string.Empty,
        Readiness.Ready => "+",
        Readiness.NeedsAttention => "!",
        Readiness.Blocked => "x",
        _ => "-",
    };

    /// <summary>Why it is in that state, in one sentence, or empty when it is Ready.</summary>
    public static string Because(
        Readiness readiness,
        bool isAvailableLocally,
        bool agentInstalled) => readiness switch
        {
            Readiness.Blocked when !isAvailableLocally => "not on this machine",
            Readiness.Blocked when !agentInstalled => "its agent is not installed here",
            Readiness.NeedsAttention => "something is worth looking at first",
            Readiness.Unknown => "it has not been read yet",
            _ => string.Empty,
        };
}
