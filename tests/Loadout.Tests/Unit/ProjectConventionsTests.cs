using FluentAssertions;
using Loadout.Core.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a repository can be seen to do, as opposed to what somebody says it does.
/// </summary>
/// <remarks>
/// <para>
/// The built-in library knows about "C#" and ".NET". What it cannot know is that
/// this repository returns a result type rather than throwing, and that is the
/// guidance that stops an agent writing code which reads as foreign.
/// </para>
/// <para>
/// Only what can be counted, and every finding says what it was counted from. A
/// judgement dressed as a measurement is worse than an empty section: the empty
/// section says it needs writing, and the measurement says it is already done.
/// </para>
/// </remarks>
public sealed class ProjectConventionsTests : IDisposable
{
    private readonly string _root;

    public ProjectConventionsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-conv-" + Guid.NewGuid().ToString("N"));

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
    public void The_test_framework_and_assertions_are_read_from_the_manifests()
    {
        Write("Directory.Packages.props",
            "<Project><PackageVersion Include=\"xunit\" /><PackageVersion Include=\"FluentAssertions\" /></Project>");
        Write("src/A.cs", "class A;");

        Detect().Should().Contain(c => c.Subject == "Test framework" && c.Finding == "xUnit");
        Detect().Should().Contain(c => c.Subject == "Assertions" && c.Finding == "FluentAssertions");
    }

    [Fact]
    public void A_repository_that_says_nothing_about_a_subject_has_nothing_written_about_it()
    {
        // Left out rather than reported as "none found". A scaffold listing what
        // it could not detect reads as a list of problems, and the author then
        // deletes lines instead of writing them.
        Write("Directory.Packages.props", "<Project></Project>");
        Write("src/A.cs", "class A;");

        Detect().Should().NotContain(c => c.Subject == "Test framework");
        Detect().Should().NotContain(c => c.Subject == "Assertions");
    }

    [Fact]
    public void How_the_code_reports_failure_is_counted_rather_than_asserted()
    {
        // Stated as the ratio it is. "Never throws" is a claim this cannot
        // support, and somebody would have to disprove it later.
        Write("src/A.cs", "class A { Result<int> M() { return Result<int>.Ok(1); } }");
        Write("src/B.cs", "class B { Result<int> M() { return Result<int>.Ok(2); } }");
        Write("src/C.cs", "class C { void M() { throw new Exception(); } }");

        var errors = Detect().Single(c => c.Subject == "Errors");

        errors.Finding.Should().Contain("returns a result type");
        errors.Finding.Should().Contain("2 against 1");
    }

    [Fact]
    public void A_repository_that_mostly_throws_is_described_that_way_round()
    {
        Write("src/A.cs", "class A { void M() { throw new Exception(); } }");
        Write("src/B.cs", "class B { void M() { throw new Exception(); } }");

        Detect().Single(c => c.Subject == "Errors").Finding.Should().StartWith("throws");
    }

    [Fact]
    public void The_share_of_files_carrying_doc_comments_is_reported()
    {
        Write("src/A.cs", "/// <summary>A.</summary>\nclass A;");
        Write("src/B.cs", "class B;");

        Detect().Single(c => c.Subject == "Comments").Finding.Should().StartWith("50%");
    }

    [Fact]
    public void Every_finding_says_how_many_files_it_came_from()
    {
        // A reader who disagrees can go and check, which is not something an
        // assertion offers.
        Write("Directory.Packages.props", "<Project><PackageVersion Include=\"xunit\" /></Project>");
        Write("src/A.cs", "class A;");

        Detect().Should().OnlyContain(c => c.Evidence > 0);
    }

    [Fact]
    public void Code_nobody_here_wrote_is_not_counted_as_this_repository()
    {
        // A node_modules or a bin full of somebody else's source would describe
        // their habits as though they were this project's.
        Write("src/A.cs", "/// <summary>A.</summary>\nclass A;");
        Write("node_modules/dep/D.cs", "class D { void M() { throw new Exception(); } }");
        Write("obj/Generated.cs", "class G { void M() { throw new Exception(); } }");

        Detect().Single(c => c.Subject == "Comments").Finding.Should().StartWith("100%");
        Detect().Should().NotContain(c => c.Subject == "Errors");
    }

    [Fact]
    public void A_repository_with_nothing_in_it_says_nothing()
    {
        Detect().Should().BeEmpty();
    }

    [Fact]
    public void A_directory_that_is_not_there_is_answered_with_nothing_rather_than_a_failure()
    {
        ProjectConventions.Detect(Path.Combine(_root, "no-such-place")).Should().BeEmpty();
    }

    private IReadOnlyList<ProjectConvention> Detect() => ProjectConventions.Detect(_root);

    private void Write(string relative, string content)
    {
        var path = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
