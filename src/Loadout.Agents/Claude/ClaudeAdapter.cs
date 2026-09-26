using Loadout.Models.Agents;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Claude;

/// <summary>
/// Launches Claude Code with configuration supplied from outside the
/// application repository (spec section 31).
/// <para>
/// The spec's example invocation is explicitly marked conceptual, and it warns
/// that an external settings directory does not necessarily behave like a
/// repository-local one. So every option here is passed only when the installed
/// binary's own help text advertises it (spec section 66). On a build that does
/// not, the launcher falls back rather than passing a flag that would be
/// rejected.
/// </para>
/// </summary>
public sealed class ClaudeAdapter : AgentAdapterBase
{
    /// <summary>
    /// Ceiling on a system prompt passed as a command-line argument.
    /// <para>
    /// Windows caps a command line at roughly 32,000 characters, and exceeding
    /// it fails in a way that looks like the agent crashing. This limit is
    /// conservative enough to leave room for the rest of the arguments, and
    /// only applies when the installed build lacks the file-based flag.
    /// </para>
    /// </summary>
    private const int MaximumInlinePromptLength = 24_000;

    /// <summary>Capability key for the file-based system prompt option.</summary>
    private const string SystemPromptFile = "external_prompt_file";

    /// <summary>How the screened settings copy is written: readable, since somebody may open it to see what was dropped.</summary>
    private static readonly System.Text.Json.JsonSerializerOptions SettingsLayout = new() { WriteIndented = true };

    public ClaudeAdapter(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IReadOnlyList<string> configuredSearchPaths)
        : base(resolver, processes, configuredSearchPaths)
    {
    }

    /// <inheritdoc />
    public override string Name => "claude";

    /// <inheritdoc />
    public override string DisplayName => "Claude Code";

    /// <inheritdoc />
    protected override string ExecutableName => "claude";

    /// <inheritdoc />
    public override IHeadlessProtocol HeadlessProtocol => ClaudeHeadlessProtocol.Instance;

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string[]> CapabilityMarkers =>
        new Dictionary<string, string[]>
        {
            [AgentCapabilities.ExternalSettings] = ["--settings"],
            [AgentCapabilities.ExternalPrompt] = ["--append-system-prompt"],

            // Looked for as a distinct capability because the two spellings
            // behave very differently: the file form has no length limit, the
            // inline form is bounded by the operating system's command line.
            //
            // Both spellings of the help are matched. Claude Code 2.1 documents
            // this flag only inside another option's description, and only as
            // "--append-system-prompt[-file]" — the brackets meaning the suffix
            // is optional. There is no entry of its own to find, so looking for
            // the plain form found nothing and the capability was recorded as
            // absent on a build that has it.
            //
            // What followed was silent and total: the launcher fell back to
            // passing the context inline, a 39KB context exceeded what a
            // command line takes, and it attached nothing at all. The agent was
            // started with no instructions, no specialists and no memory index,
            // and only a warning said so.
            [SystemPromptFile] = ["--append-system-prompt-file", "--append-system-prompt[-file]"],

            [AgentCapabilities.AdditionalDirectories] = ["--add-dir"],
            [AgentCapabilities.ProjectSkills] = ["--plugin-dir"],
            [AgentCapabilities.McpConfig] = ["--mcp-config"],
            [AgentCapabilities.SessionResume] = ["--resume", "--continue"],
            [PermissionMode] = ["--permission-mode"],
            [ToolRestrictions] = ["--allowed-tools", "--disallowed-tools"],
            [ModelSelection] = ["--model"],

            // Both streams have to be switchable for a node: messages in as
            // JSON lines and events out as JSON lines. A build with only one
            // of the two would take a prompt and answer in prose.
            [AgentCapabilities.Headless] = ["--input-format", "--output-format"],

            // Documented only inside --permission-prompts' description on
            // 2.1.270, never as an entry of its own, so the marker is the bare
            // flag name and matches wherever the help mentions it. Proven to
            // work on that build by driving a session with it.
            [PermissionAnswerer] = ["--permission-prompt-tool"],
            [OutputSchema] = ["--json-schema"],

            // Claude Code's own no-redraw mode. Detected rather than assumed,
            // because a build that does not have it must say so: a person who
            // set a screen-reader profile and silently got animations back
            // would have no way to tell.
            [ScreenReaderMode] = ["--ax-screen-reader"],
            [StrictMcp] = ["--strict-mcp-config"],

            // The positional prompt, as the usage line spells it:
            // "claude [options] [command] [prompt]".
            [OpeningPrompt] = ["[prompt]"],
        };

