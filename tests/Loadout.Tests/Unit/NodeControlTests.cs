using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Saying something to one node while it is still working.
/// </summary>
/// <remarks>
/// <para>
/// The lead reads messages between its rounds, which is the right place for
/// "change the plan". This is the other one: a worker has gone the wrong way
/// and is spending money doing it, and waiting for its turn to come back means
/// waiting for exactly the spend somebody is trying to stop.
/// </para>
/// <para>
/// Read and deleted in one go, because a message delivered twice reads as
/// insistence rather than as a bug.
/// </para>
/// </remarks>
public sealed class NodeControlTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "loadout-say-" + Guid.NewGuid().ToString("N")[..8]);

    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public NodeControlTests() => Directory.CreateDirectory(_directory);

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

    [Fact]
    public async Task What_was_left_for_a_node_is_what_that_node_is_told()
    {
        await NodeControl.SayAsync(_directory, "implementer/1", "stop, wrong file", Noon);

        NodeControl.Take(_directory, "implementer/1")
            .Should().ContainSingle().Which.Should().Be("stop, wrong file");
    }

    [Fact]
    public async Task It_is_told_once_and_not_again()
    {
        // A message that arrived twice is a node told twice, which reads as
        // insistence rather than as a bug.
        await NodeControl.SayAsync(_directory, "lead", "hold on", Noon);

        NodeControl.Take(_directory, "lead").Should().ContainSingle();
        NodeControl.Take(_directory, "lead").Should().BeEmpty();
    }

    [Fact]
    public async Task Two_things_said_in_order_are_told_in_order()
    {
        await NodeControl.SayAsync(_directory, "lead", "first", Noon);
        await NodeControl.SayAsync(_directory, "lead", "second", Noon.AddSeconds(1));

        NodeControl.Take(_directory, "lead").Should().Equal("first", "second");
    }

    [Fact]
    public async Task Something_said_to_one_node_is_not_said_to_another()
    {
        await NodeControl.SayAsync(_directory, "implementer/1", "for the implementer", Noon);
        await NodeControl.SayAsync(_directory, "reviewer/1", "for the reviewer", Noon);

        NodeControl.Take(_directory, "implementer/1")
            .Should().ContainSingle().Which.Should().Be("for the implementer");

        NodeControl.Take(_directory, "reviewer/1")
            .Should().ContainSingle().Which.Should().Be("for the reviewer");
    }

    [Fact]
    public async Task A_node_whose_name_is_a_path_still_gets_one_file()
    {
        // implementer/1 is a node, not a directory.
        await NodeControl.SayAsync(_directory, "implementer/1", "go on", Noon);

        Directory.EnumerateFiles(_directory).Should().ContainSingle();
        Directory.EnumerateDirectories(_directory).Should().BeEmpty();
    }

    [Fact]
    public void A_node_nobody_has_said_anything_to_is_told_nothing()
    {
        NodeControl.Take(_directory, "lead").Should().BeEmpty();
    }

    [Fact]
    public void A_run_directory_that_is_not_there_is_not_a_failure()
    {
        NodeControl.Take(Path.Combine(_directory, "gone"), "lead").Should().BeEmpty();
    }
}
