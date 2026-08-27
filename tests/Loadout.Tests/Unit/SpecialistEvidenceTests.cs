using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading a repository: what is counted, what is skipped, and what is said
/// when the answer is only partial.
/// </summary>
public sealed class SpecialistEvidenceTests : IDisposable
{
    private readonly string _root;

    public SpecialistEvidenceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-evid-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private void File(string relative, string text = "x")
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, text);
    }

    private Task<Models.Results.OperationResult<RepositoryEvidence>> ReadAsync() =>
        new RepositoryEvidenceReader().ReadAsync(_root);

    [Fact]
    public async Task Other_peoples_code_is_not_counted_as_this_project()
    {
        File("src/app.py");
        File("src/util.py");
        File("src/main.py");

        for (var i = 0; i < 50; i++)
        {
            File($"node_modules/pkg{i}/index.ts");
        }

        var evidence = (await ReadAsync()).Value!;

        // Not only about speed. A node_modules full of TypeScript inside a
        // Python project would make the repository look like a TypeScript
        // project, which is exactly the wrong answer.
        evidence.Count(".py").Should().Be(3);
        evidence.Count(".ts").Should().Be(0);
    }

    [Fact]
    public async Task Build_output_is_skipped_too()
    {
        File("src/Program.cs");

        for (var i = 0; i < 20; i++)
        {
            File($"obj/Debug/generated{i}.cs");
            File($"bin/Release/copy{i}.cs");
        }

        var evidence = (await ReadAsync()).Value!;

        evidence.Count(".cs").Should().Be(1, "generated copies are not the project");
    }

    [Fact]
    public async Task Manifests_are_read_so_a_dependency_can_be_seen()
    {
        File("Api.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Npgsql" Version="8.0.0" />
              </ItemGroup>
            </Project>
            """);

        var evidence = (await ReadAsync()).Value!;

        evidence.Dependencies.Should().Contain(line => line.Contains("Npgsql", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Source_files_are_never_opened()
    {
        const string Secret = "SUPER-SECRET-VALUE-IN-SOURCE";

        File("src/Program.cs", $"var apiKey = \"{Secret}\";");

        var evidence = (await ReadAsync()).Value!;

        // Only names and manifests. No repository content reaches the launcher,
        // the logs, or an agent through this path.
        evidence.Dependencies.Should().NotContain(line => line.Contains(Secret, StringComparison.Ordinal));
        evidence.Paths.Should().NotContain(p => p.Contains(Secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_scan_that_had_to_stop_early_says_so()
    {
        // The cap exists so a launch never waits on a repository walk. What
        // matters is that a partial answer is labelled: a language living past
        // the cut-off is simply not seen, and the result otherwise looks
        // exactly as confident as a complete one.
        for (var i = 0; i < 4200; i++)
        {
            File($"src/dir{i / 100}/file{i}.txt");
        }

        var evidence = (await ReadAsync()).Value!;

        evidence.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task An_ordinary_repository_is_not_reported_as_truncated()
    {
        File("src/app.py");
        File("README.md");

        (await ReadAsync()).Value!.Truncated.Should().BeFalse();
    }

    [Fact]
    public async Task Truncation_travels_as_far_as_the_answer()
    {
        var catalogue = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);

        var resolved = new SpecialistResolver().Resolve(new SpecialistRequest(
            catalogue,
            Task: "fix the tests",
            Evidence: new RepositoryEvidence([], new Dictionary<string, int>(), [], Truncated: true)));

        resolved.EvidenceTruncated.Should().BeTrue(
            "an incomplete scan has to be visible in the explanation, not only where it happened");
    }

    [Fact]
    public async Task A_directory_that_does_not_exist_is_an_empty_answer_rather_than_a_failure()
    {
        var reader = new RepositoryEvidenceReader();

        var result = await reader.ReadAsync(Path.Combine(_root, "not-here"));

        // A registered project may simply not be on this machine yet.
        result.Succeeded.Should().BeTrue();
        result.Value!.Paths.Should().BeEmpty();
    }

    [Fact]
    public async Task The_same_repository_always_gives_the_same_evidence()
    {
        File("src/b.cs");
        File("src/a.cs");
        File("src/c.cs");

        var first = (await ReadAsync()).Value!;
        var second = (await ReadAsync()).Value!;

        // Enumeration order is the filesystem's business; resolution has to be
        // reproducible regardless, or the same task composes differently on
        // different days and no test of it means anything.
        first.Paths.Should().Equal(second.Paths);
    }
}
