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
    public async Task The_skills_the_launcher_ships_reach_a_project_with_none_of_its_own()
    {
        var plugin = PluginDirectory(await BuildAsync());

        plugin.Should().NotBeNull("the launcher ships skills of its own");

        File.Exists(Path.Combine(plugin!, "skills", "finishup", "SKILL.md"))
            .Should().BeTrue("finishup ships with the launcher");
    }

    [Fact]
    public async Task A_project_can_replace_a_shipped_skill()
    {
        GivenSkill("finishup", "notes.md");

        var plugin = PluginDirectory(await BuildAsync())!;
        var copied = Path.Combine(plugin, "skills", "finishup");

        // Replaced, not merged. Leaving the shipped version's files underneath
        // a project's own would hand the session a mixture neither wrote.
        File.Exists(Path.Combine(copied, "notes.md")).Should().BeTrue(
            "this is the project's version");

        File.ReadAllText(Path.Combine(copied, "SKILL.md"))
            .Should().Contain("Whatever finishup is for", "and its SKILL.md too");
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

        // Both of the project's, in the one plugin. Asserted as containment
        // rather than as the whole list, because the launcher ships skills of
        // its own and a total is a number this test has no business pinning.
        Directory.EnumerateDirectories(Path.Combine(plugin, "skills"))
            .Select(Path.GetFileName)
            .Should().Contain(["run-the-app", "seed-the-database"]);
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
            && warning.Contains("were not loaded", StringComparison.Ordinal));
    }

    /// <summary>Writes a skill the workspace holds for every project.</summary>
    private void GivenGlobalSkill(string name)
    {
        var directory = Path.Combine(
            _workspace, "global", "agents", "claude", "skills", name);

        Directory.CreateDirectory(directory);

        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: Whatever {name} is for.\n---\n\nEverywhere.\n");
    }

    [Fact]
    public async Task A_skill_the_workspace_holds_for_everything_reaches_every_project()
    {
        // The reason this scope exists: a skill like "wrap up the session" is
        // not about one codebase, and putting a copy in each project is how it
        // goes stale in all but the one somebody remembered.
        GivenGlobalSkill("finishup");

        var plugin = PluginDirectory(await BuildAsync());

        plugin.Should().NotBeNull("a global skill is still a skill this session can use");

        File.Exists(Path.Combine(plugin!, "skills", "finishup", "SKILL.md")).Should().BeTrue();
    }

    [Fact]
    public async Task A_project_can_replace_a_global_skill_rather_than_collide_with_it()
    {
        GivenGlobalSkill("run-the-app");
        GivenSkill("run-the-app", "driver.mjs");

        var plugin = PluginDirectory(await BuildAsync())!;
        var copied = Path.Combine(plugin, "skills", "run-the-app");

        // Narrower last, the way everything else composes. The project's copy
        // brought a script with it; the global one did not, so its presence is
        // what says which of the two won.
        File.Exists(Path.Combine(copied, "driver.mjs")).Should().BeTrue(
            "the project's own version is the one that should be there");

        Directory.EnumerateDirectories(Path.Combine(plugin, "skills"))
            .Select(Path.GetFileName)
            .Count(name => string.Equals(name, "run-the-app", StringComparison.OrdinalIgnoreCase))
            .Should().Be(1, "one name is one skill, not two");
    }

    [Fact]
    public async Task A_directory_with_no_skill_file_in_it_is_not_a_skill()
    {
        Directory.CreateDirectory(Path.Combine(_skills, "notes"));

        var plugin = PluginDirectory(await BuildAsync())!;

        Directory.Exists(Path.Combine(plugin, "skills", "notes")).Should().BeFalse(
            "a folder somebody left there is not something to load");
    }
}
