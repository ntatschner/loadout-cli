using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where the documentation has come adrift from the repository.
/// </summary>
/// <remarks>
/// <para>
/// Half of these are about what it must <em>not</em> say. Pointed at this
/// project's own documentation, the first version produced seven findings and
/// every one was wrong — an invented Rust path inside a paragraph about globs,
/// and a table addressing real files from somewhere other than the root. A check
/// that is wrong about seven good references and right about none is one that
/// gets switched off, so the cases keeping it quiet are held down as firmly as
/// the ones making it speak.
/// </para>
/// </remarks>
public sealed class DocsAuditorTests : IDisposable
{
    private readonly string _root;

    public DocsAuditorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-docs-" + Guid.NewGuid().ToString("N"));

        // The directories a path has to start with to be a claim about this
        // repository at all.
        Directory.CreateDirectory(Path.Combine(_root, "src", "Loadout.Core"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
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
    public void A_link_to_a_page_that_is_not_there_is_reported()
    {
        Write("docs/README.md", "See [the guide](guide.md).");

        var finding = Audit().Should().ContainSingle(f => f.Kind == "broken-link").Subject;

        finding.Path.Should().Be("docs/README.md");
        finding.Line.Should().Be(1);
        finding.Detail.Should().Contain("guide.md");
    }

    [Fact]
    public void A_link_is_resolved_from_the_page_it_is_on()
    {
        // How a reader's browser and every renderer resolve it. Resolving from
        // the repository root instead would call every working relative link
        // broken.
        Write("docs/guide.md", "content");
        Write("docs/README.md", "See [the guide](guide.md).");

        Audit().Should().NotContain(finding => finding.Kind == "broken-link");
    }

    [Fact]
    public void A_link_up_and_across_is_resolved_too()
    {
        Write("README.md", "root");
        Write("docs/guide.md", "Back to [the front](../README.md).");
        Write("docs/README.md", "See [the guide](guide.md).");

        Audit().Should().NotContain(finding => finding.Kind == "broken-link");
    }

    [Fact]
    public void A_link_to_a_heading_on_a_page_that_exists_is_fine()
    {
        Write("docs/guide.md", "content");
        Write("docs/README.md", "See [the part about budgets](guide.md#budgets).");

        Audit().Should().NotContain(finding => finding.Kind == "broken-link");
    }

    [Theory]
    [InlineData("[the site](https://example.com/page.md)")]
    [InlineData("[write to us](mailto:someone@example.com)")]
    [InlineData("[this section](#budgets)")]
    [InlineData("[your config](~/.config/loadout/config.yaml)")]
    [InlineData("[a project's memory](projects/<slug>/memory/)")]
    public void Things_that_are_not_references_into_this_repository_are_left_alone(string markdown)
    {
        Write("docs/README.md", markdown);

        Audit().Should().NotContain(finding => finding.Kind == "broken-link");
    }

    [Fact]
    public void A_named_file_under_a_directory_this_repository_has_is_checked()
    {
        Write("docs/README.md", "It lives in `src/Loadout.Core/Gone.cs`.");

        Audit().Should().ContainSingle(finding => finding.Kind == "missing-path");
    }

    [Fact]
    public void A_named_file_that_is_there_is_not_reported()
    {
        Write("src/Loadout.Core/Real.cs", "class Real;");
        Write("docs/README.md", "It lives in `src/Loadout.Core/Real.cs`.");

        Audit().Should().NotContain(finding => finding.Kind == "missing-path");
    }

    [Fact]
    public void An_invented_path_from_somebody_elses_project_is_left_alone()
    {
        // The real false positive this was written against: a paragraph
        // explaining glob derivation, using a Rust path that was never meant to
        // be a file here.
        Write("docs/README.md",
            "A heading reading ``Merit awards (`crates/core/src/recognition/store.rs`)`` "
            + "has already said which files it concerns.");

        Audit().Should().NotContain(finding => finding.Kind == "missing-path");
    }

    [Fact]
    public void A_path_written_relative_to_somewhere_other_than_the_root_is_left_alone()
    {
        // The other six. A table whose column gives paths relative to
        // src/<project>: real files, addressed from somewhere else. Not broken,
        // and calling them missing would be wrong about every row.
        Write("src/Loadout.Core/Context/ContextCompiler.cs", "class C;");
        Write("docs/README.md", "| Composition engine | `Context/ContextCompiler.cs` | ... |");

        Audit().Should().NotContain(finding => finding.Kind == "missing-path");
    }

    [Fact]
    public void A_page_nothing_links_to_is_reported()
    {
        Write("docs/README.md", "Nothing here points anywhere.");
        Write("docs/stray.md", "Nobody arrives here.");

        Audit().Should().ContainSingle(finding => finding.Kind == "orphan")
            .Which.Path.Should().Be("docs/stray.md");
    }

    [Fact]
    public void An_index_is_never_an_orphan()
    {
        // It is the thing doing the linking, and a reader starts there.
        Write("README.md", "front page");
        Write("docs/README.md", "index");

        Audit().Should().NotContain(finding => finding.Kind == "orphan");
    }

    [Fact]
    public void A_document_that_cannot_be_read_is_reported_rather_than_skipped()
    {
        // Silence would be the audit saying a page is fine when it never opened
        // it, which is the one answer it must not give.
        Audit(["docs/never-written.md"]).Should()
            .ContainSingle(finding => finding.Kind == "unreadable");
    }

    private IReadOnlyList<DocsFinding> Audit(IReadOnlyList<string>? documents = null) =>
        DocsAuditor.Audit(_root, documents ?? Documents());

    private IReadOnlyList<string> Documents() =>
        Directory.EnumerateFiles(_root, "*.md", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_root, file).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
