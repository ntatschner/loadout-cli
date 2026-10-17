using FluentAssertions;
using Loadout.Platform.Abstractions;
using Loadout.Platform.Unix;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Adding up what a process and everything it started hold resident.</summary>
public sealed class ProcessTreeTests
{
    [Fact]
    public void A_root_counts_itself_and_every_descendant_and_nothing_else()
    {
        ProcessEntry[] table =
        [
            new(1, 0, 1000),
            new(10, 1, 100),
            new(11, 10, 20),
            new(12, 11, 3),
            new(20, 1, 5000),
        ];

        var totals = ProcessTrees.Resident(table, [10]);

        totals.Should().ContainSingle().Which.Should().Be(new KeyValuePair<int, long>(10, 123));
    }

    [Fact]
    public void A_process_whose_memory_could_not_be_read_counts_as_nothing_rather_than_failing()
    {
        ProcessEntry[] table = [new(10, 1, 100), new(11, 10, null)];

        ProcessTrees.Resident(table, [10])[10].Should().Be(100);
    }

    [Fact]
    public void A_root_the_snapshot_does_not_have_is_left_out()
    {
        ProcessTrees.Resident([new(10, 1, 100)], [99]).Should().BeEmpty();
    }

    [Fact]
    public void A_table_with_a_cycle_in_it_still_ends_and_counts_each_once()
    {
        // What reused identifiers can produce: two processes each naming the
        // other as its parent.
        ProcessEntry[] table = [new(10, 11, 100), new(11, 10, 20), new(12, 12, 7)];

        ProcessTrees.Resident(table, [10, 12]).Should().BeEquivalentTo(new Dictionary<int, long> { [10] = 120, [12] = 7 });
    }

    [Fact]
    public void Ps_output_is_read_as_kilobytes_with_its_padding_and_anything_unreadable_skipped()
    {
        var parsed = UnixProcessTree.Parse("    1     0  1024\n  250     1    16\nnot a line\n  251   250     -\n");

        parsed.Should().Equal(new ProcessEntry(1, 0, 1024 * 1024), new ProcessEntry(250, 1, 16 * 1024), new ProcessEntry(251, 250, null));
    }
}
