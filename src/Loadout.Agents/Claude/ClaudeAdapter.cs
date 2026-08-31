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
            [AgentCapabilities.McpConfig] = ["--mcp-config"],
            [AgentCapabilities.SessionResume] = ["--resume", "--continue"],
            [PermissionMode] = ["--permission-mode"],
            [ToolRestrictions] = ["--allowed-tools", "--disallowed-tools"],
        };

    /// <summary>Capability key for the permission-mode option.</summary>
    private const string PermissionMode = "permission_mode";

    /// <summary>Capability key for the tool allow and deny options.</summary>
    private const string ToolRestrictions = "tool_restrictions";

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
        AddSettings(context, descriptor, arguments, warnings);
        await AddCompiledContextAsync(context, descriptor, arguments, warnings, ct).ConfigureAwait(false);
        AddWorkspaceDirectory(context, descriptor, arguments);
        AddSecurityProfile(context, descriptor, arguments, warnings);

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
    /// (spec section 9).
    /// </summary>
    private static void AddSettings(
        AgentLaunchContext context,
        AgentDescriptor descriptor,
        List<string> arguments,
        List<string> warnings)
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

        arguments.Add("--settings");
        arguments.Add(settingsPath);
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

        if (security.AllowedTools.Count == 0 && security.DisallowedTools.Count == 0)
        {
            return;
        }

        if (!descriptor.Supports(ToolRestrictions))
        {
            warnings.Add(
                "This build of Claude Code does not advertise tool restrictions, so the security "
                + "profile's allowed and disallowed tool lists were not applied.");

            return;
        }

        if (security.AllowedTools.Count > 0)
        {
            arguments.Add("--allowed-tools");
            arguments.Add(string.Join(",", security.AllowedTools));
        }

        if (security.DisallowedTools.Count > 0)
        {
            arguments.Add("--disallowed-tools");
            arguments.Add(string.Join(",", security.DisallowedTools));
        }
    }

    /// <summary>
    /// Grants read access to the project's workspace directory so the agent can
    /// reach prompts and skills that were deliberately kept out of the
    /// application repository.
    /// </summary>
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
}
