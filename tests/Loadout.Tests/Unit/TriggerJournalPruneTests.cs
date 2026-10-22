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

    private static string Day(int daysAgo) =>
        Now.AddDays(-daysAgo).UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
