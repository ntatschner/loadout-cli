using Loadout.Models.Platform;

namespace Loadout.Core.Starters;

/// <summary>
/// Something a first project needs before it can start, and how to get it on
/// each operating system.
/// </summary>
/// <param name="Id">Stable name, used by tests and in the guide's JSON.</param>
/// <param name="Name">What it is called when it is missing.</param>
/// <param name="Why">One sentence on what it is for, for somebody who has never heard of it.</param>
/// <param name="Section">The heading in docs/plain/before-you-start.md that covers it.</param>
/// <param name="Commands">What to type, one line each, by operating system.</param>
/// <param name="Afterwards">What to do once the commands have run, or null.</param>
public sealed record Prerequisite(
    string Id,
    string Name,
    string Why,
    string Section,
    IReadOnlyDictionary<HostOperatingSystem, IReadOnlyList<string>> Commands,
    string? Afterwards = null)
{
    /// <summary>The commands for one operating system.</summary>
    public IReadOnlyList<string> CommandsFor(HostOperatingSystem os) =>
        Commands.TryGetValue(os, out var commands) ? commands : [];
}

/// <summary>
/// The one table of install commands, which the guide prints and the
/// documentation is checked against.
/// </summary>
/// <remarks>
/// <para>
/// Printed rather than run. Somebody new to all of this is better served by
/// seeing the command and typing it than by a launcher that installs things on
/// their machine, and Loadout does not install the tools it launches anywhere
/// else either.
/// </para>
/// <para>
/// One table because there are two readers: the guide, which prints the line
/// for this machine, and docs/plain/before-you-start.md, which shows all of
/// them. A test reads both, so the page cannot go on recommending a command the
/// guide has stopped printing. Package names do change, and this is the one
/// place to change them.
/// </para>
/// </remarks>
public static class Prerequisites
{
    /// <summary>
    /// Declared first: static initialisers run in order, and the identity entry
    /// below reads this.
    /// </summary>
    private static IReadOnlyList<string> Identity { get; } =
    [
        "git config --global user.name \"Your Name\"",
        "git config --global user.email \"you@example.com\"",
    ];

    /// <summary>The page the sections are on, relative to the repository root.</summary>
    public const string Page = "docs/plain/before-you-start.md";

    /// <summary>Where that page is published.</summary>
    public const string PageUrl =
        "https://github.com/ntatschner/loadout-cli/blob/main/docs/plain/before-you-start.md";

    /// <summary>Git, which keeps every version of the project's files.</summary>
    public static Prerequisite Git { get; } = new(
        "git",
        "Git",
        "Git keeps every version of your project's files, so a change you do not like can be undone.",
        "Git",
        new Dictionary<HostOperatingSystem, IReadOnlyList<string>>
        {
            [HostOperatingSystem.Windows] = ["winget install --id Git.Git -e --source winget"],
            [HostOperatingSystem.MacOS] = ["xcode-select --install"],
            [HostOperatingSystem.Linux] = ["sudo apt install git"],
        },
        "Close this terminal and open a new one, so it can find Git, then come back.");

    /// <summary>The name and email Git signs each saved version with.</summary>
    public static Prerequisite GitIdentity { get; } = new(
        "git-identity",
        "Your name in Git",
        "Git writes a name and an email on every version it saves. They stay on this computer "
        + "unless you publish the project.",
        "Your name in Git",
        new Dictionary<HostOperatingSystem, IReadOnlyList<string>>
        {
            [HostOperatingSystem.Windows] = Identity,
            [HostOperatingSystem.MacOS] = Identity,
            [HostOperatingSystem.Linux] = Identity,
        },
        "Put your own name and email between the quotes.");

    /// <summary>A coding agent, which is what does the planning and building.</summary>
    public static Prerequisite Agent { get; } = new(
        "agent",
        "A coding agent",
        "The agent is the AI that talks the project through with you and then writes it. "
        + "Loadout starts it; it does not come with Loadout.",
        "A coding agent",
        new Dictionary<HostOperatingSystem, IReadOnlyList<string>>
        {
            [HostOperatingSystem.Windows] = ["irm https://claude.ai/install.ps1 | iex"],
            [HostOperatingSystem.MacOS] = ["curl -fsSL https://claude.ai/install.sh | bash"],
            [HostOperatingSystem.Linux] = ["curl -fsSL https://claude.ai/install.sh | bash"],
        },
        "Then run claude once and sign in. It needs a Claude subscription or an API account.");

    /// <summary>Loadout's own setup, which gives projects somewhere to be recorded.</summary>
    public static Prerequisite Setup { get; } = new(
        "setup",
        "Loadout's setup",
        "Setup decides where Loadout keeps what it knows about your projects. Choosing to keep "
        + "everything on this computer is fine.",
        "Setting up Loadout",
        new Dictionary<HostOperatingSystem, IReadOnlyList<string>>
        {
            [HostOperatingSystem.Windows] = ["loadout setup"],
            [HostOperatingSystem.MacOS] = ["loadout setup"],
            [HostOperatingSystem.Linux] = ["loadout setup"],
        });

    /// <summary>Every prerequisite, in the order they are checked and documented.</summary>
    public static IReadOnlyList<Prerequisite> All { get; } = [Git, GitIdentity, Agent, Setup];
}
