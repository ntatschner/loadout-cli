using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading what a run delivered: only what its outbox lists, redacted when it
/// is shown, exactly when it is downloaded.
/// </summary>
public sealed class RunOutboxFilesTests : IDisposable
{
    private const string First = "1111111111111111111111111111111111111111";
    private const string Second = "2222222222222222222222222222222222222222";
    private const string Before = "0000000000000000000000000000000000000000";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-outbox-files-" + Guid.NewGuid().ToString("N"));
    private readonly string _repository;
    private readonly FakeGit _git;

    public RunOutboxFilesTests()
    {
        _repository = Path.Combine(_root, "repo");
        Directory.CreateDirectory(Path.Combine(_repository, "docs"));

        _git = new FakeGit(_repository);

        _git.Blobs[$"{First}:docs/usage.md"] = "# Usage, first\n"u8.ToArray();
        _git.Blobs[$"{Second}:docs/usage.md"] = "# Usage, second\n"u8.ToArray();
        _git.Blobs[$"{Second}:src/Since.cs"] = "class Since {}\n"u8.ToArray();
        _git.Blobs[$"{Second}^:old.txt"] = "what was removed\n"u8.ToArray();
        _git.Commits[$"{Second}^"] = Before;

        File.WriteAllText(Path.Combine(_repository, "PLAN.md"), "the plan\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What the run delivered, in the shape `team outbox` reads it.</summary>
    private Outbox Delivered(params OutboxAsset[] loose) => new(
        _repository,
        [
            new OutboxFile("docs/usage.md", "added", First, "writer"),
            new OutboxFile("docs/usage.md", "changed", Second, "writer"),
            new OutboxFile("src/Since.cs", "added", Second, "implementer"),
            new OutboxFile("old.txt", "removed", Second, "implementer"),
        ],
        loose.Length > 0 ? loose : [new OutboxAsset(Path.Combine(_repository, "PLAN.md"), "plan", 9, "planner", null)],
        []);

    [Fact]
    public async Task A_listed_file_reads_as_its_commit_left_it()
    {
        var read = await RunOutboxFiles.CommittedAsync(_git, Delivered(), First, "docs/usage.md");

        Encoding.UTF8.GetString(read.Value!.Bytes).Should().Be("# Usage, first\n");
        read.Value.FromCommit.Should().BeNull();
    }

    [Theory]
    [InlineData(First, "src/Since.cs")] // a real file, but not one that commit delivered
    [InlineData(Second, "README.md")]
    [InlineData(Second, "../../secrets.txt")]
    [InlineData(Second, "docs/../../../secrets.txt")]
    [InlineData(Second, "C:/Windows/win.ini")]
    [InlineData("HEAD", "docs/usage.md")]
    public async Task Anything_the_outbox_does_not_list_is_refused(string commit, string path)
    {
        _git.Blobs[$"{commit}:{path}"] = "should never be read"u8.ToArray();

        var read = await RunOutboxFiles.CommittedAsync(_git, Delivered(), commit, path);

        read.Failed.Should().BeTrue();
        read.ExitCode.Should().Be(ExitCode.PolicyViolation);
    }

    /// <remarks>
    /// A node reports its commits in its own words. Listed or not, only a
    /// hash goes on to git.
    /// </remarks>
    [Fact]
    public async Task A_listed_commit_that_is_not_a_hash_is_not_passed_to_git()
    {
        var outbox = new Outbox(_repository, [new OutboxFile("docs/usage.md", "added", "--output=x", "writer")], [], []);

        _git.Blobs["--output=x:docs/usage.md"] = "never to be read"u8.ToArray();

        var read = await RunOutboxFiles.CommittedAsync(_git, outbox, "--output=x", "docs/usage.md");

        read.ExitCode.Should().Be(ExitCode.InvalidArguments);
    }

    [Fact]
    public async Task A_removed_file_is_read_as_the_commit_before_left_it()
    {
        var read = await RunOutboxFiles.CommittedAsync(_git, Delivered(), Second, "old.txt");

        Encoding.UTF8.GetString(read.Value!.Bytes).Should().Be("what was removed\n");
        read.Value.FromCommit.Should().Be(Before);
    }

    [Fact]
    public void A_reported_file_is_read_as_it_is_on_disk()
    {
        var read = RunOutboxFiles.Loose(Delivered(), Path.Combine(_repository, "PLAN.md"));

        Encoding.UTF8.GetString(read.Value!.Bytes).Should().Be("the plan\n");
    }

    /// <remarks>
    /// The outbox lists a reported file where the node said it was, and a
    /// node can say anything. Listed is not enough for one of these.
    /// </remarks>
    [Fact]
    public void A_reported_file_outside_the_repository_is_refused_even_though_it_is_listed()
    {
        var outside = Path.Combine(_root, "secrets.txt");
        File.WriteAllText(outside, "not the run's");

        var climbing = Path.Combine(_repository, "..", "secrets.txt");

        var outbox = Delivered(
            new OutboxAsset(outside, "file", 13, "planner", null),
            new OutboxAsset(climbing, "file", 13, "planner", null));

        RunOutboxFiles.Loose(outbox, outside).ExitCode.Should().Be(ExitCode.PolicyViolation);
        RunOutboxFiles.Loose(outbox, climbing).ExitCode.Should().Be(ExitCode.PolicyViolation);
    }

    [Fact]
    public void A_reported_file_not_listed_is_refused()
    {
        File.WriteAllText(Path.Combine(_repository, "other.md"), "also in the repository");

        RunOutboxFiles.Loose(Delivered(), Path.Combine(_repository, "other.md"))
            .ExitCode.Should().Be(ExitCode.PolicyViolation);
    }

    [Fact]
    public void Lines_with_something_taken_out_are_counted()
    {
        // Shaped like tokens and not tokens: invented for this test.
        var text = "first line\nGITHUB_TOKEN=abcdefghij0123456789\nnothing here\ndb_password: hunter2hunter2\nlast\n";

        var view = RunOutboxFiles.View(new OutboxRead(Encoding.UTF8.GetBytes(text), "notes.txt"));

        view.Status.Should().Be(200);
        view.RedactedLines.Should().Be(2);
        view.Lines.Should().Be(5);
        view.Truncated.Should().BeFalse();

        var shown = Encoding.UTF8.GetString(view.Body);

        shown.Should().NotContain("abcdefghij0123456789").And.NotContain("hunter2hunter2");
        shown.Split('\n').Should().HaveCount(6, "a line redacted is still a line, so the numbers stay true");
    }

    [Fact]
    public void Text_is_cut_at_two_thousand_lines_and_says_so()
    {
        var text = string.Concat(Enumerable.Range(1, 2500).Select(one => $"line {one}\n"));

        var view = RunOutboxFiles.View(new OutboxRead(Encoding.UTF8.GetBytes(text), "long.log"));

        view.Truncated.Should().BeTrue();
        view.Lines.Should().Be(2500);

        var shown = Encoding.UTF8.GetString(view.Body);

        shown.TrimEnd('\n').Split('\n').Should().HaveCount(RunOutboxFiles.MostLines);
        shown.Should().Contain("line 2000\n").And.NotContain("line 2001");
    }

    [Fact]
    public void A_file_of_exactly_the_limit_is_not_cut()
    {
        var text = string.Concat(Enumerable.Range(1, RunOutboxFiles.MostLines).Select(one => $"line {one}\n"));

        var view = RunOutboxFiles.View(new OutboxRead(Encoding.UTF8.GetBytes(text), "exact.log"));

        view.Truncated.Should().BeFalse();
        Encoding.UTF8.GetString(view.Body).Should().Be(text);
    }

    [Fact]
    public void Text_nothing_could_check_in_time_is_not_shown()
    {
        var view = RunOutboxFiles.View(
            new OutboxRead("a long unbroken line"u8.ToArray(), "blob.txt"),
            _ => throw new RegexMatchTimeoutException());

        view.Status.Should().Be(422);
        view.Body.Should().BeEmpty();
        view.Error.Should().Contain("Download it instead");
    }

    [Fact]
    public void An_image_is_served_as_itself()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01];

        var view = RunOutboxFiles.View(new OutboxRead(png, "lead.PNG"));

        view.ContentType.Should().Be("image/png");
        view.Body.Should().Equal(png);
        view.Text.Should().BeFalse();
    }

