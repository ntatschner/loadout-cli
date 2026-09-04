using FluentAssertions;
using Loadout.Core.Editors;
using Loadout.Models.Configuration;
using Loadout.Models.Editors;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// An editor described in configuration rather than compiled in, and what that
/// buys: a profile that is actually applied.
/// </summary>
/// <remarks>
/// Naming a different command was always possible, so the seam earns nothing
/// unless it can say how that command takes a profile. VS Code takes an
/// argument and refuses to open a folder alongside it; Neovim takes an
/// environment variable and works; an editor nobody has described takes
/// neither, and must not be reported as having ignored something.
/// </remarks>
public sealed class EditorDefinitionTests
{
    private static ProjectRegistryEntry Project(string agent = "claude") =>
        new() { Slug = "alpha", Name = "Alpha", DefaultAgent = agent, EditorProfile = "" };

    private static LauncherConfig Config(string command, string? profile = null)
    {
        var config = new LauncherConfig { Editor = { Command = command } };

        if (profile is not null)
        {
            config.Editor.Profiles["claude"] = profile;
        }

        return config;
    }

    private static (EditorService Service, StubProcessLauncher Processes) Service()
    {
        var processes = new StubProcessLauncher(string.Empty);

        return (new EditorService(new StubResolver("C:/fake/editor"), processes), processes);
    }

    [Fact]
    public async Task Neovim_is_told_its_profile_through_the_environment()
    {
        var (service, processes) = Service();

        await service.OpenAsync(Config("nvim", "work"), Project(), "C:/work/alpha");

        // The whole point of the seam. NVIM_APPNAME names the configuration
        // directory the editor loads, so the profile is applied by starting it
        // rather than by asking it to switch afterwards — and unlike VS Code's
        // argument, it does not stop the folder opening.
        var request = processes.Interactive;

        request.Should().NotBeNull("a terminal editor is run in the terminal");
        request!.Environment.Should().ContainKey("NVIM_APPNAME");
        request.Environment!["NVIM_APPNAME"].Should().Be("work");
    }

    [Fact]
    public async Task Neovim_runs_in_the_terminal_rather_than_being_let_go()
    {
        var (service, processes) = Service();

        await service.OpenAsync(Config("nvim"), Project(), "C:/work/alpha");

        // A terminal editor started detached is a process with nowhere to draw.
        processes.Interactive.Should().NotBeNull();
        processes.Detached.Should().BeNull();
    }

    [Fact]
    public async Task A_windowed_editor_is_still_let_go_rather_than_waited_for()
    {
        var (service, processes) = Service();

        await service.OpenAsync(Config("code"), Project(), "C:/work/alpha");

        processes.Detached.Should().NotBeNull();
        processes.Interactive.Should().BeNull("an editor with a window outlives the launcher");
    }

    [Fact]
    public async Task An_editor_declared_in_configuration_replaces_the_one_built_in()
    {
        var config = Config("code", "Agents");

        config.CustomEditors["code"] = new EditorDefinition
        {
            Arguments = ["--folder", "${DIRECTORY}"],
            ProfileArguments = ["--profile", "${PROFILE}"],
        };

        var (service, processes) = Service();

        await service.OpenAsync(config, Project(), "C:/work/alpha");

        // The escape hatch: somebody whose editor has since learned to open a
        // folder and a profile together can say so without waiting for a
        // release, and what they said wins over what is known here.
        processes.Detached!.Arguments
            .Should().Equal("--folder", "C:/work/alpha", "--profile", "Agents");
    }

    [Fact]
    public async Task A_profile_placeholder_expands_to_nothing_when_there_is_no_profile()
    {
        var config = Config("mine");

        config.CustomEditors["mine"] = new EditorDefinition
        {
            Arguments = ["${DIRECTORY}", "${PROFILE}"],
        };

        var (service, processes) = Service();

        await service.OpenAsync(config, Project(), "C:/work/alpha");

        // Left empty rather than passed through. A template written for a
        // profile must not put the literal "${PROFILE}" on a command line when
        // nobody chose one.
        processes.Detached!.Arguments.Should().Equal("C:/work/alpha", string.Empty);
    }

    [Fact]
    public async Task A_declared_editor_can_name_a_binary_other_than_its_key()
    {
        var config = Config("mine");

        config.CustomEditors["mine"] = new EditorDefinition
        {
            Executable = "some-editor",
            Arguments = ["${DIRECTORY}"],
            Environment = { ["EDITOR_ROOT"] = "${DIRECTORY}" },
        };

        var (service, processes) = Service();

        await service.OpenAsync(config, Project(), "C:/work/alpha");

        // Environment values are templates too, so a fork that wants the folder
        // in a variable does not need code to get it there.
        processes.Detached!.Environment!["EDITOR_ROOT"].Should().Be("C:/work/alpha");
    }

    [Fact]
    public void Only_an_editor_with_somewhere_to_put_a_profile_claims_it_can_take_one()
    {
        var (service, _) = Service();

        // Neovim can. VS Code has profiles and no way to be told one at launch,
        // which is not the same thing and is the difference between "ignored"
        // and "there is nothing to ignore".
        service.Describe(Config("nvim")).CanOpenAProfile.Should().BeTrue();
        service.Describe(Config("code")).CanOpenAProfile.Should().BeFalse();

        // An editor nobody has described is not accused of dropping a profile.
        service.Describe(Config("some-editor")).CanOpenAProfile.Should().BeFalse();
        service.Describe(Config("some-editor")).Definition!.ProfileNote.Should().BeNull();
    }

    [Fact]
    public void The_reason_a_profile_cannot_be_applied_travels_with_the_editor()
    {
        var (service, _) = Service();

        // So the command that prints it does not have to know which editor it
        // is talking about.
        var editor = service.Describe(Config("code"));

        editor.Definition!.ProfileNote.Should().Contain("folder and a profile together");

        // And it is written to follow the editor's name. Both places that print
        // it compose exactly this, and the first draft began with "it", which
        // read as "code it will not open a folder and a profile together".
        $"{editor.Command} {editor.Definition.ProfileNote}"
            .Should().Be("code will not open a folder and a profile together.");
    }
}
