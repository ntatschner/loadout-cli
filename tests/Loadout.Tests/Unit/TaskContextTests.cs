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

    private static TaskItem Task(
        string id, TaskState state, string title, string? note = null) => new()
    {
        Id = id,
        Title = title,
        State = state,
        Note = note ?? string.Empty,
        DeclaredBy = "nigel",
        DeclaredUtc = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero),
    };

    private async Task<string> CompileAsync(bool tasks, StubTasks? service, string? taskId = null)
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

        var result = await compiler.CompileAsync(manifest, _workspace, _runtime, "claude", taskId: taskId);

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
    public async Task An_idea_still_being_shaped_is_not_put_in_front_of_a_session()
    {
        var idea = Task("status-page", TaskState.Open, "A status page for the home lab");
        idea.Kind = TaskKind.Idea;

        var service = new StubTasks([Task("ship-it", TaskState.Open, "Ship the thing"), idea]);

        var text = await CompileAsync(tasks: true, service);

        text.Should().Contain("ship-it");
        text.Should().NotContain("status-page", "an idea is not work a session should pick up");
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
    public async Task The_note_is_carried_because_it_is_what_the_work_actually_is()
    {
        var service = new StubTasks([
            Task(
                "setup-repository",
                TaskState.Open,
                "Put this project under version control",
                "'D:\\code\\thing' was registered before it had a Git repository. "
                + "Initialising it, making the first commit and setting a remote are the work."),
        ]);

        var text = await CompileAsync(tasks: true, service);

        // The title names the task; the note says what doing it involves, and
        // it is the half somebody wrote deliberately. Dropping it left the
        // session with a heading and nothing to act on — which is what it
        // looked like from the outside: a launch that said nothing about
        // setting up the repository, because the part that said so was cut.
        text.Should().Contain("Initialising it, making the first commit");
    }

    [Fact]
    public async Task Blocked_work_is_shown_because_somebody_has_to_unblock_it()
    {
        var service = new StubTasks([Task("waiting", TaskState.Blocked, "Waiting on the API key")]);

        var text = await CompileAsync(tasks: true, service);

        text.Should().Contain("waiting");
        text.Should().Contain("blocked");
    }

    [Fact]
    public async Task The_task_a_session_was_started_for_comes_first_and_only_once()
    {
        var service = new StubTasks([
            Task("ship-it", TaskState.Open, "Ship the thing", "Tag it, then watch the release run."),
            Task("other-work", TaskState.Doing, "Something else entirely"),
        ]);

        var text = await CompileAsync(tasks: true, service, taskId: "ship-it");

        // Said outright, under its own heading, ahead of the list: a session
        // left to work out its task from where it sits in a list might not.
        var own = text.IndexOf("## This session's task", StringComparison.Ordinal);
        var rest = text.IndexOf("## Open tasks", StringComparison.Ordinal);

        own.Should().BeGreaterThan(-1);
        rest.Should().BeGreaterThan(own);
        text[own..rest].Should().Contain("ship-it").And.Contain("Tag it, then watch the release run.");

        // And not again in the list, so the note is paid for once.
        text[rest..].Should().NotContain("ship-it");
        text[rest..].Should().Contain("other-work");

        CountOf(text, "Tag it, then watch the release run.").Should().Be(1);
    }

    [Fact]
    public async Task The_session_s_own_task_carries_its_detail_and_the_rest_point_to_theirs()
    {
        var ship = Task("ship-it", TaskState.Open, "Ship the thing");
        ship.Detail = "Tag the release.\nThen watch the run until it publishes.";
        var other = Task("other-work", TaskState.Doing, "Something else entirely");
        other.Detail = "A long description nobody in this session needs to pay for.";

        var text = await CompileAsync(tasks: true, new StubTasks([ship, other]), taskId: "ship-it");

        var rest = text.IndexOf("## Open tasks", StringComparison.Ordinal);

        // In full for the work the session was started for, since that is
        // where a long description went when its title was cut.
        text[..rest].Should().Contain("  Tag the release.").And.Contain("  Then watch the run until it publishes.");

        // Pointed to for the rest, so every launch does not pay for every one.
        text[rest..].Should().NotContain("A long description");
        text[rest..].Should().Contain("`loadout task show other-work`");
    }

    [Fact]
    public async Task A_task_with_no_detail_gets_no_pointer_to_one()
    {
        var text = await CompileAsync(tasks: true, new StubTasks([Task("other-work", TaskState.Doing, "Something")]));

        text.Should().NotContain("loadout task show");
    }

    [Fact]
    public async Task The_session_task_is_carried_even_where_the_project_lists_none()
    {
        var service = new StubTasks([Task("ship-it", TaskState.Open, "Ship the thing")]);

        var text = await CompileAsync(tasks: false, service, taskId: "ship-it");

        // Somebody picked it for this launch, which is a stronger reason than
        // a project setting about the rest of the record.
        text.Should().Contain("## This session's task").And.Contain("Ship the thing");
        text.Should().NotContain("## Open tasks");
    }

    [Fact]
    public async Task A_session_with_no_task_gets_no_heading_for_one()
    {
        var service = new StubTasks([Task("ship-it", TaskState.Open, "Ship the thing")]);

        var text = await CompileAsync(tasks: true, service);

        text.Should().NotContain("This session's task");
        text.Should().Contain("## Open tasks").And.Contain("ship-it");
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;

        for (var at = text.IndexOf(part, StringComparison.Ordinal);
            at >= 0;
            at = text.IndexOf(part, at + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>A task record that answers at once and records being asked.</summary>
    private sealed class StubTasks(IReadOnlyList<TaskItem> tasks) : ITaskService
    {
        public List<string?> Asked { get; } = [];

        public Task<OperationResult<IReadOnlyList<TaskItem>>> ListAsync(
            string? projectSlug, CancellationToken ct = default)
        {
            Asked.Add(projectSlug);

            return System.Threading.Tasks.Task.FromResult(
                OperationResult<IReadOnlyList<TaskItem>>.Ok(tasks));
        }

        public Task<OperationResult<TaskItem>> DeclareAsync(
            string? projectSlug,
            string id,
            TaskState state,
            string declaredBy,
            string? title = null,
            string? note = null,
            CancellationToken ct = default,
            TaskKind? kind = null,
            string? detail = null, IReadOnlyList<string>? doneWhen = null) =>
            throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<TaskShortened>>> ShortenTitlesAsync(
            string? projectSlug, string by, bool apply, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult> RemoveAsync(
            string? projectSlug, string id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<TaskItem>> MoveAsync(
            string? fromSlug, string? toSlug, string id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<TaskMoved>>> MovedAsync(CancellationToken ct = default) =>
            System.Threading.Tasks.Task.FromResult(OperationResult<IReadOnlyList<TaskMoved>>.Ok([]));
    }
}
