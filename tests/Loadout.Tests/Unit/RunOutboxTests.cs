using FluentAssertions;
using Loadout.Core.Git;
using Loadout.Core.Teams;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Turning the commits a run reported into the files it delivered.
/// </summary>
/// <remarks>
/// A node reports what it produced by reference, because a hash is what it can
/// say truthfully about work it has committed. Somebody asking what a run
/// delivered wants files, and getting them meant knowing which repository the
/// run used and typing git at it by hand.
/// </remarks>
public sealed class RunOutboxTests : IDisposable
{
    private const string Run = "20260915-2354-7ede";
    private const string Repository = @"D:\git\somewhere";

    private readonly string _state =
        Path.Combine(Path.GetTempPath(), "loadout-outbox-" + Guid.NewGuid().ToString("N"));

    private string Directory => Path.Combine(_state, "teams", "runs", Run);

    public RunOutboxTests() => System.IO.Directory.CreateDirectory(Directory);

    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(_state))
            {
                System.IO.Directory.Delete(_state, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Journal(params string[] lines) =>
        File.WriteAllLines(Path.Combine(Directory, "journal.jsonl"), lines);

    /// <summary>
    /// A report, written at a stated minute.
    /// </summary>
    /// <remarks>
    /// The reader takes reports oldest first, which in a run is the order the
    /// nodes finished. Two written in the same instant have no order at all,
    /// so a test that cares which came first has to say.
    /// </remarks>
    private void Report(string file, string json, int minute = 0)
    {
        var at = Path.Combine(Directory, file);

        File.WriteAllText(at, json);
        File.SetLastWriteTimeUtc(at, new DateTime(2026, 9, 15, 22, minute, 0, DateTimeKind.Utc));
    }

    private static string Launched(string node, string? worktree) =>
        "{\"at\":\"2026-09-15T22:55:02+00:00\",\"run\":\"r\",\"node\":\"" + node
        + "\",\"kind\":\"node.launched\",\"data\":{\"launch\":\"L\","
        + "\"role\":\"role.implementer\",\"directory\":\"D:\\\\git\\\\somewhere\",\"worktree\":"
        + (worktree is null ? "null" : "\"" + worktree + "\"")
        + "}}";

    /// <summary>A launch in a stated directory, for a test with its own.</summary>
    private static string LaunchedIn(string node, string directory, string? worktree) =>
        "{\"at\":\"2026-09-15T22:55:02+00:00\",\"run\":\"r\",\"node\":\"" + node
        + "\",\"kind\":\"node.launched\",\"data\":{\"launch\":\"L\","
        + "\"role\":\"role.implementer\",\"directory\":"
        + System.Text.Json.JsonSerializer.Serialize(directory)
        + ",\"worktree\":"
        + (worktree is null ? "null" : "\"" + worktree + "\"")
        + "}}";

    /// <summary>A report delivering one thing of any kind.</summary>
    private static string Delivering(string node, string kind, string reference, string? note) =>
        "{\"contract\":\"report/1\",\"node\":\"" + node
        + "\",\"status\":\"done\",\"summary\":\"Did it.\",\"deliverables\":[{\"kind\":\""
        + kind + "\",\"ref\":" + System.Text.Json.JsonSerializer.Serialize(reference)
        + (note is null ? "" : ",\"note\":" + System.Text.Json.JsonSerializer.Serialize(note))
        + "}],\"evidence\":[],\"outward_taken\":[]}";

    private static string Delivering(string node, string commit) =>
        $$"""
        {"contract":"report/1","node":"{{node}}","status":"done","summary":"Did it.",
         "deliverables":[{"kind":"commit","ref":"{{commit}}"}],"evidence":[],"outward_taken":[]}
        """;

    private RunOutbox Outbox(FakeGit git) =>
        new(new RunJournal(new StubPaths(_state)), git);

    [Fact]
    public async Task A_reported_commit_comes_back_as_the_files_it_contains()
    {
        Journal(Launched("lead", null), Launched("implementer/1", "teams/r/impl-1"));
        Report("report-implementer-1-1.json", Delivering("implementer/1", "3469742"));

        var git = new FakeGit(Repository);

        git.Touched["3469742"] =
        [
            new GitFileChange("farewell.txt", "added"),
            new GitFileChange("README.md", "changed"),
        ];

        var outbox = (await Outbox(git).ForAsync(Run)).Value!;

        // The repository is not recorded anywhere directly. A node given its
        // own worktree is launched in that worktree; a node without one is
        // launched in the repository, and that path outlives the worktrees.
        outbox.Repository.Should().Be(@"D:\git\somewhere");
        outbox.Missing.Should().BeEmpty();

        outbox.Files.Should().HaveCount(2);
        outbox.Files[0].Should().BeEquivalentTo(
            new OutboxFile("farewell.txt", "added", "3469742", "implementer/1"));
        outbox.Files[1].Path.Should().Be("README.md");
    }

    [Fact]
    public async Task A_commit_the_repository_does_not_have_is_named_rather_than_dropped()
    {
        Journal(Launched("lead", null));
        Report("final-report.json", Delivering("lead", "deadbee"));

        // The fake refuses anything it was not told about, which is what git
        // does with a hash that is not there.
        var outbox = (await Outbox(new FakeGit(Repository)).ForAsync(Run)).Value!;

        // A run that produced nothing and a run whose work nobody can reach
        // look identical in a list that simply leaves the second one out, and
        // that difference is the whole question.
        outbox.Files.Should().BeEmpty();
        outbox.Missing.Should().ContainSingle().Which.Should().Be("deadbee");
    }

    [Fact]
    public async Task One_commit_reported_by_two_nodes_is_one_piece_of_work()
    {
        Journal(Launched("lead", null));
        Report("report-implementer-1-1.json", Delivering("implementer/1", "3469742"), minute: 10);
        Report("final-report.json", Delivering("lead", "3469742"), minute: 20);

        var git = new FakeGit(Repository);

        git.Touched["3469742"] = [new GitFileChange("farewell.txt", "added")];

        var outbox = (await Outbox(git).ForAsync(Run)).Value!;

        // A lead repeating its implementer's commit is ordinary, and listing
        // the same file twice reads as two of them.
        outbox.Files.Should().ContainSingle()
            .Which.Node.Should().Be("implementer/1", "the node that reported it first");
    }

    [Fact]
    public async Task A_node_started_in_another_node_s_tree_does_not_say_where_the_run_worked()
    {
        // A reviewer has no worktree of its own and is started in the one it
        // reviews, which is gone once the work is merged. Taken for the
        // repository, it would resolve every commit against a directory that
        // is not there.
        Journal(
            Launched("implementer/1", "teams/r/impl-1"),
            "{\"at\":\"2026-09-15T22:56:02+00:00\",\"run\":\"r\",\"node\":\"reviewer\",\"kind\":\"node.launched\","
            + "\"data\":{\"launch\":\"L\",\"role\":\"role.reviewer\",\"directory\":\"D:\\\\trees\\\\impl-1\","
            + "\"worktree\":null,\"reviewing\":\"teams-r-implementer-1\"}}",
            Launched("verifier", null));
        Report("report-implementer-1-1.json", Delivering("implementer/1", "3469742"));

        var git = new FakeGit(Repository);

        git.Touched["3469742"] = [new GitFileChange("farewell.txt", "added")];

        var outbox = (await Outbox(git).ForAsync(Run)).Value!;

        outbox.Repository.Should().Be(@"D:\git\somewhere");
    }

    [Fact]
    public async Task A_run_that_never_says_where_it_worked_says_so_rather_than_nothing()
    {
        // Every node had a worktree of its own, so no event names the
        // repository.
        Journal(Launched("implementer/1", "teams/r/impl-1"));
        Report("report-implementer-1-1.json", Delivering("implementer/1", "3469742"));

        var outbox = (await Outbox(new FakeGit(Repository)).ForAsync(Run)).Value!;

        outbox.Repository.Should().BeNull();
        outbox.Files.Should().BeEmpty();

        // The commits are still worth naming: an empty answer would read as a
        // run that delivered nothing.
        outbox.Missing.Should().ContainSingle().Which.Should().Be("3469742");
    }

    [Fact]
    public async Task Only_commits_are_resolved_and_the_other_deliverables_are_left_alone()
    {
        Journal(Launched("lead", null));
        Report("final-report.json",
            """
            {"contract":"report/1","node":"lead","status":"done","summary":"Did it.",
             "deliverables":[{"kind":"decision","ref":"accept"},{"kind":"plan","ref":"round-2"}],
             "evidence":[],"outward_taken":[]}
            """);

        var outbox = (await Outbox(new FakeGit(Repository)).ForAsync(Run)).Value!;

        // A decision is not a thing with files in it, and asking git for one
        // would report every run that made a decision as having lost it.
        outbox.Files.Should().BeEmpty();
        outbox.Missing.Should().BeEmpty();

        // Nor is either of them a file that has gone missing.
        outbox.Loose.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_a_node_reported_that_no_commit_carries_is_still_delivered()
    {
        // The run this was written against: a planner wrote PLAN.md into the
        // repository, reported it, and never committed it. An outbox built out
        // of commits alone said that run had changed one README and nothing
        // else.
        var repository = Path.Combine(_state, "repo");

        System.IO.Directory.CreateDirectory(repository);
        File.WriteAllText(Path.Combine(repository, "PLAN.md"), new string('x', 120));

        Journal(LaunchedIn("planner", repository, worktree: null));
        Report("report-planner-1.json", Delivering("planner", "plan", "PLAN.md", "three pieces of work"));

        var outbox = (await Outbox(new FakeGit(repository)).ForAsync(Run)).Value!;

        outbox.Files.Should().BeEmpty();

        var loose = outbox.Loose.Should().ContainSingle().Which;

        loose.Path.Should().Be(Path.Combine(repository, "PLAN.md"), "resolved against the repository");
        loose.Kind.Should().Be("plan");
        loose.Bytes.Should().Be(120);
        loose.Node.Should().Be("planner");
        loose.Note.Should().Be("three pieces of work");
    }

    [Fact]
    public async Task A_reported_file_that_is_no_longer_there_says_so()
    {
        var repository = Path.Combine(_state, "repo");

        System.IO.Directory.CreateDirectory(repository);

        Journal(LaunchedIn("implementer/1", repository, worktree: null));
        Report("report-implementer-1-1.json", Delivering("implementer/1", "document", "notes/summary.md", null));

        var outbox = (await Outbox(new FakeGit(repository)).ForAsync(Run)).Value!;

        // Named with nothing where its size goes, for the same reason a commit
        // nobody can find is named: a run whose output has gone is not a run
        // that produced nothing.
        outbox.Loose.Should().ContainSingle().Which.Bytes.Should().BeNull();
    }

    [Theory]
    [InlineData("accept")]
    [InlineData("round-3")]
    [InlineData("round-1: implementer/1 -> reviewer -> verifier -> merge gate")]
    [InlineData("not-verified")]
    public async Task A_verdict_is_not_read_as_a_file_that_has_gone_missing(string reference)
    {
        // All four are real references off runs on this machine. A plan is
        // "PLAN.md" on one run and "round-3" on the next, so the kind cannot
        // decide this and the shape has to.
        var repository = Path.Combine(_state, "repo");

        System.IO.Directory.CreateDirectory(repository);

        Journal(LaunchedIn("lead", repository, worktree: null));
        Report("final-report.json", Delivering("lead", "plan", reference, null));

        (await Outbox(new FakeGit(repository)).ForAsync(Run)).Value!
            .Loose.Should().BeEmpty();
    }

    /// <summary>Paths whose state directory is the one this test wrote into.</summary>
    private sealed class StubPaths : Loadout.Platform.Abstractions.IPlatformPaths
    {
        private readonly string _state;

        public StubPaths(string state) => _state = state;

        public Loadout.Models.Platform.HostPlatform Host =>
            new(
                Loadout.Models.Platform.HostOperatingSystem.Linux,
                System.Runtime.InteropServices.Architecture.X64,
                "test",
                "TEST");

        public Loadout.Models.Platform.PlatformPathSet Paths =>
            new(_state, _state, _state, _state, _state);

        public void EnsureDirectoriesExist()
        {
        }

        public string CreateRuntimeDirectory() =>
            throw new NotSupportedException("Nothing here launches anything.");
    }
}
