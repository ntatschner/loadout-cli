using System.Text.Json;

using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Reading back what a run produced, from the reports and the journal it wrote
/// while it happened.
/// </summary>
/// <remarks>
/// <para>
/// None of this is new bookkeeping: every node hands back a report listing
/// what it produced and what it showed for it, and the journal records every
/// gate as it is decided. The runs on the machine this was written against
/// had twenty-eight deliverables and over a hundred pieces of evidence sitting
/// on disk that nothing ever read.
/// </para>
/// <para>
/// The shapes below are the shapes real runs produced, including the awkward
/// ones: a node called <c>implementer/1</c> whose report is filed under
/// <c>implementer-1</c>, and a gate whose answer is a JSON boolean rather than
/// a string.
/// </para>
/// </remarks>
public sealed class RunLeftBehindTests : IDisposable
{
    private readonly string _run =
        Path.Combine(Path.GetTempPath(), "loadout-left-" + Guid.NewGuid().ToString("N"));

    public RunLeftBehindTests() => Directory.CreateDirectory(_run);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_run))
            {
                Directory.Delete(_run, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    private void Report(string file, string json) =>
        File.WriteAllText(Path.Combine(_run, file), json);

    private static RunEvent Event(string kind, string data, string? node = null) =>
        new(DateTimeOffset.Parse("2026-09-15T22:58:02+00:00", System.Globalization.CultureInfo.InvariantCulture),
            node, kind, JsonDocument.Parse(data).RootElement);

    [Fact]
    public void What_each_node_produced_comes_back_with_the_node_that_produced_it()
    {
        Report("report-implementer-1-2.json",
            """
            {
              "contract": "report/1",
              "node": "implementer/1",
              "status": "done",
              "summary": "Committed it.",
              "deliverables": [{"kind": "commit", "ref": "66a1262", "note": "on branch one"}],
              "evidence": [{"kind": "command", "ref": "git show", "result": "pass"}],
              "outward_taken": []
            }
            """);

        var left = RunLeftBehind.In(_run, []);

        // The node comes from inside the file. Its name has a slash in it and
        // the file's does not, so turning one back into the other is a guess.
        left.Delivered.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new Delivered("implementer/1", "commit", "66a1262", "on branch one"));

        left.Evidence.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new Proof("implementer/1", "command", "git show", "pass", null));
    }

    [Fact]
    public void The_lead_report_is_read_as_well_as_everybody_elses()
    {
        Report("final-report.json",
            """
            {
              "contract": "report/1", "node": "lead", "status": "blocked",
              "summary": "Waiting.", "outward_taken": [],
              "deliverables": [{"kind": "plan", "ref": "round-3"}],
              "evidence": []
            }
            """);

        RunLeftBehind.In(_run, []).Delivered.Should().ContainSingle()
            .Which.Node.Should().Be("lead");
    }

    [Fact]
    public void A_report_that_will_not_parse_is_named_rather_than_skipped()
    {
        Report("report-planner-1.json", "{ this is not json");

        var left = RunLeftBehind.In(_run, []);

        // A shorter list looks exactly like a quieter run. A node whose report
        // cannot be read is a node whose work nobody can see, and that is
        // worth saying out loud.
        left.Delivered.Should().BeEmpty();
        left.Unreadable.Should().ContainSingle().Which.Should().Be("report-planner-1.json");
    }

    [Fact]
    public void Nothing_at_all_is_read_from_a_directory_that_is_not_there()
    {
        var left = RunLeftBehind.In(Path.Combine(_run, "gone"), []);

        left.Delivered.Should().BeEmpty();
        left.Evidence.Should().BeEmpty();
        left.Unreadable.Should().BeEmpty();
    }

    [Fact]
    public void A_gate_that_said_yes_is_not_read_as_a_refusal()
    {
        var left = RunLeftBehind.In(_run,
        [
            Event("gate.decided", """{"gate":"merge","branch":"teams/r/impl-1","allowed":true}"""),
            Event("merge.done", """{"branch":"teams/r/impl-1","target":"main","FastForward":true}"""),
        ]);

        // `allowed` is a JSON boolean, and the journal's string reader answers
        // null for anything that is not a string. Asked for it as a string,
        // every gate ever opened reads as refused - and the first version of
        // this reported a merge as refused on the line above reporting it done.
        left.Decisions.Should().HaveCount(2);
        left.Decisions[0].Outcome.Should().Be("allowed");
        left.Decisions[1].Outcome.Should().Be("merged");
        left.Decisions[1].Detail.Should().Be("fast forward");
    }

    [Fact]
    public void A_gate_that_said_no_is_read_as_a_refusal()
    {
        var left = RunLeftBehind.In(_run,
            [Event("gate.decided", """{"gate":"merge","branch":"b","allowed":false}""")]);

        left.Decisions.Should().ContainSingle().Which.Outcome.Should().Be("refused");
    }

    [Fact]
    public void A_permission_is_a_decision_too_and_says_who_wanted_what()
    {
        var left = RunLeftBehind.In(_run,
        [
            Event("permission.asked",
                """{"node":"implementer/1","tool":"Bash","target":"git push","allowed":false,"reason":"outward"}"""),
        ]);

        var one = left.Decisions.Should().ContainSingle().Subject;

        one.What.Should().Contain("implementer/1").And.Contain("Bash").And.Contain("git push");
        one.Outcome.Should().Be("refused");
        one.Detail.Should().Be("outward");
    }

    [Fact]
    public void Events_written_with_a_capital_letter_are_read_as_well()
    {
        // These events were written from C# anonymous objects, and some of them
        // carry Node and Tool where their neighbours carry node and tool. A
        // reader that asks for one spelling silently drops half the journal.
        var left = RunLeftBehind.In(_run,
            [Event("request.refused", """{"Node":"planner","reason":"already running"}""")]);

        left.Decisions.Should().ContainSingle()
            .Which.What.Should().Contain("planner");
    }
}
