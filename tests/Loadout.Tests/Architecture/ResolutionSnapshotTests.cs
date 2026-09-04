using System.Text;
using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Contracts;

/// <summary>
/// What each shape of repository is given, recorded so a change to it has to be
/// meant.
/// </summary>
/// <remarks>
/// <para>
/// The resolver tests cover four journeys thoroughly. What nothing covered is
/// blast radius: with seventy-odd specialists, retuning one can change what an
/// unrelated repository gets, and no test would notice because no test asserts
/// on that shape. A pack manager would make the number unbounded and the
/// specialists third-party, so this is worth more before that lands than after.
/// </para>
/// <para>
/// A failure here is not "you broke it". It is "you changed what a Django
/// repository is given — was that intended?", and the answer is often yes. Say
/// so by committing the new snapshot:
/// </para>
/// <code>LOADOUT_UPDATE_SNAPSHOTS=1 dotnet test --filter ResolutionSnapshotTests</code>
/// <para>
/// The fixtures are written to disk and scanned by the real evidence reader
/// rather than described as a <see cref="RepositoryEvidence"/> in code. Every
/// other test hands the resolver evidence somebody typed, which means a fault in
/// the scanner itself — a glob that matches the repository root, a manifest
/// opened that should not be — passes all of them. This is the only place the
/// two halves run together.
/// </para>
/// </remarks>
public sealed class ResolutionSnapshotTests : IDisposable
{
    private const string SnapshotFile = "resolution.snapshot.txt";

    private readonly string _root;

    public ResolutionSnapshotTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-shapes-" + Guid.NewGuid().ToString("N"));

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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public async Task What_each_shape_of_repository_is_given_has_not_changed_unnoticed()
    {
        var catalogue = await new SpecialistLibrary().LoadAsync(workspaceRoot: null);
        var resolver = new SpecialistResolver();
        var reader = new RepositoryEvidenceReader();

        var recorded = new StringBuilder();

        foreach (var shape in Shape.All)
        {
            var path = shape.WriteTo(_root);

            var evidence = await reader.ReadAsync(path);

            evidence.Succeeded.Should().BeTrue(evidence.Error);

            var resolved = resolver.Resolve(new SpecialistRequest(
                catalogue,
                shape.Mode,
                shape.Task,
                Evidence: evidence.Value));

            recorded.AppendLine($"# {shape.Name}");

            foreach (var selection in resolved.Selected)
            {
                recorded.AppendLine(selection.Specialist.Id);
            }

            recorded.AppendLine();
        }

        var actual = recorded.ToString().ReplaceLineEndings("\n");
        var snapshot = Path.Combine(FixturesDirectory(), SnapshotFile);

        if (Environment.GetEnvironmentVariable("LOADOUT_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshot)!);
            await File.WriteAllTextAsync(snapshot, actual);

            return;
        }

        File.Exists(snapshot).Should().BeTrue(
            $"the snapshot has to be recorded first: LOADOUT_UPDATE_SNAPSHOTS=1 dotnet test "
            + $"--filter {nameof(ResolutionSnapshotTests)}");

        var expected = (await File.ReadAllTextAsync(snapshot)).ReplaceLineEndings("\n");

        actual.Should().Be(expected,
            "resolution changed for at least one repository shape. If that was the point, "
            + "accept it with LOADOUT_UPDATE_SNAPSHOTS=1 and commit the new snapshot; if it "
            + "was not, something has widened its activation further than it meant to");
    }

    [Fact]
    public async Task The_scanner_reads_a_real_tree_rather_than_being_told_what_is_in_one()
    {
        // The other half of why the fixtures are on disk. Everything else hands
        // the resolver evidence somebody wrote out, so nothing checks that
        // walking a directory produces the same thing.
        var path = Shape.All.Single(shape => shape.Name == "dotnet-api").WriteTo(_root);

        var evidence = (await new RepositoryEvidenceReader().ReadAsync(path)).Value!;

        evidence.Count(".cs").Should().BeGreaterThan(0, "the tree really does hold C#");
        evidence.Dependencies.Should().NotBeEmpty("the manifest really was opened");

        // Directories holding other people's code are skipped, and the fixture
        // plants one to prove it: a node_modules full of TypeScript would
        // otherwise make this look like a TypeScript project.
        evidence.Count(".ts").Should().Be(0, "node_modules must not count as this repository");
    }

    /// <summary>Where the committed snapshot lives, found from the test binary.</summary>
    private static string FixturesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && directory.Name != "Loadout.Tests")
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test project directory has to be findable from the binary");

