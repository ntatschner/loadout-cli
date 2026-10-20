using FluentAssertions;
using Loadout.Core.Ideas;
using Loadout.Core.Tasks;
using Loadout.Models;
using Loadout.Models.Ideas;
using Loadout.Tests.Fakes;
using Xunit;

namespace Loadout.Tests.Integration;

/// <summary>
/// Several changes to ideas at once, against real files: each idea's changes
/// all or nothing, revisions, and records kept parsed between reads.
/// </summary>
public sealed class IdeaBatchTests : IDisposable
{
    private readonly TemporaryWorkspace _space = new();

    public void Dispose() => _space.Dispose();

    private static IdeaReply Proposed() => new()
    {
        Contract = IdeaSchema.Version,
        Understanding = "A page showing what is up.",
        Questions = [new IdeaReply.ReplyQuestion { Question = "Public?" }],
        Plan = new IdeaReply.ReplyPlan
        {
            Title = "Status page",
            Layers =
            [
                new IdeaReply.ReplyLayer
                {
                    Name = "Storage",
                    Options =
                    [
                        new IdeaReply.ReplyOption { Title = "SQLite", Recommended = true },
                        new IdeaReply.ReplyOption { Title = "Postgres" },
                    ],
                },
            ],
            Additions = [new IdeaReply.ReplyAddition { Title = "Alerts", Why = "So you hear first." }],
        },
    };

    private async Task<IdeaPlace> ProposedAsync()
    {
        var idea = (await _space.Ideas.CaptureAsync(null, "Status page", "nigel")).Value!;
        var place = new IdeaPlace(null, idea.Id);

        (await _space.Ideas.RecordReplyAsync(place, Proposed())).Succeeded.Should().BeTrue();

        return place;
    }

    private string RecordPath(IdeaPlace place) =>
        Path.Combine(TaskPaths.Ideas(_space.Workspace.LocalPath, place.Project), place.Id + ".yaml");

    private async Task<IdeaBatch> ApplyAsync(params IdeaOperation[] operations) =>
        (await _space.Ideas.ApplyAsync(operations)).Value!;

