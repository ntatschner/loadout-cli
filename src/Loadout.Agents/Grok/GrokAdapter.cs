using Loadout.Models.Agents;
using Loadout.Models.Policies;
using Loadout.Models.Results;
using Loadout.Platform.Abstractions;

namespace Loadout.Agents.Grok;

/// <summary>
/// Launches Grok CLI with the compiled instruction context, security profile,
/// model selection, and headless stream-JSON configuration (spec section 33).
/// </summary>
/// <remarks>
/// <para>
/// Grok CLI accepts inline instructions via <c>--rules &lt;string&gt;</c>. When the
/// compiled context fits comfortably on the command line, it is passed inline;
/// when it exceeds the inline threshold, a short pointer rule instructs Grok
/// to read the compiled context file from the launch's runtime directory.
/// </para>
/// <para>
/// Like the Claude and Codex adapters, security profile translation only ever
/// tightens: permissive flags (<c>--yolo</c>, <c>--sandbox none</c>,
/// <c>--permission-mode bypassPermissions</c>) are unreachable from a shared
/// workspace security profile.
/// </para>
/// </remarks>
public sealed class GrokAdapter : AgentAdapterBase
{
    /// <summary>
    /// Upper bound for passing compiled context directly on the command line via
    /// <c>--rules</c>. Above this threshold, a pointer to the runtime file is
    /// passed instead so Windows and Unix argument-length limits are never hit.
    /// </summary>
    private const int MaxInlineRulesLength = 24_000;

    /// <summary>Capability key for setting the working directory explicitly.</summary>
    private const string WorkingDirectoryFlag = "cwd_flag";

    /// <summary>Capability key for permission modes.</summary>
    private const string PermissionMode = "permission_mode";

    /// <summary>Capability key for command/tool allow and deny rules.</summary>
    private const string ToolRestrictions = "tool_restrictions";

    /// <summary>Capability key for disabling web search when network is restricted.</summary>
    private const string DisableWebSearch = "disable_web_search";

    /// <summary>Capability key for choosing the model.</summary>
    private const string ModelSelection = "model_selection";

    /// <summary>Capability key for constraining the final answer to a schema.</summary>
    private const string OutputSchema = "output_schema";

    /// <summary>Capability key for bounding headless turns.</summary>
    private const string MaxTurns = "max_turns";

    /// <summary>Capability key for suppressing automatic CLI updates in headless runs.</summary>
    private const string NoAutoUpdate = "no_auto_update";

    /// <summary>Capability key for starting an interactive session with a first message.</summary>
    private const string OpeningPrompt = "opening_prompt";

    public GrokAdapter(
        IExecutableResolver resolver,
        IProcessLauncher processes,
        IReadOnlyList<string> configuredSearchPaths)
        : base(resolver, processes, configuredSearchPaths)
    {
    }

    /// <inheritdoc />
    public override string Name => "grok";

    /// <inheritdoc />
    public override string DisplayName => "Grok CLI";

    /// <inheritdoc />
    protected override string ExecutableName => "grok";

    /// <inheritdoc />
    protected override IReadOnlyDictionary<string, string[]> CapabilityMarkers =>
        new Dictionary<string, string[]>
        {
            [AgentCapabilities.ExternalPrompt] = ["--rules"],
            [WorkingDirectoryFlag] = ["--cwd"],
            [AgentCapabilities.SessionResume] = ["--resume", "--continue"],
            [AgentCapabilities.Sandboxing] = ["--sandbox"],
            [PermissionMode] = ["--permission-mode"],
            [ToolRestrictions] = ["--allow", "--deny", "--tools", "--disallowed-tools"],
            [DisableWebSearch] = ["--disable-web-search"],
            [ModelSelection] = ["--model"],
            [AgentCapabilities.Headless] = ["--output-format", "streaming-json"],
            [OutputSchema] = ["--json-schema"],
            [MaxTurns] = ["--max-turns"],
            [NoAutoUpdate] = ["--no-auto-update"],
            [OpeningPrompt] = ["[prompt]", "prompt"],
        };

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