        return Path.Combine(directory!.FullName, "Fixtures");
    }
}

/// <summary>
/// The repository shapes worth holding still.
/// </summary>
/// <remarks>
/// Written out at test time rather than committed as trees of stub files. The
/// scan is just as real either way — these are real directories with real files
/// in them — and one readable list beats forty near-empty files nobody can take
/// in at a glance.
/// </remarks>
internal sealed record Shape(
    string Name,
    string? Mode,
    string? Task,
    IReadOnlyList<string> Files)
{
    public static IReadOnlyList<Shape> All =>
    [
        new("dotnet-api", "implement", "add a retry to the upload step",
        [
            "src/Api/Api.csproj|<Project><PackageReference Include=\"Microsoft.Extensions.Http\" /></Project>",
            "src/Api/Program.cs", "src/Api/Orders.cs", "src/Api/Customers.cs",
            "tests/Api.Tests/OrderTests.cs",
            "node_modules/left-pad/index.ts",
        ]),
        new("django-app", "implement", "add a field to the order model",
        [
            "requirements.txt|Django==5.0\npsycopg2-binary==2.9",
            "manage.py", "orders/models.py", "orders/views.py", "orders/admin.py",
        ]),
        new("rust-cli", "implement", "make the parser faster",
        [
            "Cargo.toml|[dependencies]\nclap = \"4\"",
            "src/main.rs", "src/parse.rs", "src/lib.rs",
        ]),
        new("go-service", "review", "review the deploy path",
        [
            "go.mod|module example.com/svc",

            // Three, because a language has to clear a count before it says
            // anything about a repository — one file of a kind is an accident.
            // Two would make this a fixture called "go-service" that is never
            // given any Go guidance, which tests nothing it claims to.
            "main.go", "internal/server/server.go", "internal/store/store.go",
            "Dockerfile|FROM golang:1.22",
            "k8s/deployment.yaml",
        ]),
        new("typescript-frontend", "implement", "fix the settings screen layout",
        [
            "package.json|{\"dependencies\":{\"react\":\"18\",\"vite\":\"5\"}}",
            "src/App.tsx", "src/components/Settings.tsx", "src/main.ts",
        ]),
        new("terraform-infra", "advise", "should this be one module or three",
        [
            "main.tf", "variables.tf", "modules/network/main.tf",
        ]),
        new("mixed-monorepo", "investigate", "why is this postgres query so slow",
        [
            "services/api/Api.csproj|<Project><PackageReference Include=\"Npgsql\" /></Project>",
            "services/api/Program.cs",
            "services/web/package.json|{\"dependencies\":{\"react\":\"18\"}}",
            "services/web/src/App.tsx",
            "db/schema.sql", "db/migrations/0001_init.sql",
            "infra/main.tf",
        ]),
        new("documentation-only", null, "tidy up the wording",
        [
            "README.md", "docs/guide.md", "docs/reference.md",
        ]),
    ];

    /// <summary>Writes this shape into a directory of its own and returns it.</summary>
    public string WriteTo(string root)
    {
        var path = Path.Combine(root, Name);

        foreach (var entry in Files)
        {
            var split = entry.IndexOf('|', StringComparison.Ordinal);

            var relative = split < 0 ? entry : entry[..split];
            var content = split < 0 ? string.Empty : entry[(split + 1)..];

            var file = Path.Combine(path, relative.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, content);
        }

        return path;
    }
}
