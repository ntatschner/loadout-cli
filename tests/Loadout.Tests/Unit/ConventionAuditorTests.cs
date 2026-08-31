using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Counting where a repository departs from what its own specialists ask for.
/// </summary>
/// <remarks>
/// The value of this is entirely in whether the numbers can be trusted. A count
/// that includes test code, or a build directory, or a language the project is
/// not written in, is one somebody reads once and never again — so what it
/// leaves out matters more than what it finds.
/// </remarks>
public sealed class ConventionAuditorTests : IDisposable
{
    private readonly string _root;

    public ConventionAuditorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-conventions-" + Guid.NewGuid().ToString("N"));

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

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private IReadOnlyList<ConventionFinding> Audit(params string[] applicable) =>
        ConventionAuditor.Audit(
            _root,
            id => applicable.Contains(id, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void It_counts_every_occurrence_not_every_file()
    {
        Write("src/a.rs", "let x = foo.unwrap();\nlet y = bar.unwrap();");
        Write("src/b.rs", "let z = baz.unwrap();");

        var finding = Audit("language.rust").Single();

        finding.Occurrences.Should().Be(3);
        finding.FilesRead.Should().Be(2);

        // Worst first, because that is where somebody would start.
        finding.Files[0].Path.Should().EndWith("a.rs");
        finding.Files[0].Count.Should().Be(2);
    }

    [Fact]
    public void A_language_the_project_does_not_use_is_not_counted()
    {
        Write("src/a.rs", "let x = foo.unwrap();");

        // The specialist would not be loaded either, so counting its rule here
        // would report a project against advice it was never given.
        Audit("language.powershell").Should().BeEmpty();
    }

    [Fact]
    public void Test_code_is_left_out()
    {
        Write("tests/it.rs", "let x = foo.unwrap();");
        Write("src/parser_tests.rs", "let y = bar.unwrap();");
        Write("benches/speed.rs", "let z = baz.unwrap();");

        // unwrap in a test is a test asserting something, not a panic waiting
        // for a user.
        Audit("language.rust").Should().BeEmpty();
    }

    [Fact]
    public void Build_output_and_dependencies_are_left_out()
    {
        Write("target/debug/generated.rs", "let x = foo.unwrap();");
        Write("node_modules/pkg/thing.rs", "let y = bar.unwrap();");
        Write(".git/hooks/sample.rs", "let z = baz.unwrap();");

        // Nobody wrote these and nobody will fix them.
        Audit("language.rust").Should().BeEmpty();
    }

    [Fact]
    public void Nothing_found_is_reported_as_nothing_rather_than_zero()
    {
        Write("src/a.rs", "let x = foo?;");

        // A finding with no occurrences would be a row somebody has to read to
        // discover it says nothing.
        Audit("language.rust").Should().BeEmpty();
    }

    [Fact]
    public void Every_check_quotes_the_rule_it_came_from()
    {
        foreach (var check in ConventionAuditor.Checks)
        {
            // A count with no source behind it is the tool's own opinion, and
            // the specialist library is where opinions are supposed to live.
            check.Rule.Should().NotBeNullOrWhiteSpace();
            check.SpecialistId.Should().StartWith("language.");

            // And what it cannot see is part of the finding, not a footnote.
            check.Caveat.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Select_star_is_found_case_insensitively_and_across_whitespace()
    {
        Write("db/query.sql", "select   * from users;\nSELECT * FROM orders;");

        Audit("language.sql").Single().Occurrences.Should().Be(2);
    }

    [Fact]
    public void A_repository_that_is_not_there_reports_nothing()
    {
        ConventionAuditor
            .Audit(Path.Combine(_root, "gone"), _ => true)
            .Should().BeEmpty();
    }
}
