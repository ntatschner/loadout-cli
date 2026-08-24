using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Loadout.Models.Results;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Covers moving durable facts out of always-loaded instructions into memory.
/// <para>
/// This rewrites a file somebody wrote by hand, which puts the burden of proof
/// on it. The tests that matter most are not the ones showing it moves things:
/// they are the ones showing it moves nothing it should not, changes no wording,
/// and removes nothing it has not first confirmed is safely stored elsewhere.
/// </para>
/// </summary>
public sealed class MemoryCompressorTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly MemoryService _memory;
    private readonly MemoryCompressor _compressor;

    private const string Slug = "alpha";

    public MemoryCompressorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-compress-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");

        Directory.CreateDirectory(Path.Combine(_workspace, "projects", Slug, "memory"));

        _memory = new MemoryService(TimeProvider.System);
        _compressor = new MemoryCompressor(_memory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temp tree is not worth failing the run over.
        }
    }

    /// <summary>Writes an instruction file and returns its path.</summary>
    private string WriteInstructions(params string[] lines)
    {
        var path = Path.Combine(_root, "instructions.md");

        File.WriteAllLines(path, lines);

        return path;
    }

    /// <summary>A statement long enough and assertive enough to count as durable.</summary>
    private static string Durable(string subject) =>
        $"The {subject} store is append-only and must never be rewritten in place, "
        + "because replaying history is how the projection is rebuilt.";

    [Fact]
    public async Task Durable_list_items_are_gathered_under_their_heading()
    {
        var path = WriteInstructions(
            "# Notes",
            "",
            "## Architecture",
            "",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var plan = await _compressor.PlanAsync(path);

        plan.Succeeded.Should().BeTrue(plan.Error ?? string.Empty);
        plan.Value!.Topics.Should().ContainSingle();
        plan.Value.Topics[0].Name.Should().Be("architecture");
        plan.Value.Facts.Should().Be(2);
    }

    [Fact]
    public async Task A_preview_writes_nothing_and_changes_nothing()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var before = await File.ReadAllTextAsync(path);

        var plan = await _compressor.PlanAsync(path);

        plan.Value!.Applied.Should().BeFalse();

        // Asserted against the filesystem rather than against what the result
        // claims about itself.
        (await File.ReadAllTextAsync(path)).Should().Be(before);
        Directory.EnumerateFiles(Path.Combine(_workspace, "projects", Slug, "memory"))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Applying_writes_the_topic_and_shortens_the_source()
    {
        var path = WriteInstructions(
            "# Notes",
            "",
            "Some prose that stays.",
            "",
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var plan = await _compressor.ApplyAsync(path, _workspace, Slug);

        plan.Succeeded.Should().BeTrue(plan.Error ?? string.Empty);
        plan.Value!.Applied.Should().BeTrue();

        var topics = await _memory.ListAsync(_workspace, Slug);

        topics.Value.Should().ContainSingle();
        topics.Value![0].Facts.Should().HaveCount(2);

        var remaining = await File.ReadAllTextAsync(path);

        remaining.Should().Contain("Some prose that stays.");
        remaining.Should().NotContain("append-only");
        plan.Value.BytesAfter.Should().BeLessThan(plan.Value.BytesBefore);
    }

    [Fact]
    public async Task Facts_are_moved_verbatim_and_never_reworded()
    {
        var fact = Durable("event");

        var path = WriteInstructions("## Architecture", "- " + fact, "- " + Durable("audit"));

        await _compressor.ApplyAsync(path, _workspace, Slug);

        var topics = await _memory.ListAsync(_workspace, Slug);

        // The whole claim of an automatic rewrite is that the result says
        // exactly what the source said.
        topics.Value![0].Facts.Should().Contain(fact);
    }

    [Fact]
    public async Task Prose_is_never_lifted_out_of_a_paragraph()
    {
        var path = WriteInstructions(
            "## Architecture",
            Durable("event"),
            Durable("audit"));

        var plan = await _compressor.PlanAsync(path);

        // Both lines would classify as durable, but pulling sentences out of
        // prose is how a readable document becomes a confusing one.
        plan.Value!.Topics.Should().BeEmpty();
        plan.Value.Considered.Should().Be(0);
    }

    [Fact]
    public async Task An_indented_item_stays_with_the_item_it_qualifies()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "  - " + Durable("nested qualification of the above"),
            "- " + Durable("audit"));

        var plan = await _compressor.PlanAsync(path);

        // Lifting a sub-item on its own strands the qualification it carried.
        plan.Value!.Facts.Should().Be(2);

        var remaining = await File.ReadAllTextAsync(path);

        remaining.Should().Contain("nested qualification");
    }

    [Fact]
    public async Task A_heading_yielding_too_few_facts_is_left_alone()
    {
        var path = WriteInstructions("## Architecture", "- " + Durable("event"));

        var plan = await _compressor.PlanAsync(path);

        // One fact costs an index line to save a bullet, which is not a saving.
        plan.Value!.Topics.Should().BeEmpty();

        (await File.ReadAllTextAsync(path)).Should().Contain("append-only");
    }

    [Fact]
    public async Task Content_that_will_rot_is_left_where_it_is()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"),
            "- Fixed the importer and bumped the version to 2.1 in this change.",
            "- ok");

        var plan = await _compressor.PlanAsync(path);

        // Memory that accumulates unfiltered costs a session to read and
        // misleads it, which is worse than having none.
        plan.Value!.Facts.Should().Be(2);
        plan.Value.Rejected.Values.Sum().Should().Be(2);
    }

    [Fact]
    public async Task A_heading_left_with_nothing_under_it_is_dropped()
    {
        var path = WriteInstructions(
            "# Notes",
            "",
            "## Architecture",
            "",
            "- " + Durable("event"),
            "- " + Durable("audit"),
            "",
            "## Still here",
            "",
            "Prose.");

        await _compressor.ApplyAsync(path, _workspace, Slug);

        var remaining = await File.ReadAllTextAsync(path);

        // A heading whose whole body moved is a promise the document no longer
        // keeps.
        remaining.Should().NotContain("## Architecture");
        remaining.Should().Contain("## Still here");
        remaining.Should().Contain("Prose.");
    }

    [Fact]
    public async Task A_heading_full_of_paths_still_yields_a_readable_name()
    {
        var path = WriteInstructions(
            "## Component modularization & first-run setup (`crates/core/src/modules`, "
            + "`server/src/{modules,module_gate,sweeps,modules_panel,setup}.rs`)",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var plan = await _compressor.PlanAsync(path);

        var name = plan.Value!.Topics[0].Name;

        // The name becomes a filename and an index entry. Taken from a real
        // instruction file, where headings carry the source paths they concern.
        name.Should().Be("component-modularization-first-run-setup");
        name.Length.Should().BeLessThanOrEqualTo(48);
    }

    [Fact]
    public async Task Two_headings_that_shorten_alike_do_not_overwrite_each_other()
    {
        var path = WriteInstructions(
            "## Deploy (`server/src/a.rs`)",
            "- " + Durable("event"),
            "- " + Durable("audit"),
            "## Deploy (`server/src/b.rs`)",
            "- " + Durable("third"),
            "- " + Durable("fourth"));

        var plan = await _compressor.PlanAsync(path);

        plan.Value!.Topics.Should().HaveCount(2);
        plan.Value.Topics.Select(t => t.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task The_kind_is_read_from_the_heading()
    {
        var path = WriteInstructions(
            "## Lessons learned the hard way",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var plan = await _compressor.PlanAsync(path);

        plan.Value!.Topics[0].Kind.Should().Be(MemoryKind.Lesson);
    }

    [Fact]
    public async Task Nothing_is_removed_when_the_memory_store_refuses_the_write()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var before = await File.ReadAllTextAsync(path);

        var compressor = new MemoryCompressor(new RefusingMemoryService());

        var result = await compressor.ApplyAsync(path, _workspace, Slug);

        // The rule that makes an automatic rewrite safe: the source keeps its
        // copy until the store has confirmed it holds one.
        result.Failed.Should().BeTrue();
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task Nothing_is_removed_when_the_store_loses_a_fact()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"));

        var before = await File.ReadAllTextAsync(path);

        var compressor = new MemoryCompressor(new ForgetfulMemoryService());

        var result = await compressor.ApplyAsync(path, _workspace, Slug);

        // A write that reports success but stores less than it was given is
        // exactly the case the read-back exists to catch.
        result.Failed.Should().BeTrue();
        result.Error.Should().Contain("read back");
        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task A_credential_shaped_line_is_withheld_and_the_rest_still_move()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"),
            "- Connect to the reporting replica at https://admin:hunter2@db.example.invalid/reports "
            + "rather than the primary, because the primary is write-only in production.");

        var plan = await _compressor.ApplyAsync(path, _workspace, Slug);

        plan.Succeeded.Should().BeTrue(plan.Error ?? string.Empty);

        // The memory store refuses a whole topic on one bad line, which is
        // right for a direct write and wrong here: one credential in a large
        // file would otherwise block every good fact in it.
        plan.Value!.Facts.Should().Be(2);
        plan.Value.Withheld.Values.Sum().Should().Be(1);

        // Named by pattern, never by content.
        plan.Value.Withheld.Keys.Should().Contain("credentials in a URL");
        plan.Value.Withheld.Keys.Should().NotContain(k => k.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_withheld_line_stays_exactly_where_it_already_was()
    {
        var path = WriteInstructions(
            "## Architecture",
            "- " + Durable("event"),
            "- " + Durable("audit"),
            "- Connect to the reporting replica at https://admin:hunter2@db.example.invalid/reports "
            + "rather than the primary, because the primary is write-only in production.");

        await _compressor.ApplyAsync(path, _workspace, Slug);

        var remaining = await File.ReadAllTextAsync(path);

        // Leaving it put discloses it no further than it already was. Moving it
        // would copy it into a repository that is pushed to a remote.
        remaining.Should().Contain("db.example.invalid");

        var topics = await _memory.ListAsync(_workspace, Slug);

        topics.Value!.SelectMany(t => t.Facts)
            .Should().NotContain(f => f.Contains("db.example.invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_instruction_file_is_reported_rather_than_thrown()
    {
        var result = await _compressor.PlanAsync(Path.Combine(_root, "absent.md"));

        result.Failed.Should().BeTrue();
        result.ExitCode.Should().Be(Loadout.Models.ExitCode.ProjectNotFound);
    }
}

/// <summary>A store that refuses every write, standing in for a full disk or a locked file.</summary>
internal sealed class RefusingMemoryService : StubMemoryService
{
    public override Task<OperationResult<MemoryTopic>> WriteAsync(
        string workspaceRoot,
        string slug,
        string name,
        string description,
        MemoryKind kind,
        IReadOnlyList<string> facts,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<MemoryTopic>.Fail("the disk is full"));
}

/// <summary>A store that reports success while quietly storing less than it was given.</summary>
internal sealed class ForgetfulMemoryService : StubMemoryService
{
    public override Task<OperationResult<MemoryTopic>> WriteAsync(
        string workspaceRoot,
        string slug,
        string name,
        string description,
        MemoryKind kind,
        IReadOnlyList<string> facts,
        CancellationToken ct = default) =>
        Task.FromResult(OperationResult<MemoryTopic>.Ok(new MemoryTopic(
            name, "path", description, kind, [], [], 0, DateTimeOffset.UtcNow)));
}

/// <summary>
/// The parts of the memory store the compressor does not use.
/// <para>
/// Only <see cref="WriteAsync"/> is reached by these tests; everything else
/// throws rather than returning a plausible empty answer, so a future change
/// that starts calling one of them fails loudly instead of silently passing.
/// </para>
/// </summary>
internal abstract class StubMemoryService : IMemoryService
{
    public virtual Task<OperationResult<MemoryTopic>> WriteAsync(
        string workspaceRoot,
        string slug,
        string name,
        string description,
        MemoryKind kind,
        IReadOnlyList<string> facts,
        CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<IReadOnlyList<MemoryTopic>>> ListAsync(
        string workspaceRoot, string slug, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<MemoryAudit>> AuditAsync(
        string workspaceRoot, string slug, int staleMonths = 6, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult> RebuildIndexAsync(
        string workspaceRoot, string slug, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<OperationResult<MemoryCleanup>> CleanAsync(
        string workspaceRoot, string slug, bool apply, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public IReadOnlyList<string> CleanupPaths(string workspaceRoot, string slug) =>
        throw new NotSupportedException();

    public Task<OperationResult<string?>> ReadIndexAsync(
        string workspaceRoot, string slug, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
