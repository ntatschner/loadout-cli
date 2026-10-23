using System.Globalization;
using FluentAssertions;
using Loadout.Core.Triggers;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>Deleting old days of the trigger journal, and never the days the ledger is read from.</summary>
public sealed class TriggerJournalPruneTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _state = Path.Combine(Path.GetTempPath(), "loadout-prune-" + Guid.NewGuid().ToString("N"));
    private readonly TriggerJournal _journal;

    public TriggerJournalPruneTests()
    {
        _journal = new TriggerJournal(_state);

        foreach (var daysAgo in (int[])[100, 60, 20, 0])
        {
            _journal.Append(new TriggerEntry(Now.AddDays(-daysAgo), TriggerEntryKinds.Event, Event: $"e{daysAgo}"));
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_state, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void The_default_keeps_ninety_days_and_writes_down_what_went()
    {
        var gone = _journal.Prune(Now, TriggerJournal.Kept, "kept for 90 days");

        gone.Should().Equal(Day(100));
        File.Exists(_journal.DayPath(Now.AddDays(-100))).Should().BeFalse();
        File.Exists(_journal.DayPath(Now.AddDays(-60))).Should().BeTrue();

        var record = _journal.Read(Now.AddDays(-1)).Last();
        record.Kind.Should().Be(TriggerEntryKinds.Pruned);
        record.Reason.Should().Contain("kept for 90 days").And.Contain(Day(100));
    }

    [Fact]
    public void Asking_for_less_than_the_ledger_reads_still_keeps_what_it_reads()
    {
        _journal.Prunable(Now, TimeSpan.FromDays(5)).Days.Should().Equal(Day(100), Day(60));

        _journal.Prune(Now, TimeSpan.FromDays(5), "by hand");

        File.Exists(_journal.DayPath(Now.AddDays(-20))).Should().BeTrue("20 days is inside the 31 the ledger is rebuilt from");
    }

    [Fact]
    public void Nothing_old_enough_writes_nothing()
    {
        _journal.Prune(Now, TimeSpan.FromDays(365), "by hand").Should().BeEmpty();

        _journal.Read(Now.AddDays(-1)).Should().NotContain(one => one.Kind == TriggerEntryKinds.Pruned);
    }

    [Fact]
    public void Over_its_size_the_oldest_days_go_but_never_one_the_ledger_reads()
    {
        var ages = _journal.Prunable(Now, TimeSpan.FromDays(365), allowance: 1);

        ages.Days.Should().Equal(Day(100), Day(60));
        ages.ForSize.Should().Be(2);
        ages.Over.Should().BeTrue("20 days ago and today are inside the 31 the ledger is rebuilt from, so they stay");

        var fits = Size(60) + Size(20) + Size(0);
        var enough = _journal.Prunable(Now, TimeSpan.FromDays(365), allowance: fits);

        enough.Days.Should().Equal(Day(100));
        enough.Over.Should().BeFalse();

        _journal.Prunable(Now, TimeSpan.FromDays(365), allowance: fits + Size(100)).Days.Should().BeEmpty();
    }

    [Fact]
    public void A_prune_for_size_says_so_in_what_it_writes_down()
    {
        _journal.Prune(Now, TriggerJournal.Kept, "kept for 90 days", allowance: 1).Should().Equal(Day(100), Day(60));

        _journal.Read(Now.AddDays(-1)).Last().Reason.Should().Contain("1 more to stay under");
        File.Exists(_journal.DayPath(Now.AddDays(-20))).Should().BeTrue();
    }

    [Fact]
    public void Over_with_only_the_ledgers_days_left_it_says_so_and_names_the_rule_writing_most()
    {
        foreach (var one in Enumerable.Range(0, 20))
        {
            _journal.Append(new TriggerEntry(Now.AddDays(-2), TriggerEntryKinds.Refused, Rule: "noisy", Reason: $"refused {one}"));
        }

        _journal.Append(new TriggerEntry(Now.AddDays(-2), TriggerEntryKinds.Refused, Rule: "quiet", Reason: "refused"));

        var over = _journal.OverAllowance(Now, allowance: 1);

        over.Should().Contain("only the last 31 days left").And.Contain("noisy writes the most");
        _journal.OverAllowance(Now, allowance: null).Should().BeNull("zero turns the size limit off");
        _journal.OverAllowance(Now, allowance: 64L * 1024 * 1024).Should().BeNull();
    }

    [Theory]
    [InlineData(null, 64L * 1024 * 1024)]
    [InlineData(-5, 64L * 1024 * 1024)]
    [InlineData(10, 10L * 1024 * 1024)]
    [InlineData(0, null)]
    public void The_allowance_is_the_default_unless_set_and_zero_turns_it_off(int? megabytes, long? bytes) =>
        TriggerJournal.Allowance(megabytes).Should().Be(bytes);

    private long Size(int daysAgo) => new FileInfo(_journal.DayPath(Now.AddDays(-daysAgo))).Length;

    private static string Day(int daysAgo) =>
        Now.AddDays(-daysAgo).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
