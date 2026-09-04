using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
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

    [Fact]
    public void A_number_that_no_longer_matches_what_it_counts_is_reported()
    {
        // The drift this exists for, and the one that already happened here: a
        // count left at 71 while the library grew. The sentence still reads
        // perfectly, which is why nothing else catches it.
        Specialists(3);
        Write("docs/README.md", "There are 2 specialists built into the binary.");

        Audit(policy: Counting()).Should().ContainSingle(finding => finding.Kind == "wrong-count")
            .Which.Detail.Should().Contain("there are 3");
    }

    [Fact]
    public void A_number_that_still_matches_is_silent()
    {
        Specialists(3);
        Write("docs/README.md", "There are 3 specialists built into the binary.");

        Audit(policy: Counting()).Should().NotContain(finding => finding.Kind == "wrong-count");
    }

    [Fact]
    public void A_number_written_as_a_word_is_checked_too()
    {
        // Documentation writes both, and "all six runtime identifiers" is the
        // same claim as "all 6".
        Specialists(3);
        Write("docs/README.md", "All five specialists are built in.");

        Audit(policy: Counting()).Should().ContainSingle(finding => finding.Kind == "wrong-count");
    }

    [Fact]
    public void One_of_something_is_never_read_as_a_total()
    {
        // The real false positive: "the full text of one specialist" is a
        // quantity in a sentence, not a claim about how many there are. Prose is
        // full of them.
        Specialists(3);
        Write("docs/README.md", "Prints the full text of one specialist.");

        Audit(policy: Counting()).Should().NotContain(finding => finding.Kind == "wrong-count");
    }

    [Fact]
    public void A_policy_names_the_singular_and_the_prose_writes_the_plural()
    {
        Specialists(3);
        Write("docs/README.md", "There are 2 specialists here.");

        Audit(policy: Counting()).Should().ContainSingle(finding => finding.Kind == "wrong-count");
    }

    [Fact]
    public void A_number_about_something_nobody_asked_to_count_is_left_alone()
    {
        Specialists(3);
        Write("docs/README.md", "It took 40 minutes and 12 attempts.");

        Audit(policy: Counting()).Should().NotContain(finding => finding.Kind == "wrong-count");
    }

    [Fact]
    public void A_page_set_aside_keeps_its_numbers_and_still_has_its_links_checked()
    {
        // The other real false positive: a survey of somebody else's library,
        // written before implementation, whose numbers are about that library.
        // Counted against this repository every one of them is wrong and none is
        // stale — but its links are still links.
        Specialists(3);
        Write("docs/README.md", "index");
        Write("docs/survey.md", "All 52 specialists there carry [a block](gone.md).");

        var policy = Counting();
        policy.CountsExclude.Add("survey.md");

        var findings = Audit(policy: policy);

        findings.Should().NotContain(finding => finding.Kind == "wrong-count");
        findings.Should().ContainSingle(finding => finding.Kind == "broken-link");
    }

    [Fact]
    public void Without_a_policy_no_number_is_judged_at_all()
    {
        Specialists(3);
        Write("docs/README.md", "There are 2 specialists built into the binary.");

        Audit().Should().NotContain(finding => finding.Kind == "wrong-count");
    }

    private DocsPolicy Counting() => new()
    {
        Counts = { ["specialist"] = "spec/**/*.md" },
    };

    /// <summary>Files for a count to be about, somewhere the audit will not read.</summary>
    private void Specialists(int howMany)
    {
        for (var i = 0; i < howMany; i++)
        {
            Write($"spec/one/{i}.md", "a specialist");
        }
    }

    private IReadOnlyList<DocsFinding> Audit(
        IReadOnlyList<string>? documents = null,
        DocsPolicy? policy = null) =>
        DocsAuditor.Audit(_root, documents ?? Documents(), policy);

    private IReadOnlyList<string> Documents() =>
        Directory.EnumerateFiles(Path.Combine(_root, "docs"), "*.md", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(_root, "*.md", SearchOption.TopDirectoryOnly))
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
