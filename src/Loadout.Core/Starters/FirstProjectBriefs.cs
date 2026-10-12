namespace Loadout.Core.Starters;

/// <summary>
/// What the two agent sessions of <c>loadout start</c> are asked to do.
/// </summary>
/// <remarks>
/// <para>
/// Given as the opening prompt, so the agent begins on it and the person does
/// not have to know what to type into an agent for the first time.
/// </para>
/// <para>
/// The planning session runs in advise posture, which tells the agent not to
/// change files. The plan has to end up somewhere the building session can
/// read it, so the brief makes one exception and names it, and the guide
/// checks afterwards that nothing else was touched.
/// </para>
/// </remarks>
public static class FirstProjectBriefs
{
    /// <summary>The file the plan is written to.</summary>
    public const string PlanFile = "PLAN.md";

    /// <summary>The task the planning session is launched for.</summary>
    public const string PlanTask = "Plan my first project";

    /// <summary>The task the building session is launched for.</summary>
    public const string BuildTask = "Build my first project from PLAN.md";

    /// <summary>The opening prompt of the planning session.</summary>
    public static string Plan(Starter starter, string name) =>
        $"""
        I am new to programming and to working with an AI agent. Loadout has just made this project, "{name}", from its "{starter.Name}" starter. {Contents(starter)}

        Help me decide what I want it to be. Ask me one question at a time, in plain words, and explain any word I might not know. Keep the idea small enough to finish today, using only HTML, CSS and, if it is really needed, a little JavaScript, so it opens in a browser with nothing to install.

        Do not change any files while we talk. When we agree, write the plan to {PlanFile} in the project folder, which is the one file you may write in this session: what the page shows, what it does, and how I will know it works. Keep it short enough for me to read in a minute, then tell me to end this session so Loadout can offer to build it.
        """;

    /// <summary>The opening prompt of the building session.</summary>
    public static string Build(Starter starter, string name) =>
        $"""
        I am new to programming and to working with an AI agent. This project, "{name}", has a plan in {PlanFile} that I agreed in an earlier session. Please build what it describes.

        Use only HTML, CSS and, where the plan needs it, a little plain JavaScript. Add no packages, tools or build steps, so the result still opens in a browser with nothing installed. {Page(starter)}

        As you go, tell me in plain words what each change does. When you have finished, tell me which file to open in my browser and what I should see, then tell me to end this session so Loadout can show me what changed.
        """;

    private static string Contents(Starter starter) => starter.Page is { } page
        ? $"It has {page}, which is the page, and a README."
        : "It has only a README so far.";

    private static string Page(Starter starter) => starter.Page is { } page
        ? $"Keep {page} as the page that opens first."
        : "Make index.html the page that opens first.";
}
