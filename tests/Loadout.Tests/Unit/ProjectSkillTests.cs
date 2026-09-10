using FluentAssertions;
using Loadout.Agents;
using Loadout.Agents.Claude;
using Loadout.Models.Projects;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether the skills the workspace holds for a project reach the session as
/// something it can actually use.
/// </summary>
/// <remarks>
/// <para>
/// The workspace keeps them at <c>agents/claude/skills/&lt;name&gt;/SKILL.md</c>,
/// which is not where the agent looks. So they were authored and never loaded:
/// nothing named them, and nothing could invoke them. The only thing pointing
/// anywhere near was <c>--add-dir</c> on the project's workspace directory,
/// which grants read access to a path and does not make a skill a command.
/// </para>
/// <para>
/// Built as a real invocation rather than asserted against the copier, because
/// a copier that works and a launch that never passes the flag would satisfy
/// every test of the copier and hand the session nothing.
/// </para>
/// </remarks>
public sealed class ProjectSkillTests : IDisposable
{
    private const string Help = """
        Usage: claude [options]
          --settings <file>
          --append-system-prompt-file <file>
          --add-dir <directories...>
          --plugin-dir <path>
        """;

    /// <summary>A build that knows every flag but the one under test.</summary>
    private const string HelpWithoutPlugins = """
        Usage: claude [options]
          --settings <file>
          --append-system-prompt-file <file>
          --add-dir <directories...>
        """;

    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;
    private readonly string _skills;

    public ProjectSkillTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-skills-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");
        _skills = Path.Combine(_workspace, "projects", "demo", "agents", "claude", "skills");

        Directory.CreateDirectory(_runtime);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    /// <summary>Writes a skill into the workspace the way somebody would author one.</summary>
    private void GivenSkill(string name, params string[] extraFiles)
    {
        var directory = Path.Combine(_skills, name);

        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: Whatever {name} is for.\n---\n\nDo the thing.\n");

        foreach (var file in extraFiles)
        {
            File.WriteAllText(Path.Combine(directory, file), "// carried along");
        }
    }

    private async Task<AgentInvocation> BuildAsync(string help = Help)
    {
        var adapter = new ClaudeAdapter(
            new StubResolver(Path.Combine(Path.GetTempPath(), "claude")),
            new StubProcessLauncher(help),
            []);

        var context = new AgentLaunchContext(
            new ProjectResolution(
                new ProjectRegistryEntry { Slug = "demo", Name = "Demo" },
                _root, null, 0, false),
            _root,
            _runtime,
            _workspace,
            [],
            new ProjectManifest { Slug = "demo", Name = "Demo" });

        var result = await adapter.BuildInvocationAsync(context);

        result.Succeeded.Should().BeTrue(result.Error ?? "the invocation has to build");

        return result.Value!;
    }

    private static string? PluginDirectory(AgentInvocation invocation)
    {
        var at = invocation.Arguments.ToList().IndexOf("--plugin-dir");

        return at >= 0 ? invocation.Arguments[at + 1] : null;
    }

    [Fact]
    public async Task A_project_with_no_skills_is_handed_no_plugin()
    {
        var invocation = await BuildAsync();

        PluginDirectory(invocation).Should().BeNull(
            "there is nothing to hand over, and an empty plugin is a thing to explain");
    }

    [Fact]
    public async Task A_skill_the_workspace_holds_is_handed_over_as_a_plugin()
    {
        GivenSkill("run-the-app");

        var invocation = await BuildAsync();

        var plugin = PluginDirectory(invocation);

        plugin.Should().NotBeNull("the workspace holds a skill for this project");

        // Under the per-launch runtime directory, which is deleted when the
        // agent exits. Not the repository, where agent state does not belong,
        // and not the agent's own home, where it would outlive the session.
        plugin!.Should().StartWith(_runtime);

        File.Exists(Path.Combine(plugin, ".claude-plugin", "plugin.json"))
            .Should().BeTrue("a manifest is what makes it loadable");

        File.Exists(Path.Combine(plugin, "skills", "run-the-app", "SKILL.md"))
            .Should().BeTrue("and the skill itself has to be in it");
    }

    [Fact]
    public async Task A_skill_arrives_with_the_files_it_depends_on()
    {
        GivenSkill("run-the-app", "driver.mjs", "check-anchors.mjs");

        var plugin = PluginDirectory(await BuildAsync())!;
        var copied = Path.Combine(plugin, "skills", "run-the-app");

        // A skill routinely ships the scripts it tells the session to run. One
        // copied without them fails at the first instruction it gives.
        File.Exists(Path.Combine(copied, "driver.mjs")).Should().BeTrue();
        File.Exists(Path.Combine(copied, "check-anchors.mjs")).Should().BeTrue();
    }

    [Fact]
    public async Task Every_skill_the_project_has_goes_in_one_plugin()
    {
        GivenSkill("run-the-app");
        GivenSkill("seed-the-database");

        var plugin = PluginDirectory(await BuildAsync())!;

        Directory.EnumerateDirectories(Path.Combine(plugin, "skills"))
            .Select(Path.GetFileName)
            .Should().BeEquivalentTo(["run-the-app", "seed-the-database"]);
    }

    [Fact]
    public async Task A_build_that_cannot_take_them_says_so_rather_than_going_quiet()
    {
        GivenSkill("run-the-app");

        var invocation = await BuildAsync(HelpWithoutPlugins);

        PluginDirectory(invocation).Should().BeNull("the flag is not there to pass");

        // The gap this whole change exists to close is a skill somebody wrote
        // and could not reach. Closing it silently on one build and not another
        // would put that straight back.
        invocation.Warnings.Should().Contain(warning =>
            warning.Contains("--plugin-dir", StringComparison.Ordinal)
            && warning.Contains("1 skill", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_directory_with_no_skill_file_in_it_is_not_a_skill()
    {
        Directory.CreateDirectory(Path.Combine(_skills, "notes"));

        PluginDirectory(await BuildAsync()).Should().BeNull(
            "a folder somebody left there is not something to load");
    }
}
