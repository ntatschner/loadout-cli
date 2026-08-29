using FluentAssertions;
using Loadout.Core.Projects;
using Loadout.Models.Projects;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Starting a project from one that already exists.
/// </summary>
/// <remarks>
/// A template decides what a new project believes on its first launch, so the
/// tests that matter are about what it refuses to carry across rather than what
/// it copies.
/// </remarks>
public sealed class ProjectTemplateTests : IDisposable
{
    private readonly string _root;

    public ProjectTemplateTests()
    {
        _root = Path.Combine(
            Path.GetTempPath(), "loadout-template-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
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
            // A temp directory that outlives the run is not a failed test.
        }
    }

    private void Write(string relative, string content = "x")
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ProjectManifest Template()
    {
        var manifest = new ProjectManifest
        {
            Id = "11111111-1111-1111-1111-111111111111",
            Slug = "first-service",
            Name = "First Service",
            Aliases = ["fs", "first"],
            Repository = new ProjectRepository
            {
                Remote = "https://example.invalid/first.git",

                // A feature branch, which is what the real project this was
                // first tried against had recorded.
                DefaultBranch = "feat/half-finished",
            },
        };

        manifest.Agents.Default = "codex";
        manifest.Agents.Enabled.Add("codex");
        manifest.Context.Global.Add("global/instructions/security.md");
        manifest.Context.Project.Add("instructions.md");
        manifest.Environment["API_KEY"] = new EnvironmentBinding();

        return manifest;
    }

    [Fact]
    public void Memory_is_never_copied()
    {
        Write("first-service/agents/claude/instructions.md");
        Write("first-service/rules/testing.md");
        Write("first-service/memory/architecture.md");
        Write("first-service/memory/MEMORY.md");

        var copied = ProjectTemplateService.CopyDefinition(_root, "first-service", "second-service");

        // The point of the whole feature. Memory is what an agent established
        // about a particular codebase, and none of it is true of a repository
        // nobody has written yet — a new project furnished with confident
        // claims about code that does not exist is worse than a bare one.
        copied.Should().NotContain(path => path.StartsWith("memory", StringComparison.Ordinal));

        Directory.Exists(Path.Combine(_root, "second-service", "memory"))
            .Should().BeFalse("memory belongs to the codebase it was learned from");
    }

    [Fact]
    public void Conventions_are_copied()
    {
        Write("first-service/agents/claude/instructions.md");
        Write("first-service/rules/testing.md");

        var copied = ProjectTemplateService.CopyDefinition(_root, "first-service", "second-service");

        copied.Should().Contain("agents/claude/instructions.md");
        copied.Should().Contain("rules/testing.md");

        File.Exists(Path.Combine(_root, "second-service", "rules", "testing.md"))
            .Should().BeTrue();
    }

    [Fact]
    public void A_new_project_gets_its_own_identity()
    {
        var derived = ProjectTemplateService.Derive(
            Template(), "second-service", "Second Service", remote: null, branch: "main");

        derived.Id.Should().NotBe("11111111-1111-1111-1111-111111111111");
        derived.Id.Should().NotBeNullOrWhiteSpace();
        derived.Slug.Should().Be("second-service");
        derived.Name.Should().Be("Second Service");

        // A remote identifies one repository. Inheriting it would point two
        // projects at the same code.
        derived.Repository.Remote.Should().BeEmpty();
    }

    [Fact]
    public void The_default_branch_is_not_inherited()
    {
        var derived = ProjectTemplateService.Derive(
            Template(), "second-service", "Second Service", remote: null, branch: "main");

        // The template had 'feat/half-finished' recorded, because a manifest's
        // default branch drifts with whatever the project is doing. A
        // repository with no history has no branch to inherit, and starting
        // every new one on somebody else's feature branch is the bug this
        // caught in practice.
        derived.Repository.DefaultBranch.Should().Be("main");
    }

    [Fact]
    public void Aliases_and_environment_bindings_are_not_inherited()
    {
        var derived = ProjectTemplateService.Derive(
            Template(), "second-service", "Second Service", remote: null, branch: "main");

        // An alias is a second name for one project and cannot mean two.
        derived.Aliases.Should().BeEmpty();

        // A binding points at a particular project's secrets. It carries no
        // secret itself, but a new project reading another's by default is the
        // wrong way round.
        derived.Environment.Should().BeEmpty();
    }

    [Fact]
    public void How_the_work_is_done_is_inherited()
    {
        var derived = ProjectTemplateService.Derive(
            Template(), "second-service", "Second Service", remote: null, branch: "main");

        derived.Agents.Default.Should().Be("codex");
        derived.Agents.Enabled.Should().Contain("codex");
        derived.Context.Global.Should().Contain("global/instructions/security.md");
        derived.Context.Project.Should().Contain("instructions.md");
    }

    [Fact]
    public void Without_a_template_nothing_is_carried()
    {
        var derived = ProjectTemplateService.Derive(
            null, "bare", "Bare", remote: "https://example.invalid/bare.git", branch: "main");

        derived.Slug.Should().Be("bare");
        derived.Repository.Remote.Should().Be("https://example.invalid/bare.git");
        derived.Agents.Default.Should().Be("claude");
        derived.Context.Global.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Star Platform", "star-platform")]
    [InlineData("  Trailing  ", "trailing")]
    [InlineData("Already-Hyphenated", "already-hyphenated")]
    [InlineData("tcs.core", "tcs.core")]
    [InlineData("Lots   of   space", "lots-of-space")]
    [InlineData("!!!", "")]
    public void A_name_becomes_a_handle(string name, string expected)
    {
        // A slug is typed far more often than it is read, so a display name
        // that is prose has to reduce to something a command line accepts.
        ProjectTemplateService.Slugify(name).Should().Be(expected);
    }
}
