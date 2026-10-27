using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Codex;
using Loadout.Core.Configuration;
using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether every agent that takes compiled context is told to follow its rules
/// without saying so.
/// </summary>
public sealed class QuietRulesContextTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;

    public QuietRulesContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-quiet-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");

        Directory.CreateDirectory(_runtime);
        Directory.CreateDirectory(Path.Combine(_workspace, "projects", "demo"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task<CompiledContext> CompileAsync(IConfigurationService? configuration)
    {
        var compiler = new ContextCompiler(
            new NoOpFilePermissions(),
            new RuleService(),
            new MemoryService(TimeProvider.System),
            configuration: configuration);

        var result = await compiler.CompileAsync(
            new ProjectManifest { Slug = "demo", Name = "Demo" }, _workspace, _runtime, "claude");

        result.Failed.Should().BeFalse(result.Error);

        return result.Value!;
    }

    [Fact]
    public async Task Every_compiled_context_says_to_follow_the_rules_without_saying_so()
    {
        var text = await File.ReadAllTextAsync((await CompileAsync(new StubConfig(new LauncherConfig()))).FilePath);

        text.Should().Contain("## " + ContextCompiler.QuietRulesHeading);
        text.Should().Contain("without saying that you are");
        text.Should().Contain("only when it stops or changes something you were asked for");
    }

    [Fact]
    public async Task A_compiler_built_without_configuration_still_says_it()
    {
        var text = await File.ReadAllTextAsync((await CompileAsync(null)).FilePath);

        text.Should().Contain("## " + ContextCompiler.QuietRulesHeading, "it is the default, not a setting");
    }

    [Fact]
    public async Task Turning_quiet_rules_off_leaves_the_section_out()
    {
        var config = new LauncherConfig { InstructionContext = { QuietRules = false } };

        var text = await File.ReadAllTextAsync((await CompileAsync(new StubConfig(config))).FilePath);

        text.Should().NotContain(ContextCompiler.QuietRulesHeading);
    }

    [Fact]
    public async Task Codex_is_given_it_in_the_AGENTS_md_it_reads()
    {
        var compiled = await CompileAsync(new StubConfig(new LauncherConfig()));

        var adapter = new CodexAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "codex")),
            new StubProcessLauncher("Usage: codex [options]\n  --model <model>"),
            []);

        var result = await adapter.BuildInvocationAsync(new AgentLaunchContext(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" }, _root, null, 0, false),
            _root,
            _runtime,
            _workspace,
            [],
            new ProjectManifest { Slug = "demo", Name = "Demo" },
            compiled));

        result.Succeeded.Should().BeTrue(result.Error);

        var agents = Path.Combine(_runtime, "codex-home", "AGENTS.md");

        File.Exists(agents).Should().BeTrue();
        (await File.ReadAllTextAsync(agents)).Should().Contain("## " + ContextCompiler.QuietRulesHeading);
    }

    private sealed class StubConfig : IConfigurationService
    {
        private readonly LauncherConfig _config;

        public StubConfig(LauncherConfig config) => _config = config;

        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<LauncherConfig>.Ok(_config));

        public Task<OperationResult> SaveConfigAsync(LauncherConfig config, CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context writes no configuration");

        public Task<OperationResult<MachineConfig>> LoadMachineAsync(CancellationToken ct = default) =>
            Task.FromResult(OperationResult<MachineConfig>.Ok(new MachineConfig()));

        public Task<OperationResult> SaveMachineAsync(MachineConfig machine, CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context writes no configuration");

        public Task<OperationResult<LauncherConfig>> UpdateConfigAsync(Action<LauncherConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context writes no configuration");

        public Task<OperationResult<MachineConfig>> UpdateMachineAsync(Action<MachineConfig> change, CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context writes no configuration");
    }
}
