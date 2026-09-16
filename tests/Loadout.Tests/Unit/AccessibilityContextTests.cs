using FluentAssertions;
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
/// Whether the person's own profile reaches the session working for them.
/// </summary>
/// <remarks>
/// Where it lands is the whole point. Everything above it says what the code
/// is and what the task is; this says who is reading, which is narrower, so
/// it has to be the last thing in the file. An agent carries the end of its
/// context into its first sentence.
/// </remarks>
public sealed class AccessibilityContextTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;

    public AccessibilityContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-access-" + Guid.NewGuid().ToString("N"));
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

    private async Task<string> CompileAsync(IConfigurationService? configuration, bool withTasks = false)
    {
        var compiler = new ContextCompiler(
            new NoOpFilePermissions(),
            new RuleService(),
            new MemoryService(TimeProvider.System),
            symbols: null,
            withTasks ? new OneTask() : null,
            configuration);

        var manifest = new ProjectManifest
        {
            Slug = "demo",
            Name = "Demo",
            Context = { Tasks = withTasks },
        };

        var result = await compiler.CompileAsync(manifest, _workspace, _runtime, "claude");

        result.Failed.Should().BeFalse(result.Error);

        return await File.ReadAllTextAsync(result.Value!.FilePath);
    }

    /// <summary>A project with something open, so a section follows if the order is wrong.</summary>
    private sealed class OneTask : Loadout.Core.Tasks.ITaskService
    {
        public Task<OperationResult<IReadOnlyList<Loadout.Models.Tasks.TaskItem>>> ListAsync(
            string projectSlug, CancellationToken ct = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<Loadout.Models.Tasks.TaskItem>>.Ok(
                [new Loadout.Models.Tasks.TaskItem
                {
                    Id = "ship-it",
                    Title = "Ship the thing",
                    State = Loadout.Models.Tasks.TaskState.Open,
                    DeclaredBy = "nigel",
                    DeclaredUtc = DateTimeOffset.UnixEpoch,
                }]));

        public Task<OperationResult<Loadout.Models.Tasks.TaskItem>> DeclareAsync(
            string projectSlug,
            string id,
            Loadout.Models.Tasks.TaskState state,
            string declaredBy,
            string? title = null,
            string? note = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context declares nothing");

        public Task<OperationResult> RemoveAsync(string projectSlug, string id, CancellationToken ct = default) =>
            throw new NotSupportedException("compiling a context removes nothing");
    }

    [Fact]
    public async Task A_person_who_has_set_nothing_is_told_nothing_about_it()
    {
        var text = await CompileAsync(new StubConfig(new LauncherConfig()));

        text.Should().NotContain(AccessibilityProfile.Heading);
        text.Should().NotContain("accessibility");
    }

    [Fact]
    public async Task A_compiler_built_without_configuration_compiles_what_it_compiled_before()
    {
        (await CompileAsync(null)).Should().NotContain(AccessibilityProfile.Heading);
    }

    [Fact]
    public async Task The_profile_is_the_last_thing_in_the_file()
    {
        var config = new LauncherConfig
        {
            Accessibility = new AccessibilitySettings { Preset = AccessibilityPresets.Dyslexia },
        };

        // With a section that would otherwise follow it, so the assertion is
        // about the order rather than about there being nothing else to write.
        var text = await CompileAsync(new StubConfig(config), withTasks: true);

        text.Should().Contain("Open tasks");
        text.Should().Contain(AccessibilityProfile.Heading);
        text.Should().Contain("<!-- source: config.yaml, accessibility -->");

        // A section's own subheadings start with three hashes, and "###"
        // contains "## ", so the search is for a heading at the start of a
        // line at the top level.
        var heading = text.IndexOf("\n## " + AccessibilityProfile.Heading, StringComparison.Ordinal);

        heading.Should().BeGreaterThan(0);

        text.IndexOf("\n## ", heading + 1, StringComparison.Ordinal)
            .Should().Be(-1, "who is reading is the narrowest fact, so nothing may follow it");
    }

    [Fact]
    public async Task A_configuration_that_cannot_be_read_does_not_cost_the_session_its_context()
    {
        // Somebody else's error to report, and doctor is where a person finds
        // out. A launch that failed over it would be a broken preference
        // taking the whole session with it.
        var text = await CompileAsync(new StubConfig(null));

        text.Should().NotContain(AccessibilityProfile.Heading);
        text.Should().Contain("# ", "the rest of the context is still there");
    }

    private sealed class StubConfig : IConfigurationService
    {
        private readonly LauncherConfig? _config;

        public StubConfig(LauncherConfig? config) => _config = config;

        public Task<OperationResult<LauncherConfig>> LoadConfigAsync(CancellationToken ct = default) =>
            Task.FromResult(_config is null
                ? OperationResult<LauncherConfig>.Fail("config.yaml could not be read.")
                : OperationResult<LauncherConfig>.Ok(_config));

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
