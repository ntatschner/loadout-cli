using FluentAssertions;
using Loadout.Core.Configuration;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What one <see cref="KeptFiles{T}"/> read, another of the same kind in the
/// same process can answer from, because the daemon builds a fresh one for
/// every request.
/// </summary>
/// <remarks>
/// Whether the file was read is shown by changing what is in it while putting
/// its length and last-write time back: a reader that went to the file sees
/// the new text, one that answered from what was kept sees the old.
/// </remarks>
public sealed class KeptFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "loadout-kept-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;
    private readonly YamlStore _yaml = new(new NoOpFilePermissions());
    private readonly DateTime _written = DateTime.UtcNow.AddHours(-1);

    public KeptFilesTests()
    {
        Directory.CreateDirectory(_root);
        _path = Path.Combine(_root, "note.yaml");
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
    public async Task A_second_instance_answers_from_what_the_first_read()
    {
        Write("text: alpha\n");
        (await Kept(shared: true).ReadAsync(_path, default)).Value!.Value.Text.Should().Be("alpha");

        Write("text: omega\n");

        (await Kept(shared: true).ReadAsync(_path, default)).Value!.Value.Text
            .Should().Be("alpha", "the file looks unchanged, and what the first instance read is the process's");
    }

    [Fact]
    public async Task With_sharing_off_each_instance_reads_for_itself()
    {
        Write("text: alpha\n");
        await Kept(shared: false).ReadAsync(_path, default);

        Write("text: omega\n");

        (await Kept(shared: false).ReadAsync(_path, default)).Value!.Value.Text.Should().Be("omega");
    }

    private KeptFiles<Note> Kept(bool shared) =>
        new(_yaml, () => new Note(), one => new Note { Text = one.Text }, shared);

    private void Write(string text)
    {
        File.WriteAllText(_path, text);
        File.SetLastWriteTimeUtc(_path, _written);
    }

    /// <summary>A kind of its own, so what these tests keep is never shared with the product's.</summary>
    public sealed class Note
    {
        public string Text { get; set; } = string.Empty;
    }
}
