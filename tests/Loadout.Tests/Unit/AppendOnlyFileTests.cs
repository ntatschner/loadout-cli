using System.Text;
using FluentAssertions;
using Loadout.Core.Reading;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The shared reader of an append-only file: it must give the same answer as
/// reading the whole file every time, while reading only what was appended.
/// </summary>
/// <remarks>
/// Each change sets the file's last-write time by hand, a minute on from the
/// last, so a file system with coarse times cannot make two versions look
/// alike and pass or fail a test for that reason alone.
/// </remarks>
public sealed class AppendOnlyFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-append-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly List<string> _parsed = [];
    private readonly AppendOnlyFile<string> _file;
    private DateTime _written = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    public AppendOnlyFileTests()
    {
        Directory.CreateDirectory(_root);

        _path = Path.Combine(_root, "ledger.jsonl");
        _file = new AppendOnlyFile<string>(_path, line =>
        {
            _parsed.Add(line);
            return line.StartsWith('{') ? line : null;
        });
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    [Fact]
    public async Task No_file_reads_as_null()
    {
        (await _file.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task An_unchanged_file_is_not_parsed_again()
    {
        Write("{a}\n{b}\n");

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}");
        _parsed.Clear();

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}");
        _parsed.Should().BeEmpty();
    }

    [Fact]
    public async Task An_append_is_read_from_where_the_last_reading_stopped()
    {
        Write("{a}\n{b}\n");
        await _file.ReadAsync();
        _parsed.Clear();

        Append("{c}\n");

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}", "{c}");
        _parsed.Should().Equal("{c}");
    }

    [Fact]
    public async Task A_truncated_file_is_read_whole()
    {
        Write("{a}\n{b}\n{c}\n");
        await _file.ReadAsync();

        Write("{x}\n");

        (await _file.ReadAsync()).Should().Equal("{x}");
    }

    [Fact]
    public async Task A_file_rewritten_longer_is_read_whole_rather_than_taken_for_an_append()
    {
        Write("{a}\n{b}\n");
        await _file.ReadAsync();

        Write("{x}\n{y}\n{z}\n");

        (await _file.ReadAsync()).Should().Equal("{x}", "{y}", "{z}");
    }

    [Fact]
    public async Task A_file_rewritten_to_the_same_length_is_read_again()
    {
        Write("{a}\n{b}\n");
        await _file.ReadAsync();

        Write("{x}\n{y}\n");

        (await _file.ReadAsync()).Should().Equal("{x}", "{y}");
    }

    [Fact]
    public async Task An_unfinished_last_line_is_returned_but_not_kept()
    {
        Write("{a}\n{b");

        // Not JSON-shaped yet in a real ledger; here the parser takes it, as
        // a hand-written file with no final newline would be taken.
        (await _file.ReadAsync()).Should().Equal("{a}", "{b");
        _parsed.Clear();

        Append("}\n{c}\n");

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}", "{c}");

        // Finishing the line is an append like any other, not a reason to
        // read the whole file again.
        _parsed.Should().Equal("{b}", "{c}");
    }

    [Fact]
    public async Task A_line_appended_in_two_writes_is_counted_once()
    {
        Write("{a}\n");
        await _file.ReadAsync();

        Append("{b");
        await _file.ReadAsync();

        Append("}\n");

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}");
    }

    [Fact]
    public async Task A_file_that_goes_away_reads_as_null()
    {
        Write("{a}\n");
        await _file.ReadAsync();

        File.Delete(_path);

        (await _file.ReadAsync()).Should().BeNull();
    }

    [Fact]
    public async Task A_byte_order_mark_and_carriage_returns_are_not_part_of_a_line()
    {
        File.WriteAllBytes(_path, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("{a}\r\n\r\n{b}\r\n")]);
        Touch();

        (await _file.ReadAsync()).Should().Equal("{a}", "{b}");
    }

    [Fact]
    public async Task Appends_past_the_guard_are_still_read_as_appends()
    {
        var lines = Enumerable.Range(0, 200).Select(i => $"{{line {i:D4}}}").ToList();

        Write(string.Join('\n', lines) + "\n");
        await _file.ReadAsync();
        _parsed.Clear();

        Append("{last}\n");

        (await _file.ReadAsync()).Should().Equal([.. lines, "{last}"]);
        _parsed.Should().Equal("{last}");
    }

    private void Write(string text)
    {
        File.WriteAllText(_path, text);
        Touch();
    }

    private void Append(string text)
    {
        File.AppendAllText(_path, text);
        Touch();
    }

    private void Touch()
    {
        _written = _written.AddMinutes(1);
        File.SetLastWriteTimeUtc(_path, _written);
    }
}
