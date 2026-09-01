using System.Globalization;
using Loadout.Models.Configuration;

namespace Loadout.Core.Configuration;

/// <summary>
/// The settings <c>loadout config</c> can read and write (spec section 77).
/// <para>
/// A registry rather than a switch in each command, so list, get and set can
/// never disagree about which keys exist. Keys are hyphenated because that is
/// what people type; the YAML underneath keeps its own naming.
/// </para>
/// </summary>
public static class ConfigKeys
{
    /// <summary>
    /// One setting. <c>Sample</c> is a valid value, needed only for settings
    /// whose value has a shape rather than being free text: a test infers a
    /// sample from the current value, which keeps a new setting covered the
    /// moment it is added, and that inference cannot work for a setting that
    /// parses what it is given.
    /// </summary>
    public sealed record Entry(
        string Key,
        string Description,
        Func<LauncherConfig, MachineConfig, string?> Read,
        Action<LauncherConfig, MachineConfig, string> Write,
        bool IsMachineLocal,
        string? Sample = null,
        string Group = Groups.General,
        bool IsFlag = false);

    /// <summary>
    /// <c>IsFlag</c> marks a setting whose whole vocabulary is yes and no, so
    /// a screen can offer a tick rather than a box to type <c>true</c> into.
    /// The value is still text, because that is what the command line passes
    /// and what these setters have always taken.
    /// </summary>

    /// <summary>
    /// What a setting is about, so a screen can arrange them into something
    /// readable rather than listing twenty-one fields in declaration order.
    /// </summary>
    /// <remarks>
    /// Named here rather than worked out from the key's prefix. Three of them
    /// share no prefix with anything (<c>terminal</c>, <c>clone-root</c>,
    /// <c>discovery-roots</c>), and a rule with three exceptions is not a rule.
    /// </remarks>
    public static class Groups
    {
        public const string General = "General";
        public const string Workspace = "Workspace";
        public const string Agents = "Agents";
        public const string Editor = "Editor";
        public const string Syncing = "Syncing";
        public const string Terminal = "Terminal";
        public const string Secrets = "Secrets";
        public const string Updates = "Updates";
        public const string Statusline = "Agent status line";
        public const string Instructions = "Agent instructions";
        public const string Telemetry = "Usage reporting";
        public const string Machine = "This machine";

        /// <summary>In the order a screen should show them.</summary>
        public static IReadOnlyList<string> InOrder =>
        [
            Workspace, Agents, Editor, Syncing, Terminal, Secrets, Updates,
            Statusline, Instructions, Telemetry, Machine, General,
        ];
    }

    /// <summary>Renders the agent-to-profile map as one settable string.</summary>
    private static string? FormatProfiles(Dictionary<string, string> profiles) =>
        profiles.Count == 0
            ? null
            : string.Join(";", profiles.Select(pair => $"{pair.Key}={pair.Value}"));

    /// <summary>
    /// Replaces the map from "claude=Agents;codex=Codex". Replaces rather than
    /// merges, so that removing an entry is possible at all: with a merge the
    /// only way to unset one would be to edit the YAML by hand.
    /// </summary>
    private static void WriteProfiles(Dictionary<string, string> profiles, string value)
    {
        profiles.Clear();

        foreach (var pair in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = pair.Split('=', 2, StringSplitOptions.TrimEntries);

            if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            {
                throw new FormatException(
                    $"'{pair}' is not an agent and a profile. Write them as claude=Agents, "
                    + "separated by semicolons.");
            }

            profiles[parts[0]] = parts[1];
        }
    }

    public static IReadOnlyList<Entry> All =>
    [
        new("workspace-remote", "Git URL of the central workspace",
            (c, _) => c.Workspace.Remote,
            (c, _, v) => c.Workspace.Remote = v, false,
            Group: Groups.Workspace),

        new("workspace-branch", "Branch of the central workspace",
            (c, _) => c.Workspace.Branch,
            (c, _, v) => c.Workspace.Branch = v, false,
            Group: Groups.Workspace),

        new("default-agent", "Agent launched when a project names none",
            (c, _) => c.DefaultAgent,
            (c, _, v) => c.DefaultAgent = v, false,
            Group: Groups.Agents),

        new("editor-command", "Editor opened by 'loadout code': code, code-insiders, codium, cursor",
            (c, _) => c.Editor.Command,
            (c, _, v) => c.Editor.Command = v, false,
            Group: Groups.Editor),

        // One key rather than one per agent, because the set of agents is not
        // fixed and a key list that has to be regenerated when somebody adds a
        // custom agent is a key list that will be wrong.
        new("editor-profiles", "Editor profile per agent, as claude=Agents;codex=Codex",
            (c, _) => FormatProfiles(c.Editor.Profiles),
            (c, _, v) => WriteProfiles(c.Editor.Profiles, v), false,
            Sample: "claude=Agents;codex=Codex",
            Group: Groups.Editor),

        new("sync-launch", "Sync policy at launch: auto, prompt or never",
            (c, _) => c.Sync.Launch,
            (c, _, v) => c.Sync.Launch = v, false,
            Group: Groups.Syncing),

        new("sync-exit", "Sync policy at exit: prompt, always or never",
            (c, _) => c.Sync.Exit,
            (c, _, v) => c.Sync.Exit = v, false,
            Group: Groups.Syncing),

        new("sync-timeout", "Seconds a launch-time fetch may block before going offline",
            (c, _) => c.Sync.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture),
            (c, _, v) => c.Sync.NetworkTimeoutSeconds = int.Parse(v, CultureInfo.InvariantCulture),
            false,
            Group: Groups.Syncing),