    /// <summary>Capability key for starting an interactive session with a first message.</summary>
    private const string OpeningPrompt = "opening_prompt";

    /// <summary>Capability key for routing permission prompts to a tool.</summary>
    private const string PermissionAnswerer = "permission_answerer";

    /// <summary>Capability key for constraining the final answer to a schema.</summary>
    private const string OutputSchema = "output_schema";

    /// <summary>Capability key for the agent's own mode for a screen reader.</summary>
    private const string ScreenReaderMode = "screen_reader";

    /// <summary>Capability key for connecting only the MCP servers the launcher names.</summary>
    private const string StrictMcp = "strict_mcp";

    /// <summary>Capability key for the permission-mode option.</summary>
    private const string PermissionMode = "permission_mode";

    /// <summary>Capability key for the tool allow and deny options.</summary>
    private const string ToolRestrictions = "tool_restrictions";

    /// <summary>Capability key for choosing the model.</summary>
    private const string ModelSelection = "model_selection";

    /// <inheritdoc />
    public override async Task<OperationResult<AgentInvocation>> BuildInvocationAsync(
        AgentLaunchContext context,
        CancellationToken ct = default)
    {
        var descriptor = await DetectAsync(ct).ConfigureAwait(false);

        if (!descriptor.IsInstalled || descriptor.ExecutablePath is null)
        {
            return OperationResult<AgentInvocation>.Fail(
                $"{DisplayName} is not installed.", Models.ExitCode.AgentUnavailable);
        }

        var arguments = new List<string>();
        var warnings = new List<string>();

        AddResume(context, descriptor, arguments, warnings);
        AddMcpServers(context, descriptor, arguments, warnings);
        await AddSettingsAsync(context, descriptor, arguments, warnings, ct).ConfigureAwait(false);
        await AddCompiledContextAsync(context, descriptor, arguments, warnings, ct).ConfigureAwait(false);
        AddWorkspaceDirectory(context, descriptor, arguments);
        AddReachable(arguments, context, descriptor);
        AddProjectSkills(context, descriptor, arguments, warnings);
        AddSecurityProfile(context, descriptor, arguments, warnings);
        AddModel(context, descriptor, arguments, warnings);
        AddHeadless(context, descriptor, arguments, warnings);
        AddReading(context, descriptor, arguments, warnings);

        // Everything after a bare -- belongs to the agent untouched
        // (spec section 36), so it is appended last and never inspected.
        arguments.AddRange(context.PassthroughArguments);

        // After the passthrough, so it is the last positional argument.
        AddOpeningPrompt(context, descriptor, OpeningPrompt, arguments, warnings);

        var environment = context.ResolvedEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(context.ResolvedEnvironment);

        return OperationResult<AgentInvocation>.Ok(
            new AgentInvocation(
                descriptor.ExecutablePath,
                arguments,
                environment,
                warnings,
                context.Headless is null ? null : SessionMarkers));
    }

    /// <summary>
    /// Variables a running Claude Code session sets for its own children,
    /// which a node must not inherit.
    /// </summary>
    /// <remarks>
    /// Named one by one rather than by the <c>CLAUDE_CODE_</c> prefix, because
    /// the prefix also covers things a person may rely on: an OAuth token, a
    /// provider switch, the configuration directory. These are the ones a
    /// session started from inside another session was seen to inherit, and a
    /// child carrying them loses its own transcript and answers to the
    /// wrong session. A prefix matches its own name exactly and any longer
    /// name, so each entry below is written in full.
    /// </remarks>
    private static readonly IReadOnlyList<string> SessionMarkers =
    [
        "CLAUDECODE",
        "CLAUDE_PID",
        "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_CHILD_SESSION",
        "CLAUDE_CODE_ENTRYPOINT",
        "CLAUDE_CODE_EXECPATH",
        "CLAUDE_CODE_MESSAGING_SOCKET",
        "CLAUDE_CODE_MESSAGING_TOKEN",
        "CLAUDE_CODE_BRIDGE_SESSION_ID",
        "CLAUDE_CODE_SESSION_ATTENDED",
    ];

