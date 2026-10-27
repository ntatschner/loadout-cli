using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Agents.Codex;
using Loadout.Agents.Generic;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Configuration;
using Loadout.Core.Projects;
using Loadout.Models.Agents;
using Loadout.Models.Configuration;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The agent's own remote control: which switch each agent is given, when the
/// launch warns and when it refuses, and what doctor says about it.
/// </summary>
/// <remarks>
/// The help text is a stub, so nothing here needs Claude Code installed or a
/// claude.ai sign-in. Whether Claude Code's switch then does what it says is
/// not something a test here can show.
/// </remarks>
public sealed class RemoteControlTests
{
    /// <summary>The two lines of Claude Code 2.1.311's help that matter here.</summary>
    private const string ClaudeHelp = """
        Usage: claude [options] [command] [prompt]
          --remote-control [name]               Start an interactive session with Remote
                                                Control enabled (optionally named)
        """;

    private const string OlderClaudeHelp = """
        Usage: claude [options] [command] [prompt]
          --model <model>                     Model for the current session
        """;

    // The Claude adapter

    [Fact]
    public async Task Claude_is_given_its_switch_named_after_the_project()
    {
        var invocation = await ClaudeAsync(ClaudeHelp, remote: true);

        // Always named: the option's value is optional, so a bare switch
        // would take the next positional argument as the name.
        invocation.Arguments.Should().ContainInConsecutiveOrder("--remote-control", "Demo");
    }

    [Fact]
    public async Task Claude_is_not_given_the_switch_unless_the_launch_settled_on_it()
    {
        var invocation = await ClaudeAsync(ClaudeHelp, remote: false);

        invocation.Arguments.Should().NotContain("--remote-control");
    }

    [Fact]
    public async Task A_headless_node_never_gets_the_switch()
    {
        var invocation = await ClaudeAsync(ClaudeHelp, remote: true, headless: new HeadlessOptions());

        invocation.Arguments.Should().NotContain("--remote-control");
    }

    [Fact]
    public async Task Claude_advertises_remote_control_only_when_its_help_does()
    {
        (await Claude(ClaudeHelp).DetectAsync()).Supports(AgentCapabilities.RemoteControl).Should().BeTrue();
        (await Claude(OlderClaudeHelp).DetectAsync()).Supports(AgentCapabilities.RemoteControl).Should().BeFalse();
    }

    [Fact]
    public async Task Codex_never_advertises_remote_control()
    {
        // Even with the words in its help: Loadout knows no Codex switch to pass.
        var codex = new CodexAdapter(new StubResolver(Path.Combine(Path.GetTempPath(), "codex")), new StubProcessLauncher(ClaudeHelp), []);

        (await codex.DetectAsync()).Supports(AgentCapabilities.RemoteControl).Should().BeFalse();
    }

    // Generic agents

    [Fact]
    public async Task A_described_agent_gets_its_declared_switch_before_the_passthrough()
    {
        var adapter = Generic(["--remote", "${PROJECT_SLUG}"]);

        (await adapter.DetectAsync()).Supports(AgentCapabilities.RemoteControl).Should().BeTrue();

        var result = await adapter.BuildInvocationAsync(Context(remote: true, passthrough: ["--extra"]));

        result.Value!.Arguments.Should().Equal("--base", "--remote", "demo", "--extra");
    }

    [Fact]
    public async Task A_described_agent_with_no_switch_cannot_do_it()
    {
        var adapter = Generic([]);

        (await adapter.DetectAsync()).Supports(AgentCapabilities.RemoteControl).Should().BeFalse();

        var result = await adapter.BuildInvocationAsync(Context(remote: false));

        result.Value!.Arguments.Should().Equal("--base");
    }

    // Warn or refuse

    private static readonly AgentDescriptor Capable = Descriptor("claude", "Claude Code", remote: true);
    private static readonly AgentDescriptor Incapable = Descriptor("codex", "Codex", remote: false);

    [Fact]
    public void The_default_on_an_agent_that_can_turns_it_on_and_says_nothing()
    {
        RemoteControlPolicy.Decide(null, byDefault: true, Capable, headless: false)
            .Should().Be(new RemoteControlDecision(true));
    }

    [Fact]
    public void The_default_on_an_agent_that_cannot_warns_and_starts_without_it()
    {
        var decision = RemoteControlPolicy.Decide(null, byDefault: true, Incapable, headless: false);

        decision.On.Should().BeFalse();
        decision.Refusal.Should().BeNull();
        decision.Warning.Should().Contain("Codex").And.Contain("--no-remote");
    }

    [Fact]
    public void Asking_on_this_launch_for_an_agent_that_cannot_is_refused_and_says_why()
    {
        var decision = RemoteControlPolicy.Decide(true, byDefault: false, Incapable, headless: false);

        decision.On.Should().BeFalse();
        decision.Refusal.Should().Contain("Codex does not advertise remote control");
    }

    [Fact]
    public void No_remote_overrides_the_default()
    {
        RemoteControlPolicy.Decide(false, byDefault: true, Capable, headless: false)
            .Should().Be(new RemoteControlDecision(false));

        // Nor is anything said about an agent that could not have done it anyway.
        RemoteControlPolicy.Decide(false, byDefault: true, Incapable, headless: false)
            .Should().Be(new RemoteControlDecision(false));
    }

