namespace Loadout.Tui;

/// <summary>Which of the three choices in spec section 61 to take.</summary>
public enum WorkspaceMode
{
    /// <summary>Ask. Only valid when there is a terminal to ask in.</summary>
    Ask,

    /// <summary>Clone a workspace somebody has already made (spec section 62).</summary>
    UseExisting,

    /// <summary>Create the structure and a repository for it (spec section 63).</summary>
    CreateNew,

    /// <summary>Keep everything on this machine.</summary>
    LocalOnly,
}

/// <summary>Where a newly created workspace should be published.</summary>
public enum WorkspaceHost
{
    /// <summary>Ask.</summary>
    Ask,

    /// <summary>Create a private repository through the GitHub CLI.</summary>
    GitHub,

    /// <summary>Push to a URL the caller supplies.</summary>
    Url,

    /// <summary>Leave it local for now.</summary>
    None,
}

/// <summary>
/// Answers supplied up front, so setup can run without a terminal.
/// <para>
/// The wizard asks only for what it has not been told. That keeps one code path
/// for both cases: an interactive run is simply a request with nothing filled
/// in, so the scripted path cannot drift away from the one people actually see.
/// </para>
/// <para>
/// It exists because provisioning a new machine should not require somebody to
/// sit and answer prompts, and because a flow that can only be driven by hand
/// cannot be tested end to end.
/// </para>
/// </summary>
/// <param name="Mode">Which of the three choices to take.</param>
/// <param name="Host">Where a newly created workspace should live.</param>
/// <param name="Remote">Git URL, for an existing workspace or a supplied host.</param>
/// <param name="Branch">Workspace branch.</param>
/// <param name="Name">Workspace name, and the default repository name.</param>
/// <param name="RegisterDiscovered">Register every repository discovery finds.</param>
/// <param name="Migrate">Apply the migration plan rather than only showing it.</param>
/// <param name="IncludeIgnored">Include files Git already ignores in that plan.</param>
/// <param name="InstallGlobalExcludes">Configure the global Git exclude file.</param>
/// <param name="Interactive">Whether the wizard may prompt for anything left unanswered.</param>
public sealed record SetupRequest(
    WorkspaceMode Mode = WorkspaceMode.Ask,
    WorkspaceHost Host = WorkspaceHost.Ask,
    string? Remote = null,
    string? Branch = null,
    string? Name = null,
    bool RegisterDiscovered = false,
    bool Migrate = false,
    bool IncludeIgnored = false,
    bool? InstallGlobalExcludes = null,
    bool Interactive = true)
{
    /// <summary>A fully interactive run, which is what a person gets.</summary>
    public static SetupRequest Interactive_() => new();

    /// <summary>
    /// Whether every question this run will reach has already been answered.
    /// Used to refuse early rather than failing halfway through a setup.
    /// </summary>
    public string? MissingAnswer() => Mode switch
    {
        WorkspaceMode.Ask when !Interactive =>
            "Choose a mode: --use-existing, --create-new or --local-only.",

        WorkspaceMode.UseExisting when string.IsNullOrWhiteSpace(Remote) =>
            "--use-existing needs --remote <url>.",

        WorkspaceMode.CreateNew when !Interactive && Host == WorkspaceHost.Ask =>
            "--create-new needs --github, --remote <url>, or --stay-local.",

        WorkspaceMode.CreateNew when Host == WorkspaceHost.Url
            && string.IsNullOrWhiteSpace(Remote) =>
            "--remote <url> is required when publishing to a supplied URL.",

        _ => null,
    };
}
