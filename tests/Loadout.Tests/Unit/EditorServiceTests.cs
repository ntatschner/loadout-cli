using Loadout.Core.Editors;
using Loadout.Models.Configuration;
using Loadout.Models.Projects;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Which editor profile a project opens under, and what happens when the answer
/// cannot be worked out.
/// </summary>
public sealed class EditorServiceTests
{
    private static LauncherConfig Config(params (string Agent, string Profile)[] profiles)
    {
        var config = new LauncherConfig();

        foreach (var (agent, profile) in profiles)
        {
            config.Editor.Profiles[agent] = profile;
        }

        return config;
    }

    private static ProjectRegistryEntry Project(string agent = "claude", string editorProfile = "") =>
        new() { Slug = "alpha", Name = "Alpha", DefaultAgent = agent, EditorProfile = editorProfile };

    private static EditorService Service() =>
        new(new StubResolver(), new StubProcesses());

    [Fact]
    public void The_profile_configured_for_the_agent_is_used()
    {
        Service()
            .ProfileFor(Config(("claude", "Agents")), Project())
            .Should().Be("Agents");
    }

    [Fact]
    public void A_project_can_override_the_profile_its_agent_would_use()
    {
        // Set on the project deliberately, so it wins over the agent's default.
        Service()
            .ProfileFor(Config(("claude", "Agents")), Project(editorProfile: "Alpha-only"))
            .Should().Be("Alpha-only");
    }

    [Fact]
    public void Asking_for_a_different_agent_gets_that_agents_profile()
    {
        // The same project opened for Codex should put the editor in the state
        // Codex wants, not the one Claude wants.
        Service()
            .ProfileFor(Config(("claude", "Agents"), ("codex", "Codex")), Project(), agent: "codex")
            .Should().Be("Codex");
    }

    [Fact]
    public void An_agent_with_no_profile_configured_opens_the_editor_normally()
    {
        // Null rather than a made-up name: somebody who does not use profiles
        // must get the editor they always get.
        Service()
            .ProfileFor(Config(), Project())
            .Should().BeNull();
    }

    [Fact]
    public void A_blank_profile_is_the_same_as_none()
    {
        Service()
            .ProfileFor(Config(("claude", "   ")), Project())
            .Should().BeNull();
    }

    [Fact]
    public void A_profile_is_only_called_missing_when_the_profiles_are_known()
    {
        // Null profiles means "could not be read". Reporting that as "missing"
        // would send somebody looking for a problem they do not have, which is
        // worse than saying nothing.
        var unknown = new EditorState("code", "/usr/bin/code", Profiles: null);

        unknown.IsMissing("Agents").Should().BeFalse();

        var known = new EditorState("code", "/usr/bin/code", Profiles: ["Agents"]);

        known.IsMissing("Agents").Should().BeFalse();
        known.IsMissing("Nope").Should().BeTrue();
    }

    [Fact]
    public void An_editor_with_no_profiles_is_not_the_same_as_one_that_could_not_be_read()
    {
        // Empty means the editor genuinely has none beyond the default, and a
        // name asked for against that really is missing.
        var none = new EditorState("code", "/usr/bin/code", Profiles: []);

        none.IsMissing("Agents").Should().BeTrue();
    }

    [Fact]
    public void An_editor_that_is_not_installed_says_so()
    {
        new EditorState("code", Path: null, Profiles: null).IsInstalled.Should().BeFalse();
        new EditorState("code", "/usr/bin/code", Profiles: null).IsInstalled.Should().BeTrue();
    }

    private sealed class StubResolver : Loadout.Platform.Abstractions.IExecutableResolver
    {
        public string? Resolve(string name, IReadOnlyList<string>? additionalPaths = null) => null;

        public IReadOnlyList<string> StandardSearchPaths => [];
    }


    [Fact]
    public async Task A_configured_profile_is_still_left_off_the_command_line()
    {
        var processes = new Loadout.Tests.Fakes.StubProcessLauncher(string.Empty);

        var service = new Loadout.Core.Editors.EditorService(
            new Loadout.Tests.Fakes.StubResolver("C:/fake/code"), processes);

        await service.OpenAsync(Config(("claude", "Agents")), Project(), "C:/work/alpha");

        var request = processes.Detached;

        request.Should().NotBeNull("the editor is started detached, not run to completion");

        // The whole bug, in one assertion. Asked for a folder and a profile
        // together the editor opens a window containing neither and reports
        // nothing; asked for the folder alone it opens every time. Bisecting the
        // command line put it on --profile and cleared -n, the environment, the
        // working directory and the handoff to a running instance, all of which
        // had been blamed first.
        request!.Arguments.Should().Equal("C:/work/alpha");
        request.Arguments.Should().NotContain("--profile");
        request.Arguments.Should().NotContain("Agents");
    }

    [Fact]
    public async Task Opening_passes_only_the_folder()
    {
        var processes = new Loadout.Tests.Fakes.StubProcessLauncher(string.Empty);

        var service = new Loadout.Core.Editors.EditorService(
            new Loadout.Tests.Fakes.StubResolver("C:/fake/code"), processes);

        await service.OpenAsync(Config(), Project(), "C:/work/alpha");

        var request = processes.Detached!;

        request.Arguments.Should().Equal("C:/work/alpha");

        // The editor's own variables are withheld. VS Code sets
        // ELECTRON_RUN_AS_NODE for its shim, and a copy of that makes the editor
        // we start run as Node and read the folder as a module path.
        request.RemoveEnvironmentPrefixes.Should().Contain("ELECTRON_");
        request.RemoveEnvironmentPrefixes.Should().Contain("VSCODE_");

        // Started inside the folder it is opening. Launched from the Start Menu
        // the launcher's working directory is where it was installed, and the
        // editor inherited that.
        request.WorkingDirectory.Should().Be("C:/work/alpha");
    }

    private sealed class StubProcesses : Loadout.Platform.Abstractions.IProcessLauncher
    {
        public Task<Loadout.Models.Results.OperationResult<Loadout.Platform.Abstractions.ProcessOutcome>> RunAsync(
            Loadout.Platform.Abstractions.ProcessRequest request,
            TimeSpan? timeout = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException("These tests never start anything.");

        public Task<Loadout.Models.Results.OperationResult<int>> RunInteractiveAsync(
            Loadout.Platform.Abstractions.ProcessRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException("These tests never start anything.");

        public Loadout.Models.Results.OperationResult StartDetached(
            Loadout.Platform.Abstractions.ProcessRequest request) =>
            throw new NotSupportedException("These tests never start anything.");
    }
}
