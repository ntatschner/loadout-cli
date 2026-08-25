using Loadout.Agents;
using Loadout.Models;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which layer decides the agent, and whether it says so.
/// <para>
/// Four layers can answer, in a fixed order, and until the answer carried its
/// source "why is it launching that one?" had no answer short of reading the
/// code. The order itself is unchanged; what is tested is that it is the order
/// claimed, and that the resolution reports where it came from.
/// </para>
/// </summary>
public sealed class AgentResolutionTests
{
    private static ProjectResolution Project(string registryAgent) =>
        new(
            new ProjectRegistryEntry { Slug = "alpha", Name = "Alpha", DefaultAgent = registryAgent },
            "/repos/alpha",
            null,
            0,
            false);

    private static ProjectManifest Manifest(string agent)
    {
        var manifest = new ProjectManifest();

        manifest.Agents.Default = agent;

        return manifest;
    }

    private static LauncherConfig Config(string agent) => new() { DefaultAgent = agent };

    [Fact]
    public void What_was_asked_for_on_the_command_line_wins()
    {
        var resolved = AgentLauncher.ResolveAgent(
            new LaunchRequest("alpha", "codex"),
            Manifest("claude"),
            Project("claude"),
            Config("claude"));

        resolved.Value.Should().Be("codex");
        resolved.Source.Should().Be(SettingSource.CommandLine);
    }

    [Fact]
    public void The_project_manifest_beats_the_registry_and_the_personal_default()
    {
        var resolved = AgentLauncher.ResolveAgent(
            new LaunchRequest("alpha"),
            Manifest("codex"),
            Project("claude"),
            Config("claude"));

        resolved.Value.Should().Be("codex");
        resolved.Source.Should().Be(SettingSource.ProjectManifest);
    }

    [Fact]
    public void The_registry_is_used_when_the_workspace_names_none()
    {
        var resolved = AgentLauncher.ResolveAgent(
            new LaunchRequest("alpha"),
            manifest: null,
            Project("codex"),
            Config("claude"));

        resolved.Value.Should().Be("codex");
        resolved.Source.Should().Be(SettingSource.ProjectRegistry);
    }

    [Fact]
    public void The_personal_default_is_the_last_resort_and_says_so()
    {
        var resolved = AgentLauncher.ResolveAgent(
            new LaunchRequest("alpha"),
            manifest: null,
            Project(string.Empty),
            Config("claude"));

        resolved.Value.Should().Be("claude");
        resolved.Source.Should().Be(SettingSource.SharedConfiguration);

        // The sentence somebody is shown when they ask why. It has to read as
        // an explanation rather than as an enum name.
        resolved.Explanation.Should().Contain("shared between machines");
    }

    [Theory]
    [InlineData(SettingSource.CommandLine)]
    [InlineData(SettingSource.ProjectManifest)]
    [InlineData(SettingSource.ProjectRegistry)]
    [InlineData(SettingSource.MachineConfiguration)]
    [InlineData(SettingSource.SharedConfiguration)]
    [InlineData(SettingSource.BuiltIn)]
    public void Every_source_can_explain_itself(SettingSource source)
    {
        // A source with no sentence would render as nothing at all in the one
        // place this exists to serve.
        new Resolved<string>("x", source).Explanation.Should().NotBeNullOrWhiteSpace();
    }
}
