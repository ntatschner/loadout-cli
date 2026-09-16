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
        bool IsFlag = false,
        string? WhenUnset = null);

    /// <summary>
    /// What a setting does when nobody has set it, for the settings where that
    /// is a behaviour rather than an absence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An empty value is shown as "(unset)" everywhere, and that word covers
    /// two different situations. <c>updates-source</c> unset means the launcher
    /// follows this project's own releases, which is something happening;
    /// <c>agent-search-paths</c> unset means the usual places are searched. Both
    /// read as though nothing is configured and nothing is going on.
    /// </para>
    /// <para>
    /// Named here rather than worked out from the reader, because the behaviour
    /// lives in whatever consumes the setting and cannot be recovered from a
    /// null. A setting where unset genuinely means "off" leaves this alone, and
    /// "(unset)" is then the whole truth.
    /// </para>
    /// </remarks>
    public static string? Unset(string key) =>
        All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.Ordinal))?.WhenUnset;

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
        public const string Secrets = "Secrets";
        public const string Updates = "Updates";
        public const string Statusline = "Agent status line";
        public const string Instructions = "Agent instructions";
        public const string Telemetry = "Usage reporting";
        public const string Machine = "This machine";
        public const string Accessibility = "Accessibility";

        // Three sections rather than one. Twenty-seven settings under a single
        // heading do not fit a page at eighty by twenty-four, which is the
        // size this application is meant to work at, and a setting nobody can
        // scroll to is a setting nobody can change. They divide the way the
        // settings themselves do: how a question arrives, what the prose looks
        // like, and what gets drawn.
        public const string Writing = "Writing";
        public const string Display = "Display";

        /// <summary>In the order a screen should show them.</summary>
        public static IReadOnlyList<string> InOrder =>
        [
            Workspace, Agents, Editor, Syncing, Secrets, Updates,
            Statusline, Instructions, Accessibility, Writing, Display,
            Telemetry, Machine, General,
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

        // The onboarding defaults, so the answers can be changed after the
        // first project rather than only before it. One key per setting except
        // the per-mode models, which are a set for the same reason the editor
        // profiles are: the modes are not fixed, and a key list that has to be
        // regenerated when somebody adds one is a key list that will be wrong.
        new("onboarding-agent", "Agent a newly registered project launches",
            (c, _) => c.Onboarding.Agent,
            (c, _, v) => c.Onboarding.Agent = v, false,
            Group: Groups.Agents,
            WhenUnset: "new projects take default-agent"),

        new("onboarding-model", "Model a newly registered project pins",
            (c, _) => c.Onboarding.Model,
            (c, _, v) => c.Onboarding.Model = v, false,
            Group: Groups.Agents,
            WhenUnset: "new projects pin no model, so the agent picks"),

        new("onboarding-models", "Model per mode for a new project, as review=small;implement=big",
            (c, _) => FormatProfiles(c.Onboarding.ModelByMode),
            (c, _, v) => WriteProfiles(c.Onboarding.ModelByMode, v), false,
            Sample: "review=small-model;implement=big-model",
            Group: Groups.Agents,
            WhenUnset: "every mode uses onboarding-model"),

        new("onboarding-editor", "Editor profile a newly registered project opens under",
            (c, _) => c.Onboarding.EditorProfile,
            (c, _, v) => c.Onboarding.EditorProfile = v, false,
            Group: Groups.Editor,
            WhenUnset: "new projects open in the editor's default profile"),

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

        new("updates-source",
            "Release feed URL. Empty means this project's own releases; 'off' means never check",
            (c, _) => c.Updates.Source,
            (c, _, v) => c.Updates.Source = v, false,
            Group: Groups.Updates,
            WhenUnset: "follows this project's own releases"),

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
            (_, m, v) => m.DiscoveryRoots = SplitDirectories(v),
            true,
            Group: Groups.Machine),

        new("agent-search-paths", "Comma-separated extra directories searched for agent executables",
            (c, _) => string.Join(", ", c.AgentSearchPaths),
            (c, _, v) => c.AgentSearchPaths = SplitDirectories(v),
            false,
            Group: Groups.Agents,
            WhenUnset: "agents are looked for on PATH and their usual install directories"),

        // How this person wants to be written to and asked. Every one of these
        // is settable on its own, because a preset is a starting point rather
        // than a mode: somebody can take the dyslexia bundle and still ask for
        // the field's own vocabulary, and a bundle nobody can adjust is one
        // people abandon whole.
        //
        // Named ask-, write- and show- rather than accessibility-something,
        // because the settings screen draws a key in a column twenty-four
        // characters wide and every one of these was wider than that. The
        // three words are what the settings actually divide into: how a
        // question arrives, what the prose looks like, and what gets drawn.
        new("accessibility-preset", "screen-reader, low-vision, colour-blind, dyslexia, adhd, plain-language or none",
            (c, _) => c.Accessibility.Preset,
            (c, _, v) => c.Accessibility.Preset = OneOf(v, [.. AccessibilityPresets.All]), false,
            Sample: AccessibilityPresets.None,
            Group: Groups.Accessibility,
            WhenUnset: "nothing is changed about how an agent writes to you"),

        new("ask-style", "choices, written or mixed",
            (c, _) => c.Accessibility.Questions.Style,
            (c, _, v) => c.Accessibility.Questions.Style = OneOf(v, "choices", "written", "mixed"), false,
            Sample: "choices",
            Group: Groups.Accessibility),

        new("ask-one-at-a-time", "Ask one question per message and wait",
            (c, _) => Boolean(c.Accessibility.Questions.OneAtATime),
            (c, _, v) => c.Accessibility.Questions.OneAtATime = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        new("ask-why", "Say in one sentence why a question is being asked",
            (c, _) => Boolean(c.Accessibility.Questions.Why),
            (c, _, v) => c.Accessibility.Questions.Why = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        new("ask-explain", "on-request, always or never: how much a question is unpacked",
            (c, _) => c.Accessibility.Questions.Explain,
            (c, _, v) => c.Accessibility.Questions.Explain = OneOf(v, "on-request", "always", "never"), false,
            Sample: "on-request",
            Group: Groups.Accessibility),

        new("ask-recommend", "Label the option the agent would choose, with its downside",
            (c, _) => Boolean(c.Accessibility.Questions.Recommend),
            (c, _, v) => c.Accessibility.Questions.Recommend = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        new("ask-unsure", "Offer \"I don\'t know\" as a real answer",
            (c, _) => Boolean(c.Accessibility.Questions.UnsureOption),
            (c, _, v) => c.Accessibility.Questions.UnsureOption = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        new("ask-progress", "Say \"question 2 of 4\" when more than one is coming",
            (c, _) => Boolean(c.Accessibility.Questions.Progress),
            (c, _, v) => c.Accessibility.Questions.Progress = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        new("write-verbosity", "concise, standard or full",
            (c, _) => c.Accessibility.Output.Verbosity,
            (c, _, v) => c.Accessibility.Output.Verbosity = OneOf(v, "concise", "standard", "full"), false,
            Sample: "standard",
            Group: Groups.Writing),

        new("write-technicality", "plain, mixed or technical",
            (c, _) => c.Accessibility.Output.Technicality,
            (c, _, v) => c.Accessibility.Output.Technicality = OneOf(v, "plain", "mixed", "technical"), false,
            Sample: "mixed",
            Group: Groups.Writing),

        new("write-summary-first", "Start anything longer than a screen with a summary",
            (c, _) => Boolean(c.Accessibility.Output.SummaryFirst),
            (c, _, v) => c.Accessibility.Output.SummaryFirst = Flag(v), false,
            Group: Groups.Writing,
            IsFlag: true),

        new("write-sentence-words", "Split a sentence longer than this many words",
            (c, _) => c.Accessibility.Output.Sentences.ToString(CultureInfo.InvariantCulture),
            (c, _, v) => c.Accessibility.Output.Sentences = Count(v), false,
            Sample: "25",
            Group: Groups.Writing),

        new("write-paragraph", "At most this many sentences in a paragraph",
            (c, _) => c.Accessibility.Output.Paragraph.ToString(CultureInfo.InvariantCulture),
            (c, _, v) => c.Accessibility.Output.Paragraph = Count(v), false,
            Sample: "5",
            Group: Groups.Writing),

        new("write-steps", "numbered or prose: how instructions are given",
            (c, _) => c.Accessibility.Output.Steps,
            (c, _, v) => c.Accessibility.Output.Steps = OneOf(v, "numbered", "prose"), false,
            Sample: "numbered",
            Group: Groups.Writing),

        new("write-emphasis", "bold-only or any",
            (c, _) => c.Accessibility.Output.Emphasis,
            (c, _, v) => c.Accessibility.Output.Emphasis = OneOf(v, "bold-only", "any"), false,
            Sample: "bold-only",
            Group: Groups.Writing),

        new("write-same-word", "One word for one thing, abbreviations expanded first time",
            (c, _) => Boolean(c.Accessibility.Output.SameWord),
            (c, _, v) => c.Accessibility.Output.SameWord = Flag(v), false,
            Group: Groups.Writing,
            IsFlag: true),

        new("ask-before-risky", "Restate what will happen before anything that cannot be undone",
            (c, _) => Boolean(c.Accessibility.Output.ConfirmBeforeIrreversible),
            (c, _, v) => c.Accessibility.Output.ConfirmBeforeIrreversible = Flag(v), false,
            Group: Groups.Accessibility,
            IsFlag: true),

        // Offered with its evidence: every controlled study of this finds no
        // reading gain and one finds it slower. It is here because people
        // preferred styled text even where it did not help them, and a
        // preference somebody can switch off is a fair thing to offer.
        new("write-bionic", "Bold the first half of each word in the agent\'s prose. No study finds this helps reading",
            (c, _) => Boolean(c.Accessibility.Output.Bionic),
            (c, _, v) => c.Accessibility.Output.Bionic = Flag(v), false,
            Group: Groups.Writing,
            IsFlag: true),

        new("show-colour", "full, sixteen or none",
            (c, _) => c.Accessibility.Display.Colour,
            (c, _, v) => c.Accessibility.Display.Colour = OneOf(v, "full", "sixteen", "none"), false,
            Sample: "full",
            Group: Groups.Display),

        new("show-colour-safe", "Avoid red and green as a pair",
            (c, _) => Boolean(c.Accessibility.Display.ColourSafe),
            (c, _, v) => c.Accessibility.Display.ColourSafe = Flag(v), false,
            Group: Groups.Display,
            IsFlag: true),

        new("show-glyphs", "unicode or ascii",
            (c, _) => c.Accessibility.Display.Glyphs,
            (c, _, v) => c.Accessibility.Display.Glyphs = OneOf(v, "unicode", "ascii"), false,
            Sample: "unicode",
            Group: Groups.Display),

        new("show-motion", "full, reduced or none",
            (c, _) => c.Accessibility.Display.Motion,
            (c, _, v) => c.Accessibility.Display.Motion = OneOf(v, "full", "reduced", "none"), false,
            Sample: "full",
            Group: Groups.Display),

        new("show-redraw", "allowed or never: spinners, timers and in-place edits",
            (c, _) => c.Accessibility.Display.Redraw,
            (c, _, v) => c.Accessibility.Display.Redraw = OneOf(v, "allowed", "never"), false,
            Sample: "allowed",
            Group: Groups.Display),

        new("show-tables", "tables or lists",
            (c, _) => c.Accessibility.Display.Tables,
            (c, _, v) => c.Accessibility.Display.Tables = OneOf(v, "tables", "lists"), false,
            Sample: "tables",
            Group: Groups.Display),

        new("show-menus", "arrows or numbered",
            (c, _) => c.Accessibility.Display.Menus,
            (c, _, v) => c.Accessibility.Display.Menus = OneOf(v, "arrows", "numbered"), false,
            Sample: "arrows",
            Group: Groups.Display),

        new("show-launcher", "full or text: no terminal toolkit can announce a full-screen one",
            (c, _) => c.Accessibility.Display.Launcher,
            (c, _, v) => c.Accessibility.Display.Launcher = OneOf(v, "full", "text"), false,
            Sample: "full",
            Group: Groups.Display),

        new("show-bell", "Ring the terminal bell when input is needed",
            (c, _) => Boolean(c.Accessibility.Display.Bell),
            (c, _, v) => c.Accessibility.Display.Bell = Flag(v), false,
            Group: Groups.Display,
            IsFlag: true),
    ];

    /// <summary>
    /// Splits a list of directories on either separator somebody might type.
    /// </summary>
    /// <remarks>
    /// A comma is what the help says. A semicolon is what separates paths in
    /// <c>PATH</c> on Windows, so it is what gets typed anyway — and taken as
    /// part of a path it made one root that could not exist. Discovery then
    /// reported no repositories at all, including the root that had been
    /// working before the second one was added, and said only "no repositories
    /// found": a true sentence about a machine full of code.
    /// <para>
    /// Safe on both separators because neither is legal in a Windows path, and
    /// a colon is deliberately not among them: <c>C:\git</c> would split into
    /// two roots that are each nonsense.
    /// </para>
    /// </remarks>
    private static List<string> SplitDirectories(string value) =>
        [.. value.Split(
            [',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Takes one of a fixed set of words, or says which words there are.
    /// </summary>
    /// <remarks>
    /// Refused rather than ignored. A setting whose value is a word from a
    /// list will be typed wrong eventually, and quietly falling back to the
    /// default leaves a person who asked for plain language reading jargon
    /// and no way to find out why.
    /// </remarks>
    private static string OneOf(string value, params string[] allowed)
    {
        var trimmed = value.Trim().ToLowerInvariant();

        return Array.Exists(allowed, a => string.Equals(a, trimmed, StringComparison.Ordinal))
            ? trimmed
            : throw new FormatException($"'{value.Trim()}' is not one of: {string.Join(", ", allowed)}.");
    }

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
    /// <remarks>
    /// Public because the project switches in <c>project context</c> accept the
    /// same words, and two vocabularies for yes would drift the moment one of
    /// them learned a new spelling.
    /// </remarks>
    public static bool Flag(string value) =>
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
