using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Grok;
using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Models.Agents;
using Loadout.Models.Configuration;
using Loadout.Models.Policies;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// How <see cref="GrokAdapter"/> turns a <see cref="AgentLaunchContext"/> into
/// a <c>grok</c> command line, using stubbed <c>grok --help</c> output so the
/// tests hold on machines without the Grok CLI installed.
/// </summary>
public sealed class GrokAdapterTests : IDisposable
{
    private const string FullGrokHelp = """
        Usage: grok [options] [prompt]

        Options:
          -m, --model <model>                 Model to use for the session
              --cwd <path>                    Working directory for the session
          -r, --resume [session-id]           Resume a previous session
          -c, --continue                      Continue the most recent session
              --rules <text>                  Additional system rules to append
              --sandbox <mode>                Filesystem sandbox (read-only, workspace-write, none)
              --permission-mode <mode>        Permission mode (default, plan, acceptEdits, bypassPermissions)
              --allow <specifier>             Pre-approve a tool or command specifier
              --deny <specifier>              Deny a tool or command specifier
              --tools <tools>                 Restrict available tools
              --disallowed-tools <tools>      Disallow specific tools
              --disable-web-search            Disable web search for this session
              --output-format <format>        Output format (text, json, streaming-json)
              --json-schema <schema>          JSON schema for structured output
              --max-turns <count>             Maximum agentic turns in headless mode
              --no-auto-update                Disable automatic CLI updates
          -v, --version                       Display version number
          -h, --help                          Display help for command
        """;

    private const string MinimalGrokHelp = """
        Usage: grok [options]
          -v, --version
          -h, --help
        """;

    private readonly string _runtime =
        Path.Combine(Path.GetTempPath(), "loadout-grok-" + Guid.NewGuid().ToString("N"));

    public GrokAdapterTests() => Directory.CreateDirectory(_runtime);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_runtime, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Agent_registry_resolves_grok_as_a_built_in_adapter()
    {
        var registry = new AgentRegistry(
            new StubResolver(Path.Combine(Path.GetTempPath(), "grok")),
            new StubProcessLauncher(FullGrokHelp),
            new LauncherConfig());

        registry.Resolve("grok").Value.Should().BeOfType<GrokAdapter>()
            .Which.DisplayName.Should().Be("Grok CLI");
        registry.Resolve("GROK").Value.Should().BeOfType<GrokAdapter>();
        registry.Adapters.Select(a => a.Name).Should().Contain(["claude", "codex", "grok"]);
    }

    [Fact]
    public async Task Capabilities_are_detected_from_grok_help_output()
    {
        var adapter = CreateAdapter(FullGrokHelp);

        var descriptor = await adapter.DetectAsync();
        var capabilities = descriptor.Capabilities;

        descriptor.IsInstalled.Should().BeTrue();
        descriptor.Supports(AgentCapabilities.ExternalPrompt).Should().BeTrue();
        descriptor.Supports(AgentCapabilities.SessionResume).Should().BeTrue();
        descriptor.Supports(AgentCapabilities.Sandboxing).Should().BeTrue();
        descriptor.Supports(AgentCapabilities.Headless).Should().BeTrue();
        descriptor.Supports(AgentCapabilities.AdditionalDirectories).Should().BeFalse();
        descriptor.Supports(AgentCapabilities.McpConfig).Should().BeFalse();
        capabilities["cwd_flag"].Should().BeTrue();
        capabilities["permission_mode"].Should().BeTrue();
        capabilities["tool_restrictions"].Should().BeTrue();
        capabilities["disable_web_search"].Should().BeTrue();
        capabilities["model_selection"].Should().BeTrue();
        capabilities["output_schema"].Should().BeTrue();
        capabilities["max_turns"].Should().BeTrue();
        capabilities["no_auto_update"].Should().BeTrue();
        capabilities["opening_prompt"].Should().BeTrue();
    }

