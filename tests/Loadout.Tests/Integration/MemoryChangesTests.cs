using FluentAssertions;
using Loadout.Core.Backups;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// The memory changes that are hard to undo, each taking a backup first, and
/// the backup actually undoing them.
/// </summary>
public sealed class MemoryChangesTests : IDisposable
{
    private const string Slug = "alpha";

    private const string Fact = "The read model is rebuilt from the journal on every deploy, never migrated.";

    private readonly TemporaryWorkspace _space = new();
    private readonly MemoryService _memory = new(TimeProvider.System);
    private readonly BackupService _backups;
    private readonly MemoryChanges _changes;

    public MemoryChangesTests()
    {
        _backups = new BackupService(_space.Paths, new NoOpFilePermissions(), _space.Yaml, TimeProvider.System);
        _changes = new MemoryChanges(
            _memory,
            new MemoryImporter(new FakeEnvironmentProvider(_space.Scratch("home")), _memory),
            new MemoryCompressor(_memory),
            _backups);

        Directory.CreateDirectory(Path.Combine(Root, "projects", Slug, "memory"));
    }

    private string Root => _space.Workspace.LocalPath;

    public void Dispose() => _space.Dispose();

    private async Task WriteAsync(string name, params string[] facts)
    {
        var written = await _memory.WriteAsync(
            Root, Slug, name, $"why the {name} behave the way they do after a deploy", MemoryKind.Project, facts, acknowledgedSimilar: true);

        written.Succeeded.Should().BeTrue(written.Error);
    }

    [Fact]
    public async Task Deleting_a_topic_names_what_links_to_it_and_leaves_those_alone()
    {
        await WriteAsync("deploys", Fact);
        await WriteAsync("journal", "Each event is written once and never edited after it is read back.");

        var linker = Path.Combine(Root, "projects", Slug, "memory", "journal.md");
        File.AppendAllLines(linker, ["", "Related: [[deploys]]"]);

        var preview = await _changes.PreviewDeleteAsync(Root, Slug, "deploys");

        preview.Value!.LinkedFrom.Should().Equal("journal");

        var deleted = await _changes.DeleteAsync(Root, Slug, "deploys");

        deleted.Succeeded.Should().BeTrue(deleted.Error);
        (await _memory.ListAsync(Root, Slug)).Value!.Select(t => t.Name).Should().Equal("journal");
        File.ReadAllText(linker).Should().Contain("[[deploys]]", "a dangling link marks something worth writing later");
        File.ReadAllText(Path.Combine(Root, "projects", Slug, "memory", "MEMORY.md")).Should().NotContain("deploys.md");
    }

    [Fact]
    public async Task A_deleted_topic_comes_back_from_its_backup()
    {
        await WriteAsync("deploys", Fact);

        var deleted = await _changes.DeleteAsync(Root, Slug, "deploys");

        var restored = await _backups.RestoreAsync(deleted.Value!.Backup!.Id, apply: true);

        restored.Succeeded.Should().BeTrue(restored.Error);
        (await _memory.ListAsync(Root, Slug)).Value!.Single().Facts.Should().Equal(Fact);
    }

    [Fact]
    public async Task Deleting_a_topic_that_is_not_there_says_so_and_takes_no_backup()
    {
        var deleted = await _changes.DeleteAsync(Root, Slug, "nothing-here");

        deleted.Failed.Should().BeTrue();
        deleted.Error.Should().Contain("no topic 'nothing-here'");
        (await _backups.ListAsync()).Value!.Should().BeEmpty();
    }

    [Fact]
    public async Task Compress_backs_up_the_instruction_file_and_the_topic_it_merges_into()
    {
        await WriteAsync("architecture", Fact);

        var source = Path.Combine(_space.Scratch("instructions"), "instructions.md");
        var original = string.Join(Environment.NewLine,
        [
            "## Architecture",
            "",
            "- The event store is append-only and must never be rewritten in place, because replaying history is how the projection is rebuilt.",
            "- The audit store is append-only and must never be rewritten in place, because replaying history is how the projection is rebuilt.",
        ]);
        File.WriteAllText(source, original);

        var compressed = await _changes.CompressAsync(source, Root, Slug);

        compressed.Succeeded.Should().BeTrue(compressed.Error);
        compressed.Value!.Value.Topics.Single().Merges.Should().BeTrue();
        File.ReadAllText(source).Should().NotContain("event store");

        // The restore puts back both halves: the file the facts came from, and
        // the topic as it was before they arrived.
        (await _backups.RestoreAsync(compressed.Value.Backup!.Id, apply: true)).Succeeded.Should().BeTrue();

        File.ReadAllText(source).Should().Be(original);
        (await _memory.ListAsync(Root, Slug)).Value!.Single().Facts.Should().Equal(Fact);
    }

    [Fact]
    public async Task Compress_with_nothing_to_move_takes_no_backup()
    {
        var source = Path.Combine(_space.Scratch("instructions"), "instructions.md");
        File.WriteAllText(source, "Just prose, with no list in it.");

        var compressed = await _changes.CompressAsync(source, Root, Slug);

        compressed.Succeeded.Should().BeTrue(compressed.Error);
        compressed.Value!.Backup.Should().BeNull();
    }
}
