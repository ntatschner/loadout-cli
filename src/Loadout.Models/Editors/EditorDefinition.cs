namespace Loadout.Models.Editors;

/// <summary>
/// How an editor is started, and how it is told which profile to open under.
/// <para>
/// The counterpart to <see cref="Agents.GenericAgentDefinition"/>: an editor is
/// a command, an argument template and a set of environment variables, so
/// adopting one does not mean waiting for a release. Placeholders are expanded
/// at launch: <c>${DIRECTORY}</c> for the folder being opened and
/// <c>${PROFILE}</c> for the profile chosen for it.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Profiles are the whole reason this exists. Naming a different command was
/// already possible, so a declaration that could only do that would earn
/// nothing; what it could not express is how an editor is told which profile to
/// use, and that differs in kind rather than in spelling. VS Code takes an
/// argument, Neovim takes an environment variable, and an editor with no
/// profile mechanism at all takes neither and should never be told one was
/// ignored.
/// </para>
/// <para>
/// Settings only, and nothing worked out from them. Every <c>config set</c>
/// rewrites this file, and the serialiser emits whatever it can read — so a
/// derived property here becomes a line in somebody's config.yaml that looks
/// like a setting, invites being edited, and does nothing when it is. Whether
/// an editor can be told a profile is therefore asked of
/// <c>EditorState</c>, which is not written anywhere.
/// </para>
/// </remarks>
public sealed class EditorDefinition
{
    /// <summary>Display name. Defaults to the configuration key when unset.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Executable resolved on PATH, or an absolute path.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Arguments, with placeholders expanded.</summary>
    public List<string> Arguments { get; set; } = [];

    /// <summary>Environment variables set on the editor only.</summary>
    public Dictionary<string, string> Environment { get; set; } = [];

    /// <summary>
    /// Arguments added only when a profile was chosen, such as
    /// <c>--profile ${PROFILE}</c>.
    /// </summary>
    /// <remarks>
    /// Empty for the VS Code family on purpose, and not because nobody has got
    /// round to it. Asked for a folder and a profile together the editor opens
    /// a window containing neither and says nothing about why, so the profile
    /// is deliberately left off. <c>EditorService.OpenAsync</c> carries the
    /// bisect that established it.
    /// </remarks>
    public List<string> ProfileArguments { get; set; } = [];

    /// <summary>
    /// Environment variable carrying the profile name, such as Neovim's
    /// <c>NVIM_APPNAME</c>. Set on the editor only, and only when a profile
    /// was chosen.
    /// </summary>
    public string? ProfileEnvironment { get; set; }

    /// <summary>
    /// Why a profile cannot be applied, for an editor that has profiles but no
    /// way of being told one at launch. Null where there is nothing to explain.
    /// </summary>
    /// <remarks>
    /// Written to follow the editor's name, so it reads as a sentence wherever
    /// it is printed: "code will not open a folder and a profile together".
    /// The explanation belongs with the knowledge that makes it true, rather
    /// than in the command that prints it. Without it the only honest thing a
    /// caller can say is that the profile was not used, which invites exactly
    /// the investigation this sentence exists to save.
    /// </remarks>
    public string? ProfileNote { get; set; }

    /// <summary>Variables beginning with these are withheld from the editor.</summary>
    public List<string> RemoveEnvironmentPrefixes { get; set; } = [];

    /// <summary>
    /// Whether the editor runs in the terminal it was started from rather than
    /// opening a window of its own.
    /// </summary>
    /// <remarks>
    /// A terminal editor started detached is a process with nowhere to draw. It
    /// has to inherit the real terminal, which also means the launcher waits
    /// for it, where a windowed editor outlives the launcher and is not waited
    /// for at all.
    /// </remarks>
    public bool Terminal { get; set; }

}