    [Fact]
    public async Task Working_directory_and_resume_session_reach_grok()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var context = Context(resumeSessionId: "sess-42");

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Arguments.Should().ContainInOrder("--cwd", _runtime);
        result.Value.Arguments.Should().ContainInOrder("--resume", "sess-42");
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Compiled_context_fits_inline_in_the_rules_flag()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var compiled = WriteCompiledContext("# Team Rules\nAlways run dotnet test.");
        var context = Context(compiled: compiled);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Arguments.Should().ContainInOrder("--rules", "# Team Rules\nAlways run dotnet test.");
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Oversized_compiled_context_passes_a_pointer_rule_instead_of_truncating()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var huge = new string('x', 25_000);
        var compiled = WriteCompiledContext(huge);
        var context = Context(compiled: compiled);

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);
        var rulesIndex = result.Value!.Arguments.ToList().IndexOf("--rules");
        rulesIndex.Should().BeGreaterOrEqualTo(0);
        result.Value.Arguments[rulesIndex + 1].Should().Contain(compiled.FilePath);
        result.Value.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_only_security_profile_sets_read_only_sandbox_and_plan_mode()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var profile = new SecurityProfile
        {
            Filesystem = FilesystemAccess.ReadOnly,
            Network = NetworkAccess.Restricted,
            DisallowedTools = { "WebFetch" },
            DeniedCommands = { "rm", "Bash(git push:*)" },
            AllowedTools = { "Read" },
        };

        var result = await adapter.BuildInvocationAsync(
            Context(
                security: profile,
                preApprovedCommands: ["dotnet test", "Bash(git status:*)"]));

        result.Succeeded.Should().BeTrue(result.Error);
        var args = result.Value!.Arguments;
        args.Should().ContainInOrder("--sandbox", "read-only");
        args.Should().ContainInOrder("--permission-mode", "plan");
        args.Should().Contain("--disable-web-search");
        args.Should().ContainInOrder("--allow", "Bash(dotnet test:*)");
        args.Should().ContainInOrder("--allow", "Bash(git status:*)");
        args.Should().ContainInOrder("--deny", "WebFetch");
        args.Should().ContainInOrder("--deny", "Bash(rm:*)");
        args.Should().ContainInOrder("--deny", "Bash(git push:*)");
        result.Value.Warnings.Should().ContainSingle()
            .Which.Should().Contain("allowed_tools");
    }

    [Fact]
    public async Task Restricted_filesystem_sets_read_only_sandbox_and_default_permission_mode()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var profile = new SecurityProfile
        {
            Filesystem = FilesystemAccess.Restricted,
        };

        var result = await adapter.BuildInvocationAsync(Context(security: profile));

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Arguments.Should().ContainInOrder("--sandbox", "read-only");
        result.Value.Arguments.Should().ContainInOrder("--permission-mode", "default");
    }

    [Fact]
    public async Task Model_and_headless_options_are_forwarded_to_grok()
    {
        var adapter = CreateAdapter(FullGrokHelp);
        var headless = new HeadlessOptions(
            Permission: HeadlessPermission.DenyUnlessAllowed,
            AllowedTools: ["Read", "Grep"],
            DeniedTools: ["Bash"],
            MaxTurns: 5,
            OutputSchemaJson: """{"type":"object"}""");

        var result = await adapter.BuildInvocationAsync(
            Context(model: "grok-4", headless: headless));

        result.Succeeded.Should().BeTrue(result.Error);
        var args = result.Value!.Arguments;
        args.Should().ContainInOrder("--model", "grok-4");
        args.Should().Contain("--no-auto-update");
        args.Should().ContainInOrder("--output-format", "streaming-json");
        args.Should().ContainInOrder("--max-turns", "5");
        args.Should().ContainInOrder("--permission-mode", "dontAsk");
        args.Should().ContainInOrder("--tools", "Read,Grep");
        args.Should().ContainInOrder("--disallowed-tools", "Bash");
        args.Should().ContainInOrder("--json-schema", """{"type":"object"}""");
    }

    [Fact]
    public async Task Minimal_grok_build_warns_instead_of_passing_unsupported_flags()
    {
        var adapter = CreateAdapter(MinimalGrokHelp);
        var compiled = WriteCompiledContext("Follow the rules.");
        var context = Context(
            compiled: compiled,
            resumeSessionId: "sess-99",
            model: "grok-4",
            openingPrompt: "Hello Grok");

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Arguments.Should().BeEmpty();
        result.Value.Warnings.Should().Contain(w => w.Contains("--resume"));
        result.Value.Warnings.Should().Contain(w => w.Contains("--rules"));
        result.Value.Warnings.Should().Contain(w => w.Contains("model option"));
        result.Value.Warnings.Should().Contain(w => w.Contains("Type: Hello Grok"));
    }

    [Fact]
    public async Task Project_skills_and_screen_reader_redraw_produce_honest_warnings()
    {
        var skillsDir = Path.Combine(_runtime, "projects", "demo", "agents", "grok", "skills", "review");
        Directory.CreateDirectory(skillsDir);
        File.WriteAllText(Path.Combine(skillsDir, "SKILL.md"), "# Review Skill");

        var adapter = CreateAdapter(FullGrokHelp);
        var context = Context(
            manifest: new ProjectManifest { Slug = "demo", Name = "Demo" },
            accessibility: AccessibilityProfile.Resolve(
                new AccessibilitySettings { Preset = AccessibilityPresets.ScreenReader }));

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error);
        result.Value!.Warnings.Should().Contain(w => w.Contains("screen-reader mode"));
        result.Value.Warnings.Should().Contain(w => w.Contains("agents/grok/skills"));
    }

    private GrokAdapter CreateAdapter(string help) =>
        new(
            new StubResolver(Path.Combine(Path.GetTempPath(), "grok")),
            new StubProcessLauncher(help),
            []);

    private CompiledContext WriteCompiledContext(string content)
    {
        var path = Path.Combine(_runtime, "compiled-context.md");
        File.WriteAllText(path, content);
        return new CompiledContext(path, [], [], null);
    }

    private AgentLaunchContext Context(
        CompiledContext? compiled = null,
        string? resumeSessionId = null,
        SecurityProfile? security = null,
        IReadOnlyList<string>? preApprovedCommands = null,
        string? model = null,
        HeadlessOptions? headless = null,
        string? openingPrompt = null,
        ProjectManifest? manifest = null,
        AccessibilitySettings? accessibility = null) =>
        new(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                _runtime,
                null,
                0,
                false),
            _runtime,
            _runtime,
            _runtime,
            [],
            Manifest: manifest,
            CompiledContext: compiled,
            Security: security,
            ResumeSessionId: resumeSessionId,
            PreApprovedCommands: preApprovedCommands,
            Model: model,
            Headless: headless,
            Accessibility: accessibility,
            OpeningPrompt: openingPrompt);
}
