using FluentAssertions;
using Loadout.Core.Teams;
using Xunit;

namespace Loadout.Tests.Unit;

/// <summary>
/// Putting a node's unanswerable permission question to a person.
/// </summary>
/// <remarks>
/// <para>
/// Two processes with no channel between them but a directory. The agent starts
/// the answerer, so the coordinator cannot talk to it, and the node is stopped
/// inside a turn the coordinator is awaiting — which is why the question goes
/// out as a file and the answer comes back as one.
/// </para>
/// <para>
/// What has to hold: a question waits, an answer ends the wait, nobody
/// answering ends it too and says which it was, and a half-written answer is
/// never read as one.
/// </para>
/// </remarks>
public sealed class PermissionAskTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "loadout-asks-" + Guid.NewGuid().ToString("N"));

    private readonly Clock _time = new(new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero));

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

    private static PendingAsk Ask(string id = "impl-1-abc") =>
        new(id, "implementer/1", "role.implementer", "Bash", "gh pr create", DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_question_waits_until_somebody_answers_it()
    {
        var asking = NodePermissions.AskAsync(_directory, Ask(), _time);

        // It is there to be found before anybody has answered, which is the
        // whole mechanism: the other process reads this directory.
        await WaitForAsync(() => NodePermissions.Pending(_directory).Count == 1);

        await NodePermissions.AnswerAsync(_directory, "impl-1-abc", new AskAnswer(true, "go on"));

        (await asking)!.Allowed.Should().BeTrue();
    }

    [Fact]
    public async Task An_answered_question_is_no_longer_pending()
    {
        await PutAsync(Ask());

        NodePermissions.Pending(_directory).Should().ContainSingle();

        await NodePermissions.AnswerAsync(_directory, "impl-1-abc", new AskAnswer(false, "no"));

        // Otherwise the watcher asks the same question again every quarter
        // second for the rest of the run.
        NodePermissions.Pending(_directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Nobody_answering_is_its_own_outcome_rather_than_a_refusal()
    {
        var asking = NodePermissions.AskAsync(_directory, Ask(), _time);

        await WaitForAsync(() => NodePermissions.Pending(_directory).Count == 1);

        _time.Advance(NodePermissions.Patience + TimeSpan.FromSeconds(1));

        // Null, not a decision. "Nobody answered" and "your role forbids this"
        // send whoever reads the run to different places, and the caller is
        // what turns this into the sentence the node reads.
        (await asking).Should().BeNull();
    }

    [Fact]
    public async Task A_question_carries_who_is_asking_and_what_for()
    {
        await PutAsync(Ask());

        var pending = NodePermissions.Pending(_directory).Single();

        // "May it run Bash?" is not a question anybody can answer.
        pending.Question.Should().Contain("implementer/1");
        pending.Question.Should().Contain("role.implementer");
        pending.Question.Should().Contain("gh pr create");
    }

    [Fact]
    public void A_question_the_lead_wrote_keeps_the_punctuation_it_came_with()
    {
        // The lead writes its own questions and writes them as questions, so
        // "Merge now?" is what arrives here. Every surface adds a question
        // mark, because a permission question is built without one - which
        // made the lead's read "Merge now??" everywhere but the dashboard.
        new PendingAsk("g", "lead", "role.lead", "ask", null, _time.GetUtcNow(), "question", "Merge now?")
            .Asking.Should().Be("Merge now?");

        // Not every question the lead asks ends in a question mark, and one
        // that has finished its own sentence is left alone too.
        new PendingAsk("g", "lead", "role.lead", "ask", null, _time.GetUtcNow(), "question", "Pick one.")
            .Asking.Should().Be("Pick one.");

        // And the one built here, which really does need it, still gets it.
        Ask().Asking.Should().EndWith("Let it?");
    }

    [Fact]
    public async Task Questions_are_offered_oldest_first()
    {
        var early = new PendingAsk("a", "one", "role.implementer", "Bash", "first", _time.GetUtcNow());
        var late = new PendingAsk("b", "two", "role.implementer", "Bash", "second", _time.GetUtcNow().AddMinutes(1));

        await PutAsync(late);
        await PutAsync(early);

        NodePermissions.Pending(_directory).Select(a => a.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void A_directory_with_no_questions_in_it_has_none()
    {
        NodePermissions.Pending(_directory).Should().BeEmpty();
        NodePermissions.Pending(Path.Combine(_directory, "nowhere")).Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Whichever_way_it_went_the_node_is_told_why(bool allowed)
    {
        var asking = NodePermissions.AskAsync(_directory, Ask(), _time);

        await WaitForAsync(() => NodePermissions.Pending(_directory).Count == 1);

        await NodePermissions.AnswerAsync(
            _directory, "impl-1-abc", new AskAnswer(allowed, "because I said so"));

        var answer = (await asking)!;

        answer.Allowed.Should().Be(allowed);
        answer.Reason.Should().Be("because I said so");
    }

    [Fact]
    public void A_rule_that_already_decided_is_not_a_question()
    {
        var policy = new NodePolicy(
            "run", "implementer/1", "role.implementer",
            Allow: ["Read"],
            Deny: ["Bash(git push:*)"],
            Ask: true);

        // Both of these are settled, and the server only asks about what is
        // not. A deny that could be asked past would make every deny list a
        // suggestion, and an allow that was asked about would stop a node on
        // something its role already said yes to.
        NodePermissions.Decide(policy, "Bash", "{\"command\":\"git push origin main\"}")
            .Rule.Should().Be("Bash(git push:*)", "an explicit refusal, not an open question");

        NodePermissions.Decide(policy, "Read", "{\"file_path\":\"a.cs\"}")
            .Rule.Should().Be("Read");

        // This is the one worth asking about: nothing covers it either way.
        NodePermissions.Decide(policy, "WebFetch", "{\"url\":\"https://example.com\"}")
            .Rule.Should().BeNull();
    }

    [Fact]
    public void Only_a_refusal_that_no_rule_made_is_worth_asking_about()
    {
        var watched = new NodePolicy(
            "run", "implementer/1", "role.implementer",
            Allow: ["Read"], Deny: ["Bash(git push:*)"], Ask: true);

        var alone = watched with { Ask = false };

        var unmatched = NodePermissions.Decide(watched, "WebFetch", null);
        var denied = NodePermissions.Decide(watched, "Bash", "{\"command\":\"git push origin main\"}");
        var allowed = NodePermissions.Decide(watched, "Read", null);

        NodePermissions.Askable(watched, unmatched).Should().BeTrue();

        // Each of these would be a different kind of wrong. Asking past a deny
        // makes every deny list a suggestion; asking about an allow stops a
        // node on something its role already permitted; and asking at all when
        // the run said nobody is watching is a node waiting out the patience
        // for a question nobody will see.
        NodePermissions.Askable(watched, denied).Should().BeFalse("a role that forbids it has decided");
        NodePermissions.Askable(watched, allowed).Should().BeFalse("a role that allows it has decided");
        NodePermissions.Askable(alone, unmatched).Should().BeFalse("nobody is watching this run");
        NodePermissions.Askable(null, unmatched).Should().BeFalse("a session with no policy asks nobody");
    }

    [Fact]
    public async Task An_answer_records_where_it_came_from()
    {
        // "Somebody allowed this" and "somebody allowed this from a browser on
        // the other side of the house" are different sentences to whoever
        // reads the run back. The first real click through the dashboard wrote
        // "terminal", because the command that the daemon runs is the same one
        // a terminal runs and nothing told it otherwise.
        await PutAsync(Ask());

        await NodePermissions.AnswerAsync(
            _directory, "impl-1-abc", new AskAnswer(true, "go on", Chosen: "yes", By: "dashboard"));

        // Read back the way the waiting node reads it.
        var written = System.Text.Json.JsonSerializer.Deserialize<AskAnswer>(
            await File.ReadAllTextAsync(Path.Combine(_directory, "answer-impl-1-abc.json")))!;

        written.By.Should().Be("dashboard");
        written.Chosen.Should().Be("yes");
    }

    [Fact]
    public void An_answer_that_does_not_say_where_it_came_from_is_a_terminal()
    {
        // The default, because that is where all of them came from until a
        // browser could answer one.
        new AskAnswer(true, "go on").By.Should().Be("terminal");
    }

    [Fact]
    public void A_policy_says_nothing_may_be_asked_unless_the_run_said_so()
    {
        // The default, and it matters: a policy written by anything that has
        // not thought about whether somebody is watching lets nobody be asked.
        new NodePolicy("run", "node", "role.implementer", [], []).Ask.Should().BeFalse();
    }

    /// <summary>
    /// Writes the question without waiting on it, for the tests that only care
    /// that it was written.
    /// </summary>
    private async Task PutAsync(PendingAsk ask) =>
        (await NodePermissions.PutAsync(_directory, ask)).Should().BeTrue();

    /// <summary>
    /// A clock the test moves.
    /// </summary>
    /// <remarks>
    /// Ticks behind Interlocked rather than a DateTimeOffset field, because the
    /// side waiting reads it from another thread and a struct that size is not
    /// written atomically. The delays themselves stay real — only the deadline
    /// is on this clock, which is what makes the patience test finish in
    /// milliseconds rather than five minutes.
    /// </remarks>
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private long _ticks = now.UtcTicks;

        public override DateTimeOffset GetUtcNow() =>
            new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }

    /// <summary>
    /// Waits for something the other side does, rather than sleeping a guess.
    /// </summary>
    private static async Task WaitForAsync(Func<bool> until)
    {
        for (var attempt = 0; attempt < 200 && !until(); attempt++)
        {
            await Task.Delay(10);
        }

        until().Should().BeTrue("the other side of this exchange never got there");
    }
}
