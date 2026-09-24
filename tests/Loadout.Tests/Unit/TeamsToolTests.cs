using FluentAssertions;
using Loadout.Cli.Commands;
using Loadout.Cli.Infrastructure;
using Loadout.Core.Teams;
using Loadout.Models;
using Loadout.Models.Results;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// What a session that is not part of a run can ask about the ones that are.
/// </summary>
/// <remarks>
/// <para>
/// The question somebody actually asks is "is anything of mine still going,
/// and does it want me" — and answering it otherwise means leaving the
/// conversation to go and look.
/// </para>
/// <para>
/// Reading only, and the wording is the point: a run that has stopped to ask
/// somebody something has to say so in the first line, because the whole
/// reason to ask is to find that out.
/// </para>
/// </remarks>
public sealed class TeamsToolTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Only the journal is real. The rest are not reached on this path, and
    /// standing up a memory store and a symbol index to leave them unused
    /// would hide what the test is about.
    /// </summary>
    private static LoadoutTools Tools(IRunJournal runs) =>
        new(
            instructions: null!,
            memory: null!,
            workspace: null!,
            projects: null!,
            tasks: null!,
            git: null!,
            symbols: null!,
            runs,
            new Clock(Noon.AddMinutes(10)),
            new LoadoutToolScope(null),
            catalogue: null!);

    [Fact]
    public void A_run_that_wants_somebody_says_so_in_its_first_line()
    {
        var said = Tools(new OneRun(Waiting())).Teams();

        said.Should().Contain("waiting for a person");

        // The node's own question, in the words the gate asked it, rather than
        // a summary somebody would then have to go and expand.
        said.Should().Contain("needs you: implementer/1");
        said.Should().Contain("wants to use Bash for 'dotnet test'");
        said.Should().Contain("(clears when you answer it)");
    }

    [Fact]
    public void A_run_that_is_working_says_what_it_is_costing()
    {
        var said = Tools(new OneRun(Going())).Teams();

        said.Should().Contain("going, round 2 of 5");
        said.Should().Contain("$1.00");
        said.Should().Contain("a minute").And.NotContain("needs you");
    }

    [Fact]
    public void A_run_that_finished_says_how_rather_than_that_it_is_going()
    {
        var said = Tools(new OneRun(Finished())).Teams();

        said.Should().Contain("done");
        said.Should().NotContain("going");

        // No rate on something that is not spending: a figure for a finished
        // run is a number somebody would read as current.
        said.Should().NotContain("a minute");
    }

    [Fact]
    public void Asking_only_about_what_is_going_leaves_out_what_is_not()
    {
        Tools(new OneRun(Finished())).Teams(onlyRunning: true)
            .Should().Be("Nothing is running on this machine.");
    }

    [Fact]
    public void A_machine_that_has_never_run_one_says_that_rather_than_nothing()
    {
        // An empty answer reads as a broken tool. This one reads as an answer.
        Tools(new NoRuns()).Teams().Should().Contain("No team has run");
    }

    private static RunSummary Going() =>
        new("20260918-1200-aaaa", "d", "docs-crew", "check the docs", "autonomous",
            Noon, null, null, 1.00m, 2, [], [], [], 5, "loadout-cli");

    private static RunSummary Finished() =>
        new("20260918-1200-aaaa", "d", "docs-crew", "check the docs", "autonomous",
            Noon, Noon.AddMinutes(5), "done", 1.00m, 5, [], [], [], 5, "loadout-cli");

    private static RunSummary Waiting() =>
        Going() with
        {
            Gates =
            [
                new PendingAsk(
                    "toolu_1", "implementer/1", "role.implementer", "Bash", "dotnet test", Noon),
            ],
        };

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class OneRun(RunSummary run) : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [run.RunId];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Ok([]);

        public OperationResult<RunSummary> Summarise(string runId) =>
            OperationResult<RunSummary>.Ok(run);

        public string DirectoryOf(string runId) => "C:/runs/" + runId;

        /// <summary>
        /// Nothing here deletes anything: these tests read runs.
        /// </summary>
        /// <remarks>
        /// A double that quietly reported success would be asserting its own
        /// opinion about a thing with no undo. Forgetting a run is asserted
        /// against a real disk, in RunForgettingTests.
        /// </remarks>
        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            throw new NotSupportedException("these tests read runs, they do not delete them");
    }

    private sealed class NoRuns : IRunJournal
    {
        public IReadOnlyList<string> List(int limit = 20) => [];

        public OperationResult<IReadOnlyList<RunEvent>> Read(string runId) =>
            OperationResult<IReadOnlyList<RunEvent>>.Fail("no such run", ExitCode.ProjectNotFound);

        public OperationResult<RunSummary> Summarise(string runId) =>
            OperationResult<RunSummary>.Fail("no such run", ExitCode.ProjectNotFound);

        public string DirectoryOf(string runId) => "C:/runs/" + runId;

        /// <summary>
        /// Nothing here deletes anything: these tests read runs.
        /// </summary>
        /// <remarks>
        /// A double that quietly reported success would be asserting its own
        /// opinion about a thing with no undo. Forgetting a run is asserted
        /// against a real disk, in RunForgettingTests.
        /// </remarks>
        public OperationResult<RunForgotten> Forget(string runId, bool force = false) =>
            throw new NotSupportedException("these tests read runs, they do not delete them");
    }
}
