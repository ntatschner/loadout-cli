using FluentAssertions;
using Loadout.Core.Reading;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// The shared reader of a file that is rewritten whole: the same answer as
/// reading it every time, without opening it while it has not changed.
/// </summary>
/// <remarks>
/// Whether the file was opened is shown by changing what is in it while
/// putting its length and last-write time back as they were: a reader that
/// went to the file returns the new text, one that did not returns the old.
/// </remarks>
public sealed class WholeFileTests : IDisposable
{
    // An hour ago, well settled, whatever the date the tests run on.
    private static readonly DateTime First = DateTime.UtcNow.AddHours(-1);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-whole-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public WholeFileTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "topic.md");
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
    public async Task An_unchanged_file_is_not_opened_again()
    {
        var file = new WholeFile(_path);
        Write("alpha", First);
        (await file.ReadTextAsync()).Should().Be("alpha");

        Write("omega", First);

        (await file.ReadTextAsync()).Should().Be("alpha", "nothing a stat can see changed, so the kept text is the answer");
    }

    [Fact]
    public async Task A_new_write_time_is_read_again()
    {
        var file = new WholeFile(_path);
        Write("alpha", First);
        await file.ReadTextAsync();

        Write("omega", First.AddMinutes(1));

        (await file.ReadTextAsync()).Should().Be("omega");
    }

    [Fact]
    public async Task A_new_length_is_read_again()
    {
        var file = new WholeFile(_path);
        Write("alpha", First);
        await file.ReadTextAsync();

        Write("alpha and more", First);

        (await file.ReadTextAsync()).Should().Be("alpha and more");
    }

    [Fact]
    public async Task A_missing_file_throws_as_reading_it_would()
    {
        var file = new WholeFile(_path);

        await file.Invoking(one => one.ReadTextAsync()).Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task A_file_deleted_after_it_was_read_throws_and_one_put_back_is_read()
    {
        var file = new WholeFile(_path);
        Write("alpha", First);
        await file.ReadTextAsync();

        File.Delete(_path);
        await file.Invoking(one => one.ReadTextAsync()).Should().ThrowAsync<FileNotFoundException>();

        Write("omega", First);
        (await file.ReadTextAsync()).Should().Be("omega", "the reading went when the file did");
    }

    [Fact]
    public async Task A_file_over_a_megabyte_is_read_every_time()
    {
        var file = new WholeFile(_path);
        var large = new string('a', 1024 * 1024 + 1);
        Write(large, First);
        await file.ReadTextAsync();

        var changed = new string('b', large.Length);
        Write(changed, First);

        (await file.ReadTextAsync()).Should().Be(changed);
    }

    [Fact]
    public async Task A_file_written_moments_ago_is_read_every_time()
    {
        // As a task going from open to done inside one tick of the clock: the
        // same length, and a time that need not have moved.
        var file = new WholeFile(_path);
        var now = DateTime.UtcNow;
        Write("state: open", now);
        await file.ReadTextAsync();

        Write("state: done", now);

        (await file.ReadTextAsync()).Should().Be("state: done");
    }

    [Fact]
    public void One_path_has_one_reader_however_it_is_written()
    {
        var spelled = Path.Combine(_root, ".", "topic.md");

        WholeFile.For(spelled).Should().BeSameAs(WholeFile.For(_path));
    }

    private void Write(string text, DateTime written)
    {
        File.WriteAllText(_path, text);
        File.SetLastWriteTimeUtc(_path, written);
    }
}
