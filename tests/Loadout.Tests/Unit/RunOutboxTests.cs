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