        new("secrets-provider", "native, environment, 1password, bitwarden, vault or custom",
            (c, _) => c.Secrets.Provider,
            (c, _, v) => c.Secrets.Provider = v, false,
            Group: Groups.Secrets),

        // Nothing reads this yet. ITerminalProvider is implemented for all
        // three platforms, registered for injection, and injected nowhere —
        // so an agent always launches in the terminal the launcher was
        // started from, whatever this says. Said in the description because
        // that is the one place every surface shows: config list, config get,
        // and the hint under the field on the settings screen, which is where
        // it became a problem. A setting that can be changed and does nothing
        // is worse the more prominent it is.
        new("terminal",
            "Preferred terminal. Not yet honoured: agents launch in the current one",
            (c, _) => c.Terminal.Preferred,
            (c, _, v) => c.Terminal.Preferred = v, false,
            Group: Groups.Terminal),

        new("updates-source", "Release feed URL",
            (c, _) => c.Updates.Source,
            (c, _, v) => c.Updates.Source = v, false,
            Group: Groups.Updates),

        new("agent-tools", "Serve the launcher's own tools to the agent it starts",
            (c, _) => Boolean(c.AgentTools.Enabled),
            (c, _, v) => c.AgentTools.Enabled = Flag(v), false,
            Group: Groups.Agents,
            IsFlag: true),

        new("statusline-project", "Show the project slug in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowProject),
            (c, _, v) => c.Statusline.ShowProject = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-directory", "Show the working directory in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowDirectory),
            (c, _, v) => c.Statusline.ShowDirectory = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-git", "Show the branch and whether the tree is dirty",
            (c, _) => Boolean(c.Statusline.ShowGit),
            (c, _, v) => c.Statusline.ShowGit = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-model", "Show the model name in the agent status line",
            (c, _) => Boolean(c.Statusline.ShowModel),
            (c, _, v) => c.Statusline.ShowModel = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-context", "Show how much of the context window is spent",
            (c, _) => Boolean(c.Statusline.ShowContext),
            (c, _, v) => c.Statusline.ShowContext = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-colour", "Colour the agent status line with ANSI escapes",
            (c, _) => Boolean(c.Statusline.Colour),
            (c, _, v) => c.Statusline.Colour = Flag(v), false,
            Group: Groups.Statusline,
            IsFlag: true),

        new("statusline-separator", "Text drawn between status line segments",
            (c, _) => c.Statusline.Separator,
            (c, _, v) => c.Statusline.Separator = v, false,
            Group: Groups.Statusline),

        new("specialists", "Give launched agents specialist guidance chosen for the task",
            (c, _) => Boolean(c.InstructionContext.Specialists),
            (c, _, v) => c.InstructionContext.Specialists = Flag(v), false,
            Group: Groups.Instructions,
            IsFlag: true),

        new("instruction-max-tokens", "Ceiling on specialist guidance, in estimated tokens. 0 removes it",
            (c, _) => c.InstructionContext.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (c, _, v) => c.InstructionContext.MaxTokens = Count(v), false,
            Group: Groups.Instructions),

        new("instruction-warn-percent", "Share of the instruction budget worth warning about",
            (c, _) => c.InstructionContext.WarnAtPercent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            (c, _, v) => c.InstructionContext.WarnAtPercent = Count(v), false,
            Group: Groups.Instructions),

        new("telemetry", "Tell launched agents to report token usage to this machine",
            (c, _) => Boolean(c.Telemetry.Enabled),
            (c, _, v) => c.Telemetry.Enabled = Flag(v), false,
            Group: Groups.Telemetry,
            IsFlag: true),

        new("telemetry-endpoint", "Where they report it. Must be an address on this machine",
            (c, _) => c.Telemetry.Endpoint,
            (c, _, v) => c.Telemetry.Endpoint = v, false,
            Group: Groups.Telemetry),

        // Machine-local from here down: these describe this machine's layout and
        // must never travel to another one (spec section 15).
        new("clone-root", "Where new clones are placed on this machine",
            (_, m) => m.DefaultCloneRoot,
            (_, m, v) => m.DefaultCloneRoot = v, true,
            Group: Groups.Machine),

        new("discovery-roots", "Comma-separated directories scanned for repositories",
            (_, m) => string.Join(", ", m.DiscoveryRoots),
            (_, m, v) => m.DiscoveryRoots = v
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            true,
            Group: Groups.Machine),

        new("agent-search-paths", "Comma-separated extra directories searched for agent executables",
            (c, _) => string.Join(", ", c.AgentSearchPaths),
            (c, _, v) => c.AgentSearchPaths = v
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList(),
            false,
            Group: Groups.Agents),
    ];

    /// <summary>How a flag is shown, in the spelling the setter accepts back.</summary>
    private static string Boolean(bool value) => value ? "true" : "false";

    /// <summary>
    /// Reads a whole number that cannot sensibly be negative.
    /// </summary>
    /// <remarks>
    /// Refused rather than clamped. A negative budget silently becoming zero
    /// would turn a typo into "no ceiling at all", which is the opposite of
    /// what somebody typing a budget wanted.
    /// </remarks>
    private static int Count(string value) =>
        int.TryParse(
            value.Trim(),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed) && parsed >= 0
            ? parsed
            : throw new FormatException($"'{value}' is not a whole number of zero or more.");

    /// <summary>
    /// Reads a flag generously. Somebody turning a segment off will type
    /// whichever of these came to mind, and refusing all but one spelling
    /// would be pedantry rather than validation.
    /// </summary>
    private static bool Flag(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "true" or "yes" or "on" or "1" => true,
            "false" or "no" or "off" or "0" => false,
            _ => throw new FormatException(
                $"'{value}' is not a yes or no. Use true or false."),
        };

    public static Entry? Find(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
