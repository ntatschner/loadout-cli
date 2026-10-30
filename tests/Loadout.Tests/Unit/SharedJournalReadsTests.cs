using System.Text;
using FluentAssertions;
using Loadout.Core.Reading;
using Loadout.Core.Sessions;
using Loadout.Core.Teams;
using Loadout.Core.Triggers;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The trigger journal, run journals, running sessions and Codex's history are
/// read through readers kept for the process, because the daemon read the same
/// unchanged files thousands of times an hour.
/// </summary>
/// <remarks>
/// Whether a file was read again is shown by changing what is in it while
/// keeping its length and putting its last-write time back: a reader that went
/// to the file sees the change, one that answered from what it kept does not.
/// </remarks>
public sealed class SharedJournalReadsTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    /// <summary>Swaps one piece of text for another the same length and puts the file's time back.</summary>
    private static void RewriteInPlace(string path, string from, string to)
    {
        from.Length.Should().Be(to.Length, "the rewrite has to leave the length alone");

        var written = File.GetLastWriteTimeUtc(path);
        var bytes = File.ReadAllBytes(path);
        var text = Encoding.UTF8.GetString(bytes);

        text.Should().Contain(from);

        // Bytes rather than text, so a byte-order mark or line ending is kept
        // as it was and the length really is unchanged.
        var rewritten = Encoding.UTF8.GetBytes(text.Replace(from, to, StringComparison.Ordinal));
        rewritten.Length.Should().Be(bytes.Length);

        File.WriteAllBytes(path, rewritten);
        File.SetLastWriteTimeUtc(path, written);
        new FileInfo(path).Length.Should().Be(bytes.Length);
        File.GetLastWriteTimeUtc(path).Should().Be(written);
    }

    [Fact]
    public void A_trigger_journal_day_is_parsed_once_and_then_only_where_it_grew()
    {
        var journal = new TriggerJournal(_space.Paths.Paths.State);
        var at = DateTimeOffset.UtcNow.AddMinutes(-5);

        // A long second entry puts the first outside the kilobyte the reader
        // checks before taking a longer file for an append.
        var padding = new string('p', 1200);

        journal.Append(new TriggerEntry(at, "event.seen", Reason: "first-aaaa"), new TriggerEntry(at, "event.seen", Reason: padding));
        journal.Read(at.AddMinutes(-1)).Select(one => one.Reason).Should().Equal("first-aaaa", padding);

        RewriteInPlace(journal.DayPath(at), "first-aaaa", "first-bbbb");
        journal.Read(at.AddMinutes(-1)).Select(one => one.Reason)
            .Should().Equal(["first-aaaa", padding], "the day looks unchanged, so it is not parsed again");

        journal.Append(new TriggerEntry(at.AddSeconds(1), "event.seen", Reason: "second"));

        new TriggerJournal(_space.Paths.Paths.State).Read(at.AddMinutes(-1)).Select(one => one.Reason)
            .Should().Equal(["first-aaaa", padding, "second"], "only what was appended is read, by any journal in the process");
    }

    [Fact]
    public void A_trigger_journal_still_filters_by_time_on_every_call()
    {
        var journal = new TriggerJournal(_space.Paths.Paths.State);
        var at = DateTimeOffset.UtcNow.AddMinutes(-10);

        journal.Append(new TriggerEntry(at, "event.seen", Reason: "early"), new TriggerEntry(at.AddMinutes(5), "event.seen", Reason: "late"));

        journal.Read(at.AddMinutes(-1)).Should().HaveCount(2);
        journal.Read(at.AddMinutes(1)).Select(one => one.Reason).Should().Equal("late");
    }

    private string RunJournalPath(RunJournal journal, string runId, params string[] lines)
    {
        var directory = journal.DirectoryOf(runId);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "journal.jsonl");
        File.WriteAllLines(path, lines);

        return path;
    }

    private static string Event(string kind, string at, string note = "") =>
        System.Text.Json.JsonSerializer.Serialize(new { at, kind, data = new { note } });

    [Fact]
    public void A_run_journal_is_parsed_once_and_then_only_where_it_grew()
    {
        var journal = new RunJournal(_space.Paths);
        var path = RunJournalPath(
            journal,
            "20261029-0101-aaaa",
            Event("run.started", "2026-10-29T01:01:00Z", "note-aaaa"),
            Event("node.note", "2026-10-29T01:02:00Z", new string('p', 1200)));

        journal.Read("20261029-0101-aaaa").Value![0].Data.GetProperty("note").GetString().Should().Be("note-aaaa");

        RewriteInPlace(path, "note-aaaa", "note-bbbb");
        File.AppendAllLines(path, [Event("run.reopened", "2026-10-29T02:00:00Z")]);

        var events = new RunJournal(_space.Paths).Read("20261029-0101-aaaa").Value!;

        events.Should().HaveCount(3);
        events[0].Data.GetProperty("note").GetString().Should().Be("note-aaaa", "the lines before the end were not parsed again");
        RunJournal.LastReopened(journal.DirectoryOf("20261029-0101-aaaa")).Should().Be(DateTimeOffset.Parse("2026-10-29T02:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void A_run_with_no_journal_is_still_named_as_missing()
    {
        var read = new RunJournal(_space.Paths).Read("20261029-0101-none");

        read.Succeeded.Should().BeFalse();
        read.Error.Should().Contain("No run named");
    }

    [Fact]
    public void A_journal_is_read_while_a_writer_holds_it_open()
    {
        var path = Path.Combine(_space.Scratch("held"), "journal.jsonl");
        File.WriteAllText(path, "one\n");

        // As a run's own appender holds it: writing, and letting others read.
        using var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);

        AppendOnlyFile<string>.For(path, line => line).Read().Should().Equal("one");
    }

    [Fact]
    public void The_reader_kept_for_a_file_is_the_same_one_each_time()
    {
        var path = Path.Combine(_space.Scratch("same"), "a.jsonl");

        AppendOnlyFile<string>.For(path, line => line).Should().BeSameAs(AppendOnlyFile<string>.For(path, line => line));
    }

    [Fact]
    public async Task A_running_session_entry_is_read_again_only_when_it_changes()
    {
        var registry = new SessionRegistry(_space.Paths, new NoOpFilePermissions(), _space.Processes, TimeProvider.System);
        await registry.RegisterAsync(new NewSession("launch-1", "alpha", "Name-aaaa", "claude", null, _space.Scratch("work")));

        var entry = Directory.GetFiles(registry.Path, "*.json").Single();
        File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddHours(-1));

        (await registry.ListAsync()).Single().ProjectName.Should().Be("Name-aaaa");

        RewriteInPlace(entry, "Name-aaaa", "Name-bbbb");

        (await registry.ListAsync()).Single().ProjectName.Should().Be("Name-aaaa", "the entry looks unchanged");

        File.SetLastWriteTimeUtc(entry, DateTime.UtcNow.AddHours(-2));

        (await registry.ListAsync()).Single().ProjectName.Should().Be("Name-bbbb", "a new time means a new version");
    }

    private string CodexRollout(string home, string sessionId)
    {
        var folder = Path.Combine(home, ".codex", "sessions", "2026", "10", "29");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, $"rollout-2026-10-29T10-00-00-{sessionId}.jsonl");
        File.WriteAllLines(path,
        [
            System.Text.Json.JsonSerializer.Serialize(new { type = "session_meta", payload = new { session_id = sessionId, cwd = "/work/aaaa" } }),
            """{"type":"event_msg","payload":{"type":"task_started"}}""",
        ]);

        return path;
    }

    private static void Name(string home, string sessionId, string name) =>
        File.AppendAllLines(Path.Combine(home, ".codex", "session_index.jsonl"), [$$"""{"id":"{{sessionId}}","thread_name":"{{name}}"}"""]);

    [Fact]
    public async Task A_codex_rollouts_first_line_is_read_once_while_its_name_and_time_stay_fresh()
    {
        var home = _space.Scratch("codex-home");
        var history = new CodexSessionHistory(new FakeEnvironmentProvider(home));
        var rollout = CodexRollout(home, "session-1");
        Name(home, "session-1", "First name");

        var first = (await history.ListAsync(10)).Value!.Single();
        first.Directory.Should().Be("/work/aaaa");
        first.Title.Should().Be("First name");

        RewriteInPlace(rollout, "/work/aaaa", "/work/bbbb");
        File.AppendAllLines(rollout, ["""{"type":"event_msg","payload":{"type":"turn"}}"""]);
        Name(home, "session-1", "Renamed");

        var second = (await new CodexSessionHistory(new FakeEnvironmentProvider(home)).ListAsync(10)).Value!.Single();

        second.Directory.Should().Be("/work/aaaa", "a rollout's first line does not change, so it is not read again");
        second.Title.Should().Be("Renamed", "a later name in the index wins");
        second.LastActive.Should().Be(new DateTimeOffset(File.GetLastWriteTimeUtc(rollout), TimeSpan.Zero));
    }

    [Fact]
    public async Task A_codex_rollout_found_shorter_is_taken_as_replaced_and_read_again()
    {
        var home = _space.Scratch("codex-replaced");
        var rollout = CodexRollout(home, "session-2");

        (await new CodexSessionHistory(new FakeEnvironmentProvider(home)).ListAsync(10)).Value!.Single().Directory.Should().Be("/work/aaaa");

        File.WriteAllLines(rollout, ["""{"type":"session_meta","payload":{"session_id":"session-2","cwd":"/w/b"}}"""]);

        (await new CodexSessionHistory(new FakeEnvironmentProvider(home)).ListAsync(10)).Value!.Single().Directory.Should().Be("/w/b");
    }
}
