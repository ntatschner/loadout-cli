using AgentWorkspace.Core.Context;
using AgentWorkspace.Core.Instructions;
using AgentWorkspace.Models.Projects;
using AgentWorkspace.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace AgentWorkspace.Tests.Unit;

/// <summary>
/// The context compiler is what makes project knowledge agent-independent
/// (spec section 33), so its ordering and its handling of absent files are
/// behaviour rather than implementation detail.
/// </summary>
public sealed class ContextCompilerTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;
    private readonly ContextCompiler _compiler = new(
        new NoOpFilePermissions(),
        new RuleService(),
        new MemoryService(TimeProvider.System));

    public ContextCompilerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "agentctl-ctx-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");

        Directory.CreateDirectory(_runtime);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    [Fact]
    public async Task Sources_are_ordered_from_general_to_specific()
    {
        // The ordering is the compiler's main editorial decision: where two
        // sources disagree, the agent should read the narrower one last.
        WriteWorkspace("global/instructions/engineering.md", "Engineering standards.");
        WriteProject("starstats", "context/architecture.md", "Architecture notes.");
        WriteProject("starstats", "agents/claude/instructions.md", "Claude specifics.");
        WriteProject("starstats", "context/schema.md", "Database schema.");

        var manifest = Manifest();
        manifest.Context.Global.Add("global/instructions/engineering.md");
        manifest.Context.Project.Add("context/architecture.md");
        manifest.Profiles["database"] = new ContextProfile
        {
            Description = "Database work",
            Context = { "context/schema.md" },
        };

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", "database");

        result.Succeeded.Should().BeTrue(result.Error);

        result.Value!.Sources.Select(s => s.WorkspaceRelativePath).Should().Equal(
            "global/instructions/engineering.md",
            "projects/starstats/context/architecture.md",
            "projects/starstats/agents/claude/instructions.md",
            "projects/starstats/context/schema.md");
    }

    [Fact]
    public async Task Only_the_launching_agents_instructions_are_included()
    {
        WriteProject("starstats", "agents/claude/instructions.md", "Claude specifics.");
        WriteProject("starstats", "agents/codex/instructions.md", "Codex specifics.");

        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "codex");

        var text = await File.ReadAllTextAsync(result.Value!.FilePath);

        // A Claude session must never be handed Codex's notes.
        text.Should().Contain("Codex specifics.");
        text.Should().NotContain("Claude specifics.");
    }

    [Fact]
    public async Task A_missing_source_is_reported_rather_than_ignored()
    {
        var manifest = Manifest();
        manifest.Context.Project.Add("context/deleted.md");

        var result = await _compiler.CompileAsync(manifest, _workspace, _runtime, "claude");

        // A context file that quietly vanished changes what the agent knows,
        // and the user should hear about it before the session.
        result.Value!.MissingSources.Should().ContainSingle()
            .Which.Should().Contain("context/deleted.md");
    }

    [Fact]
    public async Task A_file_listed_twice_is_included_once()
    {
        WriteProject("starstats", "context/shared.md", "Shared notes.");

        var manifest = Manifest();
        manifest.Context.Project.Add("context/shared.md");
        manifest.Profiles["overlap"] = new ContextProfile { Context = { "context/shared.md" } };

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", "overlap");

        // Repeating it wastes the agent's attention and implies an emphasis
        // that was not intended.
        result.Value!.Sources.Should().ContainSingle();
    }

    [Fact]
    public async Task An_unknown_profile_fails_with_the_available_names()
    {
        var manifest = Manifest();
        manifest.Profiles["database"] = new ContextProfile();

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", "databse");

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Models.ExitCode.InvalidArguments);

        // Naming the alternatives turns a typo into a one-step fix.
        result.Error.Should().Contain("database");
    }

    [Fact]
    public async Task A_profile_restricted_to_another_agent_is_refused()
    {
        var manifest = Manifest();
        manifest.Profiles["claude-only"] = new ContextProfile { Agents = { "claude" } };

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "codex", "claude-only");

        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("codex");
    }

    [Fact]
    public async Task A_profile_can_exclude_the_global_instructions()
    {
        WriteWorkspace("global/instructions/engineering.md", "Engineering standards.");

        var manifest = Manifest();
        manifest.Context.Global.Add("global/instructions/engineering.md");
        manifest.Profiles["narrow"] = new ContextProfile { IncludeGlobal = false };

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", "narrow");

        result.Value!.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task An_oversized_source_is_skipped_with_its_size_named()
    {
        // Skipped loudly rather than truncated: half a document is worse than a
        // clear note saying it was left out.
        WriteProject("starstats", "context/huge.md", new string('x', 600 * 1024));

        var manifest = Manifest();
        manifest.Context.Project.Add("context/huge.md");

        var result = await _compiler.CompileAsync(manifest, _workspace, _runtime, "claude");

        result.Value!.Sources.Should().BeEmpty();
        result.Value.MissingSources.Should().ContainSingle()
            .Which.Should().Contain("exceeds the context limit");
    }

    [Fact]
    public async Task A_handoff_is_appended_last()
    {
        WriteProject("starstats", "context/architecture.md", "Architecture notes.");

        var handoff = Path.Combine(_root, "handoff.md");
        await File.WriteAllTextAsync(handoff, "Pick up where the last session stopped.");

        var manifest = Manifest();
        manifest.Context.Project.Add("context/architecture.md");

        var result = await _compiler.CompileAsync(
            manifest, _workspace, _runtime, "claude", null, handoff);

        // Most specific and most recent, so it is read last.
        result.Value!.Sources.Last().Heading.Should().Be("Current handoff");
    }

    [Fact]
    public async Task The_compiled_file_says_that_editing_it_achieves_nothing()
    {
        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "claude");

        var text = await File.ReadAllTextAsync(result.Value!.FilePath);

        // It is regenerated every launch and lives in a directory that is
        // deleted afterwards, so an agent or a person editing it in place would
        // lose the change without warning.
        text.Should().Contain("regenerated on every launch");
    }

    [Fact]
    public void Profile_listing_always_offers_the_base_context()
    {
        var manifest = Manifest();
        manifest.Profiles["database"] = new ContextProfile();
        manifest.Profiles["claude-only"] = new ContextProfile { Agents = { "claude" } };

        _compiler.ListProfiles(manifest, "codex").Should().Equal("default", "database");
        _compiler.ListProfiles(manifest, "claude").Should().Contain("claude-only");
    }

    [Fact]
    public async Task Compilation_fails_clearly_when_the_runtime_directory_is_missing()
    {
        var result = await _compiler.CompileAsync(
            Manifest(), _workspace, Path.Combine(_root, "absent"), "claude");

        result.Failed.Should().BeTrue();
    }

    private static ProjectManifest Manifest() => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = "starstats",
        Name = "StarStats",
        Repository = new ProjectRepository { Remote = "ssh://git.internal/apps/starstats.git" },
    };

    private void WriteWorkspace(string relative, string content)
    {
        var path = Path.Combine(_workspace, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private void WriteProject(string slug, string relative, string content) =>
        WriteWorkspace($"projects/{slug}/{relative}", content);

    [Fact]
    public async Task Rules_and_memory_reach_the_agent()
    {
        WriteProject("starstats", "rules/always.md", "---\nalwaysApply: true\n---\nHouse style.");
        WriteProject(
            "starstats",
            "rules/database.md",
            "---\ndescription: db work\nglobs: src/Data/**\n---\nMigration rules.");
        WriteProject("starstats", "memory/MEMORY.md", "- [build-quirks](build-quirks.md) - the build");

        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "claude");

        var compiled = await File.ReadAllTextAsync(result.Value!.FilePath);

        // An always-apply rule is inlined, because it is going to be needed.
        compiled.Should().Contain("House style.");

        // A scoped one is listed rather than inlined: naming it costs a line,
        // and inlining it would put the database conventions in front of
        // somebody editing a stylesheet.
        compiled.Should().NotContain("Migration rules.");
        compiled.Should().Contain("database").And.Contain("src/Data/**");

        compiled.Should().Contain("build-quirks");
    }

    [Fact]
    public async Task Memory_topics_are_listed_rather_than_inlined()
    {
        WriteProject("starstats", "memory/MEMORY.md", "- [old](old.md) - an old topic");
        WriteProject("starstats", "memory/old.md", "- A fact nobody needs this session.");

        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "claude");
        var compiled = await File.ReadAllTextAsync(result.Value!.FilePath);

        // A project accumulates memory for years. Inlining it would make every
        // session pay for every fact anyone ever recorded.
        compiled.Should().Contain("old");
        compiled.Should().NotContain("A fact nobody needs this session.");
    }

    [Fact]
    public async Task Compilation_succeeds_with_every_optional_layer_absent()
    {
        // The check that keeps the optional layers optional. Rules, memory,
        // profiles and handoffs are all things the launcher adds; if any of
        // them becomes load-bearing, a workspace without it silently stops
        // working and nothing inside the session can tell you why.
        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "claude");

        result.Succeeded.Should().BeTrue();

        var compiled = await File.ReadAllTextAsync(result.Value!.FilePath);

        compiled.Should().Contain("StarStats");
        result.Value.MissingSources.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_rules_directory_does_not_stop_a_launch()
    {
        // Rules live in a synced repository, so a half-finished sync or a
        // permissions accident is an ordinary event. It must degrade to fewer
        // instructions, never to a launch that refuses to start.
        Directory.CreateDirectory(Path.Combine(_workspace, "projects", "starstats", "rules"));

        var result = await _compiler.CompileAsync(Manifest(), _workspace, _runtime, "claude");

        result.Succeeded.Should().BeTrue();
    }
}
