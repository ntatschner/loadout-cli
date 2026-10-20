using System.Text.Json;
using FluentAssertions;
using Loadout.Core.Sessions.Hosting;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Telling an agent waiting on its own background work apart from one
/// waiting for its person, from the end of its transcript.
/// </summary>
/// <remarks>
/// The entries are invented, in the shapes Claude Code writes: a launch's
/// task id in its tool result, and its report as a task notification. Nobody
/// has captured a real occurrence of the wrong flag, so the replay over real
/// transcripts (<see cref="BackgroundWorkReplay"/>) is the other half of the
/// evidence.
/// </remarks>
public sealed class BackgroundWorkTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_background_shell_with_no_report_is_outstanding_and_its_report_ends_it()
    {
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b1shell01", stdout = "" }, Noon);

        Scan(launched).Should().Be(new BackgroundReading(true, null));
        Scan([.. launched, Notification(Noon.AddMinutes(3), "completed", "b1shell01")])
            .Should().Be(new BackgroundReading(false, null));
    }

    [Theory]
    [InlineData("failed")]
    [InlineData("killed")]
    [InlineData("stopped")]
    public void Any_status_in_a_notification_ends_the_work(string status)
    {
        var launched = Launch("toolu_1", "PowerShell", new { backgroundTaskId = "b2shell02" }, Noon);

        Scan([.. launched, Notification(Noon.AddMinutes(1), status, "b2shell02")]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_report_that_arrives_mid_turn_is_queued_and_still_ends_the_work()
    {
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b5shell05" }, Noon);
        var report = "<task-notification>\n<task-id>b5shell05</task-id>\n<status>completed</status>\n</task-notification>";

        Scan([.. launched, Line(new { type = "queue-operation", operation = "enqueue", timestamp = Noon.AddMinutes(1), content = report })])
            .Should().Be(new BackgroundReading(false, null));
        Scan([.. launched, Line(new { type = "attachment", timestamp = Noon.AddMinutes(1), attachment = new { type = "queued_command", prompt = report } })])
            .Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_task_status_ends_the_work_only_once_it_is_no_longer_running()
    {
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b6shell06" }, Noon);
        string Status(string status) => Line(new { type = "attachment", timestamp = Noon, attachment = new { type = "task_status", taskId = "b6shell06", status } });

        Scan([.. launched, Status("running")]).Should().Be(new BackgroundReading(true, null));
        Scan([.. launched, Status("completed")]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void Reading_a_finished_tasks_output_ends_it_and_reading_a_running_one_does_not()
    {
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b7sh" }, Noon);
        List<string> Read(string status) => Launch(
            "toolu_2", "TaskOutput", new { retrieval_status = "success" }, Noon.AddMinutes(1), $"<retrieval_status>success</retrieval_status>\n<task_id>b7sh</task_id>\n<status>{status}</status>");

        Scan([.. launched, .. Read("running")]).Should().Be(new BackgroundReading(true, null));
        Scan([.. launched, .. Read("completed")]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_queued_hand_back_ends_the_agent()
    {
        var launched = Launch("toolu_1", "Agent", new { isAsync = true, agentId = "a3agent0000000003" }, Noon);
        var handBack = "<agent-message from=\"a3agent0000000003\">\n[Subagent hand-back] The text below is the final report.\n</agent-message>";

        Scan([.. launched, Line(new { type = "attachment", timestamp = Noon, attachment = new { type = "queued_command", prompt = handBack } })])
            .Should().Be(new BackgroundReading(false, null));
        Scan([.. launched, Line(new { type = "attachment", timestamp = Noon, attachment = new { type = "queued_command", prompt = "<agent-message from=\"a3agent0000000003\">\nhalf way\n</agent-message>" } })])
            .Should().Be(new BackgroundReading(true, null), "a message on the way is not the end");
    }

    [Fact]
    public void An_async_agent_ends_with_its_notification_or_its_hand_back()
    {
        var launched = Launch("toolu_1", "Agent", new { isAsync = true, status = "async_launched", agentId = "a1agent0000000001" }, Noon);

        Scan(launched).Should().Be(new BackgroundReading(true, null));
        Scan([.. launched, Notification(Noon.AddMinutes(2), "completed", "a1agent0000000001")]).Should().Be(new BackgroundReading(false, null));
        Scan([.. launched, Line(new
        {
            type = "user",
            timestamp = Noon.AddMinutes(2),
            origin = new { kind = "peer", from = "a1agent0000000001", senderTaskId = "a1agent0000000001" },
            message = new { role = "user", content = "Another Claude session sent a message: ..." },
        })]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void An_agent_that_ran_in_the_foreground_left_nothing_outstanding()
    {
        Scan(Launch("toolu_1", "Agent", new { status = "completed", agentId = "a2agent0000000002" }, Noon))
            .Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_monitor_keeps_watching_through_its_events_and_ends_when_its_time_is_up()
    {
        var launched = Launch("toolu_1", "Monitor", new { taskId = "b3watch03", timeoutMs = 1_800_000, persistent = false }, Noon);
        List<string> evented = [.. launched, Line(new
        {
            type = "user",
            timestamp = Noon.AddMinutes(5),
            origin = new { kind = "task-notification" },
            message = new { role = "user", content = "<task-notification>\n<task-id>b3watch03</task-id>\n<summary>Monitor event</summary>\n<event>line</event>\n</task-notification>" },
        })];

        BackgroundWork.Scan(evented, Noon.AddMinutes(10)).Should().Be(new BackgroundReading(true, null), "an event is not the end of a watch");
        BackgroundWork.Scan(evented, Noon.AddMinutes(31)).Should().Be(new BackgroundReading(false, null), "it expired after thirty minutes");
    }

    [Fact]
    public void A_persistent_monitor_has_no_time_of_its_own()
    {
        BackgroundWork.Scan(Launch("toolu_1", "Monitor", new { taskId = "b3watch03", timeoutMs = 1_800_000, persistent = true }, Noon), Noon.AddHours(5))
            .Should().Be(new BackgroundReading(true, null));
    }

    [Fact]
    public void Work_silent_past_the_limit_when_the_agent_goes_idle_does_not_count()
    {
        // Most often a server it left running for you to look at.
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b1shell01" }, Noon);

        BackgroundWork.Scan(launched, Noon.AddMinutes(29), TimeSpan.FromMinutes(30)).Should().Be(new BackgroundReading(true, null));
        BackgroundWork.Scan(launched, Noon.AddMinutes(31), TimeSpan.FromMinutes(30)).Should().Be(new BackgroundReading(false, null));
        BackgroundWork.Scan(launched, Noon.AddMinutes(31)).Should().Be(new BackgroundReading(true, null), "with no limit it waits as long as it takes");
    }

    [Fact]
    public void Stopping_a_task_ends_it()
    {
        List<string> lines =
        [
            .. Launch("toolu_1", "Bash", new { backgroundTaskId = "b4shell04" }, Noon),
            .. Launch("toolu_2", "TaskStop", new { message = "Successfully stopped task", task_id = "b4shell04", task_type = "local_bash" }, Noon.AddMinutes(1)),
        ];

        Scan(lines).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_wakeup_still_to_come_is_outstanding_with_its_time_and_one_that_has_fired_is_not()
    {
        var due = Noon.AddMinutes(20);
        var scheduled = Launch("toolu_1", "ScheduleWakeup", new { scheduledFor = due.ToUnixTimeMilliseconds(), clampedDelaySeconds = 1200 }, Noon);

        Scan(scheduled).Should().Be(new BackgroundReading(true, due));
        Scan([.. scheduled, Prompt(due.AddSeconds(1))]).Should().Be(new BackgroundReading(false, null), "the wakeup fired and the turn it started has ended");
    }

    [Fact]
    public void A_loop_told_to_stop_leaves_no_wakeup()
    {
        List<string> lines =
        [
            .. Launch("toolu_1", "ScheduleWakeup", new { scheduledFor = Noon.AddMinutes(20).ToUnixTimeMilliseconds() }, Noon),
            .. Launch("toolu_2", "ScheduleWakeup", new { stopped = true }, Noon.AddMinutes(1)),
        ];

        Scan(lines).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_turn_that_ended_on_a_question_has_nothing_outstanding()
    {
        Scan([Prompt(Noon), Reply(Noon.AddSeconds(30))]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void A_subagents_own_work_is_not_the_sessions()
    {
        var launched = Launch("toolu_1", "Bash", new { backgroundTaskId = "b1shell01" }, Noon)
            .Select(line => line.Replace("\"type\":", "\"isSidechain\":true,\"type\":", StringComparison.Ordinal));

        Scan([Prompt(Noon.AddSeconds(-5)), .. launched]).Should().Be(new BackgroundReading(false, null));
    }

    [Fact]
    public void Lines_that_are_not_entries_are_passed_over_and_nothing_at_all_cannot_be_read()
    {
        Scan(["not json", """{"type":"summary"}""", .. Launch("toolu_1", "Bash", new { backgroundTaskId = "b1" }, Noon), "{\"type\":\"us"])
            .Should().Be(new BackgroundReading(true, null));

        Scan(["not json", """{"type":"summary"}"""]).Should().BeNull("a file with no entries is not one this recognises");
    }

    [Fact]
    public void A_file_that_is_missing_or_not_a_transcript_cannot_be_read()
    {
        BackgroundWork.Read(null, Noon).Should().BeNull();
        BackgroundWork.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl"), Noon).Should().BeNull();

        var other = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllLines(other, Launch("toolu_1", "Bash", new { backgroundTaskId = "b1" }, Noon));

        try
        {
            BackgroundWork.Read(other, Noon).Should().BeNull("only a transcript is read");
        }
        finally
        {
            File.Delete(other);
        }
    }

    [Fact]
    public void Only_the_tail_of_a_long_transcript_is_read()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".jsonl");

        try
        {
            using (var writer = new StreamWriter(path))
            {
                // A launch at the very start, then more than the tail of
                // conversation, then a report: the report is read and the
                // launch is not, and a report of nothing known ends nothing.
                foreach (var line in Launch("toolu_1", "Bash", new { backgroundTaskId = "early" }, Noon))
                {
                    writer.WriteLine(line);
                }

                var filler = Reply(Noon.AddMinutes(1));

                for (var written = 0L; written < BackgroundWork.Tail + 4096; written += filler.Length + 1)
                {
                    writer.WriteLine(filler);
                }

                foreach (var line in Launch("toolu_2", "Bash", new { backgroundTaskId = "late" }, Noon.AddMinutes(2)))
                {
                    writer.WriteLine(line);
                }

                writer.WriteLine(Notification(Noon.AddMinutes(3), "completed", "late"));
            }

            BackgroundWork.Read(path, Noon.AddMinutes(4)).Should().Be(new BackgroundReading(false, null));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static BackgroundReading? Scan(IEnumerable<string> lines) => BackgroundWork.Scan(lines, Noon.AddMinutes(1));

    /// <summary>An assistant tool call and the tool result that answers it.</summary>
    private static List<string> Launch(string toolUseId, string tool, object result, DateTimeOffset at, string said = "started") =>
    [
        Line(new
        {
            type = "assistant",
            timestamp = at,
            message = new { role = "assistant", content = new object[] { new { type = "tool_use", id = toolUseId, name = tool, input = new { } } } },
        }),
        Line(new
        {
            type = "user",
            timestamp = at,
            toolUseResult = result,
            message = new { role = "user", content = new object[] { new { type = "tool_result", tool_use_id = toolUseId, content = said } } },
        }),
    ];

    private static string Notification(DateTimeOffset at, string status, string taskId) => Line(new
    {
        type = "user",
        timestamp = at,
        origin = new { kind = "task-notification", producer = "session-task" },
        message = new
        {
            role = "user",
            content = $"<task-notification>\n<task-id>{taskId}</task-id>\n<tool-use-id>toolu_1</tool-use-id>\n<status>{status}</status>\n<summary>done</summary>\n</task-notification>",
        },
    });

    private static string Prompt(DateTimeOffset at) =>
        Line(new { type = "user", timestamp = at, origin = new { kind = "human" }, message = new { role = "user", content = "carry on" } });

    private static string Reply(DateTimeOffset at) => Line(new
    {
        type = "assistant",
        timestamp = at,
        message = new { role = "assistant", content = new object[] { new { type = "text", text = "Which of the two did you mean?" } } },
    });

    private static string Line(object entry) => JsonSerializer.Serialize(entry);
}
