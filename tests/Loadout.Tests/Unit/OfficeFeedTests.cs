using FluentAssertions;
using Loadout.Core.Teams.Daemon;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The one reader behind every page watching the office: it reads on its own
/// clock, only while somebody is listening, and hands each page a snapshot
/// only when it has changed.
/// </summary>
public sealed class OfficeFeedTests
{
    private static readonly TimeSpan Soon = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task The_first_page_gets_the_office_as_it_stands()
    {
        using var feed = new OfficeFeed(() => "one", TimeSpan.FromMilliseconds(20));

        var (sequence, snapshot) = await feed.NextAsync(0, CancellationToken.None).WaitAsync(Soon);

        sequence.Should().Be(1);
        snapshot.Should().Be("one");
    }

    [Fact]
    public async Task Nothing_is_sent_again_until_something_changes()
    {
        var now = "one";
        using var feed = new OfficeFeed(() => now, TimeSpan.FromMilliseconds(20));

        var (first, _) = await feed.NextAsync(0, CancellationToken.None).WaitAsync(Soon);
        var next = feed.NextAsync(first, CancellationToken.None);

        // Several reads of an unchanged office, and nobody is told anything.
        await Task.Delay(200);
        next.IsCompleted.Should().BeFalse();

        now = "two";

        var (second, snapshot) = await next.WaitAsync(Soon);

        second.Should().Be(first + 1);
        snapshot.Should().Be("two");
    }

    [Fact]
    public async Task Every_page_waiting_is_told_by_the_same_read()
    {
        var reads = 0;
        var now = "one";
        using var feed = new OfficeFeed(
            () =>
            {
                Interlocked.Increment(ref reads);

                return now;
            },
            TimeSpan.FromHours(1));

        // Driven by hand: the clock is an hour, so only these looks read.
        var one = feed.NextAsync(0, CancellationToken.None);
        var two = feed.NextAsync(0, CancellationToken.None);

        (await one.WaitAsync(Soon)).Snapshot.Should().Be("one");
        (await two.WaitAsync(Soon)).Snapshot.Should().Be("one");
        reads.Should().Be(1, "the loop looks once when it starts, however many pages asked");
    }

    [Fact]
    public async Task The_feed_stops_reading_once_the_last_page_has_gone()
    {
        var reads = 0;
        using var feed = new OfficeFeed(
            () =>
            {
                Interlocked.Increment(ref reads);

                return "same";
            },
            TimeSpan.FromMilliseconds(20));

        using (var leaving = new CancellationTokenSource())
        {
            var (first, _) = await feed.NextAsync(0, CancellationToken.None).WaitAsync(Soon);
            var waiting = feed.NextAsync(first, leaving.Token);

            await Task.Delay(100);
            leaving.Cancel();

            await FluentActions.Awaiting(() => waiting).Should().ThrowAsync<OperationCanceledException>();
        }

        feed.Listening.Should().Be(0);

        // Let the loop notice. A fixed 150ms was one interval too few on a busy
        // macOS runner, where a round already under way finished after it and
        // read once more; so wait for the count to stop moving, for up to two
        // seconds - a feed that never stops still fails below - then require it
        // to stay still.
        var settled = Volatile.Read(ref reads);

        for (var waited = 0; waited < 2000; waited += 100)
        {
            await Task.Delay(100);

            var now = Volatile.Read(ref reads);

            if (now == settled)
            {
                break;
            }

            settled = now;
        }

        await Task.Delay(300);
        Volatile.Read(ref reads).Should().Be(settled, "nobody is listening, so nothing should be read");

        // And a page arriving later starts it again.
        (await feed.NextAsync(0, CancellationToken.None).WaitAsync(Soon)).Snapshot.Should().Be("same");
    }

    [Fact]
    public async Task A_journal_caught_mid_write_is_skipped_rather_than_ending_the_feed()
    {
        var fail = true;
        using var feed = new OfficeFeed(
            () => fail ? throw new IOException("being written") : "read",
            TimeSpan.FromMilliseconds(20));

        var next = feed.NextAsync(0, CancellationToken.None);

        await Task.Delay(100);
        fail = false;

        (await next.WaitAsync(Soon)).Snapshot.Should().Be("read");
    }
}
