using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Projects;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a project loads on every launch, before the task is even known.
/// </summary>
/// <remarks>
/// This list was being derived in two places and the two did not agree. The
/// rules commands walked the manifest; the doctor check counted rules alone and
/// so reported a comfortable budget for a project whose instructions file had
/// grown past anything comfortable. Holding it down here is what stops the two
/// drifting apart again.
/// </remarks>
public sealed class CoreInstructionsTests : IDisposable
{
    private readonly string _workspace;

    private const string Slug = "alpha";

    public CoreInstructionsTests()
    {
        _workspace = Path.Combine(
            Path.GetTempPath(), "loadout-core-instructions-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(Path.Combine(_workspace, "projects", Slug));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workspace))
            {
                Directory.Delete(_workspace, recursive: true);
            }
        }
        catch (IOException)
        {
            // A temp directory that outlives the run is not a failed test.
        }
    }

    private string Write(string relative, int bytes)
    {
        var path = Path.Combine(_workspace, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', bytes));

        return path;
    }

    private static ProjectManifest Manifest(
        IEnumerable<string>? global = null,
        IEnumerable<string>? project = null,
        string agent = "claude")
    {
        var manifest = new ProjectManifest();

        manifest.Context.Global.AddRange(global ?? []);
        manifest.Context.Project.AddRange(project ?? []);
        manifest.Agents.Default = agent;

        return manifest;
    }

    [Fact]
    public void The_order_is_the_one_the_compiler_reads_in()
    {
        Write("context/house-style.md", 10);
        Write("projects/alpha/instructions.md", 10);
        Write("projects/alpha/agents/claude/instructions.md", 10);

        var paths = CoreInstructions.PathsFor(
            Manifest(global: ["context/house-style.md"], project: ["instructions.md"]),
            _workspace,
            Slug);

        // Order is not cosmetic: it is what the compiler assembles, and a
        // report that lists them in a different order describes a file nobody
        // is going to be given.
        paths.Should().HaveCount(3);
        paths[0].Should().EndWith("house-style.md");
        paths[1].Should().EndWith(Path.Combine("alpha", "instructions.md"));
        paths[2].Should().EndWith(Path.Combine("claude", "instructions.md"));
    }

    [Fact]
    public void A_file_the_manifest_names_is_listed_even_when_it_is_missing()
    {
        var paths = CoreInstructions.PathsFor(
            Manifest(project: ["instructions.md"]), _workspace, Slug);

        // Dropping it here would turn "the manifest points at something that is
        // not there" into silence, and that is a finding somebody needs.
        paths.Should().ContainSingle().Which.Should().EndWith("instructions.md");
    }

    [Fact]
    public void The_agent_file_is_listed_only_when_it_exists()
    {
        var withoutIt = CoreInstructions.PathsFor(Manifest(), _workspace, Slug);

        // Most projects have none, so its absence is ordinary rather than a
        // defect worth reporting on every one of them.
        withoutIt.Should().BeEmpty();

        Write("projects/alpha/agents/claude/instructions.md", 10);

        CoreInstructions.PathsFor(Manifest(), _workspace, Slug)
            .Should().ContainSingle().Which.Should().EndWith("instructions.md");
    }

    [Fact]
    public void The_largest_file_is_the_one_worth_naming()
    {
        Write("context/house-style.md", 100);
        Write("projects/alpha/instructions.md", 9_000);

        var largest = CoreInstructions.Largest(
            Manifest(global: ["context/house-style.md"], project: ["instructions.md"]),
            _workspace,
            Slug);

        // Telling somebody their instruction layer is large without saying
        // which file it is leaves them to go and measure four files.
        largest.Should().NotBeNull();
        largest!.Value.Path.Should().EndWith(Path.Combine("alpha", "instructions.md"));
        largest.Value.Bytes.Should().Be(9_000);
    }

    [Fact]
    public void A_project_that_declares_nothing_has_nothing_to_name()
    {
        CoreInstructions.Largest(Manifest(), _workspace, Slug).Should().BeNull();
    }

    [Fact]
    public void A_missing_file_is_never_the_largest()
    {
        Write("context/house-style.md", 50);

        var largest = CoreInstructions.Largest(
            Manifest(global: ["context/house-style.md"], project: ["gone.md"]),
            _workspace,
            Slug);

        // Sizing a file that is not there would either throw or report zero,
        // and both are worse than passing over it.
        largest!.Value.Path.Should().EndWith("house-style.md");
    }
}