    [Fact]
    public void Remote_turns_it_on_with_the_default_off()
    {
        RemoteControlPolicy.Decide(true, byDefault: false, Capable, headless: false).On.Should().BeTrue();
    }

    [Fact]
    public void Off_by_default_means_off()
    {
        RemoteControlPolicy.Decide(null, byDefault: false, Capable, headless: false)
            .Should().Be(new RemoteControlDecision(false));
    }

    [Fact]
    public void A_headless_node_is_neither_switched_on_nor_refused()
    {
        RemoteControlPolicy.Decide(true, byDefault: true, Incapable, headless: true)
            .Should().Be(new RemoteControlDecision(false));
    }

    // The flags

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public void The_flags_read_as_one_choice(bool remote, bool noRemote, bool? expected)
    {
        RemoteControlOption.From(remote, noRemote).Should().Be(expected);
    }

    [Fact]
    public void Both_flags_at_once_are_refused()
    {
        RemoteControlOption.Validate(true, true, Spectre.Console.ValidationResult.Success).Successful.Should().BeFalse();
        RemoteControlOption.Validate(true, false, Spectre.Console.ValidationResult.Success).Successful.Should().BeTrue();
    }

    [Fact]
    public void The_setting_is_registered_as_a_flag_and_off_until_set()
    {
        var entry = ConfigKeys.All.Single(e => e.Key == "remote-control");

        entry.IsFlag.Should().BeTrue();
        entry.Read(new LauncherConfig(), new MachineConfig()).Should().Be("false");

        var config = new LauncherConfig();
        entry.Write(config, new MachineConfig(), "true");
        config.RemoteControl.Enabled.Should().BeTrue();
    }

    // Doctor

    [Fact]
    public async Task Doctor_says_remote_control_is_off_and_how_to_ask_for_it()
    {
        var check = await DoctorAsync(enabled: false, defaultAgent: "claude", Capable, Incapable);

        check.Severity.Should().Be(DiagnosticSeverity.Info);
        check.Detail.Should().Contain("off by default").And.Contain("--remote");
    }

    [Fact]
    public async Task Doctor_names_which_agents_start_with_it_and_which_cannot()
    {
        var check = await DoctorAsync(enabled: true, defaultAgent: "claude", Capable, Incapable);

        check.Severity.Should().Be(DiagnosticSeverity.Info);
        check.Detail.Should().Contain("Starts with it: Claude Code").And.Contain("starts without it: Codex");
    }

    [Fact]
    public async Task Doctor_warns_when_the_default_agent_cannot_do_it()
    {
        var check = await DoctorAsync(enabled: true, defaultAgent: "codex", Capable, Incapable);

        check.Severity.Should().Be(DiagnosticSeverity.Warning);
    }

    private static async Task<DiagnosticCheck> DoctorAsync(bool enabled, string defaultAgent, params AgentDescriptor[] agents)
    {
        var config = new LauncherConfig { DefaultAgent = defaultAgent };
        config.RemoteControl.Enabled = enabled;

        var contributor = new AgentDiagnosticContributor(new Registry(agents), new StubConfig(config));

        var checks = await contributor.ContributeAsync();

        return checks.Single(c => c.Name == "Remote control");
    }

    private static AgentDescriptor Descriptor(string name, string display, bool remote) =>
        new(name, display, true, "/bin/" + name, "1.0",
            new Dictionary<string, bool> { [AgentCapabilities.RemoteControl] = remote });

    private static ClaudeAdapter Claude(string help) =>
        new(new StubResolver(Path.Combine(Path.GetTempPath(), "claude")), new StubProcessLauncher(help), []);

    private static async Task<AgentInvocation> ClaudeAsync(string help, bool remote, HeadlessOptions? headless = null)
    {
        var result = await Claude(help).BuildInvocationAsync(Context(remote, headless: headless));

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }

    private static GenericAgentAdapter Generic(List<string> remoteArguments) =>
        new("mine",
            new GenericAgentDefinition
            {
                Executable = "mine",
                Arguments = ["--base"],
                RemoteControlArguments = remoteArguments,
            },
            new StubResolver(Path.Combine(Path.GetTempPath(), "mine")),
            new StubProcessLauncher(string.Empty),
            []);

    private static AgentLaunchContext Context(bool remote, HeadlessOptions? headless = null, IReadOnlyList<string>? passthrough = null) =>
        new(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                Path.GetTempPath(),
                null,
                0,
                false),
            Path.GetTempPath(),
            Path.GetTempPath(),
            null,
            passthrough ?? [],
            Headless: headless,
            RemoteControl: remote);

    private sealed class Registry(IReadOnlyList<AgentDescriptor> descriptors) : IAgentRegistry
    {
        public IReadOnlyList<IAgentAdapter> Adapters => [];

        public OperationResult<IAgentAdapter> Resolve(string name) => throw new NotSupportedException();

        public Task<IReadOnlyList<AgentDescriptor>> DetectAllAsync(CancellationToken ct = default) =>
            Task.FromResult(descriptors);
    }

    private sealed class StubConfig(LauncherConfig config) : IConfigurationService
    {
        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<LauncherConfig>.Ok(config));

        public Task<OperationResult> SaveConfigAsync(LauncherConfig changed, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<MachineConfig>.Ok(new MachineConfig()));

        public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
