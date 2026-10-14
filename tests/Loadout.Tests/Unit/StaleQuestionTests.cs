using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Teams;
using Loadout.Models.Platform;
using Loadout.Platform.Linux;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// A question left open by a run that stopped, once the run has been picked up
/// again.
/// </summary>
/// <remarks>
/// The ask file stays in the run's directory with no answer beside it, and the
/// open-question list read every such file as waiting. Nothing would ever
/// answer it - the wait it belonged to ended with the run - so the dashboard
/// and the Teams window showed it, greyed out once its deadline passed, for as
/// long as the run was kept.
/// </remarks>
public sealed class StaleQuestionTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-stale-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    /// <summary>What is on disk for a run reopened before closing answers were written.</summary>
    [Fact]
    public async Task A_question_from_before_the_run_was_picked_up_is_not_waiting()
    {
        await AskAsync("gate-old", Start.AddMinutes(5));

        var summary = RunJournal.Fold("r", _directory,
        [
            Event(Start, "run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
            Event(Start.AddMinutes(65), "run.finished", new { ended = "stopped" }),
            Event(Start.AddMinutes(90), "run.reopened", new { was = "stopped", autonomy = "supervised", rounds = 10 }),
        ]);

        summary.Waiting.Should().BeEmpty("nothing will answer a question whose wait ended with the run");
    }

    [Fact]
    public async Task A_question_asked_after_the_run_was_picked_up_is_waiting()
    {
        await AskAsync("gate-old", Start.AddMinutes(5));
        await AskAsync("gate-new", Start.AddMinutes(95));

        var summary = RunJournal.Fold("r", _directory,
        [
            Event(Start, "run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
            Event(Start.AddMinutes(65), "run.finished", new { ended = "stopped" }),
            Event(Start.AddMinutes(90), "run.reopened", new { was = "stopped", autonomy = "supervised", rounds = 10 }),
        ]);

        summary.Waiting.Select(one => one.Id).Should().Equal("gate-new");
    }

    [Fact]
    public async Task A_run_never_picked_up_keeps_its_open_question()
    {
        await AskAsync("gate-old", Start.AddMinutes(5));

        var summary = RunJournal.Fold("r", _directory,
        [
            Event(Start, "run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
        ]);

        summary.Waiting.Select(one => one.Id).Should().Equal("gate-old");
    }

    [Fact]
    public void A_retired_question_says_where_it_went()
    {
        RunJournal.Wording(Event(Start, "node.answered", new
        {
            tool = string.Empty,
            allowed = false,
            by = "retired",
            question = "Which database should it use?",
        })).Should().Be("question retired when the run was picked up again: Which database should it use?");

        RunJournal.Wording(Event(Start, "node.answered", new
        {
            tool = string.Empty,
            allowed = false,
            by = "ended",
            question = "Which database should it use?",
        })).Should().Be("question closed unanswered when the run ended: Which database should it use?");
    }

    /// <summary>
    /// What 'team gate', 'team tell' and the remedy list read, none of which
    /// fold the whole run first.
    /// </summary>
    [Fact]
    public async Task The_open_questions_read_from_the_journal_leave_out_those_from_before_the_run_was_picked_up()
    {
        await AskAsync("gate-old", Start.AddMinutes(5));
        await AskAsync("gate-new", Start.AddMinutes(95));

        await WriteJournalAsync(_directory,
            Event(Start, "run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
            Event(Start.AddMinutes(90), "run.reopened", new { was = "stopped" }));

        RunJournal.Open(_directory).Select(one => one.Id).Should().Equal("gate-new");
    }

    [Fact]
    public async Task A_run_with_no_journal_keeps_every_open_question()
    {
        await AskAsync("gate-old", Start.AddMinutes(5));

        RunJournal.Open(_directory).Select(one => one.Id).Should().Equal("gate-old");
    }

    [Fact]
    public async Task A_remedy_question_from_before_the_run_was_picked_up_is_not_listed_as_waiting()
    {
        var paths = new LinuxPaths(
            new FakeEnvironmentProvider(
                Path.Combine(_directory, "home"),
                new Dictionary<string, string> { ["XDG_STATE_HOME"] = Path.Combine(_directory, "state") }),
            new NoOpFilePermissions(),
            new HostPlatform(HostOperatingSystem.Linux, System.Runtime.InteropServices.Architecture.X64, "test", "TEST"));

        var run = Path.Combine(paths.Paths.State, "teams", "runs", "20261014-0900-abcd");

        await NodePermissions.PutAsync(run, new PendingAsk(
            "remedy-old", "implementer", "role.implementer", "Bash", "dotnet test", Start.AddMinutes(5), Kind: "remedy"));
        await NodePermissions.PutAsync(run, new PendingAsk(
            "remedy-new", "implementer", "role.implementer", "Bash", "dotnet test", Start.AddMinutes(95), Kind: "remedy"));

        await WriteJournalAsync(run,
            Event(Start, "run.started", new { team = "t", goal = "g", autonomy = "supervised" }),
            Event(Start.AddMinutes(90), "run.reopened", new { was = "stopped" }));

        new RemedyBook(paths).Waiting("t").Value!.Select(one => one.Id).Should().Equal("remedy-new");
    }

    private static Task WriteJournalAsync(string directory, params RunEvent[] events)
    {
        Directory.CreateDirectory(directory);

        return File.WriteAllLinesAsync(
            Path.Combine(directory, "journal.jsonl"),
            events.Select(one => JsonSerializer.Serialize(new
            {
                at = one.At,
                run = "r",
                node = one.Node,
                kind = one.Kind,
                data = one.Data,
            })));
    }

    private Task AskAsync(string id, DateTimeOffset at) =>
        NodePermissions.PutAsync(_directory, new PendingAsk(
            id, "lead", "the lead", string.Empty, null, at,
            Kind: "question", Asked: "Which database should it use?", Options: ["Postgres", "SQLite"],
            Recommendation: "SQLite", Until: at.AddMinutes(10)));

    private static RunEvent Event(DateTimeOffset at, string kind, object data) =>
        new(at, null, kind, JsonSerializer.SerializeToElement(data));
}