    /// <summary>
    /// Hands Claude the MCP servers the workspace declares for this project.
    /// <para>
    /// Passed as files rather than folded into settings, because that is the
    /// form the flag takes and because it keeps the scopes visible: the widest
    /// file first and the project's after it, which is the order Claude applies
    /// them in and the order a clash is reported in.
    /// </para>
    /// </summary>
    private static void AddMcpServers(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.McpConfigFiles is not { Count: > 0 } files)
        {
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.McpConfig))
        {
            warnings.Add(
                "This build of Claude Code does not advertise --mcp-config, so the workspace's "
                + "MCP servers were not applied.");

            return;
        }

        foreach (var file in files)
        {
            arguments.Add("--mcp-config");
            arguments.Add(file);
        }
    }

    /// <summary>
    /// Asks Claude to pick up a previous conversation (spec section 66).
    /// <para>
    /// Only when the installed build advertises the flag. Passing --resume to
    /// one that does not know it would fail the whole launch, which is a far
    /// worse outcome than starting a fresh session.
    /// </para>
    /// </summary>
    private static void AddResume(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.ResumeSessionId is not { Length: > 0 } session)
        {
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.SessionResume))
        {
            warnings.Add(
                "This build of Claude Code does not advertise --resume, so a new session was "
                + "started instead of continuing the previous one.");

            return;
        }

        arguments.Add("--resume");
        arguments.Add(session);
    }

    /// <summary>
    /// Points Claude at the project's settings file in the workspace, so the
    /// application repository needs no .claude directory of its own
    /// (spec section 9) — screened first, because the workspace travels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The file went to Claude as it was from the first commit, as the way of
    /// moving a repository's own <c>.claude/settings.json</c> out of the
    /// repository. That predates the rule the security profile follows — a
    /// shared file may only tighten — and was never weighed against it. A
    /// hook in this file is a command run after every edit, on whichever
    /// machine pulls the workspace next, so the hooks are now screened: the
    /// launcher's own is kept and pointed at this machine's launcher,
    /// anything <c>commands.allowed_hooks</c> in config.yaml names is kept,
    /// and the rest is dropped and said. Everything else in the file passes
    /// untouched, as it did.
    /// </para>
    /// <para>
    /// The screened copy is written into the runtime directory, which is
    /// owner-only and deleted when the launch ends, and that is the path
    /// Claude is given. A file with no hooks is passed as it is; nothing is
    /// copied for nothing. A file that cannot be read as JSON is not passed
    /// at all, because a file this cannot see into is a file this cannot
    /// vouch for.
    /// </para>
    /// </remarks>
    private static async Task AddSettingsAsync(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings,
        CancellationToken ct)
    {
        // What the person's own accessibility profile asks of Claude's own
        // interface. Settled first, because a person who has set one gets a
        // settings file whether or not their project has one.
        var reading = Reading(context);

        var settingsPath = context.WorkspacePath is null || context.Manifest is null
            ? null
            : Path.Combine(
                context.WorkspacePath,
                "projects",
                context.Manifest.Slug,
                "agents",
                "claude",
                "settings.json");

        if (settingsPath is not null && !File.Exists(settingsPath))
        {
            settingsPath = null;
        }

        if (settingsPath is null && reading.Count == 0)
        {
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.ExternalSettings))
        {
            warnings.Add(
                settingsPath is null
                    ? "This build of Claude Code does not advertise --settings, so how you asked to "
                        + "be shown things was not passed to it."
                    : "This build of Claude Code does not advertise --settings, so the project's "
                        + "settings.json was not applied.");

            return;
        }

        // An empty document when the project has no settings of its own: the
        // screening below has nothing to screen, and the accessibility keys
        // have somewhere to go.
        var text = "{}";

        if (settingsPath is not null)
        {
            try
            {
                text = await File.ReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add($"The project's settings.json could not be read, so it was not applied: {ex.Message}");

                return;
            }
        }

        var launcher = Core.Agents.LauncherInvocation.Current() ?? "loadout";

        // Both forms of a pre-approval: as written, and as this adapter sends
        // it to Claude, so an allow entry the file and the machine agree on
        // survives whichever way somebody spelled it.
        var preApproved = new List<string>(context.PreApprovedCommands ?? []);

        preApproved.AddRange(Specifiers(context.PreApprovedCommands));

        var screened = Core.Policies.SettingsScreen.Screen(
            text, context.AllowedHooks ?? [], preApproved, launcher);

        if (screened is null)
        {
            warnings.Add(
                "The project's settings.json is not a JSON object, so it was not applied: a file "
                + "the launcher cannot read is one it cannot vouch for.");

            return;
        }

        var slug = context.Manifest?.Slug ?? "this project";

        foreach (var dropped in screened.DroppedHooks)
        {
            warnings.Add(
                $"The hook '{dropped}' in the project's settings.json was not applied. A shared "
                + "settings file may only tighten, and a hook runs a command after every edit. "
                + $"Allow it on this machine under commands.allowed_hooks.{slug} in config.yaml.");
        }

        foreach (var dropped in screened.DroppedApprovals)
        {
            warnings.Add(
                $"The approval '{dropped}' in the project's settings.json was not applied. A shared "
                + "settings file may only tighten, and an approval removes a prompt. Pre-approve it "
                + $"on this machine under commands.pre_approved.{slug} in config.yaml.");
        }

        foreach (var dropped in screened.DroppedSettings)
        {
            warnings.Add(
                $"'{dropped}' in the project's settings.json was not applied: a shared settings "
                + "file may only tighten, and nothing on this machine can put that back.");
        }

        var path = settingsPath;

        // A node's hooks are off from every scope, the machine's included,
        // and the only way to say so is a key in the settings handed over.
        // It goes into the copy written to the runtime directory, never into
        // the project's own file, which travels.
        var disableHooks = context.Headless?.DisableHooks == true;

        if (disableHooks)
        {
            screened.Document["disableAllHooks"] = true;
        }

        foreach (var (key, value) in reading)
        {
            // The person's own preference, and the last word: a project that
            // turned animation on does not get to turn it back on for
            // somebody who asked for none.
            screened.Document[key] = value;
        }

        if (screened.Changed || disableHooks || reading.Count > 0 || path is null)
        {
            path = Path.Combine(context.RuntimeDirectory, "settings.json");

            try
            {
                await File.WriteAllTextAsync(
                    path, screened.Document.ToJsonString(SettingsLayout), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add(
                    $"The screened settings could not be written, so the project's settings.json "
                    + $"was not applied: {ex.Message}");

                return;
            }
        }

        arguments.Add("--settings");
        arguments.Add(path!);
    }

    /// <summary>
    /// Switches on Claude's own mode for somebody using a screen reader.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for a session somebody is watching, and only where the profile
    /// asks for no redraws, which is what that mode is: no spinners, no
    /// timers, no in-place edits. A terminal screen reader reads every one of
    /// those aloud again on every frame.
    /// </para>
    /// <para>
    /// A build that does not advertise the flag is said out loud rather than
    /// quietly left animated. Somebody who set the profile and got a spinner
    /// anyway would have nothing to go on.
    /// </para>
    /// </remarks>
    private static void AddReading(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Headless is not null
            || context.Accessibility is not { } profile
            || !string.Equals(profile.Display.Redraw, "never", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (descriptor.Supports(ScreenReaderMode))
        {
            arguments.Add("--ax-screen-reader");

            return;
        }

        warnings.Add(
            "This build of Claude Code does not advertise --ax-screen-reader, so it will still "
            + "redraw its own display while it works. Everything Loadout prints follows your "
            + "profile either way.");
    }

    /// <summary>
    /// What the person's accessibility profile asks of Claude's own interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only for a session somebody is watching. A node has no terminal to
    /// animate, no spinner to re-read and nobody to hear a bell, so none of
    /// this is set for one.
    /// </para>
    /// <para>
    /// The theme is deliberately left alone. Claude ships colour-blind-safe
    /// themes in a light and a dark variant, and which of the two somebody
    /// wants is not knowable from here; choosing one would flip the colours
    /// of a terminal that was already set up the way they like it. What
    /// Loadout can do about colour is in its own output.
    /// </para>
    /// </remarks>
    private static Dictionary<string, System.Text.Json.Nodes.JsonNode?> Reading(AgentLaunchContext context)
    {
        var keys = new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(StringComparer.Ordinal);

        if (context.Headless is not null || context.Accessibility is not { } profile)
        {
            return keys;
        }

        if (!string.Equals(profile.Display.Motion, "full", StringComparison.OrdinalIgnoreCase))
        {
            keys["prefersReducedMotion"] = true;
        }

        if (string.Equals(profile.Display.Redraw, "never", StringComparison.OrdinalIgnoreCase))
        {
            // A tip that appears and disappears inside a spinner is a redraw
            // in the place a screen reader is most likely to be listening.
            keys["spinnerTipsEnabled"] = false;
        }

        if (profile.Display.Bell)
        {
            keys["preferredNotifChannel"] = "terminal_bell";
        }

        return keys;
    }

    /// <summary>
    /// Attaches the compiled context as a system prompt, preferring the file
    /// form and falling back to the inline form only while it fits.
    /// </summary>
    private static async Task AddCompiledContextAsync(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings,
        CancellationToken ct)
    {
        if (context.CompiledContext is null)
        {
            return;
        }

        var path = context.CompiledContext.FilePath;

        if (descriptor.Supports(SystemPromptFile))
        {
            arguments.Add("--append-system-prompt-file");
            arguments.Add(path);
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.ExternalPrompt))
        {
            warnings.Add(
                "This build of Claude Code advertises no way to append a system prompt, so the "
                + $"compiled context was not attached. It is at {path} for the duration of the session.");

            return;
        }

        string content;

        try
        {
            content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"The compiled context could not be read: {ex.Message}");
            return;
        }

        if (content.Length > MaximumInlinePromptLength)
        {
            // Truncating would hand the agent half a document and let it
            // believe that was everything, which is worse than telling the user
            // plainly that the context did not fit.
            warnings.Add(
                $"The compiled context is {content.Length / 1024}KB, which exceeds what this build of "
                + "Claude Code can accept on the command line, so it was not attached. Narrow it with "
                + "a --profile, or upgrade to a build that accepts --append-system-prompt-file.");

            return;
        }

        arguments.Add("--append-system-prompt");
        arguments.Add(content);
    }

    /// <summary>
    /// Translates the generic security profile into Claude's own controls
    /// (spec section 58).
    /// <para>
    /// Only ever tightens. The permissive values this option accepts —
    /// bypassPermissions, dontAsk — are deliberately unreachable from here: a
    /// file in a shared workspace repository must not be able to switch off
    /// somebody's safety controls on their machine.
    /// </para>
    /// </summary>
    private static void AddSecurityProfile(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Security is null)
        {
            return;
        }

        var security = context.Security;

        var mode = security.Filesystem switch
        {
            // Plan mode reads without writing, which is what a review or
            // production-investigation profile is asking for.
            Models.Policies.FilesystemAccess.ReadOnly => "plan",

            // Restricted still permits changes but asks first.
            Models.Policies.FilesystemAccess.Restricted => "manual",

            _ => security.Approvals == Models.Policies.ApprovalPolicy.Strict ? "manual" : null,
        };

        if (mode is not null)
        {
            if (descriptor.Supports(PermissionMode))
            {
                arguments.Add("--permission-mode");
                arguments.Add(mode);
            }
            else
            {
                // A profile that cannot be enforced must be visible. Spec
                // section 5 is explicit that a gap is never allowed to
                // disappear quietly, and this one is about permissions.
                warnings.Add(
                    "This build of Claude Code does not advertise --permission-mode, so the security "
                    + $"profile's filesystem setting ({security.Filesystem}) was not applied.");
            }
        }

        // Said, not dropped. --allowed-tools pre-approves rather than
        // restricts, so honouring it from a profile would let a file in a
        // shared workspace switch off the approval prompts of everyone who
        // clones it. That is the one thing SecurityProfile's own doc comment
        // rules out, and it was doing it.
        if (security.AllowedTools.Count > 0)
        {
            warnings.Add(
                "The security profile's allowed_tools was not applied: it pre-approves tools "
                + "rather than restricting them, and a shared profile may only tighten. Put "
                + "pre-approvals in commands.pre_approved in config.yaml, which stays on this "
                + "machine.");
        }

        var preApproved = Specifiers(context.PreApprovedCommands);
        var denied = Denials(security);

        if (preApproved.Count == 0 && denied.Count == 0)
        {
            return;
        }

        if (!descriptor.Supports(ToolRestrictions))
        {
            warnings.Add(
                "This build of Claude Code does not advertise tool restrictions, so the "
                + "command policy was not applied.");

            return;
        }

        if (preApproved.Count > 0)
        {
            arguments.Add("--allowed-tools");
            arguments.Add(string.Join(",", preApproved));
        }

        if (denied.Count > 0)
        {
            arguments.Add("--disallowed-tools");
            arguments.Add(string.Join(",", denied));
        }
    }

    /// <summary>Everything the profile forbids, as Claude's own specifiers.</summary>
    private static List<string> Denials(Models.Policies.SecurityProfile security) =>
    [
        .. security.DisallowedTools,
        .. Specifiers(security.DeniedCommands),
    ];

    /// <summary>
    /// Turns commands into the tool specifiers Claude understands.
    /// </summary>
    /// <remarks>
    /// <c>git push</c> becomes <c>Bash(git push:*)</c>, so the denial covers the
    /// command and its arguments rather than only the bare word. An entry that
    /// already carries a bracket is passed through untouched: somebody who has
    /// written a specifier by hand meant it, and wrapping it again would produce
    /// something that matches nothing at all — which fails open for a denial,
    /// and is the worst way for this to be wrong.
    /// </remarks>
    internal static List<string> Specifiers(IReadOnlyList<string>? commands)
    {
        if (commands is null)
        {
            return [];
        }

        return
        [
            .. commands
                .Select(command => command.Trim())
                .Where(command => command.Length > 0)
                .Select(command => command.Contains('(', StringComparison.Ordinal)
                    ? command
                    : $"Bash({command}:*)"),
        ];
    }

    /// <summary>
    /// Grants read access to the project's workspace directory, so the agent
    /// can open prompts and other files kept out of the application repository.
    /// </summary>
    /// <remarks>
    /// Read access only. A skill sitting under this directory is a file the
    /// session can open if it already knows the path, and nothing more: it is
    /// not a command anybody can reach and nothing announces it. Skills are
    /// handed over separately, by <see cref="AddProjectSkills"/>.
    /// </remarks>

    /// <summary>
    /// Asks for the model the project pinned, where the agent can be told.
    /// </summary>
    /// <remarks>
    /// Added before the passthrough arguments, so somebody who still types a
    /// model after <c>--</c> gets the one they typed: the manifest ends the
    /// retyping, it does not take the choice away. A build that cannot be told
    /// says so rather than starting on a different model than the project asked
    /// for, which is the kind of gap spec section 5 refuses to let disappear.
    /// </remarks>
    private static void AddModel(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Model is not { Length: > 0 } model)
        {
            return;
        }

        if (!descriptor.Supports(ModelSelection))
        {
            warnings.Add(
                "This build of Claude Code does not advertise a model option, so the project's "
                + $"model ({model}) was not applied.");

            return;
        }

        arguments.Add("--model");
        arguments.Add(model);
    }

    /// <summary>
    /// Puts Claude into its message-in, event-out mode and applies everything
    /// a node must be told explicitly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape came out of driving Claude Code 2.1.270 from a process with
    /// its pipes held: <c>-p</c> with both stream formats and <c>--verbose</c>,
    /// which the stream output needs; the caps the agent enforces itself; the
    /// permission mode and tool lists, so nothing is inherited from this
    /// machine's interactive settings; the tool that answers when it would
    /// otherwise prompt, since nobody is at the keyboard; the schema the final
    /// answer must fit; only the MCP servers the launcher names, because
    /// without that a node connected every server on the machine, several of
    /// them waiting on an authentication nobody was there to give.
    /// </para>
    /// <para>
    /// <c>--bare</c> is deliberately not used. It removes the hooks and the
    /// plugins and it also removes the credentials, and the session answers
    /// "Not logged in" to everything. Hooks are switched off through the
    /// settings handed over instead.
    /// </para>
    /// </remarks>
    private static void AddHeadless(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.Headless is not { } headless)
        {
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.Headless))
        {
            warnings.Add(
                "This build of Claude Code does not advertise --input-format and --output-format, "
                + "so it cannot be driven without a terminal. The session was not started headlessly.");

            return;
        }

        arguments.Add("-p");
        arguments.Add("--verbose");
        arguments.Add("--input-format");
        arguments.Add("stream-json");
        arguments.Add("--output-format");
        arguments.Add("stream-json");

        if (headless.MaxTurns is { } turns)
        {
            arguments.Add("--max-turns");
            arguments.Add(turns.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (headless.BudgetUsd is { } budget)
        {
            // Invariant, always: a comma here would be read as no cap at all.
            arguments.Add("--max-budget-usd");
            arguments.Add(budget.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (descriptor.Supports(PermissionMode))
        {
            arguments.Add("--permission-mode");
            arguments.Add(headless.Permission switch
            {
                HeadlessPermission.AcceptEdits => "acceptEdits",
                HeadlessPermission.DenyUnlessAllowed => "dontAsk",
                HeadlessPermission.Bypass => "bypassPermissions",
                _ => "default",
            });
        }
        else
        {
            warnings.Add(
                "This build of Claude Code does not advertise --permission-mode, so the node's "
                + $"permission setting ({headless.Permission}) was not applied.");
        }

        var allowed = headless.AllowedTools ?? [];
        var denied = headless.DeniedTools ?? [];

        if (allowed.Count > 0 || denied.Count > 0)
        {
            if (descriptor.Supports(ToolRestrictions))
            {
                if (allowed.Count > 0)
                {
                    arguments.Add("--allowed-tools");
                    arguments.Add(string.Join(",", allowed));
                }

                if (denied.Count > 0)
                {
                    arguments.Add("--disallowed-tools");
                    arguments.Add(string.Join(",", denied));
                }
            }
            else
            {
                warnings.Add(
                    "This build of Claude Code does not advertise tool restrictions, so the node's "
                    + "allow and deny lists were not applied.");
            }
        }

        if (headless.PermissionAnswerer is { Length: > 0 } answerer)
        {
            if (descriptor.Supports(PermissionAnswerer))
            {
                arguments.Add("--permission-prompt-tool");
                arguments.Add(answerer);
            }
            else
            {
                warnings.Add(
                    "This build of Claude Code does not advertise --permission-prompt-tool, so "
                    + "anything the node would have asked about will be denied instead.");
            }
        }

        if (headless.OutputSchemaJson is { Length: > 0 } schema)
        {
            if (descriptor.Supports(OutputSchema))
            {
                arguments.Add("--json-schema");
                arguments.Add(schema);
            }
            else
            {
                warnings.Add(
                    "This build of Claude Code does not advertise --json-schema, so the node's "
                    + "report will arrive as free text rather than the shape asked for.");
            }
        }

        if (headless.IsolateMcpServers)
        {
            if (descriptor.Supports(StrictMcp))
            {
                arguments.Add("--strict-mcp-config");
            }
            else
            {
                warnings.Add(
                    "This build of Claude Code does not advertise --strict-mcp-config, so the node "
                    + "will connect this machine's own MCP servers as well as the launcher's.");
            }
        }

        // The settings builder folds the key into the project's screened
        // copy when there is one. With no project settings file there is
        // nothing to fold it into, so it goes inline.
        if (headless.DisableHooks && !arguments.Contains("--settings"))
        {
            if (descriptor.Supports(AgentCapabilities.ExternalSettings))
            {
                arguments.Add("--settings");
                arguments.Add("{\"disableAllHooks\":true}");
            }
            else
            {
                warnings.Add(
                    "This build of Claude Code does not advertise --settings, so this machine's "
                    + "hooks will run inside the node.");
            }
        }
    }

    private static void AddWorkspaceDirectory(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments)
    {
        if (context.WorkspacePath is null
            || context.Manifest is null
            || !descriptor.Supports(AgentCapabilities.AdditionalDirectories))
        {
            return;
        }

        var projectWorkspace = Path.Combine(
            context.WorkspacePath, "projects", context.Manifest.Slug);

        if (Directory.Exists(projectWorkspace))
        {
            arguments.Add("--add-dir");
            arguments.Add(projectWorkspace);
        }
    }

    /// <summary>
    /// Anywhere beyond the project this session has been told it may work in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A team's directory is the first of these. Its nodes are briefed with the
    /// path and told to keep what the team learns there, and without this the
    /// agent refused every write to it: the directory existed, the path was
    /// right, the instruction was clear, and nothing could be written.
    /// </para>
    /// <para>
    /// Only ones that exist. Naming a directory that is not there is how a
    /// session fails to start over a path nobody meant to depend on.
    /// </para>
    /// </remarks>
    private static void AddReachable(List<string> arguments, AgentLaunchContext context, AgentDescriptor descriptor)
    {
        if (context.ReachableDirectories is not { Count: > 0 } directories
            || !descriptor.Supports(AgentCapabilities.AdditionalDirectories))
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (directory is { Length: > 0 } && Directory.Exists(directory))
            {
                arguments.Add("--add-dir");
                arguments.Add(directory);
            }
        }
    }

    /// <summary>
    /// Hands the session the skills the workspace holds for this project, as
    /// commands it can actually reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The workspace keeps them at <c>agents/&lt;agent&gt;/skills/&lt;name&gt;/SKILL.md</c>,
    /// which is not where the agent looks, so until now they were authored and
    /// never loaded: nothing named them and nothing could invoke them.
    /// </para>
    /// <para>
    /// Written into the per-launch runtime directory and passed with
    /// <c>--plugin-dir</c>, which loads for that session only. That is the same
    /// bargain the compiled context makes, and it is what keeps this out of
    /// both the application repository, where agent state does not belong, and
    /// the agent's own configuration home, where it would outlive the session
    /// and collide with the next project's.
    /// </para>
    /// <para>
    /// A manifest is written rather than relying on a bare directory of skills
    /// being accepted. Both shapes may work; only this one is known to.
    /// </para>
    /// </remarks>
    private static void AddProjectSkills(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
    {
        if (context.WorkspacePath is null || context.Manifest is null)
        {
            return;
        }

        // The launcher's own, then the workspace's, then this project's, with
        // a later one of the same name replacing the earlier. Asked of the same
        // enumeration the budget counts, so what a session is handed and what
        // it was told that would cost cannot disagree — two answers to one
        // question is the drift this whole report exists to prevent.
        var offered = Loadout.Core.Instructions.SkillExport.Offered(
            context.WorkspacePath, context.Manifest.Slug, "claude");

        if (offered.Count == 0)
        {
            return;
        }

        // Said rather than skipped. A skill somebody wrote and cannot reach is
        // exactly the state this exists to end, and a build that cannot take
        // them should not leave that looking like it worked.
        if (!descriptor.Supports(AgentCapabilities.ProjectSkills))
        {
            warnings.Add(
                "This build of Claude Code does not advertise --plugin-dir, so the "
                + $"{offered.Count} skill(s) available to "
                + $"{context.Manifest.Slug} were not loaded.");

            return;
        }

        var plugin = Path.Combine(context.RuntimeDirectory, "skills", $"loadout-{context.Manifest.Slug}");

        try
        {
            Directory.CreateDirectory(Path.Combine(plugin, ".claude-plugin"));

            File.WriteAllText(
                Path.Combine(plugin, ".claude-plugin", "plugin.json"),
                $$"""
                {
                  "name": "loadout-{{context.Manifest.Slug}}",
                  "version": "0.0.0",
                  "description": "Skills for {{context.Manifest.Slug}}, for this session only."
                }
                """);

            // The resolved set, already narrowed: one name is one skill, and
            // whichever layer won has won by the time it gets here.
            foreach (var (name, content) in offered)
            {
                var into = Path.Combine(plugin, "skills", name);

                Directory.CreateDirectory(into);
                File.WriteAllText(Path.Combine(into, "SKILL.md"), content);
            }

            // The files a skill on disk brings with it — the scripts it tells
            // the session to run. A shipped skill is a single document by
            // construction and has none. Copied after the text above, so a
            // skill that won on name keeps its own companions.
            foreach (var (name, from) in OnDisk(context))
            {
                if (offered.ContainsKey(name))
                {
                    CopyDirectory(from, Path.Combine(plugin, "skills", name));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"The skills for {context.Manifest.Slug} could not be prepared, so none were "
                + $"loaded: {ex.Message}");

            return;
        }

        arguments.Add("--plugin-dir");
        arguments.Add(plugin);
    }

    /// <summary>
    /// The skills this project has as directories, so their companion files
    /// can be carried over. Keyed by name, narrower last.
    /// </summary>
    private static IEnumerable<(string Name, string From)> OnDisk(AgentLaunchContext context)
    {
        if (context.WorkspacePath is null || context.Manifest is null)
        {
            yield break;
        }

        string[] roots =
        [
            Path.Combine(context.WorkspacePath, "global", "agents", "claude", "skills"),
            Path.Combine(
                context.WorkspacePath, "projects", context.Manifest.Slug, "agents", "claude", "skills"),
        ];

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (File.Exists(Path.Combine(directory, "SKILL.md")))
                {
                    yield return (Path.GetFileName(directory), directory);
                }
            }
        }
    }

    /// <summary>Copies a skill and whatever it brings with it.</summary>
    /// <remarks>
    /// Everything, not just the <c>SKILL.md</c>. A skill routinely ships the
    /// scripts it tells the session to run, and one copied without them is a
    /// skill that fails at the first instruction it gives.
    /// </remarks>
    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }
}
