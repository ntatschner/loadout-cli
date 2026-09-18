using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Everything a node did, as against the few lines the journal kept.
/// </summary>
/// <remarks>
/// <para>
/// Two files because there are two wants. The journal is thin on purpose -
/// one line every few seconds, repeats dropped - so that watching a run does
/// not mean reading everything it did. Working out where a node went wrong is
/// the opposite want, and a file that serves both serves neither.
/// </para>
/// <para>
/// The last line is half written while a node is still going, which is
/// ordinary rather than exceptional, so a line that will not parse is skipped
/// rather than failing the read.
/// </para>
/// </remarks>
public sealed class NodeStreamTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "loadout-stream-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public NodeStreamTests() => Directory.CreateDirectory(_directory);

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

    private void Write(string node, params string[] lines) =>
        File.WriteAllLines(NodeStream.PathFor(_directory, node), lines);

    private void Kept(string node, params NodeStep[] steps) =>
        Write(node, [.. steps.Select(NodeStream.Line)]);

    [Fact]
    public void A_nodes_stream_is_not_one_of_the_papers()
    {
        // The papers are documents a run wrote for somebody. A trajectory is
        // not, and it is the one file here that can reach tens of megabytes.
        RunDocuments.Showable(NodeStream.FileFor("implementer/1")).Should().BeFalse();

        // And a node whose name has a slash in it still gets one file.
        NodeStream.FileFor("implementer/1").Should().NotContain("/");
    }

    [Fact]
    public void Every_step_comes_back_in_the_order_it_happened()
    {
        Kept("implementer/1",
            new NodeStep(Noon, "started", null, "claude-opus-5"),
            new NodeStep(Noon.AddSeconds(2), "tool", "Read", "docs/teams.md"),
            new NodeStep(Noon.AddSeconds(3), "said", null, null, "I will start with the docs."));

        var read = NodeStream.Read(_directory, "implementer/1");

        read.Succeeded.Should().BeTrue(read.Error);
        var steps = read.Value!;

        steps.Select(step => step.Kind).Should().Equal("started", "tool", "said");
        steps[1].Tool.Should().Be("Read");
        steps[1].Target.Should().Be("docs/teams.md");
    }

    [Fact]
    public void A_half_written_last_line_is_skipped_rather_than_failing_the_read()
    {
        // Ordinary rather than exceptional: the node is still writing.
        Write("lead",
            NodeStream.Line(new NodeStep(Noon, "tool", "Read", "one.txt")),
            """{"at":"2026-09-18T12:00:02+00:00","kind":"to""");

        var read = NodeStream.Read(_directory, "lead");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value.Should().ContainSingle();
    }

    [Fact]
    public void A_node_that_quoted_a_whole_file_back_does_not_keep_the_whole_file()
    {
        var huge = new string('x', NodeStream.MostPerStep * 4);

        var line = NodeStream.Line(new NodeStep(Noon, "answered", null, null, huge));

        line.Length.Should().BeLessThan(NodeStream.MostPerStep * 2);

        // Read back rather than matched against the line: JSON escapes
        // anything that is not ASCII, so the file says … where the step
        // says a single character, and a test of the line is a test of the
        // serialiser's escaping rules.
        var step = NodeStream.Parse(line);

        step!.Text.Should().EndWith("…", "it says it was cut rather than simply stopping");
        step.Text.Should().HaveLength(NodeStream.MostPerStep + 1);
    }

    [Fact]
    public void Only_the_last_steps_are_handed_over()
    {
        // Read from the end: the interesting part of a node that went wrong is
        // where it stopped, not where it started.
        Kept("lead", [.. Enumerable.Range(0, 50)
            .Select(i => new NodeStep(Noon.AddSeconds(i), "tool", "Read", $"file-{i}.txt"))]);

        var read = NodeStream.Read(_directory, "lead", most: 10);

        read.Value.Should().HaveCount(10);
        read.Value![^1].Target.Should().Be("file-49.txt");
        read.Value[0].Target.Should().Be("file-40.txt");
    }

    [Fact]
    public void A_page_that_already_has_some_is_only_sent_the_rest()
    {
        // Asking again every few seconds and being sent the same two thousand
        // steps each time is most of a megabyte a minute to say nothing.
        Kept("lead", [.. Enumerable.Range(0, 20)
            .Select(i => new NodeStep(Noon.AddSeconds(i), "tool", "Read", $"file-{i}.txt"))]);

        var read = NodeStream.Read(_directory, "lead", after: 18);

        read.Value.Should().HaveCount(2);
        read.Value![0].Target.Should().Be("file-18.txt");

        NodeStream.Count(_directory, "lead").Should().Be(20);
    }

    [Fact]
    public void A_count_beyond_the_end_starts_again_rather_than_showing_nothing()
    {
        // The file was replaced, or the caller is confused. Either way the
        // honest answer is everything, not silence.
        Kept("lead", new NodeStep(Noon, "tool", "Read", "one.txt"));

        NodeStream.Read(_directory, "lead", after: 99).Value.Should().ContainSingle();
    }

    [Fact]
    public void What_looks_like_a_credential_never_reaches_the_page()
    {
        Kept("lead", new NodeStep(
            Noon, "tool", "Bash", "git push https://x:ghp_0123456789abcdefghijklmnopqrstuvwxyzAB@github.com/a/b"));

        var read = NodeStream.Read(_directory, "lead");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value![0].Target.Should().NotContain("ghp_0123456789abcdefghijklmnopqrstuvwxyzAB");
    }

    [Fact]
    public void A_run_from_before_this_existed_says_so_rather_than_showing_nothing()
    {
        var read = NodeStream.Read(_directory, "lead");

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain("recorded no stream");
    }

    [Fact]
    public void A_node_still_writing_can_still_be_read()
    {
        // The file is open for writing while a node runs, so a reader that
        // asked for exclusive access would fail on every live run - which is
        // the only kind anybody watches.
        var path = NodeStream.PathFor(_directory, "lead");

        using var held = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(held) { AutoFlush = true };

        writer.WriteLine(NodeStream.Line(new NodeStep(Noon, "tool", "Read", "one.txt")));

        var read = NodeStream.Read(_directory, "lead");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value.Should().ContainSingle();
    }
}
