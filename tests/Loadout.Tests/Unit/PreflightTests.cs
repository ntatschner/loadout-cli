using Loadout.Core.Context;
using Loadout.Core.Diagnostics;
using Loadout.Core.Git;
using Loadout.Core.Workspace;
using Loadout.Models.Agents;
using Loadout.Models.Diagnostics;
using Loadout.Models.Projects;
using Loadout.Platform.Common;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Preflight decides whether a launch may proceed (spec section 59). What
/// blocks and what merely warns is the substance of it: too strict and the
/// launcher is unusable offline, too lax and an agent starts without the
/// credentials or context the user assumed it had.
/// </summary>
public sealed class PreflightTests
{
    private static readonly AgentDescriptor InstalledAgent = new(
        "claude", "Claude Code", true, "/usr/local/bin/claude", "2.1.0",
        new Dictionary<string, bool>());

    private static PreflightService Service(Dictionary<string, string>? secrets = null) =>
        new(
            new GitManager(new ProcessLauncher(), new ExecutableResolver(
                new FakeEnvironmentProvider("/home/test"), [])),
            new FakeSecretProvider(secrets));

    [Fact]
    public async Task A_launch_with_everything_present_is_allowed()
    {
        var result = await Service().RunAsync(Context());

        result.Value!.CanLaunch.Should().BeTrue();
    }

    [Fact]
    public async Task A_missing_agent_blocks_the_launch()
    {
        var context = Context() with
        {
            Agent = AgentDescriptor.NotInstalled("claude", "Claude Code"),
        };

        var result = await Service().RunAsync(context);

        result.Value!.CanLaunch.Should().BeFalse();
        result.Value.Blocking.Should().ContainSingle().Which.Category.Should().Be("Agent");
    }

    [Fact]
    public async Task A_missing_working_directory_blocks_the_launch()
    {
        var context = Context() with { WorkingDirectory = "/nowhere/at/all" };

        var result = await Service().RunAsync(context);

        result.Value!.CanLaunch.Should().BeFalse();
        result.Value.Blocking.Should().Contain(c => c.Category == "Repository");
    }

    [Fact]
    public async Task Running_offline_warns_but_never_blocks()
    {
        var context = Context() with { SyncOutcome = WorkspaceSyncOutcome.Offline };

        var result = await Service().RunAsync(context);

        // Spec section 48 makes offline an explicitly supported mode. Blocking
        // here would mean an unreachable server stops all work.
        result.Value!.CanLaunch.Should().BeTrue();
        result.Value.Warnings.Should().Contain(c => c.Category == "Workspace");
    }

    [Fact]
    public async Task A_workspace_conflict_warns_but_never_blocks()
    {
        var context = Context() with { SyncOutcome = WorkspaceSyncOutcome.Conflict };

        var result = await Service().RunAsync(context);

        result.Value!.CanLaunch.Should().BeTrue();
        result.Value.Warnings.Should().Contain(c => c.Detail.Contains("diverged"));
    }

    [Fact]
    public async Task A_required_secret_that_cannot_be_resolved_blocks_the_launch()
    {
        var manifest = Manifest();
        manifest.Environment["ANTHROPIC_API_KEY"] = new EnvironmentBinding
        {
            Secret = "anthropic/default",
            Required = true,
        };

        var result = await Service().RunAsync(Context() with { Manifest = manifest });

        result.Value!.CanLaunch.Should().BeFalse();
        result.Value.Blocking.Should().Contain(c => c.Category == "Environment");
    }

    [Fact]
    public async Task An_optional_secret_that_is_absent_only_warns()
    {
        var manifest = Manifest();
        manifest.Environment["OPTIONAL_KEY"] = new EnvironmentBinding
        {
            Secret = "vendor/optional",
            Required = false,
        };

        var result = await Service().RunAsync(Context() with { Manifest = manifest });

        result.Value!.CanLaunch.Should().BeTrue();
        result.Value.Environment.Should().NotContainKey("OPTIONAL_KEY");
    }

    [Fact]
    public async Task A_resolved_secret_reaches_the_environment_but_never_the_report()
    {
        var manifest = Manifest();
        manifest.Environment["ANTHROPIC_API_KEY"] = new EnvironmentBinding
        {
            Secret = "anthropic/default",
        };

        var service = Service(new Dictionary<string, string>
        {
            ["anthropic/default"] = "sk-ant-the-actual-secret-value",
        });

        var result = await service.RunAsync(Context() with { Manifest = manifest });

        result.Value!.Environment["ANTHROPIC_API_KEY"].Should().Be("sk-ant-the-actual-secret-value");

        // The checks end up in logs and in the doctor report, so they must name
        // the reference and never the value (spec sections 52 and 80).
        result.Value.Checks.Should().NotContain(
            c => c.Detail.Contains("sk-ant-the-actual-secret-value"));

        result.Value.Checks.Should().Contain(c => c.Detail.Contains("anthropic/default"));
    }

    [Fact]
    public async Task A_missing_context_source_is_surfaced_as_a_warning()
    {
        var context = Context() with
        {
            CompiledContext = new CompiledContext(
                "/runtime/compiled-context.md",
                [new ContextSource("projects/x/context/a.md", "Project context", 100)],
                ["projects/x/context/deleted.md"],
                null),
        };

        var result = await Service().RunAsync(context);

        result.Value!.CanLaunch.Should().BeTrue();
        result.Value.Warnings.Should().Contain(c => c.Detail.Contains("deleted.md"));
    }

    [Fact]
    public async Task Launching_with_no_compiled_context_warns_rather_than_blocking()
    {
        var result = await Service().RunAsync(Context() with { CompiledContext = null });

        // A project with no manifest still launches; the agent simply starts
        // with repository content only, and the user is told.
        result.Value!.CanLaunch.Should().BeTrue();
        result.Value.Warnings.Should().Contain(c => c.Category == "Context");
    }

    [Fact]
    public async Task Every_check_carries_a_reason_whatever_its_severity()
    {
        var result = await Service().RunAsync(Context() with { SyncOutcome = WorkspaceSyncOutcome.Offline });

        // Spec section 5 requires gaps to be documented and surfaced, which is
        // worth nothing if a check can appear with an empty explanation.
        result.Value!.Checks.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Detail));
        result.Value.Checks.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.Name));
    }

    private static PreflightContext Context() => new(
        new ProjectResolution(
            new ProjectRegistryEntry { Slug = "starstats", Name = "StarStats" },
            Directory.GetCurrentDirectory(),
            null,
            0,
            false),
        Manifest(),
        Directory.GetCurrentDirectory(),
        InstalledAgent,
        new CompiledContext(
            "/runtime/compiled-context.md",
            [new ContextSource("projects/starstats/context/a.md", "Project context", 100)],
            [],
            null),
        WorkspaceSyncOutcome.Synced);

    private static ProjectManifest Manifest() => new()
    {
        Slug = "starstats",
        Name = "StarStats",
    };
}
