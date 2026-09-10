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
        };

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
        AddProjectSkills(context, descriptor, arguments, warnings);
        AddSecurityProfile(context, descriptor, arguments, warnings);
        AddModel(context, descriptor, arguments, warnings);

        // Everything after a bare -- belongs to the agent untouched
        // (spec section 36), so it is appended last and never inspected.
        arguments.AddRange(context.PassthroughArguments);

        var environment = context.ResolvedEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(context.ResolvedEnvironment);

        return OperationResult<AgentInvocation>.Ok(
            new AgentInvocation(descriptor.ExecutablePath, arguments, environment, warnings));
    }

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
        if (context.WorkspacePath is null || context.Manifest is null)
        {
            return;
        }

        var settingsPath = Path.Combine(
            context.WorkspacePath,
            "projects",
            context.Manifest.Slug,
            "agents",
            "claude",
            "settings.json");

        if (!File.Exists(settingsPath))
        {
            return;
        }

        if (!descriptor.Supports(AgentCapabilities.ExternalSettings))
        {
            warnings.Add(
                "This build of Claude Code does not advertise --settings, so the project's "
                + "settings.json was not applied.");

            return;
        }

        string text;

        try
        {
            text = await File.ReadAllTextAsync(settingsPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"The project's settings.json could not be read, so it was not applied: {ex.Message}");

            return;
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

        var slug = context.Manifest.Slug;

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

        if (screened.Changed)
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
        arguments.Add(path);
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

        // Workspace-wide first, then the project, so a project that writes a
        // skill of the same name replaces the general one rather than colliding
        // with it. That is the order everything else composes in.
        var roots = new[]
        {
            Path.Combine(context.WorkspacePath, "global", "agents", "claude", "skills"),
            Path.Combine(
                context.WorkspacePath, "projects", context.Manifest.Slug, "agents", "claude", "skills"),
        };

        var skills = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (File.Exists(Path.Combine(directory, "SKILL.md")))
                {
                    skills[Path.GetFileName(directory)] = directory;
                }
            }
        }

        if (skills.Count == 0 && BuiltInSkills.Names.Count == 0)
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
                + $"{skills.Count + BuiltInSkills.Names.Count} skill(s) available to "
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

            // Shipped first, so a workspace or project skill of the same name
            // replaces it. Built-in, then workspace, then project: the same
            // order the specialist library and the rules already resolve in,
            // and the same reason — the narrower answer is the later one.
            foreach (var name in BuiltInSkills.Names)
            {
                BuiltInSkills.WriteTo(name, Path.Combine(plugin, "skills", name));
            }

            foreach (var (name, from) in skills)
            {
                var into = Path.Combine(plugin, "skills", name);

                // Cleared rather than merged. A project's version of a skill is
                // a replacement, and leaving the shipped one's files underneath
                // would hand the session a mixture neither author wrote.
                if (Directory.Exists(into))
                {
                    Directory.Delete(into, recursive: true);
                }

                CopyDirectory(from, into);
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
