using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A node's own account of what it is doing.
/// </summary>
/// <remarks>
/// <para>
/// Beside what the run observes, never instead of it. The run's line says which
/// tool was called and what it was pointed at — precise, and silent about why.
/// This says why and may be wrong. Somebody watching a run that has gone quiet
/// needs both, because a node looping on one file and a node carefully reading
/// forty look the same from outside.
/// </para>
/// <para>
/// What has to hold: it is written where the coordinator will find it, a
/// credential in a sentence never reaches the file, and a line long enough to
/// wrap a terminal is cut before it gets there.
/// </para>
/// </remarks>
public sealed class NodeProgressTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-said-" + Guid.NewGuid().ToString("N"));

    private static readonly DateTimeOffset Noon = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private static NodeSaid Said(string doing, int step = 1, int of = 3, string node = "implementer/1") =>
        new(Noon, node, "role.implementer", step, of, doing);

    private IReadOnlyList<NodeSaid> Read(string node = "implementer/1") =>
        NodeProgress.Read(Path.Combine(_directory, NodeProgress.FileName(node)));

    [Fact]
    public async Task What_a_node_says_is_kept_in_order_under_its_own_name()
    {
        await NodeProgress.AppendAsync(_directory, Said("reading the failing test", 1));
        await NodeProgress.AppendAsync(_directory, Said("writing the fix", 2));

        Read().Select(one => one.Doing).Should()
            .Equal("reading the failing test", "writing the fix");
    }

    [Fact]
    public async Task One_node_cannot_read_another_s_account()
    {
        // A file per node, named from the policy the session was started with.
        // Nothing here takes a node's word for which node it is.
        await NodeProgress.AppendAsync(_directory, Said("mine", node: "implementer/1"));

        Read("implementer/2").Should().BeEmpty();
    }

    [Fact]
    public async Task A_credential_in_a_sentence_never_reaches_the_file()
    {
        // A node describing its own work is exactly where a token pasted from
        // a command ends up, and this file is read by a screen and by a page.
        await NodeProgress.AppendAsync(
            _directory, Said("pushing with https://user:hunter2@github.com/a/b"));

        var kept = Read().Single().Doing;

        kept.Should().NotContain("hunter2");
        kept.Should().Contain("redacted");
    }

    [Fact]
    public async Task A_sentence_too_long_for_a_line_is_cut_before_it_is_written()
    {
        await NodeProgress.AppendAsync(_directory, Said(new string('x', 400)));

        Read().Single().Doing.Length.Should().Be(NodeProgress.Longest);
    }

    [Theory]
    [InlineData(1, 3, "1 of 3: reading")]
    [InlineData(2, 0, "2: reading")]
    public void The_line_says_how_far_through_it_is_only_when_it_knows(int step, int of, string expected)
    {
        // Nought means it does not know yet, and "1 of 0" is worse than saying
        // nothing about the total.
        Said("reading", step, of).Line.Should().Be(expected);
    }

    [Fact]
    public void Nothing_written_reads_as_nothing_said()
    {
        Read().Should().BeEmpty();
        NodeProgress.Read(Path.Combine(_directory, "nowhere", "said-x.jsonl")).Should().BeEmpty();
    }

    [Fact]
    public async Task A_half_written_line_is_skipped_and_the_rest_still_read()
    {
        await NodeProgress.AppendAsync(_directory, Said("first"));

        await File.AppendAllTextAsync(
            Path.Combine(_directory, NodeProgress.FileName("implementer/1")),
            "{\"At\":\"2026-09-16T12:0\n");

        await NodeProgress.AppendAsync(_directory, Said("third"));

        // A read that catches a write is the ordinary case, not a fault.
        Read().Select(one => one.Doing).Should().Equal("first", "third");
    }

    [Fact]
    public async Task An_instance_name_with_a_slash_in_it_still_makes_a_file_name()
    {
        await NodeProgress.AppendAsync(_directory, Said("working", node: "implementer/1"));

        NodeProgress.FileName("implementer/1").Should().NotContain("/");
        Read("implementer/1").Should().ContainSingle();
    }
}
