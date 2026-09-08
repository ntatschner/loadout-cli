using Loadout.Core.Context;
using Loadout.Core.Instructions;
using Loadout.Core.Tasks;
using Loadout.Models.Projects;
using Loadout.Models.Results;
using Loadout.Models.Tasks;
using Loadout.Tests.Fakes;
using FluentAssertions;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Whether what a project is working on reaches the session working on it.
/// <para>
/// The tools to read and write the task record were served to the agent from
/// the day they existed, and nothing ever told it they were there: no
/// specialist named them, and no task appeared in a compiled context. So the
/// record was only ever written by a person at a keyboard, and "where were we"
/// went on being answered from whatever was still in the conversation.
/// </para>
/// </summary>
public sealed class TaskContextTests : IDisposable
{
    private readonly string _root;
    private readonly string _workspace;
    private readonly string _runtime;

    public TaskContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "loadout-taskctx-" + Guid.NewGuid().ToString("N"));
        _workspace = Path.Combine(_root, "workspace");
        _runtime = Path.Combine(_root, "runtime");

        Directory.CreateDirectory(_runtime);
        Directory.CreateDirectory(Path.Combine(_workspace, "projects", "demo"));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not worth failing a run over a temp directory.
        }
    }

    private static TaskItem Task(string id, TaskState state, string title) => new()
    {
        Id = id,
        Title = title,
        State = state,
        DeclaredBy = "nigel",
        DeclaredUtc = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
    };

    private async Task<string> CompileAsync(bool tasks, StubTasks? service)
    {
        var compiler = new ContextCompiler(
            new NoOpFilePermissions(),
            new RuleService(),
            new MemoryService(TimeProvider.System),
            symbols: null,
            service);

        var manifest = new ProjectManifest
        {
            Slug = "demo",
            Name = "Demo",
            Context = { Tasks = tasks },
        };

        var result = await compiler.CompileAsync(manifest, _workspace, _runtime, "claude");

        result.Failed.Should().BeFalse(result.Error);

        return await File.ReadAllTextAsync(result.Value!.FilePath);
    }

    [Fact]
    public async Task Open_tasks_are_absent_until_the_project_asks_for_them()
    {
        var service = new StubTasks([Task("ship-it", TaskState.Open, "Ship the thing")]);

        var text = await CompileAsync(tasks: false, service);

        text.Should().NotContain("Open tasks");

        // Not even asked for. A task record is a claim somebody made rather
        // than a fact about the code, so a project that does not keep one
        // should not pay to be told it has none.
        service.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_tasks_reach_the_session_once_asked_for()
    {
        var service = new StubTasks([
            Task("ship-it", TaskState.Open, "Ship the thing"),
            Task("setup-repository", TaskState.Doing, "Put this project under version control"),
        ]);

        var text = await CompileAsync(tasks: true, service);

        text.Should().Contain("## Open tasks");
        text.Should().Contain("ship-it");
        text.Should().Contain("Put this project under version control");

        // Attributed and dated, because a claim without either cannot be
        // weighed against what the repository actually shows.
        text.Should().Contain("nigel, 2026-09-08");

        // And told how to move one on, which is the half that makes the record
        // maintain itself rather than rot.
        text.Should().Contain("loadout_task_declare");
    }

    [Fact]
    public async Task A_finished_task_is_not_put_in_front_of_a_later_session()
    {
        var service = new StubTasks([
            Task("old-news", TaskState.Done, "Something already finished"),
            Task("dropped", TaskState.Dropped, "Something abandoned"),
        ]);

        var text = await CompileAsync(tasks: true, service);

        // The record keeps them for the audit trail. Paying for them on every
        // later launch buys nothing anybody has to act on.
        text.Should().NotContain("old-news");
        text.Should().NotContain("dropped");
        text.Should().NotContain("Open tasks", "a heading over nothing teaches nobody anything");
    }

    [Fact]
    public async Task Blocked_work_is_shown_because_somebody_has_to_unblock_it()
    {
        var service = new StubTasks([Task("waiting", TaskState.Blocked, "Waiting on the API key")]);

        var text = await CompileAsync(tasks: true, service);

        text.Should().Contain("waiting");
        text.Should().Contain("blocked");
    }

    /// <summary>A task record that answers at once and records being asked.</summary>
    private sealed class StubTasks(IReadOnlyList<TaskItem> tasks) : ITaskService
    {
        public List<string> Asked { get; } = [];

        public Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
            string projectSlug, CancellationToken ct = default)
        {
            Asked.Add(projectSlug);

            return System.Threading.Tasks.Task.FromResult(
                OperationResult<IReadOnlyList<TaskItem>>.Ok(tasks));
        }

        public Task<OperationResult<TaskItem>> DeclareAsync(
            string projectSlug,
            string id,
            TaskState state,
            string declaredBy,
            string? title = null,
            string? note = null,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> RemoveAsync(
            string projectSlug, string id, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