        if (descriptor.Supports(WorkingDirectoryFlag))
        {
            arguments.Add("--cwd");
            arguments.Add(context.WorkingDirectory);
        }

        if (context.ResumeSessionId is { Length: > 0 } session)
        {
            if (descriptor.Supports(AgentCapabilities.SessionResume))
            {
                arguments.Add("--resume");
                arguments.Add(session);
            }
            else
            {
                warnings.Add(
                    "This build of Grok CLI does not advertise --resume, so a new "
                    + "session was started instead of continuing the previous one.");
            }
        }

        await AddCompiledContextAsync(context, descriptor, arguments, warnings, ct)
            .ConfigureAwait(false);

        if (context.Headless is null)
        {
            AddSecurityProfile(context, descriptor, arguments, warnings);
        }

        AddModel(context, descriptor, arguments, warnings);
        AddHeadless(context, descriptor, arguments, warnings);
        AddReading(context, warnings);
        ReportProjectSkills(context, warnings);

        arguments.AddRange(context.PassthroughArguments);

        AddOpeningPrompt(context, descriptor, OpeningPrompt, arguments, warnings);

        var environment = context.ResolvedEnvironment is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(context.ResolvedEnvironment);

        return OperationResult<AgentInvocation>.Ok(
            new AgentInvocation(descriptor.ExecutablePath, arguments, environment, warnings));
    }

    /// <summary>
    /// Supplies the compiled instruction context via <c>--rules</c>.
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

        if (!descriptor.Supports(AgentCapabilities.ExternalPrompt))
        {
            warnings.Add(
                "This build of Grok CLI does not advertise --rules, so the compiled "
                + $"instruction context ({context.CompiledContext.FilePath}) could not be injected automatically.");

            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(context.CompiledContext.FilePath, ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            arguments.Add("--rules");

            if (content.Length <= MaxInlineRulesLength)
            {
                arguments.Add(content);
            }
            else
            {
                arguments.Add(
                    $"Read and follow the compiled Loadout workspace instructions in '{context.CompiledContext.FilePath}' before taking any action.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add(
                $"The compiled instruction context could not be read for --rules: {ex.Message}");
        }
    }

    /// <summary>
    /// Translates the generic security profile into Grok CLI's sandbox,
    /// permission-mode, web-search, and command restriction flags (spec section 58).
    /// </summary>
    /// <remarks>
    /// Only ever tightens. <c>--yolo</c>, <c>--sandbox none</c>, and
    /// <c>--permission-mode bypassPermissions</c> are deliberately unreachable
    /// from a shared security profile.
    /// </remarks>
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

        // 1. Sandbox mode: read-only for ReadOnly/Restricted, workspace-write otherwise.
        var sandbox = security.Filesystem switch
        {
            FilesystemAccess.ReadOnly => "read-only",
            FilesystemAccess.Restricted => "read-only",
            _ => "workspace-write",
        };

        if (descriptor.Supports(AgentCapabilities.Sandboxing))
        {
            arguments.Add("--sandbox");
            arguments.Add(sandbox);
        }
        else
        {
            warnings.Add(
                "This build of Grok CLI does not advertise --sandbox, so the security profile's "
                + $"filesystem setting ({security.Filesystem}) was not applied.");
        }

        // 2. Permission mode: plan for ReadOnly, default when strict approvals are requested.
        var mode = security.Filesystem switch
        {
            FilesystemAccess.ReadOnly => "plan",
            FilesystemAccess.Restricted => "default",
            _ => security.Approvals == ApprovalPolicy.Strict ? "default" : null,
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
                warnings.Add(
                    "This build of Grok CLI does not advertise --permission-mode, so the security "
                    + $"profile's permission mode ({mode}) was not applied.");
            }
        }

        // 3. Network policy: disable web search when network access is tightened.
        if (security.Network != NetworkAccess.Standard && descriptor.Supports(DisableWebSearch))
        {
            arguments.Add("--disable-web-search");
        }

        // 4. Shared profiles may only tighten: warn if allowed_tools is set.
        if (security.AllowedTools.Count > 0)
        {
            warnings.Add(
                "The security profile's allowed_tools was not applied: it pre-approves tools "
                + "rather than restricting them, and a shared profile may only tighten. Put "
                + "pre-approvals in commands.pre_approved in config.yaml, which stays on this "
                + "machine.");
        }

        // 5. Machine-local pre-approvals (--allow) and shared denials (--deny).
        var preApproved = Specifiers(context.PreApprovedCommands);
        var denied = Denials(security);

        if (preApproved.Count == 0 && denied.Count == 0)
        {
            return;
        }

        if (!descriptor.Supports(ToolRestrictions))
        {
            warnings.Add(
                "This build of Grok CLI does not advertise tool restrictions, so the "
                + "command policy was not applied.");

            return;
        }

        foreach (var allow in preApproved)
        {
            arguments.Add("--allow");
            arguments.Add(allow);
        }

        foreach (var deny in denied)
        {
            arguments.Add("--deny");
            arguments.Add(deny);
        }
    }

    /// <summary>Everything the profile forbids, as Grok's own <c>Tool(pattern)</c> specifiers.</summary>
    private static List<string> Denials(SecurityProfile security) =>
    [
        .. security.DisallowedTools,
        .. Specifiers(security.DeniedCommands),
    ];

    /// <summary>
    /// Turns commands into the <c>Bash(command:*)</c> tool specifiers Grok CLI understands.
    /// </summary>
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
    /// Asks for the model the project pinned, where the agent can be told.
    /// </summary>
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
                "This build of Grok CLI does not advertise a model option, so the project's "
                + $"model ({model}) was not applied.");

            return;
        }

        arguments.Add("--model");
        arguments.Add(model);
    }

    /// <summary>
    /// Configures Grok CLI for non-interactive headless execution with streaming JSON output.
    /// </summary>
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
                "This build of Grok CLI does not advertise --output-format streaming-json, "
                + "so it cannot be driven without a terminal. The session was not started headlessly.");

            return;
        }

        if (descriptor.Supports(NoAutoUpdate))
        {
            arguments.Add("--no-auto-update");
        }

        arguments.Add("--output-format");
        arguments.Add("streaming-json");

        if (headless.MaxTurns is { } turns && descriptor.Supports(MaxTurns))
        {
            arguments.Add("--max-turns");
            arguments.Add(turns.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
                "This build of Grok CLI does not advertise --permission-mode, so the node's "
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
                    arguments.Add("--tools");
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
                    "This build of Grok CLI does not advertise tool restrictions, so the node's "
                    + "allow and deny lists were not applied.");
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
                    "This build of Grok CLI does not advertise --json-schema, so the node's "
                    + "output will not be validated against the schema.");
            }
        }
    }

    /// <summary>
    /// Reports when an accessibility profile asks for no redraws, since Grok CLI
    /// has no dedicated screen-reader flag.
    /// </summary>
    private static void AddReading(
        AgentLaunchContext context,
        List<string> warnings)
    {
        if (context.Headless is not null || context.Accessibility is not { } profile)
        {
            return;
        }

        if (string.Equals(profile.Display.Redraw, "never", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(
                "Grok CLI has no screen-reader mode, so its display will still repaint while it "
                + "works. Everything Loadout prints follows your profile.");
        }
    }

    /// <summary>
    /// Says that the skills the workspace holds for this project under
    /// <c>agents/grok/skills</c> are not being loaded, where there are any.
    /// </summary>
    private static void ReportProjectSkills(AgentLaunchContext context, List<string> warnings)
    {
        if (context.WorkspacePath is null || context.Manifest is null)
        {
            return;
        }

        var source = Path.Combine(
            context.WorkspacePath, "projects", context.Manifest.Slug, "agents", "grok", "skills");

        if (!Directory.Exists(source))
        {
            return;
        }

        var skills = Directory.EnumerateDirectories(source)
            .Count(directory => File.Exists(Path.Combine(directory, "SKILL.md")));

        if (skills == 0)
        {
            return;
        }

        warnings.Add(
            $"The workspace holds {skills} skill(s) for {context.Manifest.Slug} under "
            + "agents/grok/skills, and Grok CLI has no way to load a plugin for one session, so "
            + "they were not handed over.");
    }
}