    [Fact]
    public async Task An_ideas_answers_choices_and_verdicts_are_made_together_and_each_says_so()
    {
        var place = await ProposedAsync();

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Answering, null, place.Id, Question: "q1", Answer: "private"),
            new IdeaOperation(IdeaOperation.Choose, null, place.Id, Layer: "L1", Option: "b"),
            new IdeaOperation(IdeaOperation.Judge, null, place.Id, Piece: "A1", Verdict: IdeaVerdict.Keep));

        batch.Applied.Should().BeTrue(string.Join("; ", batch.Outcomes.Select(one => one.Error)));
        batch.Outcomes.Select(one => one.Revision).Distinct().Should().ContainSingle("they were written once, together");

        var record = (await _space.Ideas.ReadWithRevisionAsync(place)).Value!;
        record.Revision.Should().Be(batch.Outcomes[0].Revision);
        record.Record.Rounds[0].Questions[0].Answer.Should().Be("private");
        record.Record.Plan!.Layers[0].Chosen.Should().Be("L1b");
        record.Record.Plan.Additions[0].Verdict.Should().Be(IdeaVerdict.Keep);
    }

    [Fact]
    public async Task One_refused_change_leaves_the_idea_exactly_as_it_was_and_says_why_for_the_rest()
    {
        var place = await ProposedAsync();
        var before = await File.ReadAllBytesAsync(RecordPath(place));

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Answering, null, place.Id, Question: "q1", Answer: "private"),
            new IdeaOperation(IdeaOperation.Choose, null, place.Id, Layer: "L1", Option: "z"),
            new IdeaOperation(IdeaOperation.Judge, null, place.Id, Piece: "A1", Verdict: IdeaVerdict.Drop));

        batch.Outcomes.Should().OnlyContain(one => !one.Applied);
        batch.Outcomes[1].Error.Should().Contain("L1a, L1b");
        batch.Outcomes[1].HeldBack.Should().BeFalse();
        batch.Outcomes[0].HeldBack.Should().BeTrue();
        batch.Outcomes[0].Error.Should().Contain("operation 1");
        (await File.ReadAllBytesAsync(RecordPath(place))).Should().Equal(before);
    }

    [Fact]
    public async Task Different_ideas_stand_or_fall_on_their_own()
    {
        var place = await ProposedAsync();
        var other = (await _space.Ideas.CaptureAsync(null, "Another thought", "nigel")).Value!;

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Dismiss, null, other.Id),
            new IdeaOperation(IdeaOperation.Answering, null, place.Id, Question: "q1", Answer: "public"));

        batch.Outcomes.Select(one => one.Applied).Should().Equal(false, true);
        batch.Outcomes[0].HeldBack.Should().BeFalse();
    }

    [Fact]
    public async Task A_capture_and_a_remove_in_a_batch_change_the_list_too()
    {
        var place = await ProposedAsync();

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Capture, null, Text: "Look at the fonts", By: "nigel"),
            new IdeaOperation(IdeaOperation.Remove, null, place.Id));

        batch.Applied.Should().BeTrue(string.Join("; ", batch.Outcomes.Select(one => one.Error)));
        batch.Outcomes[0].Id.Should().Be("look-at-the-fonts");
        batch.Outcomes[0].Revision.Should().NotBeNull();
        (await _space.Tasks.ListAsync(null)).Value!.Select(one => one.Id).Should().Equal("look-at-the-fonts");
        File.Exists(RecordPath(place)).Should().BeFalse();
    }

    [Fact]
    public async Task A_change_made_from_an_older_revision_is_refused_and_the_record_is_not_touched()
    {
        var place = await ProposedAsync();
        var old = (await _space.Ideas.ReadWithRevisionAsync(place)).Value!.Revision!;
        (await _space.Ideas.AnswerAsync(place, "q1", "somebody else")).Succeeded.Should().BeTrue();
        var before = await File.ReadAllBytesAsync(RecordPath(place));

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Answering, null, place.Id, Question: "q1", Answer: "mine", Revision: old),
            new IdeaOperation(IdeaOperation.Remove, null, place.Id, Revision: old));

        batch.Outcomes.Select(one => one.Code).Should().Equal(ExitCode.ChangedSinceRead, ExitCode.ChangedSinceRead);
        (await File.ReadAllBytesAsync(RecordPath(place))).Should().Equal(before);
    }

    [Fact]
    public async Task A_credential_in_an_answer_is_refused_by_pattern_and_nothing_is_written()
    {
        var place = await ProposedAsync();
        var before = await File.ReadAllBytesAsync(RecordPath(place));

        var batch = await ApplyAsync(
            new IdeaOperation(IdeaOperation.Answering, null, place.Id, Question: "q1", Answer: "use ghp_" + new string('e', 36)));

        batch.Outcomes[0].Code.Should().Be(ExitCode.PolicyViolation);
        batch.Outcomes[0].Error.Should().NotContain("ghp_eeee");
        (await File.ReadAllBytesAsync(RecordPath(place))).Should().Equal(before);
    }

    [Fact]
    public async Task A_kept_record_is_handed_out_as_a_copy_with_everything_in_it()
    {
        var place = await ProposedAsync();
        (await _space.Ideas.AnswerAsync(place, "q1", "private")).Succeeded.Should().BeTrue();
        File.SetLastWriteTimeUtc(RecordPath(place), DateTime.UtcNow.AddMinutes(-5));

        var first = (await _space.Ideas.ReadAsync(place)).Value!;
        first.Ask = "Changed by a caller";
        var second = (await _space.Ideas.ReadAsync(place)).Value!;
        second.Ask.Should().Be("Status page", "what a caller changes is its own copy");
        second.Plan!.Layers[0].Options[1].Title = "Changed by a caller";

        var third = (await _space.Ideas.ReadAsync(place)).Value!;
        third.Plan!.Layers[0].Options[1].Title.Should().Be("Postgres");
        third.Should().BeEquivalentTo(IdeaService.Copy(third), "a copy carries every field");
    }

    [Fact]
    public async Task A_record_changed_by_something_else_is_read_again_even_with_the_same_stamp()
    {
        var place = await ProposedAsync();
        (await _space.Ideas.ReadAsync(place)).Value!.Ask.Should().Be("Status page");

        var stamp = File.GetLastWriteTimeUtc(RecordPath(place));
        var text = await File.ReadAllTextAsync(RecordPath(place));
        await File.WriteAllTextAsync(RecordPath(place), text.Replace("Status page", "Statue page", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(RecordPath(place), stamp);

        (await _space.Ideas.ReadAsync(place)).Value!.Ask.Should().Be("Statue page");
    }

    [Fact]
    public async Task A_stamp_moves_when_an_idea_changes_and_names_the_idea()
    {
        var place = await ProposedAsync();
        var before = await _space.Ideas.StampsAsync();

        (await _space.Ideas.AnswerAsync(place, "q1", "private")).Succeeded.Should().BeTrue();
        var after = await _space.Ideas.StampsAsync();

        before.Single().Place.Should().Be(place);
        after.Single().Stamp.Should().NotBe(before.Single().Stamp);
    }

    [Fact]
    public async Task A_recent_change_moves_the_stamp_even_when_the_time_and_length_do_not()
    {
        var place = await ProposedAsync();
        var stamp = File.GetLastWriteTimeUtc(RecordPath(place));
        var before = (await _space.Ideas.StampsAsync()).Single().Stamp;

        var text = await File.ReadAllTextAsync(RecordPath(place));
        await File.WriteAllTextAsync(RecordPath(place), text.Replace("Status page", "Statue page", StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(RecordPath(place), stamp);

        (await _space.Ideas.StampsAsync()).Single().Stamp.Should().NotBe(before);
    }

    [Fact]
    public void A_batch_of_idea_changes_is_read_from_json_with_what_each_op_needs()
    {
        var read = IdeaOperations.Parse(
            """[{"op":"answer","id":"a","question":"q1","answer":"yes"},{"op":"judge","project":"-","id":"b","piece":"L1","verdict":"Improve","request":"cheaper"},{"op":"capture","text":"T"}]""",
            "website",
            "studio");

        read.Succeeded.Should().BeTrue(read.Error);
        read.Value!.Should().Equal(
            new IdeaOperation(IdeaOperation.Answering, "website", "a", Question: "q1", Answer: "yes", By: "studio"),
            new IdeaOperation(IdeaOperation.Judge, null, "b", Piece: "L1", Verdict: IdeaVerdict.Improve, Request: "cheaper", By: "studio"),
            new IdeaOperation(IdeaOperation.Capture, "website", Text: "T", By: "studio"));
    }

    [Theory]
    [InlineData("""[{"op":"refine","id":"a"}]""", "op 'refine'")]
    [InlineData("""[{"op":"answer","id":"a","question":"q1"}]""", "without 'answer'")]
    [InlineData("""[{"op":"judge","id":"a","piece":"L1","verdict":"undecided"}]""", "verdict 'undecided'")]
    [InlineData("""[{"op":"capture"}]""", "without 'text'")]
    [InlineData("""[{"op":"dismiss","id":"a","colour":"red"}]""", "'colour'")]
    public void A_request_that_is_not_a_batch_of_idea_changes_is_refused_with_what_is_wrong(string json, string said)
    {
        var read = IdeaOperations.Parse(json, null, "studio");

        read.Failed.Should().BeTrue();
        read.Error.Should().Contain(said);
    }
}
