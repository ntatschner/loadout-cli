using FluentAssertions;
using Loadout.Core.Instructions;
using Loadout.Models.Instructions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Where a fact is kept, according to who it is true for.
/// </summary>
/// <remarks>
/// <para>
/// Memory began with one scope, and a store written under it fills up with facts
/// that are not about the project. This project's own memory holds "Restart
/// Manager is disabled by policy", "spawned terminals inherit session markers"
/// and "driving consoles kills live sessions" — all filed under loadout-cli, and
/// not one of them about loadout-cli.
/// </para>
/// <para>
/// Two extra scopes rather than one global tier, and the difference is the whole
/// point: the workspace syncs, so a fact that is true here and false on the next
/// machine cannot live in it.
/// </para>
/// </remarks>
public sealed class MemoryScopeTests : IDisposable
{
    private const string Slug = "starstats";

    private readonly string _root;
    private readonly string _workspace;
    private readonly string _machine;
    private readonly MemoryService _memory;

    public MemoryScopeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-scope-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _machine = Path.Combine(_root, "state");

        Directory.CreateDirectory(Path.Combine(_workspace, "projects", Slug, "memory"));
        Directory.CreateDirectory(_machine);

        _memory = new MemoryService(TimeProvider.System, _machine);
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

    [Fact]
    public async Task What_is_true_of_this_machine_is_kept_outside_the_workspace()
    {
        // The whole reason this scope exists. The workspace is a Git repository
        // that syncs, and "the Restart Manager is disabled here" is false on the
        // next machine — so it must live somewhere the workspace cannot carry.
        var written = await WriteAsync(
            "restart-manager",
            "why installers fail with 1603 over a running app",
            MemoryScope.Machine);

        written.Succeeded.Should().BeTrue(written.Error);
        written.Value!.Path.Should().StartWith(_machine);
        written.Value.Path.Should().NotStartWith(_workspace);
    }

    [Fact]
    public async Task What_is_true_of_this_person_travels_with_the_workspace()
    {
        // As true on the next machine as on this one, so it should travel
        // exactly as project memory does — but not under any one project.
        var written = await WriteAsync(
            "review-habits",
            "how this person wants a review carried out",
            MemoryScope.User);

        written.Succeeded.Should().BeTrue(written.Error);
        written.Value!.Path.Should().StartWith(_workspace);
        written.Value.Path.Should().NotContain("projects");
    }

    [Fact]
    public async Task A_project_fact_still_goes_exactly_where_it_always_did()
    {
        var written = await WriteAsync(
            "upload-retries",
            "why the upload gives up after two attempts",
            MemoryScope.Project);

        written.Value!.Path.Should().Contain(Path.Combine("projects", Slug, "memory"));
    }

    [Fact]
    public async Task Listing_a_project_returns_every_scope_a_session_is_subject_to()
    {
        // A session working here is subject to all three. Listing one of them
        // would list a third of what it is actually given.
        await WriteAsync("upload-retries", "why the upload gives up", MemoryScope.Project);
        await WriteAsync("review-habits", "how this person wants reviews done", MemoryScope.User);
        await WriteAsync("restart-manager", "why installers fail over a running app", MemoryScope.Machine);

        var listed = await _memory.ListAsync(_workspace, Slug);

        listed.Value!.Select(topic => topic.Scope)
            .Should().BeEquivalentTo(
                [MemoryScope.Project, MemoryScope.User, MemoryScope.Machine]);
    }

    [Fact]
    public async Task A_topic_carries_the_scope_of_where_it_is_rather_than_what_it_says()
    {
        await WriteAsync("restart-manager", "why installers fail over a running app", MemoryScope.Machine);

        var listed = await _memory.ListAsync(_workspace, Slug);

        listed.Value!.Single().Scope.Should().Be(MemoryScope.Machine);
    }

    [Fact]
    public async Task The_index_a_session_sees_carries_all_three_and_labels_the_other_two()
    {
        // A session told "the Restart Manager is disabled" needs to know that is
        // a claim about the machine rather than about the code it is reading.
        await WriteAsync("upload-retries", "why the upload gives up", MemoryScope.Project);
        await WriteAsync("review-habits", "how this person wants reviews done", MemoryScope.User);
        await WriteAsync("restart-manager", "why installers fail over a running app", MemoryScope.Machine);

        var index = (await _memory.ReadIndexAsync(_workspace, Slug)).Value!;

        index.Should().Contain("upload-retries");
        index.Should().Contain("review-habits");
        index.Should().Contain("restart-manager");
        index.Should().Contain("true of this person's work");
        index.Should().Contain("true of this machine only");
    }

    [Fact]
    public async Task A_project_with_only_its_own_memory_reads_exactly_as_it_always_has()
    {
        // No headings appear until there is something else to distinguish it
        // from, so nothing changes for a project that never uses the new scopes.
        await WriteAsync("upload-retries", "why the upload gives up", MemoryScope.Project);

        var index = (await _memory.ReadIndexAsync(_workspace, Slug)).Value!;

        index.Should().StartWith("# Project memory index");
        index.Should().NotContain("##");
    }

    [Fact]
    public async Task Without_a_machine_store_a_machine_fact_is_refused_rather_than_written_elsewhere()
    {
        // Falling back to the workspace would sync the one thing this scope
        // exists to keep local, which is worse than not recording it.
        var memory = new MemoryService(TimeProvider.System, machineRoot: null);

        var written = await memory.WriteAsync(
            _workspace,
            Slug,
            "restart-manager",
            "why installers fail with 1603 over a running app",
            MemoryKind.Lesson,
            ["The Restart Manager is disabled by policy on this machine."],
            acknowledgedSimilar: true,
            MemoryScope.Machine);

        written.Failed.Should().BeTrue();
        Directory.Exists(Path.Combine(_workspace, "memory")).Should().BeFalse();
    }

    private Task<Loadout.Models.Results.OperationResult<MemoryTopic>> WriteAsync(
        string name,
        string description,
        MemoryScope scope) =>
        _memory.WriteAsync(
            _workspace,
            Slug,
            name,
            description,
            MemoryKind.Lesson,
            [$"Something durable worth writing down about {name}."],
            acknowledgedSimilar: true,
            scope);
}
