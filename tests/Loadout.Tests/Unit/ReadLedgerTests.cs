using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Diagnostics;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The read ledger tells a second read of the same contents from a read of
/// new ones, which is the whole of what it is for.
/// </summary>
public sealed class ReadLedgerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-read-ledger-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _state;
    private readonly string _journal;
    private readonly ReadLedger.Ledger _ledger;

    public ReadLedgerTests()
    {
        _state = Path.Combine(_root, "state");
        _journal = Path.Combine(_state, "teams", "runs", "20261028-1737-967d", "journal.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(_journal)!);
        File.WriteAllText(_journal, "{\"kind\":\"run.started\"}\n");
        File.SetLastWriteTimeUtc(_journal, new DateTime(2026, 10, 28, 9, 0, 0, DateTimeKind.Utc));
        _ledger = new ReadLedger.Ledger(_state, _root, "team daemon", 4242, new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Reading_unchanged_contents_twice_is_two_reads_of_one_version()
    {
        Read(_journal, "RunJournal.Read");
        Read(_journal, "TeamDaemonCommand.Notice");

        var source = _ledger.Summarise().Sources.Should().ContainSingle().Subject;

        source.Reads.Should().Be(2);
        source.Versions.Should().Be(1);
        source.ReadsPerChange.Should().Be(2, "the second read saw nothing the first had not");
        source.Callers.Select(one => one.Caller).Should().BeEquivalentTo("RunJournal.Read", "TeamDaemonCommand.Notice");
    }

    [Fact]
    public void A_read_after_each_change_is_one_read_per_change()
    {
        Read(_journal, "RunJournal.Read");
        File.AppendAllText(_journal, "{\"kind\":\"node.launched\"}\n");
        Read(_journal, "RunJournal.Read");

        var source = _ledger.Summarise().Sources.Should().ContainSingle().Subject;

        source.Versions.Should().Be(2);
        source.ReadsPerChange.Should().Be(1);
    }

    [Fact]
    public void A_rewrite_to_the_same_length_is_a_change()
    {
        Read(_journal, "RunJournal.Read");
        File.SetLastWriteTimeUtc(_journal, new DateTime(2026, 10, 28, 9, 0, 5, DateTimeKind.Utc));
        Read(_journal, "RunJournal.Read");

        _ledger.Summarise().Sources.Single().Versions.Should().Be(2, "the length alone would have missed it");
    }

    [Fact]
    public void A_file_that_is_not_there_is_not_counted()
    {
        Read(Path.Combine(_state, "events", "marks.json"), "EventStore.Marks");

        _ledger.Summarise().Sources.Should().BeEmpty();
    }

    [Fact]
    public void A_site_reading_a_tail_counts_the_bytes_it_read()
    {
        using (var trace = ReadLedger.BeginInto(_ledger, _journal, "BackgroundWork.ReadTail"))
        {
            trace.Read(7);
        }

        _ledger.Summarise().Sources.Single().Bytes.Should().Be(7);
    }

    [Fact]
    public void A_whole_read_counts_the_file_length()
    {
        Read(_journal, "RunJournal.Read");

        _ledger.Summarise().Sources.Single().Bytes.Should().Be(new FileInfo(_journal).Length);
    }

    [Fact]
    public void Journals_of_different_runs_are_one_source()
    {
        var other = Path.Combine(_state, "teams", "runs", "20261027-1059-75ed", "journal.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "{}\n");

        Read(_journal, "RunJournal.Read");
        Read(other, "RunJournal.Read");

        var summary = _ledger.Summarise();

        summary.Files.Should().HaveCount(2);
        var source = summary.Sources.Should().ContainSingle().Subject;
        source.Pattern.Should().Be("<State>/teams/runs/*/journal.jsonl");
        source.FileCount.Should().Be(2);
        source.Versions.Should().Be(2);
    }

    [Theory]
    [InlineData("<State>/teams/runs/20261028-1737-967d/journal.jsonl", "<State>/teams/runs/*/journal.jsonl")]
    [InlineData("~/.claude/projects/D--git-ailauncher/0f3c9a12-77aa-4e5b-9d1c-1b2c3d4e5f60.jsonl", "~/.claude/projects/D--git-ailauncher/*.jsonl")]
    [InlineData("<State>/sessions/registry.json", "<State>/sessions/registry.json")]
    [InlineData("~/memory/topic-2.md", "~/memory/*.md")]
    public void A_pattern_hides_each_segment_holding_a_digit(string shown, string pattern) =>
        ReadLedger.Pattern(shown).Should().Be(pattern);

    [Fact]
    public void Projects_named_without_digits_are_folded_into_one_source()
    {
        foreach (var slug in new[] { "courtreview", "keyjutsu" })
        {
            var tasks = Path.Combine(_state, "workspace", "projects", slug, "tasks.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(tasks)!);
            File.WriteAllText(tasks, "tasks: []\n");
            Read(tasks, "YamlStore.ReadAsync");
        }

        var source = _ledger.Summarise().Sources.Should().ContainSingle().Subject;

        source.Pattern.Should().Be("<State>/workspace/projects/*/tasks.yaml");
        source.FileCount.Should().Be(2);
    }

    [Fact]
    public void Two_files_in_one_directory_are_never_folded() =>
        ReadLedger.Fold(["<State>/events/marks.json", "<State>/events/recorder.json"])
            .Values.Should().BeEquivalentTo("<State>/events/marks.json", "<State>/events/recorder.json");

    [Fact]
    public void A_file_with_no_sibling_keeps_its_pattern() =>
        ReadLedger.Fold(["<State>/machines.yaml", "<State>/teams/runs/*/journal.jsonl"])
            .Values.Should().BeEquivalentTo("<State>/machines.yaml", "<State>/teams/runs/*/journal.jsonl");

    [Theory]
    [InlineData("D:\\git\\ailauncher\\src\\Loadout.Core\\Teams\\RunJournal.cs", "Read", "RunJournal.Read")]
    [InlineData("/home/runner/work/src/Loadout.Core/Events/EventStore.cs", "Marks", "EventStore.Marks")]
    [InlineData("", "Read", "Read")]
    public void A_caller_is_named_by_its_file_and_member(string site, string member, string caller) =>
        ReadLedger.Caller(site, member).Should().Be(caller);

    [Fact]
    public void The_summary_is_written_under_the_state_directory_and_reads_back()
    {
        Read(_journal, "RunJournal.Read");
        Read(_journal, "RunJournal.Read");

        _ledger.Write();

        var written = _ledger.Destination;
        written.Should().StartWith(ReadLedger.Directory(Path.GetFullPath(_state)));
        Path.GetFileName(written).Should().Be("20261028-090000-4242-team-daemon.json");

        using var document = JsonDocument.Parse(File.ReadAllText(written));
        var source = document.RootElement.GetProperty("sources")[0];
        source.GetProperty("pattern").GetString().Should().Be("<State>/teams/runs/*/journal.jsonl");
        source.GetProperty("readsPerChange").GetDouble().Should().Be(2);
    }

    [Fact]
    public void The_summary_says_which_build_made_it()
    {
        _ledger.Write();

        using var document = JsonDocument.Parse(File.ReadAllText(_ledger.Destination));
        document.RootElement.GetProperty("version").GetString()
            .Should().Be(typeof(ReadLedger).Assembly.GetName().Version!.ToString(3));
    }

    [Theory]
    [InlineData(new[] { "team", "daemon", "--log", @"C:\state\teams\daemon.log" }, "team-daemon")]
    [InlineData(new[] { "daemon", "--log", @"C:\state\teams\daemon.log" }, "daemon")]
    [InlineData(new[] { "statusline", "30" }, "statusline-30")]
    [InlineData(new[] { "--json", "task", "list" }, "task-list")]
    [InlineData(new[] { "--json" }, "launcher")]
    [InlineData(new[] { "task", "list", "extra" }, "task-list")]
    [InlineData(new string[0], "launcher")]
    public void A_process_is_named_by_the_words_before_its_first_option(string[] args, string label) =>
        ReadLedger.Label(args).Should().Be(label);

    [Fact]
    public void Without_being_asked_the_process_counts_nothing()
    {
        ReadLedger.StartIfAsked("0", _state, "status");
        ReadLedger.StartIfAsked(null, _state, "status");

        ReadLedger.IsOn.Should().BeFalse();
        ReadLedger.Begin(_journal).Should().BeNull();
    }

    private void Read(string path, string caller)
    {
        using var trace = ReadLedger.BeginInto(_ledger, path, caller);

        if (File.Exists(path))
        {
            _ = File.ReadAllText(path);
        }
    }
}