    [Fact]
    public void An_svg_is_an_image_and_is_still_redacted_whole()
    {
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'>\n<!-- API_KEY=abcdefghij0123456789 -->\n</svg>\n";

        var view = RunOutboxFiles.View(new OutboxRead(Encoding.UTF8.GetBytes(svg), "mark.svg"));

        view.ContentType.Should().Be("image/svg+xml");
        view.RedactedLines.Should().Be(1);
        Encoding.UTF8.GetString(view.Body).Should().NotContain("abcdefghij0123456789").And.EndWith("</svg>\n");
    }

    [Fact]
    public void Something_neither_text_nor_an_image_is_not_shown()
    {
        RunOutboxFiles.View(new OutboxRead([0x50, 0x4B, 0x03, 0x04, 0x00, 0x00], "bundle.bin"))
            .Status.Should().Be(415);
    }

    /// <remarks>
    /// Each path once, as the run left it: the later commit's usage page, not
    /// both; no file the run removed; and the reported plan where it lives.
    /// </remarks>
    [Fact]
    public async Task The_zip_holds_exactly_the_files_the_run_left()
    {
        var zip = await RunOutboxFiles.ZipAsync(_git, Delivered());

        zip.Entries.Should().BeEquivalentTo("docs/usage.md", "src/Since.cs", "PLAN.md");
        zip.LeftOut.Should().BeEmpty();

        using var archive = new ZipArchive(new MemoryStream(zip.Bytes));

        archive.Entries.Select(one => one.FullName).Should().BeEquivalentTo(zip.Entries);

        using var reader = new StreamReader(archive.GetEntry("docs/usage.md")!.Open());

        (await reader.ReadToEndAsync()).Should().Be("# Usage, second\n");
    }

    [Fact]
    public async Task What_cannot_go_in_the_zip_is_named_inside_it()
    {
        var outbox = Delivered(new OutboxAsset(Path.Combine(_root, "elsewhere.md"), "file", 1, "planner", null));

        var zip = await RunOutboxFiles.ZipAsync(_git, outbox);

        zip.LeftOut.Should().ContainSingle().Which.Should().Contain("elsewhere.md");

        using var archive = new ZipArchive(new MemoryStream(zip.Bytes));

        archive.GetEntry("left-out.txt").Should().NotBeNull();
        archive.Entries.Should().NotContain(one => one.FullName.Contains("elsewhere", StringComparison.Ordinal));
    }
}
