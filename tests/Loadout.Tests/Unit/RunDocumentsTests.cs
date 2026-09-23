using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The papers a run leaves behind, and who is allowed to ask for them.
/// </summary>
/// <remarks>
/// <para>
/// Two separate things are worth being sure of. One is that a name arriving
/// from a browser cannot be turned into a path outside the run's own
/// directory, which is the whole reason names are matched against a shape
/// rather than looked up.
/// </para>
/// <para>
/// The other is the order. A run with five nodes writes twenty of these, and
/// sorted by file name every answer - which is one word - sits above every
/// brief and report, which are what somebody opened the list for.
/// </para>
/// </remarks>
public sealed class RunDocumentsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "loadout-papers-" + Guid.NewGuid().ToString("N")[..8]);

    public RunDocumentsTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_directory, name);

        File.WriteAllText(path, text);

        return path;
    }

    [Theory]
    [InlineData("brief-lead.json")]
    [InlineData("report-implementer-1-2.json")]
    [InlineData("final-report.json")]
    [InlineData("policy-lead.json")]
    [InlineData("ask-implementer-1-toolu_01A.json")]
    [InlineData("answer-implementer-1-toolu_01A.json")]
    public void What_a_run_writes_can_be_asked_for(string name)
    {
        RunDocuments.Showable(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("../../../.ssh/id_rsa.json")]
    [InlineData("..\\config.json")]
    [InlineData("brief-/../../secrets.json")]
    [InlineData("C:/Users/someone/.aws/credentials.json")]
    [InlineData("journal.jsonl")]
    [InlineData("brief-lead.txt")]
    [InlineData("settings.json")]
    [InlineData("brief-.json")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused_before_it_becomes_a_path(string? name)
    {
        RunDocuments.Showable(name).Should().BeFalse();
    }

    [Theory]
    [InlineData("brief-lead.json", "brief", "lead")]
    [InlineData("report-planner-1.json", "report", "planner-1")]
    [InlineData("policy-verifier.json", "policy", "verifier")]
    [InlineData("ask-implementer-1-toolu_9.json", "question", "implementer-1-toolu_9")]
    [InlineData("answer-implementer-1-toolu_9.json", "answer", "implementer-1-toolu_9")]
    [InlineData("final-report.json", "final report", "the run")]
    public void A_name_says_what_it_is_and_who_it_is_about(string name, string kind, string subject)
    {
        var (readKind, readSubject) = RunDocuments.Describe(name);

        readKind.Should().Be(kind);
        readSubject.Should().Be(subject);
    }

    [Fact]
    public void The_run_report_comes_first_and_a_node_keeps_its_brief_and_report_together()
    {
        // Sorted by file name, every "answer-" is above every "brief-", and
        // the thing somebody opened the list for is at the bottom.
        Write("answer-planner-1-toolu_9.json", "{}");
        Write("ask-planner-1-toolu_9.json", "{}");
        Write("report-planner-1.json", "{}");
        Write("brief-planner-1.json", "{}");
        Write("brief-lead.json", "{}");
        Write("final-report.json", "{}");
        Write("journal.jsonl", "not this one");

        var papers = RunDocuments.In(_directory).Select(one => one.Name).ToList();

        papers.Should().Equal(
            "final-report.json",
            "brief-lead.json",
            "brief-planner-1.json",
            "report-planner-1.json",
            "ask-planner-1-toolu_9.json",
            "answer-planner-1-toolu_9.json");
    }

    [Fact]
    public void A_document_about_a_node_is_about_that_node_on_its_second_attempt_too()
    {
        var first = new RunDocument("brief-implementer-1.json", "brief", "implementer-1", 10, DateTimeOffset.UtcNow);
        var again = new RunDocument("brief-implementer-1-2.json", "brief", "implementer-1-2", 10, DateTimeOffset.UtcNow);
        var other = new RunDocument("brief-implementer-10.json", "brief", "implementer-10", 10, DateTimeOffset.UtcNow);

        first.Mentions("implementer-1").Should().BeTrue();
        again.Mentions("implementer-1").Should().BeTrue();

        // Not a prefix match on its own: implementer-10 is a different node.
        other.Mentions("implementer-1").Should().BeFalse();
    }

    [Fact]
    public void A_name_that_is_not_one_of_these_is_never_read_even_if_the_file_is_there()
    {
        Write("journal.jsonl", "the whole journal");

        var read = RunDocuments.Read(_directory, "journal.jsonl");

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("not one of the documents");
    }

    [Fact]
    public void What_looks_like_a_credential_never_reaches_the_page()
    {
        // The pattern, never the value - the rule the whole project keeps.
        // A brief carries whatever the goal said and a report carries whatever
        // a node chose to quote, and nobody wrote either thinking about who
        // would read it over a network later.
        Write("report-lead.json", """{"outcome":"used ghp_0123456789abcdefghijklmnopqrstuvwxyzAB to push"}""");

        var read = RunDocuments.Read(_directory, "report-lead.json");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value.Should().NotContain("ghp_0123456789abcdefghijklmnopqrstuvwxyzAB");
    }

    /// <summary>Roughly the shape a real report has, at whatever size is wanted.</summary>
    private static string Jsonish(int bytes)
    {
        const string One = """{"node":"implementer/1","said":"read one file and wrote another"},""";

        return "[" + string.Concat(Enumerable.Repeat(One, (bytes / One.Length) + 1)) + "]";
    }

    [Fact]
    public void A_document_too_big_for_a_browser_is_cut_and_says_so()
    {
        Write("report-lead.json", Jsonish(RunDocuments.Most + 5000));

        var read = RunDocuments.Read(_directory, "report-lead.json");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Length.Should().BeLessThan(RunDocuments.Most + 500);

        // Said out loud rather than done quietly: a report that just stops is
        // a report somebody reads the wrong conclusion from.
        read.Value.Should().Contain("cut here");
    }

    [Fact]
    public void A_document_that_cannot_be_checked_for_credentials_is_not_shown()
    {
        // A long unbroken run of characters - a base64 blob a node pasted into
        // its report - can use up the redactor's whole budget, and then it
        // gives up. Unchecked is the one thing this must not be, so the choice
        // is between showing nothing and showing something nobody has looked
        // at, and it shows nothing.
        //
        // Made to fail here rather than waited for: a quarter of a megabyte of
        // 'x' times out on this machine some runs and not others, which is a
        // test that would pass or fail on how busy a build agent was.
        Write("report-lead.json", "{}");

        var read = RunDocuments.Read(
            _directory,
            "report-lead.json",
            _ => throw new System.Text.RegularExpressions.RegexMatchTimeoutException());

        read.Failed.Should().BeTrue("nothing goes out that the redactor could not get through");
        read.Error.Should().Contain("could not be checked");
    }

    [Fact]
    public void A_run_that_wrote_nothing_lists_nothing_rather_than_failing()
    {
        RunDocuments.In(Path.Combine(_directory, "not-here")).Should().BeEmpty();
    }
}
